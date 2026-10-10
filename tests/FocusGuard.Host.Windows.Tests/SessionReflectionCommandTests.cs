using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Client.Journal;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Host.Windows.Bridge;
using FocusGuard.Host.Windows.Hosting;

namespace FocusGuard.Host.Windows.Tests;

public sealed class SessionReflectionCommandTests
{
    [Fact]
    public async Task SessionSummaryProjectsOnlyTheMatchingLocalObjectiveAndReflection()
    {
        using var data = new Data();
        var sessionId = Guid.NewGuid();
        data.Journal.Save(new(sessionId, Guid.NewGuid(), "Focus", 25, SessionMode.SimpleTimer,
            null, null, false, "Read chapter four", "Finished the draft.", false));
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var bridge = new NativeBridgeDispatcher(new SummaryClient(sessionId), new VerifiedNativeHostAuthorization(true),
            lifetime, sessionJournalStore: data.Journal);

        var response = await Call(bridge, lease.Token, "session.summary", new { sessionId = sessionId.ToString("N") });
        var result = response.GetProperty("result");

        Assert.Null(Error(response));
        Assert.Equal("Read chapter four", result.GetProperty("objective").GetString());
        Assert.Equal("Finished the draft.", result.GetProperty("reflection").GetString());
        Assert.Equal(2, result.GetProperty("cycleMetrics").GetProperty("completedFocusCycles").GetInt32());
        Assert.Equal(1, result.GetProperty("cycleMetrics").GetProperty("completedBreaks").GetInt32());
    }

    [Fact]
    public async Task ReflectionSaveUpdatesOnlyTheExistingLocalJournalEntry()
    {
        using var data = new Data();
        var sessionId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        data.Journal.Save(new(sessionId, profileId, "Focus", 25, SessionMode.SimpleTimer,
            null, null, false, "Read chapter four", "Old note", false));
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var bridge = new NativeBridgeDispatcher(new SummaryClient(sessionId), new VerifiedNativeHostAuthorization(true),
            lifetime, sessionJournalStore: data.Journal);

        var response = await Call(bridge, lease.Token, "session.reflection.save", new
        {
            sessionId = sessionId.ToString("N"), reflection = "  Finished the draft.  ", confirmed = true,
        });
        var result = response.GetProperty("result");
        var saved = new SessionJournalStore(data.Path).GetLatest(sessionId)!;

        Assert.Null(Error(response));
        Assert.Equal(sessionId.ToString("D"), result.GetProperty("sessionId").GetString());
        Assert.Equal("Finished the draft.", result.GetProperty("reflection").GetString());
        Assert.Equal(profileId, saved.ProfileId);
        Assert.Equal("Read chapter four", saved.Objective);
        Assert.Equal("Finished the draft.", saved.Reflection);
    }

    [Fact]
    public async Task ReflectionCannotCreateAnEntryForAnUnknownSession()
    {
        using var data = new Data();
        var sessionId = Guid.NewGuid();
        using var lifetime = new DocumentLifetime();
        var lease = lifetime.BeginDocument();
        var bridge = new NativeBridgeDispatcher(new SummaryClient(Guid.NewGuid()), new VerifiedNativeHostAuthorization(true),
            lifetime, sessionJournalStore: data.Journal);

        var response = await Call(bridge, lease.Token, "session.reflection.save", new
        {
            sessionId = sessionId.ToString("N"), reflection = "Do not create arbitrary notes", confirmed = true,
        });

        Assert.Equal("SessionSummaryUnavailable", Error(response));
        Assert.Null(new SessionJournalStore(data.Path).GetLatest(sessionId));
    }

    [Theory]
    [InlineData("session.reflection.save", "{\"sessionId\":\"11111111111141118111111111111111\",\"reflection\":\"x\"}")]
    [InlineData("session.reflection.save", "{\"sessionId\":\"11111111111141118111111111111111\",\"reflection\":\"x\",\"confirmed\":true,\"path\":\"C:\\\\secret\"}")]
    public void ReflectionParserRejectsMissingAcknowledgmentAndUnknownFields(string method, string parameters) =>
        Assert.False(new BridgeRequestParser().TryParse(Request(Guid.NewGuid().ToString("N"), method,
            JsonSerializer.Deserialize<JsonElement>(parameters)), out _, out _));

    private static async Task<JsonElement> Call(NativeBridgeDispatcher bridge, string token, string method, object parameters)
    {
        var response = await bridge.HandleAsync("https://focusguard.app/index.html", Request(token, method, parameters), CancellationToken.None);
        using var json = JsonDocument.Parse(response!);
        return json.RootElement.Clone();
    }

    private static string Request(string token, string method, object parameters) => JsonSerializer.Serialize(new
    { version = 1, requestId = Guid.NewGuid().ToString("N"), documentToken = token, method, @params = parameters });
    private static string? Error(JsonElement response) => response.GetProperty("error").GetString();

    private sealed class Data : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fg212-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(_directory, "session-journal.json");
        public SessionJournalStore Journal { get; }
        public Data() => Journal = new SessionJournalStore(Path);
        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class SummaryClient(Guid sessionId) : IFocusControlClient
    {
        public ValueTask<PrepareSessionResponse> PrepareAsync(PrepareSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(ActivatePreparedSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SkipBreakResponse> SkipBreakAsync(SkipBreakRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivateSessionResponse> ActivateAsync(ActivateSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<StopSessionResponse> StopAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivityResponse> GetActivityAsync(int maximumCount, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<CaptureResponse> CaptureAsync(CaptureRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(ResolveCapturesRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(Guid? id, CancellationToken token) =>
            ValueTask.FromResult(id == sessionId
                ? new SessionSummaryResponse(true, sessionId, CaptureProtocol.CompletedOutcome, "Focus", 25, 25, 2, 1,
                    DateTimeOffset.UtcNow, [], new CycleHistoryMetrics(2, 1))
                : new SessionSummaryResponse(false, null, "Unavailable", "", 0, 0, 0, 0, null, []));
    }
}
