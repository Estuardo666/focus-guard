using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Service;

namespace FocusGuard.Host.Windows.Tests;

public sealed class ActiveSessionCommandTests
{
    [Theory]
    [InlineData("session.stopNormal", "{\"sessionId\":\"11111111111141118111111111111111\",\"operationId\":\"22222222222242228222222222222222\",\"confirmed\":false}")]
    [InlineData("session.stopNormal", "{\"sessionId\":\"11111111111141118111111111111111\",\"operationId\":\"22222222222242228222222222222222\",\"confirmed\":true,\"strictMode\":false}")]
    [InlineData("session.skipBreak", "{\"sessionId\":\"11111111111141118111111111111111\",\"operationId\":\"22222222222242228222222222222222\",\"confirmed\":true,\"phaseIndex\":49}")]
    [InlineData("session.skipBreak", "{\"sessionId\":\"11111111111141118111111111111111\",\"operationId\":\"22222222222242228222222222222222\",\"confirmed\":true}")]
    public void UnsafeOrUnconfirmedControlsAreRejected(string method, string parameters) =>
        Assert.False(new BridgeRequestParser().TryParse(Request(Guid.NewGuid().ToString("N"), method, JsonSerializer.Deserialize<JsonElement>(parameters)), out _, out _));

    [Fact]
    public async Task RealPipePhaseProjectionAndDirectedControlsPreserveIdentityAndConsumeOperations()
    {
        var clock = new Clock(); var service = new InMemoryFocusService(clock);
        var pipe = "FocusGuard.Tests.Fg197." + Guid.NewGuid().ToString("N");
        using var server = new FocusControlPipeService(service, pipe);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await server.StartAsync(cancellation.Token);
        try
        {
            var client = new NamedPipeFocusControlClient(pipe);
            var prepared = await client.PrepareAsync(new(Guid.NewGuid(), "Active cycle", SessionMode.FocusCycle, 0,
                new(60, 60, 60, 2, CycleTerminationMode.CycleCount, 3, null), false, 0, ApplicationRules: [], WebsiteRules: []), cancellation.Token);
            var started = await client.ActivatePreparedAsync(new(prepared.PreparationId!.Value), cancellation.Token);
            var id = started.SessionId!.Value.ToString("N");
            using var lifetime = new DocumentLifetime(); var lease = lifetime.BeginDocument();
            var bridge = new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(true), lifetime);
            async Task<JsonElement> Call(string method, object parameters)
            {
                var response = await bridge.HandleAsync("https://focusguard.app/index.html", Request(lease.Token, method, parameters), cancellation.Token);
                using var parsed = JsonDocument.Parse(response!); return parsed.RootElement.Clone();
            }
            var initial = (await Call("status.get", new { })).GetProperty("status");
            Assert.Equal("Focus", initial.GetProperty("currentPhase").GetProperty("kind").GetString());
            Assert.Equal(60, initial.GetProperty("currentPhase").GetProperty("remainingSeconds").GetInt64());
            Assert.Equal("ShortBreak", initial.GetProperty("nextPhase").GetProperty("kind").GetString());
            Assert.Equal(started.EndUtc, initial.GetProperty("finalEndUtc").GetDateTimeOffset());
            clock.Advance(TimeSpan.FromSeconds(60));
            var op = Guid.NewGuid().ToString("N");
            var skip = await Call("session.skipBreak", new { sessionId = id, phaseIndex = 1, operationId = op, confirmed = true });
            Assert.Equal(JsonValueKind.Null, skip.GetProperty("error").ValueKind);
            Assert.Equal("BreakSkipped", skip.GetProperty("result").GetProperty("action").GetString());
            var status = await client.GetStatusAsync(cancellation.Token);
            Assert.Equal(started.EndUtc!.Value.AddSeconds(-60), status.EndUtc);
            Assert.Equal(SessionPhaseKind.Focus, status.CurrentPhase!.Kind);
            clock.Advance(TimeSpan.FromSeconds(60));
            Assert.Equal("DuplicateOperation", (await Call("session.skipBreak", new { sessionId = id, phaseIndex = 3, operationId = op, confirmed = true })).GetProperty("error").GetString());
            var oldEnd = (await client.GetStatusAsync(cancellation.Token)).EndUtc;
            Assert.Equal("SessionChanged", (await Call("session.stopNormal", new { sessionId = Guid.NewGuid().ToString("N"), operationId = Guid.NewGuid().ToString("N"), confirmed = true })).GetProperty("error").GetString());
            Assert.Equal(oldEnd, (await client.GetStatusAsync(cancellation.Token)).EndUtc);
            var readOnly = new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(), lifetime);
            var denied = await readOnly.HandleAsync("https://focusguard.app/index.html", Request(lease.Token, "session.stopNormal", new { sessionId = id, operationId = Guid.NewGuid().ToString("N"), confirmed = true }), cancellation.Token);
            Assert.Contains("Unauthorized", denied!);
            Assert.Equal(JsonValueKind.Null, (await Call("session.stopNormal", new { sessionId = id, operationId = Guid.NewGuid().ToString("N"), confirmed = true })).GetProperty("error").ValueKind);
            Assert.Equal("Stopped", (await client.GetStatusAsync(cancellation.Token)).SessionState);
            lifetime.BeginDocument();
            var stale = await bridge.HandleAsync("https://focusguard.app/index.html", Request(lease.Token, "session.stopNormal", new { sessionId = id, operationId = Guid.NewGuid().ToString("N"), confirmed = true }), cancellation.Token);
            Assert.Contains("StaleDocument", stale!);
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public void PhaseProjectionRejectsWrongModeBoundsAndOrdering()
    {
        var now = DateTimeOffset.UtcNow;
        var current = new PhaseStatus(1, SessionPhaseKind.ShortBreak, 1, 60, now.AddMinutes(2), now.AddMinutes(1));
        var status = new StatusResponse(true, false, "Active", 180, Guid.NewGuid(), Guid.NewGuid(), now, now.AddMinutes(3), false, false, SessionMode.FocusCycle, current, null, now.AddMinutes(3));
        Assert.True(BridgeLimits.IsValidStatus(status));
        Assert.False(BridgeLimits.IsValidStatus(status with { Mode = SessionMode.SimpleTimer }));
        Assert.False(BridgeLimits.IsValidStatus(status with { CurrentPhase = current with { Index = 49 } }));
        Assert.False(BridgeLimits.IsValidStatus(status with { NextPhase = current }));
        Assert.False(BridgeLimits.IsValidStatus(status with { CurrentPhase = current with { EndUtc = now.AddMinutes(4) } }));
    }

