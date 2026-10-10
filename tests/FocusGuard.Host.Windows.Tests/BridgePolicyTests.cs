using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Contracts;
using FocusGuard.Host.Windows;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;

namespace FocusGuard.Host.Windows.Tests;

public sealed class BridgePolicyTests
{
    private const string TrustedSource = "https://focusguard.app/index.html";

    [Theory]
    [InlineData("https://focusguard.app/index.html", true)]
    [InlineData("https://FOCUSGUARD.APP/index.html#today", true)]
    [InlineData("https://focusguard.app/index.html?next=https://evil.example", false)]
    [InlineData("https://focusguard.app/other.html", false)]
    [InlineData("http://focusguard.app/index.html", false)]
    [InlineData("https://focusguard.app.evil.example/index.html", false)]
    [InlineData("https://evil.example/index.html", false)]
    [InlineData("https://user@focusguard.app/index.html", false)]
    [InlineData("https://focusguard.app:444/index.html", false)]
    [InlineData("file:///C:/temp/index.html", false)]
    public void TopLevelNavigationRequiresExactLocalEntryPoint(string candidate, bool expected)
    {
        Assert.Equal(expected, WebViewSecurityPolicy.IsAllowedTopLevelNavigation(candidate));
    }

    [Theory]
    [InlineData("https://focusguard.app/host.js", true)]
    [InlineData("https://focusguard.app/host.css", true)]
    [InlineData("https://focusguard.app/chunks/count-label.js", true)]
    [InlineData("https://focusguard.app/chunks/CompactFocus.js", true)]
    [InlineData("https://focusguard.app/index.html", true)]
    [InlineData("https://focusguard.app/assets/app-a1b2.js", false)]
    [InlineData("https://focusguard.app/fonts/Geist-Regular.ttf", true)]
    [InlineData("https://focusguard.app/fonts/Geist-SemiBold.ttf", true)]
    [InlineData("https://focusguard.app/fonts/unknown.ttf", false)]
    [InlineData("https://focusguard.app/fonts/Geist-Regular.ttf?x=1", false)]
    [InlineData("https://focusguard.app/fonts/OFL-Geist.txt", false)]
    [InlineData("https://focusguard.app/unknown.txt", false)]
    [InlineData("https://focusguard.app/%68ost.js", true)]
    [InlineData("https://focusguard.app/%2fhost.js", false)]
    [InlineData("https://focusguard.app/host.js#other", false)]
    [InlineData("https://focusguard.app/assets/app.css?remote=1", false)]
    [InlineData("https://evil.example/app.js", false)]
    [InlineData("https://focusguard.app.evil.example/app.js", false)]
    [InlineData("file:///C:/app.js", false)]
    public void ResourceRequestsStayOnMappedOrigin(string candidate, bool expected)
    {
        Assert.Equal(expected, WebViewSecurityPolicy.IsAllowedLocalResource(candidate));
    }

    [Fact]
    public void RequestParserAcceptsOnlyVersionedStatusReadWithExactShape()
    {
        var token = Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid();
        var json = MakeRequest(requestId, token);

        var parsed = new BridgeRequestParser().TryParse(json, out var request, out var errorCode);

        Assert.True(parsed);
        Assert.Empty(errorCode);
        Assert.Equal(requestId, request!.RequestId);
        Assert.Equal(token, request.DocumentToken);
        Assert.Equal("status.get", request.Method);
    }

