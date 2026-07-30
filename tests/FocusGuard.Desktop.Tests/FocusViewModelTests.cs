using Avalonia.Media;
using FocusGuard.Contracts;
using FocusGuard.Desktop;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Desktop.Tests;

public sealed class FocusViewModelTests
{
    [Fact]
    public void DurationPresetsIncludeFiniteTwoMinuteTestOption()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());

        var testPreset = Assert.Single(
            viewModel.DurationPresets,
            preset => preset.Label == "2 minutes (test)");
        Assert.Equal(TimeSpan.FromMinutes(2), testPreset.Duration);
        Assert.Equal(TimeSpan.FromMinutes(50), viewModel.SelectedDurationPreset.Duration);
    }

    [Fact]
    public async Task NewViewModelLoadsAuthoritativeInactiveStatus()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());

        await viewModel.RefreshAsync();

        Assert.Equal("Deep Work", viewModel.ProfileName);
        Assert.Equal(TimeSpan.FromMinutes(50), viewModel.SelectedDurationPreset.Duration);
        Assert.False(viewModel.IsStrictMode);
        Assert.True(viewModel.CanStart);
        Assert.Contains("No session is active", viewModel.StatusMessage);
        Assert.Contains("Service ready", viewModel.ServiceHealthLabel);
        Assert.Contains("Enforcement is off", viewModel.EnforcementWarning);
    }

    [Fact]
    public async Task NormalStartActivatesThroughServiceClientAndCanStop()
    {
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(client, new MemoryProfileStore());
        await viewModel.RefreshAsync();

        viewModel.StartFocusCommand.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => client.LastActivation is not null, TimeSpan.FromSeconds(1)));
        Assert.True(SpinWait.SpinUntil(() => viewModel.IsActive, TimeSpan.FromSeconds(1)));

        Assert.False(client.LastActivation!.StrictMode);
        Assert.False(client.LastActivation.EnforcementEnabled);
        Assert.Null(client.LastActivation.RecoveryCredential);
        Assert.Single(client.LastActivation.ApplicationRules!);
        Assert.Single(client.LastActivation.WebsiteRules!);
        Assert.True(viewModel.CanStop);

        viewModel.StopFocusCommand.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => client.StopCount == 1, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task EnforcementRequiresExplicitOptIn()
    {
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(client, new MemoryProfileStore())
        {
            IsEnforcementEnabled = true
        };
        await viewModel.RefreshAsync();

        viewModel.StartFocusCommand.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => client.LastActivation is not null, TimeSpan.FromSeconds(1)));
        Assert.True(client.LastActivation!.EnforcementEnabled);
        Assert.Contains("Each application or website channel", viewModel.EnforcementWarning);
    }

    [Fact]
    public async Task StrictReviewShowsCodeOnceAndSendsOnlyCredential()
    {
        var client = new RecordingClient();
        var phraseGenerator = new StrictConfirmationPhraseGenerator(_ => 0);
        var viewModel = new FocusViewModel(client, new MemoryProfileStore(), phraseGenerator)
        {
            IsStrictMode = true
        };
        await viewModel.RefreshAsync();

        viewModel.StartFocusCommand.Execute(null);

        Assert.True(viewModel.IsStrictReviewVisible);
        Assert.Equal("Calm Balance", viewModel.RequiredConfirmationText);
        Assert.Equal(2, viewModel.RequiredConfirmationText.Split(' ').Length);
        var displayedCode = viewModel.RecoveryCodeDisplay;
        Assert.NotEmpty(displayedCode);
        Assert.Null(client.LastActivation);

        viewModel.AcknowledgeRecoverySaved = true;
        viewModel.AcknowledgeUnsavedWork = true;
        viewModel.TypedConfirmation = viewModel.RequiredConfirmationText;
        viewModel.ConfirmStrictCommand.Execute(null);

        Assert.True(SpinWait.SpinUntil(() => client.LastActivation is not null, TimeSpan.FromSeconds(2)));
        Assert.True(client.LastActivation!.StrictMode);
        Assert.Equal(900, client.LastActivation.RecoveryDelaySeconds);
        Assert.True(RecoveryCodeService.Validate(displayedCode, client.LastActivation.RecoveryCredential));
        Assert.Empty(viewModel.RecoveryCodeDisplay);
        Assert.False(viewModel.IsStrictReviewVisible);
    }

    [Fact]
    public async Task CancellingStrictReviewClearsEveryOneTimeGateWithoutActivating()
    {
        var client = new RecordingClient();
        var phraseGenerator = new StrictConfirmationPhraseGenerator(_ => 0);
        var viewModel = new FocusViewModel(client, new MemoryProfileStore(), phraseGenerator)
        {
            IsStrictMode = true
        };
        await viewModel.RefreshAsync();

        viewModel.StartFocusCommand.Execute(null);
        viewModel.AcknowledgeRecoverySaved = true;
        viewModel.AcknowledgeUnsavedWork = true;
        viewModel.TypedConfirmation = viewModel.RequiredConfirmationText;

        viewModel.CancelStrictCommand.Execute(null);

        Assert.False(viewModel.IsStrictReviewVisible);
        Assert.Empty(viewModel.RecoveryCodeDisplay);
        Assert.Empty(viewModel.RequiredConfirmationText);
        Assert.Empty(viewModel.TypedConfirmation);
        Assert.False(viewModel.AcknowledgeRecoverySaved);
        Assert.False(viewModel.AcknowledgeUnsavedWork);
        Assert.Null(client.LastActivation);
    }

    [Fact]
    public async Task ActivityLoadsSanitizedEventsFromServiceClient()
    {
        var client = new RecordingClient
        {
            Activity =
            [
                new FocusAuditEvent(
                    Guid.NewGuid(),
                    new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero),
                    FocusAuditEventKind.WebsiteBlocked,
                    "https://example.com/private",
                    "Blocked")
            ]
        };
        var viewModel = new FocusViewModel(client, new MemoryProfileStore());

        await viewModel.ShowActivityAsync();

        Assert.True(viewModel.IsActivityVisible);
        Assert.False(viewModel.IsFocusVisible);
        var item = Assert.Single(viewModel.ActivityEvents);
        Assert.Equal("https://example.com/private", item.Target);
        Assert.False(viewModel.IsActivityEmpty);
    }

    [Fact]
    public async Task SavedRulesAreListedAndOneApplicationRuleCanBeRemovedPersistently()
    {
        var store = new MemoryProfileStore();
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();

        var application = Assert.Single(viewModel.SavedApplicationRules);
        Assert.True(viewModel.HasSavedApplicationRules);

        await viewModel.RemoveApplicationRuleAsync(application.Rule.Id);

        Assert.Empty(viewModel.SavedApplicationRules);
        Assert.False(viewModel.HasSavedApplicationRules);
        Assert.Single(viewModel.SavedWebsiteRules);
        var persisted = Assert.Single(await store.ListProfilesAsync());
        Assert.Empty(persisted.ApplicationRules);
        Assert.Single(persisted.WebsiteRules);
        Assert.Contains("Active policies are unchanged", viewModel.StatusMessage);
    }

    [Fact]
    public async Task OneWebsiteRuleCanBeRemovedWithoutChangingApplicationRules()
    {
        var store = new MemoryProfileStore();
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();
        var website = Assert.Single(viewModel.SavedWebsiteRules);

        await viewModel.RemoveWebsiteRuleAsync(website.Rule.Id);

        Assert.Empty(viewModel.SavedWebsiteRules);
        Assert.Single(viewModel.SavedApplicationRules);
        var persisted = Assert.Single(await store.ListProfilesAsync());
        Assert.Empty(persisted.WebsiteRules);
        Assert.Single(persisted.ApplicationRules);
    }

    [Fact]
    public async Task ActiveSessionPreventsProfileRuleRemoval()
    {
        var client = new RecordingClient();
        var store = new MemoryProfileStore();
        var viewModel = new FocusViewModel(client, store);
        await viewModel.RefreshAsync();
        var application = Assert.Single(viewModel.SavedApplicationRules);

        viewModel.StartFocusCommand.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => viewModel.IsActive, TimeSpan.FromSeconds(1)));
        await viewModel.RemoveApplicationRuleAsync(application.Rule.Id);

        Assert.Single(viewModel.SavedApplicationRules);
        Assert.Single((await store.ListProfilesAsync()).Single().ApplicationRules);
        Assert.Contains("cannot be changed", viewModel.StatusMessage);
        Assert.False(application.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task FirstRunHeaderShowsASentenceRatherThanAZeroTotal()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());

        await viewModel.RefreshAsync();

        Assert.False(viewModel.HasHistory);
        Assert.False(viewModel.HasMetric);
        Assert.True(viewModel.HasMetricPlaceholder);
        Assert.Equal(string.Empty, viewModel.MetricValue);
        Assert.Null(viewModel.WeekMinutes);
    }

    [Fact]
    public async Task SuppliedHistoryTurnsTheMetricIntoTheDayTotal()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());
        await viewModel.RefreshAsync();

        viewModel.ApplyFocusHistory([20d, 45d, 0d, 90d, 60d, 75d, 138d], 138d);

        Assert.True(viewModel.HasHistory);
        Assert.True(viewModel.HasMetric);
        Assert.False(viewModel.HasMetricPlaceholder);
        Assert.Equal("2h 18m", viewModel.MetricValue);
        Assert.Equal("FOCUSED TODAY", viewModel.MetricLabel);
        Assert.Equal(
            [("2", false), ("h", true), (" 18", false), ("m", true)],
            viewModel.MetricSegments.Select(segment => (segment.Text, segment.IsUnit)));
    }

    // Under an hour there is no hour run to set smaller, so the split collapses to two.
    [Fact]
    public async Task HistoryUnderAnHourDropsTheHourRun()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());
        await viewModel.RefreshAsync();

        viewModel.ApplyFocusHistory([45d], 45d);

        Assert.Equal(
            [("45", false), ("m", true)],
            viewModel.MetricSegments.Select(segment => (segment.Text, segment.IsUnit)));
    }

    [Fact]
    public async Task ActiveSessionSwapsTheMetricToAnAccentCountdownAndHidesSwatches()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());
        await viewModel.RefreshAsync();
        Assert.True(viewModel.HasProfileSwatches);

        viewModel.StartFocusCommand.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => viewModel.IsActive, TimeSpan.FromSeconds(1)));

        Assert.Equal(viewModel.RemainingText, viewModel.MetricValue);
        // The caption carries the session length so the countdown needs no context hunt.
        Assert.Equal("REMAINING OF 50 MIN", viewModel.MetricLabel);
        Assert.Equal(viewModel.RemainingText, Assert.Single(viewModel.MetricSegments).Text);
        Assert.False(Assert.Single(viewModel.MetricSegments).IsUnit);
        Assert.Same(viewModel.ProfileAccentBrush, viewModel.MetricBrush);
        Assert.False(viewModel.HasProfileSwatches);
        Assert.False(viewModel.HasHistory);
    }

    [Fact]
    public async Task HeaderSubtitleFallsBackToRuleCountsWhenTheProfileHasNoDescription()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());

        await viewModel.RefreshAsync();

        Assert.Equal("1 sites and 1 apps blocked", viewModel.HeaderSubtitle);
    }

    [Fact]
    public async Task HeaderSubtitlePrefersTheProfileDescription()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());
        await viewModel.RefreshAsync();

        viewModel.SelectProfile(new StoredProfile(
            ProfileId.New(),
            "Estudio",
            [],
            [],
            Description: "Solo video y streaming fuera."));

        Assert.Equal("Solo video y streaming fuera.", viewModel.HeaderSubtitle);
    }

    [Fact]
    public async Task ProfileAccentComesFromTheProfileAndFallsBackWithoutOne()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());
        await viewModel.RefreshAsync();
        var fallback = viewModel.ProfileAccentBrush;

        viewModel.SelectProfile(new StoredProfile(
            ProfileId.New(),
            "Estudio",
            [],
            [],
            AccentHex: "#3E9C7A"));

        Assert.NotNull(fallback);
        var accent = Assert.IsType<SolidColorBrush>(viewModel.ProfileAccentBrush);
        Assert.Equal(Color.Parse("#3E9C7A"), accent.Color);
    }

    // Swapping the policy under a running session is the same hazard the padlocked
    // Profiles screen guards against, so the swatch row has to refuse it too.
    [Fact]
    public async Task SwatchSelectionIsRefusedWhileASessionRuns()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());
        await viewModel.RefreshAsync();
        var original = viewModel.SelectedProfile;

        viewModel.StartFocusCommand.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => viewModel.IsActive, TimeSpan.FromSeconds(1)));
        viewModel.SelectProfile(new StoredProfile(ProfileId.New(), "Estudio", [], []));

        Assert.Same(original, viewModel.SelectedProfile);
    }

    private sealed class MemoryProfileStore : IProfileStore
    {
        private readonly List<StoredProfile> _profiles =
        [
            new(
                new ProfileId(Guid.Parse("84c70293-0738-4dde-a0d2-2cd585f054a6")),
                "Deep Work",
                [new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "chat.exe")],
                [new WebsiteRule(Guid.NewGuid(), RuleAction.Block, "example.com")])
        ];

        public Task SaveProfileAsync(StoredProfile profile, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _profiles.RemoveAll(existing => existing.Id == profile.Id);
            _profiles.Add(profile);
            return Task.CompletedTask;
        }

        public Task<StoredProfile?> LoadProfileAsync(
            ProfileId profileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_profiles.FirstOrDefault(profile => profile.Id == profileId));

        public Task<IReadOnlyList<StoredProfile>> ListProfilesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredProfile>>(_profiles.ToArray());

        public Task<bool> DeleteProfileAsync(
            ProfileId profileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_profiles.RemoveAll(profile => profile.Id == profileId) == 1);
    }

    private sealed class RecordingClient : IFocusControlClient
    {
        private StatusResponse _status = Inactive();

        public ActivateSessionRequest? LastActivation { get; private set; }

        public int StopCount { get; private set; }

        public IReadOnlyList<FocusAuditEvent> Activity { get; init; } = [];

        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_status);
        }

        public ValueTask<ActivateSessionResponse> ActivateAsync(
            ActivateSessionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastActivation = request;
            var start = DateTimeOffset.UtcNow;
            var sessionId = Guid.NewGuid();
            _status = new StatusResponse(
                true,
                false,
                "Active",
                request.DurationSeconds,
                sessionId,
                request.ProfileId,
                start,
                start.AddSeconds(request.DurationSeconds),
                request.StrictMode,
                EnforcementConnected: false);
            return ValueTask.FromResult(new ActivateSessionResponse(
                true,
                "Activated",
                sessionId,
                start,
                start.AddSeconds(request.DurationSeconds)));
        }

        public ValueTask<StopSessionResponse> StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCount++;
            _status = Inactive() with { SessionState = "Stopped" };
            return ValueTask.FromResult(new StopSessionResponse(true, "Stopped"));
        }

        public ValueTask<ActivityResponse> GetActivityAsync(
            int maximumCount,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ActivityResponse(Activity.Take(maximumCount).ToArray()));

        public IReadOnlyList<CapturedItemView> Captures { get; init; } = [];

        public SessionSummaryResponse? Summary { get; set; }

        public List<CaptureRequest> Captured { get; } = [];

        public List<ResolveCapturesRequest> Resolved { get; } = [];

        public ValueTask<CaptureResponse> CaptureAsync(
            CaptureRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Captured.Add(request);
            return ValueTask.FromResult(new CaptureResponse(true, "Captured", Guid.NewGuid()));
        }

        public ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(
            Guid? sessionId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Summary ?? new SessionSummaryResponse(
                Available: Captures.Count > 0,
                SessionId: Captures.Count > 0 ? Guid.NewGuid() : null,
                Outcome: CaptureProtocol.CompletedOutcome,
                ProfileName: "Deep Work",
                PlannedMinutes: 50,
                FocusedMinutes: 50,
                BlockedAttempts: 14,
                StreakDays: 6,
                EndedAtUtc: DateTimeOffset.UtcNow,
                Captures: Captures));
        }

        public ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(
            ResolveCapturesRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Resolved.Add(request);
            return ValueTask.FromResult(
                new ResolveCapturesResponse(true, "Resolved", request.CaptureIds.Count));
        }

        private static StatusResponse Inactive() =>
            new(
                true,
                false,
                "Inactive",
                0,
                null,
                null,
                null,
                null,
                StrictMode: false,
                EnforcementConnected: false);
    }
}
