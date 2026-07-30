using FocusGuard.Contracts;
using FocusGuard.Desktop;

namespace FocusGuard.Desktop.Tests;

public sealed class SessionSummaryViewModelTests
{
    [Fact]
    public void CompletedSessionLeadsWithTheAchievementAndKeepsItsStreak()
    {
        var viewModel = new SessionSummaryViewModel(Summary(), new StubClient());

        Assert.True(viewModel.IsCompleted);
        Assert.True(viewModel.HasStreak);
        Assert.Contains("50", viewModel.FocusedValue);
        Assert.Equal("14", viewModel.BlocksValue);
        Assert.Contains("6", viewModel.StreakValue);
    }

    [Fact]
    public void StoppedSessionDropsTheStreakAndSaysWhatActuallyHappened()
    {
        var summary = Summary() with
        {
            Outcome = CaptureProtocol.StoppedOutcome,
            FocusedMinutes = 32,
            StreakDays = 0
        };

        var viewModel = new SessionSummaryViewModel(summary, new StubClient());

        Assert.False(viewModel.IsCompleted);
        Assert.False(viewModel.HasStreak);
        Assert.Contains("32", viewModel.Subtitle);
        Assert.Contains("50", viewModel.Subtitle);
    }

    [Fact]
    public void DismissAllClearsTheListAndResolvesEveryCaptureInOneRequest()
    {
        var client = new StubClient();
        var viewModel = new SessionSummaryViewModel(Summary(), client);

        viewModel.DismissAllCommand.Execute(null);

        Assert.False(viewModel.HasCaptures);
        var request = Assert.Single(client.Resolved);
        Assert.Equal(2, request.CaptureIds.Count);
        Assert.Equal(CaptureProtocol.DismissedResolution, request.Resolution);
    }

    [Fact]
    public void DismissingOneCaptureLeavesTheRestOfTheList()
    {
        var client = new StubClient();
        var viewModel = new SessionSummaryViewModel(Summary(), client);
        var first = viewModel.Captures[0];

        first.DismissCommand.Execute(null);

        Assert.Single(viewModel.Captures);
        Assert.Equal(first.Id, Assert.Single(Assert.Single(client.Resolved).CaptureIds));
    }

    [Fact]
    public void EachCaptureShowsWhereItCameFromSoItCanBeJudgedInTwoSeconds()
    {
        var viewModel = new SessionSummaryViewModel(Summary(), new StubClient());

        Assert.Contains("x.com", viewModel.Captures[0].Origin);
        Assert.Contains("ChatGPT.exe", viewModel.Captures[1].Origin);
    }

    [Fact]
    public void AnotherSessionOffersTheSameLengthThatJustFinished()
    {
        var viewModel = new SessionSummaryViewModel(Summary(), new StubClient());
        var requested = 0;
        viewModel.RequestedAnotherSession += (_, _) => requested = viewModel.PlannedMinutes;

        viewModel.AnotherSessionCommand.Execute(null);

        Assert.Equal(50, requested);
        Assert.Contains("50", viewModel.AnotherSessionLabel);
    }

    private static SessionSummaryResponse Summary() =>
        new(
            Available: true,
            SessionId: Guid.NewGuid(),
            Outcome: CaptureProtocol.CompletedOutcome,
            ProfileName: "Deep Work",
            PlannedMinutes: 50,
            FocusedMinutes: 50,
            BlockedAttempts: 14,
            StreakDays: 6,
            EndedAtUtc: DateTimeOffset.UtcNow,
            Captures:
            [
                new CapturedItemView(
                    Guid.NewGuid(),
                    "Revisar respuesta de Carlos",
                    CaptureProtocol.WebsiteKind,
                    "https://x.com/",
                    "x.com",
                    DateTimeOffset.UtcNow,
                    CaptureProtocol.PendingResolution),
                new CapturedItemView(
                    Guid.NewGuid(),
                    "Preguntarle sobre el deploy",
                    CaptureProtocol.ApplicationKind,
                    "C:\\Apps\\ChatGPT.exe",
                    "ChatGPT.exe",
                    DateTimeOffset.UtcNow,
                    CaptureProtocol.PendingResolution)
            ]);

    private sealed class StubClient : IFocusControlClient
    {
        public List<ResolveCapturesRequest> Resolved { get; } = [];

        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<ActivateSessionResponse> ActivateAsync(
            ActivateSessionRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<StopSessionResponse> StopAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<ActivityResponse> GetActivityAsync(
            int maximumCount,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<CaptureResponse> CaptureAsync(
            CaptureRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(
            Guid? sessionId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(
            ResolveCapturesRequest request,
            CancellationToken cancellationToken)
        {
            Resolved.Add(request);
            return ValueTask.FromResult(
                new ResolveCapturesResponse(true, "Resolved", request.CaptureIds.Count));
        }
    }
}
