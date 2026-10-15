using System.Reflection;
using FocusGuard.Client;
using FocusGuard.Client.Preferences;
using FocusGuard.Host.Windows.Hosting;

namespace FocusGuard.Host.Windows.Tests;

public sealed class MinimizedStartupTests
{
    [Fact]
    public async Task MainWindowOpenedAfterHiddenHandleCreationLoadsRenderer()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusGuard.MinimizedStartup", Guid.NewGuid().ToString("N"));
            var settings = new DesktopSettingsStore(Path.Combine(root, "desktop-settings.json"));
            settings.SaveVerified(new DesktopSettings(Theme: AppTheme.Light, UiLanguage: "es"));
            var client = new NamedPipeFocusControlClient("unused-minimized-renderer-test");
            using var main = new MainForm(client, new Phase4AuthorizationNotIntegrated(), HostSmokeScenario.LocalLoad,
                desktopSettingsStore: settings);
            using var shell = new HostShell(main, client, new Phase4AuthorizationNotIntegrated(), null,
                (_, _) => null!, startMinimized: true, instance: null);
            using var timer = new System.Windows.Forms.Timer { Interval = 500 };
            var opening = false;
            timer.Tick += async (_, _) =>
            {
                if (opening) return;
                opening = true;
                timer.Stop();
                try
                {
                    Assert.False(main.Visible);
                    Assert.True(main.PresentToUser());
                    await Task.Delay(4000);
                    var view = (ResourceWebView2?)typeof(MainForm).GetField("_webView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main);
                    Assert.NotNull(view);
                    var info = await view.CoreWebView2.ExecuteScriptAsync("JSON.stringify({children:document.getElementById('root')?.childElementCount,body:document.body.innerText.slice(0,200)})");
                    Assert.True(main.SmokeSucceeded, "Main renderer did not answer after hidden startup. " + info +
                        " visible=" + view.Visible + " suspended=" + view.CoreWebView2.IsSuspended);
                    completion.TrySetResult();
                }
                catch (Exception error) { completion.TrySetException(error); }
                finally { main.CloseToTray = false; main.Close(); }
            };
            timer.Start();
            System.Windows.Forms.Application.Run(shell);
            Directory.Delete(root, recursive: true);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(25));
    }
}
