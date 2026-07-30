using System.Diagnostics;
using FocusGuard.Application;
using FocusGuard.Infrastructure.Windows.Processes;
using Xunit.Abstractions;

namespace FocusGuard.Infrastructure.Windows.Tests;

public sealed class OwnerPresentProcessIntegrationTests
{
    private const string OwnerPresentOptIn = "FOCUSGUARD_RUN_OWNER_PRESENT_PROCESS_TEST";
    private readonly ITestOutputHelper _output;

    public OwnerPresentProcessIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "OwnerPresent")]
    [Trait("Category", "ProcessIntegration")]
    public async Task ExactTestTarget_IsTerminatedAndAudited_WithoutWideningAllowlist()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(OwnerPresentOptIn), "1", StringComparison.Ordinal))
        {
            return;
        }

        var repositoryRoot = FindRepositoryRoot();
        var testTargetPath = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            "src",
            "FocusGuard.TestTarget",
            "bin",
            "Release",
            "net10.0",
            "FocusGuard.TestTarget.exe"));
        Assert.True(File.Exists(testTargetPath), $"Test target not found: {testTargetPath}");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = testTargetPath,
            Arguments = "--mode cooperative --lifetime 60",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        Assert.NotNull(process);

        try
        {
            var banner = await ReadRequiredLineAsync(process.StandardOutput);
            var reportedPid = await ReadRequiredLineAsync(process.StandardOutput);
            Assert.Equal("FocusGuard Test Target", banner);
            Assert.Equal($"PID: {process.Id}", reportedPid);

            var startedAtUtc = new DateTimeOffset(process.StartTime.ToUniversalTime());
            _output.WriteLine($"Target PID: {process.Id}");
            _output.WriteLine($"Target start UTC: {startedAtUtc:O}");
            _output.WriteLine($"Target path: {testTargetPath}");
            var enabledController = new WindowsApplicationProcessController(
                new ProcessTerminationDeveloperOptions(true, [testTargetPath]),
                new WindowsProcessGateway());

            var rejected = await enabledController.TerminateAsync(
                new ApplicationProcessTarget(Path.Combine(repositoryRoot, "FocusGuard.NotAllowed.exe")),
                ProcessTerminationOptions.Normal,
                CancellationToken.None);
            Assert.Equal(ProcessTerminationStatus.NotAllowed, rejected.Status);
            Assert.False(process.HasExited);
            _output.WriteLine($"Non-allowlisted result: {rejected.Status}");

            var disabledController = new WindowsApplicationProcessController(
                ProcessTerminationDeveloperOptions.Disabled,
                new WindowsProcessGateway());
            var disabled = await disabledController.TerminateAsync(
                new ApplicationProcessTarget(testTargetPath, process.Id, startedAtUtc),
                ProcessTerminationOptions.Normal,
                CancellationToken.None);
            Assert.Equal(ProcessTerminationStatus.Disabled, disabled.Status);
            Assert.False(process.HasExited);
            _output.WriteLine($"Default-disabled result: {disabled.Status}");

            var auditSink = new RecordingAuditSink();
            var terminator = new AuditedProcessTerminator(enabledController, auditSink);
            var result = await terminator.TerminateAsync(
                new ApplicationProcessTarget(testTargetPath, process.Id, startedAtUtc),
                ProcessTerminationOptions.Normal,
                CancellationToken.None);

            Assert.Equal(ProcessTerminationStatus.Terminated, result.Status);
            Assert.Equal(process.Id, result.ProcessId);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(process.HasExited);
            var auditEvent = Assert.Single(auditSink.Events);
            Assert.Equal(ProcessTerminationStatus.Terminated, auditEvent.Status);
            Assert.Equal(process.Id, auditEvent.ProcessId);
            Assert.DoesNotContain(testTargetPath, auditEvent.ToString(), StringComparison.OrdinalIgnoreCase);
            _output.WriteLine($"Termination result: {result.Status}");
            _output.WriteLine($"Audit status: {auditEvent.Status}; PID: {auditEvent.ProcessId}");
            _output.WriteLine($"Confirmed exited: {process.HasExited}");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private static async Task<string> ReadRequiredLineAsync(StreamReader reader)
    {
        var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        return Assert.IsType<string>(line);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusGuard.slnx")))
        {
            directory = directory.Parent;
        }

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }

    private sealed class RecordingAuditSink : IProcessTerminationAuditSink
    {
        public List<ProcessTerminationAuditEvent> Events { get; } = [];

        public Task WriteAsync(ProcessTerminationAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
}
