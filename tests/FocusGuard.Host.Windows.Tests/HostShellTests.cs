using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;

namespace FocusGuard.Host.Windows.Tests;

public sealed class HostShellTests
{
    private const string TrustedSource = "https://focusguard.app/index.html";
    private static readonly Guid SessionA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTimeOffset Start = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private static StatusResponse Running(Guid id, SessionMode mode = SessionMode.SimpleTimer, PhaseStatus? phase = null, bool enforcement = true) =>
        new(true, true, "Active", 600, id, Guid.NewGuid(), Start, Start.AddMinutes(25), false, enforcement, mode, phase);

    private static StatusResponse Ended(string state = "Expired") =>
        new(true, true, state, 0, SessionA, null, Start, Start.AddMinutes(25), false, false);

    private static PhaseStatus Phase(int index, SessionPhaseKind kind, int cycle) => new(index, kind, cycle, 300, Start.AddMinutes(25 * (index + 1)));

    [Fact]
    public void Closure_is_reported_once_per_session_and_never_for_a_session_never_seen_running()
    {
        var tracker = new SessionClosureTracker();
        Assert.Null(tracker.Observe(Ended()));
        Assert.Null(tracker.Observe(Running(SessionA)));
        Assert.Equal(SessionA, tracker.Observe(Ended()));
        Assert.Null(tracker.Observe(Ended()));
    }

    [Fact]
    public void Phase_tracker_reports_focus_and_short_break_transitions_without_repeating_them()
    {
        var tracker = new CyclePhaseTransitionTracker();
        var first = tracker.Observe(Running(SessionA, SessionMode.FocusCycle, Phase(0, SessionPhaseKind.Focus, 1)));
        Assert.Equal(SessionPhaseKind.Focus, first!.StartedKind);
        Assert.Null(tracker.Observe(Running(SessionA, SessionMode.FocusCycle, Phase(0, SessionPhaseKind.Focus, 1))));
        var toBreak = tracker.Observe(Running(SessionA, SessionMode.FocusCycle, Phase(1, SessionPhaseKind.ShortBreak, 1)));
        Assert.Equal(SessionPhaseKind.Focus, toBreak!.EndedKind);
        Assert.Equal(SessionPhaseKind.ShortBreak, toBreak.StartedKind);
        var finished = tracker.Observe(Ended());
        Assert.Equal(SessionPhaseKind.ShortBreak, finished!.EndedKind);
        Assert.Null(finished.StartedKind);
    }

    [Fact]
    public void Application_block_screen_ignores_the_history_seen_on_the_first_read()
    {
        var tracker = new ApplicationBlockTracker();
        var old = new FocusAuditEvent(Guid.NewGuid(), Start.AddMinutes(1), FocusAuditEventKind.ApplicationBlocked, "Steam.exe", "Terminated");
        Assert.Null(tracker.Observe(SessionA, Start, [old]));
        var site = new FocusAuditEvent(Guid.NewGuid(), Start.AddMinutes(2), FocusAuditEventKind.WebsiteBlocked, "x.com", "Blocked");
        Assert.Null(tracker.Observe(SessionA, Start, [old, site]));
        var fresh = new FocusAuditEvent(Guid.NewGuid(), Start.AddMinutes(3), FocusAuditEventKind.ApplicationBlocked, "Discord.exe", "Terminated");
        Assert.Equal("Discord.exe", tracker.Observe(SessionA, Start, [old, site, fresh]));
        Assert.Null(tracker.Observe(SessionA, Start, [old, site, fresh]));
    }

