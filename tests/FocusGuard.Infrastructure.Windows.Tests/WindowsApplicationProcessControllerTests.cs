using FocusGuard.Application;
using FocusGuard.Infrastructure.Windows.Processes;

namespace FocusGuard.Infrastructure.Windows.Tests;

public sealed class WindowsApplicationProcessControllerTests
{
    private static readonly string AllowedPath =
        Path.GetFullPath(@"C:\FocusGuardDev\FocusGuard.TestTarget.exe");

    [Fact]
    public async Task DisabledByDefault_DoesNotInspectProcesses()
    {
        var gateway = new FakeGateway();
        var controller = new WindowsApplicationProcessController(
            ProcessTerminationDeveloperOptions.Disabled,
            gateway);

        var result = await controller.TerminateAsync(
            new ApplicationProcessTarget(AllowedPath),
            ProcessTerminationOptions.Normal,
            CancellationToken.None);

        Assert.Equal(ProcessTerminationStatus.Disabled, result.Status);
        Assert.Equal(0, gateway.FindCalls);
    }

    [Theory]
    [InlineData(@"C:\FocusGuardDev\notepad.exe", ProcessTerminationStatus.NotAllowed)]
    [InlineData(@"C:\Windows\System32\lsass.exe", ProcessTerminationStatus.ProtectedProcess)]
    [InlineData(@"C:\Program Files\FocusGuard\FocusGuard.Recovery.exe", ProcessTerminationStatus.ProtectedProcess)]
    [InlineData(@"C:\Elsewhere\FocusGuard.TestTarget.exe", ProcessTerminationStatus.NotAllowed)]
    public async Task RejectedTarget_DoesNotInspectProcesses(
        string targetPath,
        ProcessTerminationStatus expectedStatus)
    {
        var gateway = new FakeGateway();
        var controller = CreateEnabledController(gateway);

        var result = await controller.TerminateAsync(
            new ApplicationProcessTarget(targetPath),
            ProcessTerminationOptions.Normal,
            CancellationToken.None);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(0, gateway.FindCalls);
    }

