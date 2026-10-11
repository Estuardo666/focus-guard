using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FocusGuard.Host.Windows.Tests;

public sealed class WebViewResourceIntegrationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Real_webview_preserves_edits_suspends_timers_and_recovers_from_memory_pressure(bool packagedUi, bool idleUi)
    {
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var form = new Form
            {
                Width = packagedUi ? 1200 : 400,
                Height = packagedUi ? 800 : 300,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Left = -10000,
                Top = -10000
            };
            using var view = new ResourceWebView2 { Dock = DockStyle.Fill };
            form.Controls.Add(view);
            form.Shown += async (_, _) =>
            {
                var root = Path.Combine(Path.GetTempPath(), "FocusGuard.ResourceTest", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                try
                {
                    await File.WriteAllTextAsync(Path.Combine(root, "index.html"),
                        "<input id='draft' value='unsaved'><script>window.ticks=0;setInterval(()=>window.ticks++,50)</script>");
                    var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(root, "userdata"));
                    await view.EnsureCoreWebView2Async(environment);
                    var core = view.CoreWebView2;
                    await core.AddScriptToExecuteOnDocumentCreatedAsync("window.__startupLayoutScore=0;new PerformanceObserver(list=>{for(const entry of list.getEntries())if(!entry.hadRecentInput)window.__startupLayoutScore+=entry.value;}).observe({type:'layout-shift',buffered:true});");
                    var uiFolder = packagedUi ? FindUiFolder() : root;
                    core.SetVirtualHostNameToFolderMapping(WebViewSecurityPolicy.VirtualHostName, uiFolder, CoreWebView2HostResourceAccessKind.Deny);
                    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                    var token = Guid.NewGuid().ToString("N");
                    if (packagedUi)
                    {
                        // Renderer fixture only: the test never connects to or mutates the installed service.
                        core.WebMessageReceived += (_, e) =>
                        {
                            using var message = JsonDocument.Parse(e.WebMessageAsJson);
                            var request = message.RootElement;
                            var method = request.GetProperty("method").GetString();
                            if (method is "preferences.get" or "profiles.list")
                            {
                                object result = method == "profiles.list"
                                    ? new { items = new[] { new { id = "22222222-2222-4222-8222-222222222222", name = "Startup profile", applicationRuleCount = 0, websiteRuleCount = 0, blockedApplicationCount = 0, blockedWebsiteCount = 0 } }, offset = 0, total = 1, pilotEnabled = true }
                                    : new
                                    {
                                        uiLanguage = "es",
                                        theme = "Dark",
                                        reduceMotion = false,
                                        sessionEnd = "None",
                                        cyclePhaseNotifications = false,
                                        enforcementEnabled = false,
                                        lastDurationMinutes = 50,
                                        lastSessionMode = "SimpleTimer",
                                        lastCyclePresetId = (string?)null
                                    };
                                core.PostWebMessageAsJson(JsonSerializer.Serialize(new { version = 1, requestId = request.GetProperty("requestId").GetString(), documentToken = token, error = (string?)null, result }, json));
                                return;
                            }
                            if (method != "status.get") return;
                            core.PostWebMessageAsJson(JsonSerializer.Serialize(new
                            {
                                version = 1,
                                requestId = request.GetProperty("requestId").GetString(),
                                documentToken = token,
                                error = (string?)null,
                                status = new
                                {
                                    serviceReady = true,
                                    extensionReady = true,
                                    sessionState = idleUi ? "Inactive" : "Active",
                                    remainingSeconds = idleUi ? 0 : 600,
                                    sessionId = idleUi ? null : "11111111-1111-4111-8111-111111111111",
                                    profileId = idleUi ? null : "22222222-2222-4222-8222-222222222222",
                                    startUtc = idleUi ? (DateTimeOffset?)null : DateTimeOffset.UtcNow,
                                    endUtc = idleUi ? (DateTimeOffset?)null : DateTimeOffset.UtcNow.AddMinutes(10),
                                    strictMode = false,
                                    enforcementConnected = !idleUi,
                                    mode = "SimpleTimer"
                                }
                            }, json));
                        };
                    }
                    ulong available = 4UL * 1024 * 1024 * 1024;
                    using var resources = new WebViewResourceController(form, view, () => (available, 8UL * 1024 * 1024 * 1024));
                    core.NavigationCompleted += (_, _) =>
                    {
                        resources.RefreshPage();
                        if (packagedUi) core.PostWebMessageAsJson(JsonSerializer.Serialize(
                            new HostInitializationMessage("host.initialize", 1, token, "FocusGuard", "Test", "Ready", "Unavailable"), json));
                    };
                    core.Navigate(WebViewSecurityPolicy.EntryUri.AbsoluteUri);
                    if (packagedUi)
                    {
                        await UntilAsync(async () => await core.ExecuteScriptAsync("!!document.querySelector('.focus-orb canvas')") == "true");
                        if (idleUi) await UntilAsync(async () => await core.ExecuteScriptAsync("!!document.querySelector('[role=slider]:not([aria-disabled=true])') && !!document.querySelector('.connection-strip.ready')") == "true");
                        await CaptureAsync(core, idleUi ? "idle-normal" : "normal");
                        var layoutScore = double.Parse(await core.ExecuteScriptAsync("window.__startupLayoutScore"), System.Globalization.CultureInfo.InvariantCulture);
                        Assert.True(layoutScore < 0.05, $"The settled first view shifted: score={layoutScore}.");
                        await core.ExecuteScriptAsync("const draft=document.createElement('input');draft.id='draft';document.body.appendChild(draft);window.ticks=0;setInterval(()=>window.ticks++,50)");
                    }
                    await UntilAsync(async () => await core.ExecuteScriptAsync("!!document.getElementById('draft')") == "true");
                    await core.ExecuteScriptAsync("document.getElementById('draft').value='edited and unsaved';window.marker={id:123}");
                    available = 512UL * 1024 * 1024;
                    resources.RefreshPage();
                    await UntilAsync(async () => await core.ExecuteScriptAsync("document.documentElement.dataset.resourceConstrained==='true'") == "true");
                    if (packagedUi)
                    {
                        await UntilAsync(async () => await core.ExecuteScriptAsync("!document.querySelector('.focus-orb canvas')&&!!document.querySelector('.orb-fallback')") == "true");
                        Assert.Equal("true", await core.ExecuteScriptAsync(idleUi ? "!!document.querySelector('[role=slider]')" : "!!document.querySelector('.session-orb .timer')"));
                        await CaptureAsync(core, idleUi ? "idle-low-memory" : "low-memory");
                    }
                    Assert.Equal("\"edited and unsaved\"", await core.ExecuteScriptAsync("document.getElementById('draft').value"));
                    form.Hide();
                    await UntilAsync(() => Task.FromResult(core.IsSuspended), () => $"visible={form.Visible}, control={view.Visible}, result={resources.LastSuspendSucceeded}, error={resources.LastFailure}");
                    Assert.False(view.Visible);
                    await Task.Delay(200);
                    form.Show();
                    await UntilAsync(() => Task.FromResult(!core.IsSuspended));
                    await UntilAsync(async () => await core.ExecuteScriptAsync("document.documentElement.dataset.hostHidden==='false'") == "true");
                    Assert.Equal("123", await core.ExecuteScriptAsync("window.marker.id"));
                    Assert.Equal("\"edited and unsaved\"", await core.ExecuteScriptAsync("document.getElementById('draft').value"));
                    form.WindowState = FormWindowState.Minimized;
                    await UntilAsync(() => Task.FromResult(core.IsSuspended));
                    form.WindowState = FormWindowState.Normal;
                    await UntilAsync(() => Task.FromResult(!core.IsSuspended));
                    await UntilAsync(async () => await core.ExecuteScriptAsync("document.documentElement.dataset.hostHidden==='false'") == "true");
                    var ticks = int.Parse(await core.ExecuteScriptAsync("window.ticks"), System.Globalization.CultureInfo.InvariantCulture);
                    await Task.Delay(150);
                    Assert.True(int.Parse(await core.ExecuteScriptAsync("window.ticks"), System.Globalization.CultureInfo.InvariantCulture) > ticks);
                    available = 4UL * 1024 * 1024 * 1024;
                    resources.RefreshPage();
                    await UntilAsync(async () => await core.ExecuteScriptAsync("document.documentElement.dataset.resourceConstrained==='false'") == "true");
                    if (packagedUi)
                    {
                        await UntilAsync(async () => await core.ExecuteScriptAsync("!!document.querySelector('.focus-orb canvas')") == "true");
                        if (!idleUi) await UntilAsync(async () => await core.ExecuteScriptAsync("document.querySelector('.app-shell')?.dataset.immersive==='true'") == "true");
                        await CaptureAsync(core, idleUi ? "idle-recovered" : "recovered");
                    }
                    complete.TrySetResult();
                }
                catch (Exception exception)
                {
                    string detail;
                    try { detail = await view.CoreWebView2.ExecuteScriptAsync("JSON.stringify({ready:document.readyState,hidden:document.hidden,flags:document.documentElement.dataset,webgl:typeof WebGL2RenderingContext,body:document.body?.innerText.slice(0,1000)})"); }
                    catch (Exception diagnostic) { detail = diagnostic.GetType().Name; }
                    complete.TrySetException(new InvalidOperationException("Renderer evidence: " + detail, exception));
                }
                finally { form.Close(); }
                // The unique test profile never touches the owner's profile.
            };
            System.Windows.Forms.Application.Run(form);
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await complete.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static string FindUiFolder()
    {
        var copied = Path.Combine(AppContext.BaseDirectory, "ui");
        if (File.Exists(Path.Combine(copied, "index.html"))) return copied;
        // A focused test build with BuildProjectReferences=false does not copy transitive content items.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) continue;
            var built = Path.Combine(directory.FullName, "src", "FocusGuard.UI", "dist");
            Assert.True(File.Exists(Path.Combine(built, "index.html")), "Build the production React assets before this integration test.");
            return built;
        }
        throw new InvalidOperationException("Production React assets are missing from the test output.");
    }

    private static async Task CaptureAsync(CoreWebView2 core, string name)
    {
        await Task.Delay(900); // Capture the settled interface, not the cabin's entry transition.
        var folder = Path.Combine(AppContext.BaseDirectory, "resource-evidence");
        Directory.CreateDirectory(folder);
        await using var stream = File.Create(Path.Combine(folder, name + ".png"));
        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
    }

    private static async Task UntilAsync(Func<Task<bool>> predicate, Func<string>? diagnostic = null)
    {
        var clock = Stopwatch.StartNew();
        while (!await predicate())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(8), "Real WebView2 did not reach the expected resource state. " + diagnostic?.Invoke());
            await Task.Delay(50);
        }
    }
}