    [Fact]
    public async Task Monitor_raises_session_end_and_block_events_from_status_and_audit_reads()
    {
        var client = new ScriptedClient();
        using var monitor = new SessionEventMonitor(client);
        var ended = new List<Guid>();
        var blocked = new List<string>();
        monitor.SessionEnded += e => ended.Add(e.SessionId);
        monitor.ApplicationBlocked += e => blocked.Add(e.Target);

        await monitor.ObserveAsync(Running(SessionA), CancellationToken.None);
        client.Events = [new(Guid.NewGuid(), Start.AddMinutes(4), FocusAuditEventKind.ApplicationBlocked, "Discord.exe", "Terminated")];
        await Task.Delay(SessionEventMonitor.ActivityInterval + TimeSpan.FromMilliseconds(50));
        await monitor.ObserveAsync(Running(SessionA), CancellationToken.None);
        await monitor.ObserveAsync(Ended(), CancellationToken.None);

        Assert.Equal(["Discord.exe"], blocked);
        Assert.Equal([SessionA], ended);
        Assert.Equal(2, client.ActivityCalls);
    }

    [Fact]
    public async Task Monitor_never_reads_the_audit_log_without_connected_enforcement()
    {
        var client = new ScriptedClient();
        using var monitor = new SessionEventMonitor(client);
        await monitor.ObserveAsync(Running(SessionA, enforcement: false), CancellationToken.None);
        Assert.Equal(0, client.ActivityCalls);
    }

    [Theory]
    [InlineData("app.open", "{\"section\":\"preferences\"}", "Open")]
    [InlineData("app.open", "{\"section\":\"activity\",\"sessionId\":\"11111111111141118111111111111111\"}", "Open")]
    [InlineData("app.compact", "{}", "Compact")]
    [InlineData("app.quit", "{}", "Quit")]
    [InlineData("surface.close", "{}", "CloseSurface")]
    [InlineData("surface.ready", "{}", "SurfaceReady")]
    public async Task Shell_commands_reach_the_shell_controller(string method, string parameters, string expected)
    {
        var (dispatcher, token, shell) = Create(withShell: true);
        var response = await dispatcher.HandleAsync(TrustedSource, Request(token, method, parameters), CancellationToken.None);
        using var parsed = JsonDocument.Parse(response!);
        Assert.Equal(JsonValueKind.Null, parsed.RootElement.GetProperty("error").ValueKind);
        Assert.Equal(expected, Assert.Single(shell!.Commands).Action.ToString());
    }

    [Theory]
    [InlineData("app.open", "{\"section\":\"https://example.com\"}")]
    [InlineData("app.open", "{\"section\":\"profiles\",\"extra\":1}")]
    [InlineData("app.open", "{\"section\":\"profiles\",\"sessionId\":\"11111111111141118111111111111111\"}")]
    [InlineData("app.quit", "{\"force\":true}")]
    [InlineData("surface.ready", "{\"at\":1}")]
    [InlineData("app.run", "{}")]
    public async Task Unknown_or_malformed_shell_commands_are_rejected_before_the_shell(string method, string parameters)
    {
        var (dispatcher, token, shell) = Create(withShell: true);
        var response = await dispatcher.HandleAsync(TrustedSource, Request(token, method, parameters), CancellationToken.None);
        Assert.True(response is null || response.Contains("MethodOrParametersRejected", StringComparison.Ordinal));
        Assert.Empty(shell!.Commands);
    }

