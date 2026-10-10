using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;

namespace FocusGuard.Host.Windows.Tests;

public sealed class CaptureActionCommandTests
{
    [Theory]
    [InlineData("Expired")]
    [InlineData("Recovered")]
    public async Task CaptureCanBeDismissedAfterAStrictSessionHasEnded(string terminalState)
    {
        var client = new CaptureClient(Status(terminalState, strict: true));
        var (bridge, token) = Bridge(client);

        var response = await Call(bridge, token, "capture.dismiss", client);

        Assert.Null(Error(response));
        Assert.Equal("Dismissed", response.GetProperty("result").GetProperty("action").GetString());
        Assert.Equal([client.CaptureId], Assert.Single(client.Resolved).CaptureIds);
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("Activating")]
    [InlineData("RecoveryPending")]
    public async Task CaptureStaysLockedWhileAStrictSessionIsInForce(string state)
    {
        var client = new CaptureClient(Status(state, strict: true));
        var (bridge, token) = Bridge(client);

        var response = await Call(bridge, token, "capture.dismiss", client);

        Assert.Equal("SessionStateChanged", Error(response));
        Assert.Empty(client.Resolved);
    }

    private static (NativeBridgeDispatcher Bridge, string Token) Bridge(CaptureClient client)
    {
        var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        return (new NativeBridgeDispatcher(client, new VerifiedNativeHostAuthorization(true), lifetime), lease.Token);
    }

    private static StatusResponse Status(string state, bool strict)
    {
        var start = new DateTimeOffset(2026, 10, 10, 6, 41, 0, TimeSpan.Zero);
        return new StatusResponse(true, true, state, state is "Active" or "Activating" ? 300 : 0,
            Guid.NewGuid(), Guid.NewGuid(), start, start.AddMinutes(10), strict,
            EnforcementConnected: state == "Active", Mode: SessionMode.SimpleTimer);
    }

    private static async Task<JsonElement> Call(NativeBridgeDispatcher bridge, string token, string method, CaptureClient client)
    {
        var request = JsonSerializer.Serialize(new
        {
            version = 1, requestId = Guid.NewGuid().ToString("N"), documentToken = token, method,
            @params = new
            {
                sessionId = client.SessionId.ToString("N"), captureId = client.CaptureId.ToString("N"),
                operationId = Guid.NewGuid().ToString("N"), confirmed = true,
            },
        });
        var response = await bridge.HandleAsync("https://focusguard.app/index.html", request, CancellationToken.None);
        using var json = JsonDocument.Parse(response!);
        return json.RootElement.Clone();
    }

    private static string? Error(JsonElement response) => response.GetProperty("error").GetString();

    private sealed class CaptureClient(StatusResponse status) : IFocusControlClient
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public Guid CaptureId { get; } = Guid.NewGuid();
        public List<ResolveCapturesRequest> Resolved { get; } = [];

        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken token) => ValueTask.FromResult(status);

        public ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(Guid? id, CancellationToken token) =>
            ValueTask.FromResult(new SessionSummaryResponse(true, SessionId, CaptureProtocol.CompletedOutcome, "gpt test", 10, 10, 0, 0,
                new DateTimeOffset(2026, 10, 10, 6, 51, 0, TimeSpan.Zero),
                [new CapturedItemView(CaptureId, "Reply to Ana", CaptureProtocol.WebsiteKind, "https://example.com/", "example.com",
                    new DateTimeOffset(2026, 10, 10, 6, 45, 0, TimeSpan.Zero), CaptureProtocol.PendingResolution)]));

        public ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(ResolveCapturesRequest request, CancellationToken token)
        {
            Resolved.Add(request);
            return ValueTask.FromResult(new ResolveCapturesResponse(true, "Resolved", request.CaptureIds.Count));
        }

        public ValueTask<PrepareSessionResponse> PrepareAsync(PrepareSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(ActivatePreparedSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SkipBreakResponse> SkipBreakAsync(SkipBreakRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivateSessionResponse> ActivateAsync(ActivateSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<StopSessionResponse> StopAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivityResponse> GetActivityAsync(int maximumCount, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<CaptureResponse> CaptureAsync(CaptureRequest request, CancellationToken token) => throw new NotSupportedException();
    }
}