    [Fact]
    public async Task MissingAllowedTarget_ReturnsNotFound()
    {
        var controller = CreateEnabledController(new FakeGateway());

        var result = await controller.TerminateAsync(
            new ApplicationProcessTarget(AllowedPath),
            ProcessTerminationOptions.Normal,
            CancellationToken.None);

        Assert.Equal(ProcessTerminationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task AlreadyExitedTarget_IsNotTerminated()
    {
        var gateway = FakeGateway.WithTarget(AllowedPath);
        gateway.HasExitedValue = true;
        var controller = CreateEnabledController(gateway);

        var result = await controller.TerminateAsync(
            CreateIdentifiedTarget(gateway),
            ProcessTerminationOptions.Normal,
            CancellationToken.None);

        Assert.Equal(ProcessTerminationStatus.AlreadyExited, result.Status);
        Assert.Equal(0, gateway.TerminateCalls);
    }

    [Fact]
    public async Task CooperativeExit_IsConfirmedWithoutForcedTermination()
    {
        var gateway = FakeGateway.WithTarget(AllowedPath);
        gateway.WaitForExitValue = true;
        var controller = CreateEnabledController(gateway);

        var result = await controller.TerminateAsync(
            CreateIdentifiedTarget(gateway),
            ProcessTerminationOptions.Normal,
            CancellationToken.None);

        Assert.Equal(ProcessTerminationStatus.Terminated, result.Status);
        Assert.Equal(1, gateway.GracefulCloseCalls);
        Assert.Equal(0, gateway.TerminateCalls);
    }

    [Fact]
    public async Task GraceTimeout_ForcesOnlyVerifiedTargetAndConfirmsExit()
    {
        var gateway = FakeGateway.WithTarget(AllowedPath);
        gateway.WaitForExitValue = false;
        gateway.ExitAfterTerminate = true;
        var controller = CreateEnabledController(gateway);

        var result = await controller.TerminateAsync(
            CreateIdentifiedTarget(gateway),
            ProcessTerminationOptions.Normal,
            CancellationToken.None);

        Assert.Equal(ProcessTerminationStatus.Terminated, result.Status);
        Assert.Equal(1, gateway.TerminateCalls);
    }

    [Fact]
    public async Task AccessDenied_IsExplicitAndDoesNotEscape()
    {
        var gateway = FakeGateway.WithTarget(AllowedPath);
        gateway.Exception = new UnauthorizedAccessException();
        var controller = CreateEnabledController(gateway);

        var result = await controller.TerminateAsync(
            CreateIdentifiedTarget(gateway),
            ProcessTerminationOptions.Normal,
            CancellationToken.None);

        Assert.Equal(ProcessTerminationStatus.AccessDenied, result.Status);
    }

    [Fact]
    public async Task CancelledRequest_DoesNotInspectProcesses()
    {
        var gateway = new FakeGateway();
        var controller = CreateEnabledController(gateway);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await controller.TerminateAsync(
            new ApplicationProcessTarget(AllowedPath),
            ProcessTerminationOptions.Normal,
            cancellation.Token);

        Assert.Equal(ProcessTerminationStatus.Cancelled, result.Status);
        Assert.Equal(0, gateway.FindCalls);
    }

    [Fact]
    public async Task AmbiguousTarget_IsRejectedWithoutTermination()
    {
        var gateway = FakeGateway.WithTarget(AllowedPath);
        gateway.Processes.Add(gateway.Processes[0] with { ProcessId = 43 });
        var controller = CreateEnabledController(gateway);

        var result = await controller.TerminateAsync(
            new ApplicationProcessTarget(AllowedPath),
            ProcessTerminationOptions.Normal,
            CancellationToken.None);

        Assert.Equal(ProcessTerminationStatus.Failed, result.Status);
        Assert.Equal("ambiguous-target", result.ErrorCode);
        Assert.Equal(0, gateway.TerminateCalls);
    }

    private static WindowsApplicationProcessController CreateEnabledController(FakeGateway gateway) =>
        new(
            new ProcessTerminationDeveloperOptions(true, [AllowedPath]),
            gateway);

    private static ApplicationProcessTarget CreateIdentifiedTarget(FakeGateway gateway)
    {
        var process = Assert.Single(gateway.Processes);
        return new(process.ExecutablePath, process.ProcessId, process.StartedAtUtc);
    }

    private sealed class FakeGateway : IWindowsProcessGateway
    {
        public List<WindowsProcessInstance> Processes { get; } = [];

        public int FindCalls { get; private set; }

        public int GracefulCloseCalls { get; private set; }

        public int TerminateCalls { get; private set; }

        public bool HasExitedValue { get; set; }

        public bool WaitForExitValue { get; set; }

        public bool ExitAfterTerminate { get; set; }

        public Exception? Exception { get; set; }

        public static FakeGateway WithTarget(string path)
        {
            var gateway = new FakeGateway();
            gateway.Processes.Add(new WindowsProcessInstance(
                42,
                new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero),
                path));
            return gateway;
        }

        public IReadOnlyList<WindowsProcessInstance> FindByExecutablePath(string executablePath)
        {
            FindCalls++;
            ThrowIfConfigured();
            return Processes;
        }

        public bool TryRequestGracefulClose(WindowsProcessInstance process)
        {
            GracefulCloseCalls++;
            ThrowIfConfigured();
            return true;
        }

        public Task<bool> WaitForExitAsync(
            WindowsProcessInstance process,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ThrowIfConfigured();
            return Task.FromResult(WaitForExitValue);
        }

        public void Terminate(WindowsProcessInstance process)
        {
            TerminateCalls++;
            ThrowIfConfigured();
            if (ExitAfterTerminate)
            {
                HasExitedValue = true;
            }
        }

        public bool HasExited(WindowsProcessInstance process)
        {
            ThrowIfConfigured();
            return HasExitedValue;
        }

        private void ThrowIfConfigured()
        {
            if (Exception is not null)
            {
                throw Exception;
            }
        }
    }
}
