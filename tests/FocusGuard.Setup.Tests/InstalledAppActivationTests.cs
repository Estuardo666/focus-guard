namespace FocusGuard.Setup.Tests;

public sealed class InstalledAppActivationTests
{
    [Fact]
    public async Task A_launcher_that_returns_without_showing_a_window_must_not_close_Setup()
    {
        var calls = 0;
        var activation = new InstalledAppActivation(() => calls++, () => false);
        Assert.False(await activation.OpenAsync(TimeSpan.FromMilliseconds(500)));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task An_existing_tray_window_can_be_activated_without_starting_another_instance()
    {
        var shown = false;
        var calls = 0;
        var activation = new InstalledAppActivation(() => { calls++; shown = true; }, () => shown);
        Assert.True(await activation.OpenAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_failed_first_activation_can_retry_and_confirm_presentation()
    {
        var calls = 0;
        var activation = new InstalledAppActivation(() => calls++, () => calls == 2);
        Assert.True(await activation.OpenAsync(TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public async Task Cancellation_does_not_report_success()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var activation = new InstalledAppActivation(() => { }, () => false);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => activation.OpenAsync(TimeSpan.FromSeconds(1), cancellation.Token));
    }
}
