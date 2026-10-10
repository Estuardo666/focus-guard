using System.IO.Pipes;
using System.Text.Json;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FocusGuard.Service.Tests;

public sealed class ServiceListenerFailureDiagnosticsTests
{
    [Fact]
    public void HostCanResolvePipeServicesWithoutTestFailureReporter()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ExtensionHealthState>();
        services.AddSingleton<IFocusService, InMemoryFocusService>();
        services.AddHostedService<ExtensionHealthPipeService>();
        services.AddHostedService<FocusControlPipeService>();
        using var provider = services.BuildServiceProvider();

        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        Assert.Collection(hostedServices,
            service => Assert.IsType<ExtensionHealthPipeService>(service),
            service => Assert.IsType<FocusControlPipeService>(service));
    }

    [Fact]
    public async Task ControlListenerFaultIsReportedAndStillPropagates()
    {
        var failure = new InvalidOperationException("synthetic listener failure");
        var reports = new List<(string Worker, Exception Exception)>();
        Task Report(string worker, Exception exception)
        {
            reports.Add((worker, exception));
            return Task.CompletedTask;
        }

        using var service = new FocusControlPipeService(
            new InMemoryFocusService(),
            "FocusGuard.Tests.ListenerFailure." + Guid.NewGuid().ToString("N"),
            security: new ThrowingPipeSecurity(failure),
            failureReporter: Report);

        var observed = await Record.ExceptionAsync(() => service.StartAsync(CancellationToken.None));
        if (observed is null)
            observed = await Record.ExceptionAsync(() => service.ExecuteTask!);

        Assert.IsType<InvalidOperationException>(observed);
        var report = Assert.Single(reports);
        Assert.Equal("FocusControlPipeService.ListenerLoop", report.Worker);
        Assert.Same(failure, report.Exception);
    }

    [Fact]
    public async Task ExtensionHealthListenerFaultIsReportedAndStillPropagates()
    {
        var failure = new InvalidOperationException("synthetic extension listener failure");
        var reports = new List<(string Worker, Exception Exception)>();
        Task Report(string worker, Exception exception)
        {
            reports.Add((worker, exception));
            return Task.CompletedTask;
        }

        using var service = new ExtensionHealthPipeService(
            new ExtensionHealthState(),
            new InMemoryFocusService(),
            pipeName: "FocusGuard.Tests.HealthListenerFailure." + Guid.NewGuid().ToString("N"),
            security: new ThrowingPipeSecurity(failure),
            failureReporter: Report);

        var observed = await Record.ExceptionAsync(() => service.StartAsync(CancellationToken.None));
        if (observed is null)
            observed = await Record.ExceptionAsync(() => service.ExecuteTask!);

        Assert.IsType<InvalidOperationException>(observed);
        var report = Assert.Single(reports);
        Assert.Equal("ExtensionHealthPipeService.ListenerLoop", report.Worker);
        Assert.Same(failure, report.Exception);
    }

    [Fact]
    public async Task FailureLogKeepsMethodTraceButDoesNotWriteExceptionMessage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "focusguard-failure-log-" + Guid.NewGuid().ToString("N"));
        Exception failure;
        try
        {
            throw new InvalidOperationException("private exception payload");
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }

        try
        {
            await ServiceFailureLog.WriteAsync("test.listener", failure, diagnosticsDirectory: directory);
            var path = Path.Combine(directory, "service-failures.jsonl");
            var line = await File.ReadAllTextAsync(path);
            using var record = JsonDocument.Parse(line);
            Assert.Equal("test.listener", record.RootElement.GetProperty("worker").GetString());
            var trace = record.RootElement.GetProperty("methodTrace").EnumerateArray()
                .Select(frame => frame.GetString())
                .Where(frame => frame is not null)
                .ToArray();
            Assert.Contains(trace, frame => frame!.Contains(nameof(ServiceListenerFailureDiagnosticsTests), StringComparison.Ordinal));
            Assert.DoesNotContain("private exception payload", line, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ThrowingPipeSecurity(Exception failure) : IIpcServerSecurity
    {
        public NamedPipeServerStream CreatePipe(string name, bool firstInstance, int maximumInstances) => throw failure;
        public void AuthorizeControl(NamedPipeServerStream pipe, IpcMessageType requestType) { }
        public void AuthorizeAgent(NamedPipeServerStream pipe, string requestType) { }
        public void AuthorizeBrowser(NamedPipeServerStream pipe, string requestType) { }
    }
}