using FocusGuard.Desktop;

namespace FocusGuard.Desktop.Tests;

public sealed class ActiveFocusViewModelTests
{
    [Fact]
    public void Refresh_FormatsRemainingTimeAndExpiresAtEnd()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var viewModel = new ActiveFocusViewModel(clock);

        clock.SetUtcNow(new DateTimeOffset(2030, 1, 1, 12, 49, 30, TimeSpan.Zero));
        viewModel.Refresh();
        Assert.Equal("00:30", viewModel.RemainingText);
        Assert.False(viewModel.IsExpired);

        clock.SetUtcNow(new DateTimeOffset(2030, 1, 1, 12, 50, 0, TimeSpan.Zero));
        viewModel.Refresh();
        Assert.True(viewModel.IsExpired);
        Assert.Equal("00:00", viewModel.RemainingText);
        Assert.Contains("released", viewModel.StatusMessage);
    }

    [Fact]
    public void StopCommand_ReleasesNormalSessionInSimulator()
    {
        var viewModel = new ActiveFocusViewModel(new TestTimeProvider(DateTimeOffset.UtcNow));

        Assert.True(viewModel.CanStop);
        viewModel.StopFocusCommand.Execute(null);

        Assert.False(viewModel.CanStop);
        Assert.Contains("stopped", viewModel.StatusMessage);
    }

    private sealed class TestTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
    }
}
