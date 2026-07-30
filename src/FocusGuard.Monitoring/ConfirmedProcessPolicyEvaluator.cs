namespace FocusGuard.Monitoring;

public sealed class ConfirmedProcessPolicyEvaluator
{
    private readonly ProcessMonitorPolicyReceiver _receiver;
    private readonly SimulatedProcessMonitor _evaluator;

    public ConfirmedProcessPolicyEvaluator(
        ProcessMonitorPolicyReceiver receiver,
        TimeSpan gracePeriod,
        TimeProvider? timeProvider = null)
    {
        _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
        _evaluator = new SimulatedProcessMonitor(gracePeriod, timeProvider);
    }

    public IReadOnlyList<ProcessAction> Evaluate(IEnumerable<ProcessSnapshot> snapshots) =>
        _evaluator.Evaluate(_receiver.GetActiveDelivery()?.Policy, snapshots);
}