    [Theory]
    [InlineData("{\"version\":1,\"version\":1}")]
    [InlineData("{\"version\":2,\"requestId\":\"00000000000000000000000000000001\",\"documentToken\":\"00000000000000000000000000000002\",\"method\":\"status.get\",\"params\":{}}")]
    [InlineData("{\"version\":1,\"requestId\":\"00000000000000000000000000000001\",\"documentToken\":\"00000000000000000000000000000002\",\"method\":\"session.activate\",\"params\":{}}")]
    [InlineData("{\"version\":1,\"requestId\":\"00000000000000000000000000000001\",\"documentToken\":\"00000000000000000000000000000002\",\"method\":\"status.get\",\"params\":{\"profileId\":\"x\"}}")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void RequestParserRejectsWrongVersionUnknownMethodsAndMalformedShapes(string json)
    {
        var parsed = new BridgeRequestParser().TryParse(json, out var request, out _);

        Assert.False(parsed);
        Assert.Null(request);
    }

    [Fact]
    public void RequestParserRejectsOversizedMessages()
    {
        var json = new string(' ', BridgeLimits.MaximumRequestBytes + 1);

        var parsed = new BridgeRequestParser().TryParse(json, out _, out var errorCode);

        Assert.False(parsed);
        Assert.Equal("RequestTooLarge", errorCode);
    }

    [Fact]
    public void StatusProjectionAcceptsRealInactiveServiceContract()
    {
        var status = CreateStatus();

        Assert.Equal("Inactive", status.SessionState);
        Assert.Null(status.SessionId);
        Assert.Null(status.ProfileId);
        Assert.Null(status.StartUtc);
        Assert.Null(status.EndUtc);
        Assert.Equal(0, status.RemainingSeconds);
        Assert.False(status.StrictMode);
        Assert.False(status.EnforcementConnected);
        Assert.True(BridgeLimits.IsValidStatus(status));
    }

    [Theory]
    [InlineData("Active", 0, false, false)]
    [InlineData("Inactive", 1, false, false)]
    [InlineData("Inactive", 0, true, false)]
    [InlineData("Inactive", 0, false, true)]
    [InlineData("Unknown", 0, false, false)]
    public void StatusProjectionRejectsInconsistentOrUnknownInactiveStates(
        string sessionState,
        long remainingSeconds,
        bool strictMode,
        bool enforcementConnected)
    {
        var status = CreateStatus() with
        {
            SessionState = sessionState,
            RemainingSeconds = remainingSeconds,
            StrictMode = strictMode,
            EnforcementConnected = enforcementConnected
        };

        Assert.False(BridgeLimits.IsValidStatus(status));
    }

    [Fact]
    public void StatusProjectionRequiresARealUtcSessionIntervalAndNonemptyIdentities()
    {
        var status = CreateStatus(session: true);

        Assert.True(BridgeLimits.IsValidStatus(status));
        Assert.False(BridgeLimits.IsValidStatus(status with { SessionId = Guid.Empty }));
        Assert.False(BridgeLimits.IsValidStatus(status with { ProfileId = Guid.Empty }));
        Assert.False(BridgeLimits.IsValidStatus(status with { StartUtc = null }));
        Assert.False(BridgeLimits.IsValidStatus(status with { StartUtc = status.StartUtc!.Value.ToOffset(TimeSpan.FromHours(-5)) }));
        Assert.False(BridgeLimits.IsValidStatus(status with { EndUtc = status.StartUtc }));
    }

    [Fact]
    public async Task DefaultPhase4GateFailsClosedWithoutCallingClient()
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var client = new RecordingClient(CreateStatus());
        var dispatcher = new NativeBridgeDispatcher(client, new Phase4AuthorizationNotIntegrated(), lifetime);

        var response = await dispatcher.HandleAsync(
            TrustedSource,
            MakeRequest(Guid.NewGuid(), lease.Token),
            CancellationToken.None);

        Assert.Equal(0, client.StatusCalls);
        Assert.Equal("AuthorizationUnavailable", ReadResponse(response!).Error);
    }

    [Fact]
    public async Task AuthorizedStatusReadMapsTypedClientResponse()
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var expectedStatus = CreateStatus(session: true);
        var client = new RecordingClient(expectedStatus);
        var dispatcher = new NativeBridgeDispatcher(client, new TestAuthorization(HostAuthorizationState.Authorized), lifetime);

        var response = await dispatcher.HandleAsync(
            TrustedSource,
            MakeRequest(Guid.NewGuid(), lease.Token),
            CancellationToken.None);

