using FocusGuard.Application;

namespace FocusGuard.Application.Tests;

public sealed class ProcessTerminationTests
{
    [Theory]
    [InlineData(ProcessTerminationStatus.Terminated)]
    [InlineData(ProcessTerminationStatus.AccessDenied)]
    [InlineData(ProcessTerminationStatus.Failed)]
    public async Task ResultIsAuditedWithoutExecutablePath(ProcessTerminationStatus status)
    {
        var controller = new FakeApplicationProcessController();
        controller.Enqueue(new ProcessTerminationResult(status, 42, "test-code"));
        var audit = new RecordingAuditSink();
        var terminator = new AuditedProcessTerminator(
            controller,
            audit,
            new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero)));

        var result = await terminator.TerminateAsync(
            new ApplicationProcessTarget(@"C:\safe\FocusGuard.TestTarget.exe", 42),
            ProcessTerminationOptions.Normal,
            CancellationToken.None);

        Assert.Equal(status, result.Status);
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal(status, auditEvent.Status);
        Assert.Equal(42, auditEvent.ProcessId);
        Assert.DoesNotContain("FocusGuard.TestTarget", auditEvent.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationIsConvertedToResultAndAudited()
    {
        var audit = new RecordingAuditSink();
        var terminator = new AuditedProcessTerminator(new CancellingController(), audit);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await terminator.TerminateAsync(
            new ApplicationProcessTarget(@"C:\safe\FocusGuard.TestTarget.exe", 42),
            ProcessTerminationOptions.Normal,
            cancellation.Token);

        Assert.Equal(ProcessTerminationStatus.Cancelled, result.Status);
        Assert.Equal(ProcessTerminationStatus.Cancelled, Assert.Single(audit.Events).Status);
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

    private sealed class CancellingController : IApplicationProcessController
    {
        public Task<ProcessTerminationResult> TerminateAsync(
            ApplicationProcessTarget target,
            ProcessTerminationOptions options,
            CancellationToken cancellationToken) =>
            Task.FromCanceled<ProcessTerminationResult>(cancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
