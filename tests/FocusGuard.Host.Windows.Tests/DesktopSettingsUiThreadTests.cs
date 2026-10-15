using System.Text.Json;
using System.Windows.Forms;
using FocusGuard.Client.Preferences;

namespace FocusGuard.Host.Windows.Tests;

public sealed class DesktopSettingsUiThreadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SynchronousPreferenceAccessCompletesOnUiThreadAfterLeaseContention(bool save)
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard.SettingsUiThread", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "desktop-settings.json");
        var expected = new DesktopSettings(Theme: AppTheme.Light, UiLanguage: "en", LastDurationMinutes: 35);
        await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(expected,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var store = new DesktopSettingsStore(path);
        _ = store.Load();
        if (save) expected = expected with { LastDurationMinutes = 45, ReduceMotion = true };
        using var heldLease = new FileStream(path + ".sync.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var complete = new TaskCompletionSource<DesktopSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var context = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            started.TrySetResult();
            try
            {
                if (save) store.SaveVerified(expected);
                complete.TrySetResult(store.Load());
            }
            catch (Exception error) { complete.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(150); // Force AcquireAsync through its asynchronous contention retry on the UI context.
        heldLease.Dispose();
        var loaded = await complete.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(expected, loaded);
        Directory.Delete(root, recursive: true);
    }
}