    private static string Request(string token, string method, object parameters) => JsonSerializer.Serialize(new
    { version = 1, requestId = Guid.NewGuid().ToString("N"), documentToken = token, method, @params = parameters });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostDirectedReplyIsUnknownConsumedAndNeverUsesLegacyControl(bool skip)
    {
        var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid();
        var fake = new FailedControlClient
        {
            Status = new(true, false, "Active", 180, id, Guid.NewGuid(), now, now.AddMinutes(3), false, false,
            skip ? SessionMode.FocusCycle : SessionMode.SimpleTimer,
            skip ? new(1, SessionPhaseKind.ShortBreak, 1, 60, now.AddMinutes(2), now.AddMinutes(1)) : null)
        };
        using var lifetime = new DocumentLifetime(); var lease = lifetime.BeginDocument();
        var bridge = new NativeBridgeDispatcher(fake, new VerifiedNativeHostAuthorization(true), lifetime);
        var op = Guid.NewGuid().ToString("N");
        object parameters = skip ? new { sessionId = id.ToString("N"), operationId = op, confirmed = true, phaseIndex = 1 }
            : new { sessionId = id.ToString("N"), operationId = op, confirmed = true };
        var method = skip ? "session.skipBreak" : "session.stopNormal";
        var first = await bridge.HandleAsync("https://focusguard.app/index.html", Request(lease.Token, method, parameters), CancellationToken.None);
        Assert.Contains("OutcomeUnknown", first!);
        var second = await bridge.HandleAsync("https://focusguard.app/index.html", Request(lease.Token, method, parameters), CancellationToken.None);
        Assert.Contains("DuplicateOperation", second!);
        Assert.Equal(1, fake.Calls);
        fake.Status = fake.Status with { StrictMode = true };
        parameters = skip ? new { sessionId = id.ToString("N"), operationId = Guid.NewGuid().ToString("N"), confirmed = true, phaseIndex = 1 }
            : new { sessionId = id.ToString("N"), operationId = Guid.NewGuid().ToString("N"), confirmed = true };
        Assert.Contains("NormalSessionRequired", (await bridge.HandleAsync("https://focusguard.app/index.html", Request(lease.Token, method, parameters), CancellationToken.None))!);
        Assert.Equal(1, fake.Calls);
    }

    private sealed class FailedControlClient : IFocusControlClient, IReviewedSessionControlClient
    {
        public required StatusResponse Status { get; set; }
        public int Calls { get; private set; }
        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken token) => ValueTask.FromResult(Status);
        public ValueTask<StopSessionResponse> StopReviewedAsync(ReviewedStopSessionRequest request, CancellationToken token) { Calls++; throw new IOException("Lost reply"); }
        public ValueTask<SkipBreakResponse> SkipReviewedAsync(ReviewedSkipBreakRequest request, CancellationToken token) { Calls++; throw new IOException("Lost reply"); }
        public ValueTask<StopSessionResponse> StopAsync(CancellationToken token) => throw new InvalidOperationException("Legacy stop must never execute");
        public ValueTask<SkipBreakResponse> SkipBreakAsync(SkipBreakRequest request, CancellationToken token) => throw new InvalidOperationException("Legacy skip must never execute");
        public ValueTask<PrepareSessionResponse> PrepareAsync(PrepareSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(ActivatePreparedSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivateSessionResponse> ActivateAsync(ActivateSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivityResponse> GetActivityAsync(int count, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<CaptureResponse> CaptureAsync(CaptureRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(Guid? id, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(ResolveCapturesRequest request, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2040, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }
}
