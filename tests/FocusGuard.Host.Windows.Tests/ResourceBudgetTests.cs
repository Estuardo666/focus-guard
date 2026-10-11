using FocusGuard.Host.Windows.Hosting;

namespace FocusGuard.Host.Windows.Tests;

public sealed class ResourceBudgetTests
{
    private const ulong MiB = 1024UL * 1024;

    [Theory]
    [InlineData(512, 8192, true)]
    [InlineData(1200, 16384, true)]
    [InlineData(1024, 8192, false)]
    [InlineData(2000, 8192, false)]
    public void Low_available_ram_or_low_fraction_simplifies_decoration(ulong availableMiB, ulong totalMiB, bool expected)
    {
        Assert.Equal(expected, new MemoryPressurePolicy().Observe(availableMiB * MiB, totalMiB * MiB));
    }

    [Fact]
    public void Recovery_requires_both_thresholds_to_avoid_repeated_gpu_allocations()
    {
        var policy = new MemoryPressurePolicy();
        Assert.True(policy.Observe(512 * MiB, 16384 * MiB));
        Assert.True(policy.Observe(1200 * MiB, 16384 * MiB));
        Assert.True(policy.Observe(2000 * MiB, 16384 * MiB));
        Assert.False(policy.Observe(3000 * MiB, 16384 * MiB));
        Assert.False(policy.Observe(2000 * MiB, 16384 * MiB));
    }

    [Fact]
    public void Unavailable_or_invalid_sample_retains_previous_mode()
    {
        var policy = new MemoryPressurePolicy();
        Assert.False(policy.Observe(0, 0));
        Assert.True(policy.Observe(512 * MiB, 8192 * MiB));
        Assert.True(policy.Observe(0, 0));
        Assert.True(policy.Observe(9000 * MiB, 8192 * MiB));
    }

    [Fact]
    public async Task Reopening_while_suspend_is_pending_resumes_again_when_it_completes()
    {
        var pending = new TaskCompletionSource<bool>();
        var resumes = 0;
        var visibility = new List<bool>();
        using var controller = new RendererVisibilityController(() => pending.Task, () => resumes++, visibility.Add);
        var hiding = controller.UpdateAsync(false);
        await controller.UpdateAsync(true);
        Assert.Equal(1, resumes);
        pending.SetResult(true);
        await hiding;
        Assert.Equal(2, resumes);
        Assert.Equal([false, true], visibility);
    }

    [Fact]
    public async Task Multiple_hidden_updates_do_not_queue_suspensions_and_last_visibility_wins()
    {
        var pending = new TaskCompletionSource<bool>();
        var suspends = 0;
        var resumes = 0;
        using var controller = new RendererVisibilityController(() => { suspends++; return pending.Task; }, () => resumes++, _ => { });
        var hiding = controller.UpdateAsync(false);
        await controller.UpdateAsync(false);
        await controller.UpdateAsync(true);
        await controller.UpdateAsync(false);
        pending.SetResult(true);
        await hiding;
        Assert.Equal(1, suspends);
        Assert.Equal(1, resumes);
    }

    [Fact]
    public async Task Disposal_prevents_late_resume_or_new_native_calls()
    {
        var pending = new TaskCompletionSource<bool>();
        var resumes = 0;
        var changes = 0;
        var controller = new RendererVisibilityController(() => pending.Task, () => resumes++, _ => changes++);
        var hiding = controller.UpdateAsync(false);
        await controller.UpdateAsync(true);
        controller.Dispose();
        pending.SetResult(true);
        await hiding;
        await controller.UpdateAsync(true);
        Assert.Equal(1, resumes);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task A_failed_suspend_can_be_retried_and_never_prevents_reopening()
    {
        var attempts = 0;
        var resumes = 0;
        using var controller = new RendererVisibilityController(() => ++attempts == 1
            ? Task.FromException<bool>(new InvalidOperationException()) : Task.FromResult(false), () => resumes++, _ => { });
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.UpdateAsync(false));
        await controller.UpdateAsync(false);
        await controller.UpdateAsync(true);
        Assert.Equal(2, attempts);
        Assert.Equal(1, resumes);
    }
}