    [Fact]
    public async Task Main_window_bridge_without_a_shell_answers_window_unavailable()
    {
        var (dispatcher, token, _) = Create(withShell: false);
        var response = await dispatcher.HandleAsync(TrustedSource, Request(token, "app.quit", "{}"), CancellationToken.None);
        Assert.Contains("WindowUnavailable", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sign_in_start_is_read_and_written_only_through_the_main_window_registration()
    {
        var lifetime = new DocumentLifetime();
        var token = lifetime.BeginDocument().Token;
        var startup = new FakeStartup();
        var dispatcher = new NativeBridgeDispatcher(new ScriptedClient(), new VerifiedNativeHostAuthorization(true), lifetime, startupRegistration: startup);

        using (var read = JsonDocument.Parse((await dispatcher.HandleAsync(TrustedSource, Request(token, "app.startup.get", "{}"), CancellationToken.None))!))
            Assert.False(read.RootElement.GetProperty("result").GetProperty("startupEnabled").GetBoolean());
        using (var write = JsonDocument.Parse((await dispatcher.HandleAsync(TrustedSource, Request(token, "app.startup.set", "{\"enabled\":true}"), CancellationToken.None))!))
        {
            Assert.Equal(JsonValueKind.Null, write.RootElement.GetProperty("error").ValueKind);
            Assert.True(write.RootElement.GetProperty("result").GetProperty("startupEnabled").GetBoolean());
        }
        Assert.True(startup.IsEnabled);

        var rejected = await dispatcher.HandleAsync(TrustedSource, Request(token, "app.startup.set", "{\"enabled\":\"yes\"}"), CancellationToken.None);
        Assert.True(rejected is null || rejected.Contains("MethodOrParametersRejected", StringComparison.Ordinal));
        Assert.True(startup.IsEnabled);

        var (popup, popupToken, _) = Create(withShell: true);
        Assert.Contains("WindowUnavailable", await popup.HandleAsync(TrustedSource, Request(popupToken, "app.startup.set", "{\"enabled\":false}"), CancellationToken.None), StringComparison.Ordinal);
    }

    private sealed class FakeStartup : FocusGuard.Client.Platform.IStartupRegistration
    {
        public bool IsEnabled { get; private set; }
        public bool TrySetEnabled(bool enabled) { IsEnabled = enabled; return true; }
    }

    [Theory]
    [InlineData("Tray", false)]
    [InlineData("Toast", true)]
    [InlineData("Summary", false)]
    [InlineData("Block", false)]
    public void Every_popup_kind_can_be_constructed_and_only_notices_skip_activation(string kind, bool noActivate)
    {
        // WinForms reads CreateParams from the base constructor, before the surface spec is assigned (installed .141 crashed here).
        using var form = new SurfaceForm(new SurfaceSpec(Enum.Parse<SurfaceKind>(kind), new Dictionary<string, object?>()), null!, _ => null!,
            new System.Drawing.Rectangle(0, 0, 360, 480), dark: true);
        var createParams = (System.Windows.Forms.CreateParams)typeof(System.Windows.Forms.Form)
            .GetProperty("CreateParams", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(form)!;
        Assert.Equal(noActivate, (createParams.ExStyle & 0x08000000) != 0);
        Assert.NotEqual(0, createParams.ExStyle & 0x00000080);
    }

    private static (NativeBridgeDispatcher Dispatcher, string Token, RecordingShell? Shell) Create(bool withShell)
    {
        var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var shell = withShell ? new RecordingShell() : null;
        return (new NativeBridgeDispatcher(new ScriptedClient(), new VerifiedNativeHostAuthorization(true), lifetime, shellController: shell), lease.Token, shell);
    }

    private static string Request(string token, string method, string parameters) =>
        $"{{\"version\":{BridgeLimits.ProtocolVersion},\"requestId\":\"{Guid.NewGuid():N}\",\"documentToken\":\"{token}\",\"method\":\"{method}\",\"params\":{parameters}}}";

    private sealed class RecordingShell : IHostShellController
    {
        public List<HostAppCommand> Commands { get; } = [];
        public ValueTask<bool> ExecuteAsync(HostAppCommand command, CancellationToken cancellationToken) { Commands.Add(command); return ValueTask.FromResult(true); }
    }

    private sealed class ScriptedClient : IFocusControlClient
    {
        public IReadOnlyList<FocusAuditEvent> Events { get; set; } = [];
        public int ActivityCalls { get; private set; }
        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Ended());
        public ValueTask<ActivityResponse> GetActivityAsync(int maximumCount, CancellationToken cancellationToken) { ActivityCalls++; return ValueTask.FromResult(new ActivityResponse(Events)); }
        public ValueTask<PrepareSessionResponse> PrepareAsync(PrepareSessionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(ActivatePreparedSessionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SkipBreakResponse> SkipBreakAsync(SkipBreakRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ActivateSessionResponse> ActivateAsync(ActivateSessionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<StopSessionResponse> StopAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<CaptureResponse> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(Guid? sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(ResolveCapturesRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
