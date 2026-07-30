namespace FocusGuard.Application;

public sealed record ApplicationProcessTarget(
    string ExecutablePath,
    int? ProcessId = null,
    DateTimeOffset? StartedAtUtc = null);

public sealed record ProcessTerminationOptions(TimeSpan GracePeriod)
{
    public static ProcessTerminationOptions Normal { get; } = new(TimeSpan.FromSeconds(2));
}

public enum ProcessTerminationStatus
{
    Terminated,
    AlreadyExited,
    NotFound,
    Disabled,
    NotAllowed,
    AccessDenied,
    ProtectedProcess,
    Cancelled,
    Failed
}

public sealed record ProcessTerminationResult(
    ProcessTerminationStatus Status,
    int? ProcessId,
    string? ErrorCode = null,
    string? Message = null);

public interface IApplicationProcessController
{
    Task<ProcessTerminationResult> TerminateAsync(
        ApplicationProcessTarget target,
        ProcessTerminationOptions options,
        CancellationToken cancellationToken);
}

public sealed record ProcessTerminationAuditEvent(
    DateTimeOffset TimestampUtc,
    ProcessTerminationStatus Status,
    int? ProcessId,
    string? ErrorCode);

public interface IProcessTerminationAuditSink
{
    Task WriteAsync(ProcessTerminationAuditEvent auditEvent, CancellationToken cancellationToken);
}

public sealed class AuditedProcessTerminator
{
    private readonly IApplicationProcessController _controller;
    private readonly IProcessTerminationAuditSink _auditSink;
    private readonly TimeProvider _timeProvider;

    public AuditedProcessTerminator(
        IApplicationProcessController controller,
        IProcessTerminationAuditSink auditSink,
        TimeProvider? timeProvider = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _auditSink = auditSink ?? throw new ArgumentNullException(nameof(auditSink));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ProcessTerminationResult> TerminateAsync(
        ApplicationProcessTarget target,
        ProcessTerminationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        ProcessTerminationResult result;
        try
        {
            result = await _controller.TerminateAsync(target, options, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = new(ProcessTerminationStatus.Cancelled, target.ProcessId, "operation-cancelled");
        }

        var auditEvent = new ProcessTerminationAuditEvent(
            _timeProvider.GetUtcNow().ToUniversalTime(),
            result.Status,
            result.ProcessId,
            result.ErrorCode);
        await _auditSink.WriteAsync(auditEvent, CancellationToken.None);
        return result;
    }
}

public sealed class FakeApplicationProcessController : IApplicationProcessController
{
    private readonly Queue<ProcessTerminationResult> _results = new();

    public List<ApplicationProcessTarget> Requests { get; } = [];

    public void Enqueue(ProcessTerminationResult result) => _results.Enqueue(result);

    public Task<ProcessTerminationResult> TerminateAsync(
        ApplicationProcessTarget target,
        ProcessTerminationOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(target);
        var result = _results.Count > 0
            ? _results.Dequeue()
            : new ProcessTerminationResult(ProcessTerminationStatus.NotFound, target.ProcessId);
        return Task.FromResult(result);
    }
}
