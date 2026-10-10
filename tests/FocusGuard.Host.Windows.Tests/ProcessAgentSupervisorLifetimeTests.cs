using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Platform.Windows;

namespace FocusGuard.Host.Windows.Tests;

public sealed class ProcessAgentSupervisorLifetimeTests
{
    [Fact]
    public void StartsSupervisionImmediatelyAndStopsItsLoopWhenDisposed()
    {
        var checks = 0;
        var supervisor = new ProcessAgentSupervisor(
            "test-install-root",
            ensureRunning: _ =>
            {
                Interlocked.Increment(ref checks);
                return true;
            },
            delay: (_, _, cancellationToken) =>
                Task.Delay(Timeout.Infinite, cancellationToken));

        var lifetime = new ProcessAgentSupervisorLifetime(supervisor);

        try
        {
            Assert.Equal(1, checks);
            lifetime.Dispose();
            lifetime.Dispose();
            Assert.Equal(1, checks);
        }
        finally
        {
            lifetime.Dispose();
        }
    }
}
