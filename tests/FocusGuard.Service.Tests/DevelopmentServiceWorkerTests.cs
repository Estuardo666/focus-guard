using FocusGuard.Service;

namespace FocusGuard.Service.Tests;

public sealed class DevelopmentServiceWorkerTests
{
    [Fact]
    public async Task Worker_StartsAndStopsWithoutEnforcement()
    {
        var state = new DevelopmentServiceState();
        using var worker = new DevelopmentServiceWorker(state);

        await worker.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => state.IsRunning, TimeSpan.FromSeconds(1));

        Assert.True(state.IsRunning);
        Assert.NotNull(state.StartedUtc);
        Assert.Equal(state.StartedUtc, state.LastHeartbeatUtc);

        await worker.StopAsync(CancellationToken.None);

        Assert.False(state.IsRunning);
    }

    [Fact]
    public void Constants_UseDevelopmentOnlyIdentity()
    {
#if FOCUSGUARD_PRODUCTION
        Assert.Equal("FocusGuard", DevelopmentServiceConstants.ServiceName);
        Assert.Equal("FocusGuard Service", DevelopmentServiceConstants.DisplayName);
#else
        Assert.Equal("FocusGuard.Development", DevelopmentServiceConstants.ServiceName);
        Assert.Equal("FocusGuard Development Service", DevelopmentServiceConstants.DisplayName);
#endif
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellation.Token);
        }
    }
}