        var parsed = ReadResponse(response!);
        Assert.Equal(1, client.StatusCalls);
        Assert.Null(parsed.Error);
        Assert.Equal(expectedStatus.SessionState, parsed.Status!.SessionState);
        Assert.Equal(expectedStatus.SessionId?.ToString("D"), parsed.Status.SessionId);
        Assert.Equal(expectedStatus.ProfileId?.ToString("D"), parsed.Status.ProfileId);
        Assert.Equal(expectedStatus.RemainingSeconds, parsed.Status.RemainingSeconds);
    }

    [Fact]
    public async Task CompactWindowCommandIsTypedAndDoesNotRequireSessionAuthorizationOrServiceIpc()
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var client = new RecordingClient(CreateStatus());
        var authorization = new TestAuthorization(HostAuthorizationState.Denied);
        var window = new RecordingWindowController();
        var dispatcher = new NativeBridgeDispatcher(client, authorization, lifetime, windowController: window);

        var response = await dispatcher.HandleAsync(TrustedSource,
            MakeWindowRequest(Guid.NewGuid(), lease.Token, "window.compact.set", enabled: true), CancellationToken.None);
        var parsed = JsonSerializer.Deserialize<BridgeCommandResponse>(response!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Null(parsed.Error);
        var presentation = (JsonElement)parsed.Result!;
        Assert.True(presentation.GetProperty("compact").GetBoolean());
        Assert.False(presentation.GetProperty("alwaysOnTop").GetBoolean());
        Assert.True(presentation.GetProperty("detailsExpanded").GetBoolean());
        Assert.Equal(1, window.CompactCalls);
        Assert.Equal(0, window.AlwaysOnTopCalls);
        Assert.Equal(0, authorization.Calls);
        Assert.Equal(0, client.StatusCalls);
    }

    [Fact]
    public void CompactWindowResizesAndRestoresTheNativeWindowWithoutChangingServiceState()
    {
        Exception? failure = null;
        var client = new RecordingClient(CreateStatus());
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new MainForm(client,
                    new TestAuthorization(HostAuthorizationState.Authorized), HostSmokeScenario.None);
                var fullClientSize = form.ClientSize;
                var nonClientHeight = Math.Max(0, form.Height - fullClientSize.Height);
                var compact = form.SetCompactModeAsync(true, CancellationToken.None).GetAwaiter().GetResult();
                Assert.True(compact.Compact);
                Assert.False(form.MaximizeBox);
                Assert.Equal(220 + nonClientHeight, form.MinimumSize.Height);
                Assert.Equal(new System.Drawing.Size(480, 340), form.ClientSize);

                var collapsed = form.SetDetailsExpandedAsync(false, CancellationToken.None).GetAwaiter().GetResult();
                Assert.False(collapsed.DetailsExpanded);
                Assert.Equal(new System.Drawing.Size(480, 220), form.ClientSize);
                var expanded = form.SetDetailsExpandedAsync(true, CancellationToken.None).GetAwaiter().GetResult();
                Assert.True(expanded.DetailsExpanded);
                Assert.Equal(new System.Drawing.Size(480, 340), form.ClientSize);

                var topmost = form.SetAlwaysOnTopAsync(true, CancellationToken.None).GetAwaiter().GetResult();
                Assert.True(topmost.AlwaysOnTop);
                Assert.True(form.TopMost);

                var restored = form.SetCompactModeAsync(false, CancellationToken.None).GetAwaiter().GetResult();
                Assert.False(restored.Compact);
                Assert.True(restored.AlwaysOnTop);
                Assert.True(form.MaximizeBox);
                Assert.Equal(fullClientSize, form.ClientSize);
                Assert.Equal(0, client.StatusCalls);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }

    [Theory]
    [InlineData("window.compact.set", true)]
    [InlineData("window.alwaysOnTop.set", false)]
    [InlineData("window.compactDetails.set", false)]
    public void WindowCommandParserAcceptsOnlyBooleanEnabledValue(string method, bool enabled)
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var parser = new BridgeRequestParser();
        var valid = MakeWindowRequest(Guid.NewGuid(), lease.Token, method, enabled);

        Assert.True(parser.TryParse(valid, out var request, out var error));
        Assert.Empty(error);
        Assert.NotNull(request!.WindowCommand);
        Assert.Equal(method == "window.compact.set" ? enabled : null, request.WindowCommand.Compact);
        Assert.Equal(method == "window.alwaysOnTop.set" ? enabled : null, request.WindowCommand.AlwaysOnTop);
        Assert.Equal(method == "window.compactDetails.set" ? enabled : null, request.WindowCommand.DetailsExpanded);
    }

    [Theory]
    [InlineData("window.compact.set", "true")]
    [InlineData("window.alwaysOnTop.set", "false")]
    public void WindowCommandsRejectExtraOrInvalidParameters(string method, string enabled)
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var parser = new BridgeRequestParser();
        var json = JsonSerializer.Serialize(new
        {
            version = BridgeLimits.ProtocolVersion,
            requestId = Guid.NewGuid().ToString("N"),
            documentToken = lease.Token,
            method,
            @params = new { enabled, extra = true }
        });

        Assert.False(parser.TryParse(json, out _, out _));
    }

    [Fact]
    public async Task BridgeRejectsSpoofedOriginBeforeAuthorizationOrClientAccess()
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var client = new RecordingClient(CreateStatus());
        var authorization = new TestAuthorization(HostAuthorizationState.Authorized);
        var dispatcher = new NativeBridgeDispatcher(client, authorization, lifetime);

        var response = await dispatcher.HandleAsync(
            "https://focusguard.app.evil.example/index.html",
            MakeRequest(Guid.NewGuid(), lease.Token),
            CancellationToken.None);

        Assert.Null(response);
        Assert.Equal(0, authorization.Calls);
        Assert.Equal(0, client.StatusCalls);
    }

    [Fact]
    public async Task BridgeRejectsStaleDocumentToken()
    {
        using var lifetime = new DocumentLifetime();
        var oldLease = lifetime.BeginDocument();
        lifetime.BeginDocument();
        var client = new RecordingClient(CreateStatus());
        var dispatcher = new NativeBridgeDispatcher(client, new TestAuthorization(HostAuthorizationState.Authorized), lifetime);

        var response = await dispatcher.HandleAsync(
            TrustedSource,
            MakeRequest(Guid.NewGuid(), oldLease.Token),
            CancellationToken.None);

        Assert.Equal("StaleDocument", ReadResponse(response!).Error);
        Assert.Equal(0, client.StatusCalls);
    }

    [Fact]
    public async Task DocumentInvalidationCancelsInFlightStatusAndSuppressesStaleResult()
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var client = new CancellationWaitingClient();
        var dispatcher = new NativeBridgeDispatcher(client, new TestAuthorization(HostAuthorizationState.Authorized), lifetime);

        var pending = dispatcher.HandleAsync(
            TrustedSource,
            MakeRequest(Guid.NewGuid(), lease.Token),
            CancellationToken.None).AsTask();
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        lifetime.Invalidate();

        Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(2)));
        await client.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(client.CancellationObserved);
    }

    [Fact]
    public async Task BridgeDeadlineContainsEvenATransportThatIgnoresCancellation()
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var pending = new TaskCompletionSource<StatusResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingClient(CreateStatus()) { Pending = pending.Task };
        var dispatcher = new NativeBridgeDispatcher(client, new TestAuthorization(HostAuthorizationState.Authorized),
            lifetime, new AcceleratedDeadlineClock());
        try
        {
            var response = await dispatcher.HandleAsync(TrustedSource,
                MakeRequest(Guid.NewGuid(), lease.Token), default).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("ServiceUnavailable", ReadResponse(response!).Error);
            Assert.False(pending.Task.IsCompleted);
        }
        finally { pending.TrySetResult(CreateStatus()); }
    }

    private sealed class AcceleratedDeadlineClock : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(BridgeLimits.RequestTimeout, dueTime);
            return TimeProvider.System.CreateTimer(callback, state, TimeSpan.FromMilliseconds(50), period);
        }
    }

    [Fact]
    public async Task DuplicateRequestIdsAndPerMinuteFloodAreRejected()
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var client = new RecordingClient(CreateStatus());
        var dispatcher = new NativeBridgeDispatcher(client, new Phase4AuthorizationNotIntegrated(), lifetime);
        var firstId = Guid.NewGuid();

        var first = await dispatcher.HandleAsync(TrustedSource, MakeRequest(firstId, lease.Token), CancellationToken.None);
        var duplicate = await dispatcher.HandleAsync(TrustedSource, MakeRequest(firstId, lease.Token), CancellationToken.None);
        for (var index = 1; index < BridgeLimits.MaximumRequestsPerMinute; index++)
        {
            await dispatcher.HandleAsync(TrustedSource, MakeRequest(Guid.NewGuid(), lease.Token), CancellationToken.None);
        }

        var flood = await dispatcher.HandleAsync(TrustedSource, MakeRequest(Guid.NewGuid(), lease.Token), CancellationToken.None);

        Assert.Equal("AuthorizationUnavailable", ReadResponse(first!).Error);
        Assert.Equal("DuplicateOrRateLimited", ReadResponse(duplicate!).Error);
        Assert.Equal("DuplicateOrRateLimited", ReadResponse(flood!).Error);
        Assert.Equal(0, client.StatusCalls);
    }

    [Fact]
    public async Task MinuteBoundaryDoesNotPermitAnotherBurstInsideRollingMinute()
    {
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var clock = new RollingRequestClock();
        var dispatcher = new NativeBridgeDispatcher(new RecordingClient(CreateStatus()), new Phase4AuthorizationNotIntegrated(), lifetime, clock);
        await dispatcher.HandleAsync(TrustedSource, MakeRequest(Guid.NewGuid(), lease.Token), default);
        clock.Elapsed = TimeSpan.FromSeconds(59);
        for (var index = 1; index < BridgeLimits.MaximumRequestsPerMinute; index++)
            await dispatcher.HandleAsync(TrustedSource, MakeRequest(Guid.NewGuid(), lease.Token), default);
        clock.Elapsed = TimeSpan.FromSeconds(61);
        var expiredSlot = await dispatcher.HandleAsync(TrustedSource, MakeRequest(Guid.NewGuid(), lease.Token), default);
        var burst = await dispatcher.HandleAsync(TrustedSource, MakeRequest(Guid.NewGuid(), lease.Token), default);
        Assert.Equal("AuthorizationUnavailable", ReadResponse(expiredSlot!).Error);
        Assert.Equal("DuplicateOrRateLimited", ReadResponse(burst!).Error);
    }

    private sealed class RollingRequestClock : TimeProvider
    {
        public TimeSpan Elapsed { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Elapsed.Ticks;
    }

    private static string MakeRequest(Guid requestId, string documentToken) => JsonSerializer.Serialize(new
    {
        version = BridgeLimits.ProtocolVersion,
        requestId = requestId.ToString("N"),
        documentToken,
        method = "status.get",
        @params = new { }
    });

    private static string MakeWindowRequest(Guid requestId, string documentToken, string method, bool enabled) => JsonSerializer.Serialize(new
    {
        version = BridgeLimits.ProtocolVersion,
        requestId = requestId.ToString("N"),
        documentToken,
        method,
        @params = new { enabled }
    });

    private static BridgeResponse ReadResponse(string json) =>
        JsonSerializer.Deserialize<BridgeResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static StatusResponse CreateStatus(bool session = false)
    {
        var now = DateTimeOffset.UtcNow;
        return new StatusResponse(
            ServiceReady: true,
            ExtensionReady: false,
            SessionState: session ? "Active" : "Inactive",
            RemainingSeconds: session ? 312 : 0,
            SessionId: session ? Guid.Parse("ed785d22-9c31-43f7-91cf-bdd1174e8dc5") : null,
            ProfileId: session ? Guid.Parse("c5226aec-1c9f-48aa-b963-a38143825e29") : null,
            StartUtc: session ? now.AddMinutes(-5) : null,
            EndUtc: session ? now.AddMinutes(5) : null,
            StrictMode: false,
            EnforcementConnected: false);
    }

    private sealed class TestAuthorization(HostAuthorizationState state) : IHostControlAuthorization
    {
        public int Calls { get; private set; }

        public HostAuthorizationDecision Authorize(HostControlOperation operation)
        {
            Calls++;
            return new HostAuthorizationDecision(state, state.ToString());
        }
    }

    private sealed class RecordingWindowController : IHostWindowController
    {
        public int CompactCalls { get; private set; }
        public int AlwaysOnTopCalls { get; private set; }
        public HostWindowPresentation State { get; private set; } = new(false, false, true);

        public ValueTask<HostWindowPresentation> SetCompactModeAsync(bool enabled, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompactCalls++;
            State = State with { Compact = enabled };
            return ValueTask.FromResult(State);
        }

        public ValueTask<HostWindowPresentation> SetAlwaysOnTopAsync(bool enabled, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AlwaysOnTopCalls++;
            State = State with { AlwaysOnTop = enabled };
            return ValueTask.FromResult(State);
        }

        public ValueTask<HostWindowPresentation> SetDetailsExpandedAsync(bool enabled, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = State with { DetailsExpanded = enabled };
            return ValueTask.FromResult(State);
        }
    }

    private sealed class RecordingClient(StatusResponse status) : IFocusControlClient
    {
        public int StatusCalls { get; private set; }
        public Task<StatusResponse>? Pending { get; init; }

        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken cancellationToken)
        {
            StatusCalls++;
            return Pending is null ? ValueTask.FromResult(status) : new(Pending);
        }

        public ValueTask<PrepareSessionResponse> PrepareAsync(PrepareSessionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(ActivatePreparedSessionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SkipBreakResponse> SkipBreakAsync(SkipBreakRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ActivateSessionResponse> ActivateAsync(ActivateSessionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<StopSessionResponse> StopAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ActivityResponse> GetActivityAsync(int maximumCount, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<CaptureResponse> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(Guid? sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(ResolveCapturesRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CancellationWaitingClient : IFocusControlClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }

        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<StatusResponse> GetStatusAsync(CancellationToken cancellationToken)
        {
            Started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                Canceled.TrySetResult();
                throw;
            }

            throw new InvalidOperationException("The wait should not complete without cancellation.");
        }

        public ValueTask<PrepareSessionResponse> PrepareAsync(PrepareSessionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(ActivatePreparedSessionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SkipBreakResponse> SkipBreakAsync(SkipBreakRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ActivateSessionResponse> ActivateAsync(ActivateSessionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<StopSessionResponse> StopAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ActivityResponse> GetActivityAsync(int maximumCount, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<CaptureResponse> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(Guid? sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(ResolveCapturesRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
