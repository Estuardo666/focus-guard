using Avalonia.Media;
using FocusGuard.Client;
using FocusGuard.Client.Preferences;
using FocusGuard.Contracts;
using FocusGuard.Desktop;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Desktop.Tests;

public sealed class FocusViewModelTests
{
    [Theory]
    [InlineData(false, RuleAction.Block)]
    [InlineData(true, RuleAction.Allow)]
    public async Task CatalogAdditionUsesVisibleApplicationAction(bool selectAllow, RuleAction expected)
    {
        var store = new MemoryProfileStore();
        var viewModel = new FocusViewModel(new RecordingClient(), store) { ApplicationCatalog = new RuleTestCatalog() };
        await viewModel.RefreshAsync();
        if (selectAllow) viewModel.ApplicationAction = viewModel.ApplicationActions.Single(choice => choice.Action == RuleAction.Allow);
        await viewModel.LoadApplicationsAsync();
        Assert.Single(viewModel.MatchingApplications).AddCommand.Execute(null);
        var saved = (await store.ListProfilesAsync()).Single().ApplicationRules.Single(rule => rule.Value == @"C:\RuleTest\fixture.exe");
        Assert.Equal(expected, saved.Action);
        Assert.Equal(ApplicationMatchKind.ExactPath, saved.MatchKind);
    }

    private sealed class RuleTestCatalog : FocusGuard.Client.Platform.IApplicationCatalog
    {
        public Task<IReadOnlyList<FocusGuard.Client.Platform.InstalledApplication>> ListAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<FocusGuard.Client.Platform.InstalledApplication>>([new("Rule fixture", @"C:\RuleTest\fixture.exe")]);
    }

    [Theory]
    [InlineData(false, RuleAction.Block)]
    [InlineData(true, RuleAction.Allow)]
    public async Task ApplicationAdditionPersistsExplicitActionAndDisplaysIt(bool selectAllow, RuleAction expected)
    {
        var store = new MemoryProfileStore();
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();
        if (selectAllow) viewModel.ApplicationAction = viewModel.ApplicationActions.Single(choice => choice.Action == RuleAction.Allow);
        viewModel.ApplicationRuleInput = "FocusGuard.TestTarget.exe";
        viewModel.AddApplicationRuleCommand.Execute(null);
        var saved = (await store.ListProfilesAsync()).Single().ApplicationRules.Single(rule => rule.Value == "FocusGuard.TestTarget.exe");
        Assert.Equal(expected, saved.Action);
        Assert.Equal(ApplicationMatchKind.FileName, saved.MatchKind);
        var row = viewModel.SavedApplicationRules.Single(rule => rule.Rule.Id == saved.Id);
        Assert.Equal(expected == RuleAction.Allow ? Resources.UiStrings.RuleAllow : Resources.UiStrings.RuleBlock, row.ActionLabel);
        Assert.Equal(RuleAction.Block, (await store.ListProfilesAsync()).Single().ApplicationRules.Single(rule => rule.Value == "chat.exe").Action);
    }

    [Theory]
    [InlineData(false, 2700, 2025, 0)]
    [InlineData(true, 2700, 2700, 0)]
    [InlineData(true, 2700, 2025, 90)]
    [InlineData(true, 2700, 1350, 180)]
    [InlineData(true, 2700, 0, 360)]
    public void RingSweepAngleRepresentsClockwiseElapsedTime(
        bool isActive,
        long totalSeconds,
        long remainingSeconds,
        double expected)
    {
        Assert.Equal(
            expected,
            FocusViewModel.CalculateRingSweepAngle(isActive, totalSeconds, remainingSeconds),
            precision: 6);
    }

    [Fact]
    public void WindowsStartupPreferenceReflectsAndUpdatesRegistration()
    {
        var registration = new RecordingStartupRegistration(isEnabled: true);
        var viewModel = new FocusViewModel(
            new RecordingClient(),
            new MemoryProfileStore(),
            startupRegistration: registration);

        Assert.True(viewModel.StartsWithWindows);

        viewModel.StartsWithWindows = false;

        Assert.False(viewModel.StartsWithWindows);
        Assert.Equal([false], registration.Requests);
    }

    [Fact]
    public void FailedWindowsStartupChangeLeavesVisiblePreferenceUnchanged()
    {
        var registration = new RecordingStartupRegistration(isEnabled: false, succeeds: false);
        var viewModel = new FocusViewModel(
            new RecordingClient(),
            new MemoryProfileStore(),
            startupRegistration: registration);

        viewModel.StartsWithWindows = true;

        Assert.False(viewModel.StartsWithWindows);
        Assert.Equal([true], registration.Requests);
    }

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
    public void EditingDurationSelectsMatchingPresetOrCustomChoice()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());

        viewModel.SelectedDurationMinutes = 90;
        Assert.Equal(TimeSpan.FromMinutes(90), viewModel.SelectedDurationPreset.Duration);
        Assert.False(viewModel.IsCustomDurationSelected);

        viewModel.SelectedDurationMinutes = 47;
        Assert.True(viewModel.IsCustomDurationSelected);
        Assert.Equal(47, viewModel.CustomDurationMinutes);
        Assert.Equal(47, viewModel.SelectedDurationMinutes);
    }

    [Fact]
    public void SettingsOpensAsModalWithoutReplacingTheCurrentSection()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());
        var originalSection = viewModel.Section;

        viewModel.ShowSection(NavigationSection.Settings);

        Assert.True(viewModel.IsSettingsVisible);
        Assert.Equal(originalSection, viewModel.Section);

        viewModel.CloseSettings();
        Assert.False(viewModel.IsSettingsVisible);
        Assert.Equal(originalSection, viewModel.Section);
    }

    [Fact]
    public void CycleModeExposesBuiltInPresetsAndReadablePreview()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());

        viewModel.SelectedSessionMode = viewModel.SessionModes[1];

        Assert.True(viewModel.IsCycleMode);
        Assert.Equal(3, viewModel.CyclePresets.Count);
        Assert.Equal("Pomodoro clásico", viewModel.SelectedCyclePreset!.Name);
        Assert.Contains("25 min", viewModel.CycleCompositionText);
        Assert.Contains("5 min", viewModel.CycleCompositionText);
        Assert.Contains("15 min", viewModel.CycleLongBreakText);
        Assert.Contains("4", viewModel.CycleLengthText);
        Assert.NotEmpty(viewModel.CycleEndText);
    }

    [Fact]
    public async Task RefreshLoadsStoredCustomPresetAndProfileBreakExceptions()
    {
        var store = new MemoryProfileStore();
        var configuration = FocusCycleConfiguration.Create(
            TimeSpan.FromMinutes(30),
            TimeSpan.FromMinutes(7),
            TimeSpan.FromMinutes(18),
            3,
            CycleTerminationMode.CycleCount,
            cycleCount: 5).Value!;
        var preset = new StoredCyclePreset(Guid.NewGuid(), "Escritura", configuration, false);
        store.AddPreset(preset);
        store.SetCycleSettings(new ProfileCycleSettings(
            store.PrimaryProfileId,
            preset.Id,
            [new BreakApplicationRule(Guid.NewGuid(), RuleAction.Allow, ApplicationMatchKind.FileName, "music.exe")],
            [new BreakWebsiteRule(Guid.NewGuid(), RuleAction.Allow, "music.example")]));

        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();

        Assert.Equal(preset.Id, viewModel.SelectedCyclePreset!.Id);
        Assert.True(viewModel.IsEditingCyclePreset);
        Assert.Equal(30, viewModel.CycleFocusMinutes);
        Assert.Equal(2, viewModel.BreakRules.Count);
    }

    [Fact]
    public async Task StrictCycleReviewUsesPreparedServicePhasesBeforeActivation()
    {
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(
            client,
            new MemoryProfileStore())
        {
            IsStrictMode = true
        };
        await viewModel.RefreshAsync();
        viewModel.SelectedSessionMode = viewModel.SessionModes[1];

        viewModel.StartFocusCommand.Execute(null);

        Assert.True(SpinWait.SpinUntil(() => viewModel.IsStrictReviewVisible, TimeSpan.FromSeconds(1)));
        Assert.True(viewModel.HasPreparedCyclePlan);
        Assert.Contains("Focus", viewModel.PreparedCyclePlanText);
        Assert.Null(client.LastActivation);

        viewModel.AdvanceStrictStep();
        viewModel.TypedConfirmation = viewModel.RequiredConfirmationText;
        viewModel.ConfirmStrictCommand.Execute(null);

        Assert.True(SpinWait.SpinUntil(() => client.PreparedActivationCount == 1, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task NewViewModelLoadsAuthoritativeInactiveStatus()
    {
        using var directory = new TemporaryDirectory();
        var viewModel = new FocusViewModel(
            new RecordingClient(),
            new MemoryProfileStore(),
            settingsStore: new DesktopSettingsStore(Path.Combine(directory.Path, "desktop-settings.json")));

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
        using var directory = new TemporaryDirectory();
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(
            client,
            new MemoryProfileStore(),
            settingsStore: new DesktopSettingsStore(Path.Combine(directory.Path, "desktop-settings.json")));
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
    public async Task NormalStartDoesNotActivateAProfileDeletedSinceRefresh()
    {
        var client = new RecordingClient();
        var store = new MemoryProfileStore();
        var viewModel = new FocusViewModel(client, store);
        await viewModel.RefreshAsync();
        await store.DeleteProfileAsync(viewModel.SelectedProfile!.Id);

        viewModel.StartFocusCommand.Execute(null);

        Assert.Null(client.LastActivation);
        Assert.False(viewModel.IsActive);
        Assert.True(viewModel.StartFocusCommand.CanExecute(null));
        Assert.Equal(
            FocusGuard.Desktop.Resources.UiStrings.Get("SessionProfileUnavailable"),
            viewModel.StatusMessage);
    }

    [Fact]
    public async Task NormalStartReportsAProfileDatabaseReadFailureWithoutActivating()
    {
        using var directory = new TemporaryDirectory();
        var databasePath = Path.Combine(directory.Path, "profiles.db");
        var store = new SqliteProfileStore(databasePath);
        await store.SaveProfileAsync(new StoredProfile(ProfileId.New(), "Deep work", [], []));
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(client, store);
        await viewModel.RefreshAsync();
        await File.WriteAllTextAsync(databasePath, "invalid SQLite database");

        viewModel.StartFocusCommand.Execute(null);

        Assert.True(SpinWait.SpinUntil(
            () => viewModel.StatusMessage.StartsWith("Activation failed", StringComparison.Ordinal),
            TimeSpan.FromSeconds(2)));
        Assert.Null(client.LastActivation);
        Assert.False(viewModel.IsActive);
        Assert.True(viewModel.StartFocusCommand.CanExecute(null));
    }

    [Fact]
    public async Task StartingATimerAnnouncesTheStopButtonItRevealed()
    {
        // The Stop button binds to CanStopSimpleSession, not to CanStop. Raising only
        // CanStop left the button bound to a value computed while the session was still
        // inactive, so it never appeared and a non-strict session could not be ended.
        using var directory = new TemporaryDirectory();
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(
            client,
            new MemoryProfileStore(),
            settingsStore: new DesktopSettingsStore(Path.Combine(directory.Path, "desktop-settings.json")));
        await viewModel.RefreshAsync();

        var announced = new List<string>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is { } name)
            {
                announced.Add(name);
            }
        };

        viewModel.StartFocusCommand.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => viewModel.IsActive, TimeSpan.FromSeconds(1)));

        Assert.True(viewModel.CanStopSimpleSession);
        Assert.Contains(nameof(FocusViewModel.CanStopSimpleSession), announced);
        Assert.Contains(nameof(FocusViewModel.IsNormalSessionActive), announced);
    }

    [Fact]
    public async Task CycleStartUsesPrepareAndActivatePreparedProtocol()
    {
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(client, new MemoryProfileStore());
        await viewModel.RefreshAsync();
        viewModel.SelectedSessionMode = viewModel.SessionModes[1];

        viewModel.StartFocusCommand.Execute(null);

        Assert.True(SpinWait.SpinUntil(() => client.LastPreparation is not null, TimeSpan.FromSeconds(1)));
        Assert.Equal(SessionMode.FocusCycle, client.LastPreparation!.Mode);
        Assert.NotNull(client.LastPreparation.CycleConfiguration);
        Assert.True(SpinWait.SpinUntil(() => client.PreparedActivationCount == 1, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task EnforcementRequiresExplicitOptIn()
    {
        using var directory = new TemporaryDirectory();
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(
            client,
            new MemoryProfileStore(),
            settingsStore: new DesktopSettingsStore(Path.Combine(directory.Path, "desktop-settings.json")))
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
    public async Task EnforcementOptInSurvivesDesktopRestartAndReachesActivation()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "desktop-settings.json");
        var settingsStore = new DesktopSettingsStore(settingsPath);
        settingsStore.Save(DesktopSettings.Default with { EnforcementEnabled = true });
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(
            client,
            new MemoryProfileStore(),
            settingsStore: settingsStore);
        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsEnforcementEnabled);
        viewModel.StartFocusCommand.Execute(null);
        Assert.True(SpinWait.SpinUntil(
            () => client.LastActivation is not null,
            TimeSpan.FromSeconds(1)));
        Assert.True(client.LastActivation!.EnforcementEnabled);
    }

    [Fact]
    public void EnforcementToggleIsPersistedForTheNextDesktopProcess()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "desktop-settings.json");
        var settingsStore = new DesktopSettingsStore(settingsPath);
        var viewModel = new FocusViewModel(
            new RecordingClient(),
            new MemoryProfileStore(),
            settingsStore: settingsStore);

        viewModel.IsEnforcementEnabled = true;

        Assert.True(new DesktopSettingsStore(settingsPath).Load().EnforcementEnabled);
    }

    [Fact]
    public async Task StrictReviewShowsCodeOnceAndSendsOnlyCredential()
    {
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(client, new MemoryProfileStore())
        {
            IsStrictMode = true
        };
        await viewModel.RefreshAsync();

        viewModel.StartFocusCommand.Execute(null);

        Assert.True(viewModel.IsStrictReviewVisible);
        // Confirmation is the profile's own name, readable from the screen the user
        // came from, rather than a random phrase that only tests transcription.
        Assert.Equal(viewModel.ProfileName, viewModel.RequiredConfirmationText);
        Assert.True(viewModel.IsStrictStepOne);
        // Step one alone cannot activate: the consequences have not been shown yet.
        Assert.False(viewModel.CanConfirmStrict);
        var displayedCode = viewModel.RecoveryCodeDisplay;
        Assert.NotEmpty(displayedCode);
        Assert.Null(client.LastActivation);

        viewModel.AdvanceStrictStep();
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
        var viewModel = new FocusViewModel(client, new MemoryProfileStore())
        {
            IsStrictMode = true
        };
        await viewModel.RefreshAsync();

        viewModel.StartFocusCommand.Execute(null);
        viewModel.AdvanceStrictStep();
        viewModel.TypedConfirmation = viewModel.RequiredConfirmationText;

        viewModel.CancelStrictCommand.Execute(null);

        Assert.False(viewModel.IsStrictReviewVisible);
        Assert.Empty(viewModel.RecoveryCodeDisplay);
        Assert.Empty(viewModel.RequiredConfirmationText);
        Assert.Empty(viewModel.TypedConfirmation);
        Assert.True(viewModel.IsStrictStepOne);
        Assert.False(viewModel.IsStrictLimitsVisible);
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
    public async Task DuplicateProfilePersistsAndSelectsIndependentRuleCopies()
    {
        var store = new MemoryProfileStore();
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();
        var source = viewModel.SelectedProfile!;

        await viewModel.DuplicateSelectedProfileAsync();

        var profiles = await store.ListProfilesAsync();
        Assert.Equal(2, profiles.Count);
        var duplicate = Assert.Single(profiles, profile => profile.Id != source.Id);
        Assert.Same(viewModel.SelectedProfile, viewModel.Profiles.Single(profile => profile.Id == duplicate.Id));
        Assert.Equal("Copy of Deep Work", duplicate.Name);
        Assert.Equal(source.Description, duplicate.Description);
        Assert.Equal(source.AccentHex, duplicate.AccentHex);
        Assert.Equal(source.ApplicationRules.Single().Value, duplicate.ApplicationRules.Single().Value);
        Assert.NotEqual(source.ApplicationRules.Single().Id, duplicate.ApplicationRules.Single().Id);
        Assert.Equal(source.WebsiteRules.Single().Pattern, duplicate.WebsiteRules.Single().Pattern);
        Assert.NotEqual(source.WebsiteRules.Single().Id, duplicate.WebsiteRules.Single().Id);
    }

    [Fact]
    public async Task DuplicateProfileUsesCollisionFreeName()
    {
        var store = new MemoryProfileStore();
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();
        var source = viewModel.SelectedProfile!;

        await viewModel.DuplicateSelectedProfileAsync();
        viewModel.SelectedProfile = source;
        await viewModel.DuplicateSelectedProfileAsync();

        var names = (await store.ListProfilesAsync()).Select(profile => profile.Name).ToArray();
        Assert.Contains("Copy of Deep Work", names);
        Assert.Contains("Copy of Deep Work (2)", names);
    }

    [Fact]
    public async Task DuplicateProfileNameStaysWithinStorageLimit()
    {
        var store = new MemoryProfileStore();
        var source = new StoredProfile(ProfileId.New(), new string('A', 80), [], []);
        await store.SaveProfileAsync(source);
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();
        viewModel.SelectedProfile = source;

        await viewModel.DuplicateSelectedProfileAsync();

        Assert.Equal(80, viewModel.SelectedProfile!.Name.Length);
        Assert.StartsWith("Copy of ", viewModel.SelectedProfile.Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActiveSessionDisablesAndRefusesProfileDuplication()
    {
        var store = new MemoryProfileStore();
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();

        viewModel.StartFocusCommand.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => viewModel.IsActive, TimeSpan.FromSeconds(1)));

        Assert.False(viewModel.DuplicateProfileCommand.CanExecute(null));
        await viewModel.DuplicateSelectedProfileAsync();

        Assert.Single(await store.ListProfilesAsync());
        Assert.Contains("cannot be changed", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RenameProfilePersistsNormalizedNameAndPreservesContents()
    {
        var store = new MemoryProfileStore();
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();
        var original = viewModel.SelectedProfile!;
        viewModel.ProfileNameInput = "  Writing  ";

        await viewModel.RenameSelectedProfileAsync();

        var renamed = Assert.Single(await store.ListProfilesAsync());
        Assert.Equal(original.Id, renamed.Id);
        Assert.Equal("Writing", renamed.Name);
        Assert.Equal(original.ApplicationRules, renamed.ApplicationRules);
        Assert.Equal(original.WebsiteRules, renamed.WebsiteRules);
        Assert.Equal(renamed.Id, viewModel.SelectedProfile!.Id);
        Assert.Equal("Writing", viewModel.ProfileNameInput);
    }

    [Fact]
    public async Task RenameProfileRejectsInvalidUnchangedAndDuplicateNames()
    {
        var store = new MemoryProfileStore();
        var duplicate = new StoredProfile(ProfileId.New(), "Writing", [], []);
        await store.SaveProfileAsync(duplicate);
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();
        var original = viewModel.SelectedProfile!;

        viewModel.ProfileNameInput = " ";
        Assert.False(viewModel.RenameProfileCommand.CanExecute(null));
        Assert.NotEmpty(viewModel.ProfileNameValidationMessage);

        viewModel.ProfileNameInput = new string('A', 81);
        Assert.False(viewModel.RenameProfileCommand.CanExecute(null));

        viewModel.ProfileNameInput = original.Name;
        Assert.False(viewModel.RenameProfileCommand.CanExecute(null));

        viewModel.ProfileNameInput = "writing";
        Assert.False(viewModel.RenameProfileCommand.CanExecute(null));
        Assert.Contains("already exists", viewModel.ProfileNameValidationMessage);

        await viewModel.RenameSelectedProfileAsync();
        Assert.Contains((await store.ListProfilesAsync()), profile => profile.Name == original.Name);
    }

    [Fact]
    public async Task ActiveSessionDisablesAndRefusesProfileRename()
    {
        var store = new MemoryProfileStore();
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();
        viewModel.ProfileNameInput = "Writing";

        viewModel.StartFocusCommand.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => viewModel.IsActive, TimeSpan.FromSeconds(1)));

        Assert.False(viewModel.RenameProfileCommand.CanExecute(null));
        await viewModel.RenameSelectedProfileAsync();

        Assert.Equal("Deep Work", (await store.ListProfilesAsync()).Single().Name);
        Assert.Contains("cannot be changed", viewModel.StatusMessage);
    }

    [Fact]
    public async Task DeleteProfileRequiresVisibleConfirmationAndCanBeCancelled()
    {
        var store = new MemoryProfileStore();
        await store.SaveProfileAsync(new StoredProfile(ProfileId.New(), "Writing", [], []));
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();

        await viewModel.RequestProfileDeleteAsync();

        Assert.True(viewModel.IsDeleteProfileConfirmationVisible);
        Assert.Contains("Deep Work", viewModel.DeleteProfileConfirmationText);
        Assert.Contains("1 application", viewModel.DeleteProfileConfirmationText);
        Assert.Equal(2, (await store.ListProfilesAsync()).Count);

        await viewModel.CancelProfileDeleteAsync();

        Assert.False(viewModel.IsDeleteProfileConfirmationVisible);
        Assert.Equal(2, (await store.ListProfilesAsync()).Count);
    }

    [Fact]
    public async Task ConfirmProfileDeleteRemovesOnlySelectionAndSelectsFallback()
    {
        var store = new MemoryProfileStore();
        var fallback = new StoredProfile(ProfileId.New(), "Writing", [], []);
        await store.SaveProfileAsync(fallback);
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();
        var deletedId = viewModel.SelectedProfile!.Id;

        await viewModel.RequestProfileDeleteAsync();
        await viewModel.ConfirmProfileDeleteAsync();

        var remaining = Assert.Single(await store.ListProfilesAsync());
        Assert.Equal(fallback.Id, remaining.Id);
        Assert.DoesNotContain(viewModel.Profiles, profile => profile.Id == deletedId);
        Assert.Equal(fallback.Id, viewModel.SelectedProfile!.Id);
        Assert.False(viewModel.IsDeleteProfileConfirmationVisible);
    }

    [Fact]
    public async Task LastProfileCannotBeDeleted()
    {
        var store = new MemoryProfileStore();
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();

        Assert.False(viewModel.DeleteProfileCommand.CanExecute(null));
        await viewModel.RequestProfileDeleteAsync();

        Assert.False(viewModel.IsDeleteProfileConfirmationVisible);
        Assert.Single(await store.ListProfilesAsync());
        Assert.Contains("at least one profile", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ActiveSessionDisablesAndRefusesProfileDelete()
    {
        var store = new MemoryProfileStore();
        await store.SaveProfileAsync(new StoredProfile(ProfileId.New(), "Writing", [], []));
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();

        viewModel.StartFocusCommand.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => viewModel.IsActive, TimeSpan.FromSeconds(1)));

        Assert.False(viewModel.DeleteProfileCommand.CanExecute(null));
        await viewModel.RequestProfileDeleteAsync();

        Assert.False(viewModel.IsDeleteProfileConfirmationVisible);
        Assert.Equal(2, (await store.ListProfilesAsync()).Count);
        Assert.Contains("cannot be changed", viewModel.StatusMessage);
    }

    [Fact]
    public async Task NewProfileSuggestionsStartExplicitAndExposeExactDomains()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());
        await viewModel.RefreshAsync();

        await viewModel.BeginCreateProfileAsync();

        Assert.True(viewModel.IsCreatingProfile);
        Assert.All(viewModel.NewProfileSuggestions, suggestion => Assert.False(suggestion.IsSelected));
        var twitter = Assert.Single(
            viewModel.NewProfileSuggestions,
            suggestion => suggestion.Label == "X / Twitter");
        Assert.Equal("x.com · twitter.com", twitter.DomainSummary);
    }

    [Fact]
    public async Task GuidedProfileCreationPersistsSelectedAndCustomRules()
    {
        var store = new MemoryProfileStore();
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();
        await viewModel.BeginCreateProfileAsync();
        viewModel.NewProfileNameInput = "  Social break  ";
        Assert.Single(viewModel.NewProfileSuggestions, suggestion => suggestion.Label == "X / Twitter")
            .IsSelected = true;
        viewModel.NewProfileCustomRuleInput = "discord.exe";
        await viewModel.AddNewProfileCustomRuleAsync();

        await viewModel.CreateProfileAsync();

        var profiles = await store.ListProfilesAsync();
        Assert.Equal(2, profiles.Count);
        var created = Assert.Single(profiles, profile => profile.Name == "Social break");
        Assert.Equal(created.Id, viewModel.SelectedProfile!.Id);
        Assert.Equal(["twitter.com", "x.com"], created.WebsiteRules.Select(rule => rule.Pattern).Order().ToArray());
        Assert.Equal("discord.exe", Assert.Single(created.ApplicationRules).Value);
        Assert.False(viewModel.IsCreatingProfile);
    }

    [Fact]
    public async Task GuidedProfileCreationAllowsEmptyProfile()
    {
        var store = new MemoryProfileStore();
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();
        await viewModel.BeginCreateProfileAsync();
        viewModel.NewProfileNameInput = "Writing";

        await viewModel.CreateProfileAsync();

        var created = Assert.Single(
            await store.ListProfilesAsync(),
            profile => profile.Name == "Writing");
        Assert.Empty(created.ApplicationRules);
        Assert.Empty(created.WebsiteRules);
    }

    [Fact]
    public async Task GuidedProfileCreationRejectsInvalidAndDuplicateCustomRules()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());
        await viewModel.RefreshAsync();
        await viewModel.BeginCreateProfileAsync();

        viewModel.NewProfileCustomRuleInput = "reddit.com";
        await viewModel.AddNewProfileCustomRuleAsync();
        viewModel.NewProfileCustomRuleInput = "REDDIT.com";
        await viewModel.AddNewProfileCustomRuleAsync();

        Assert.Single(viewModel.NewProfileDraftRules);
        Assert.Contains("already added", viewModel.NewProfileRuleValidationMessage);

        viewModel.NewProfileCustomRuleInput = "reddit.com?private=1";
        await viewModel.AddNewProfileCustomRuleAsync();
        Assert.Single(viewModel.NewProfileDraftRules);
        Assert.NotEmpty(viewModel.NewProfileRuleValidationMessage);
    }

    [Fact]
    public async Task GuidedProfileCreationCancelAndActiveSessionDoNotPersist()
    {
        var store = new MemoryProfileStore();
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();
        await viewModel.BeginCreateProfileAsync();
        viewModel.NewProfileNameInput = "Temporary";
        await viewModel.CancelCreateProfileAsync();

        Assert.False(viewModel.IsCreatingProfile);
        Assert.Single(await store.ListProfilesAsync());

        viewModel.StartFocusCommand.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => viewModel.IsActive, TimeSpan.FromSeconds(1)));
        Assert.False(viewModel.NewProfileCommand.CanExecute(null));
        await viewModel.BeginCreateProfileAsync();

        Assert.False(viewModel.IsCreatingProfile);
        Assert.Single(await store.ListProfilesAsync());
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

    // Four states, not a bool: "ready", "blocking now", "one channel down" and "nothing
    // is enforced" call for different words and different colours. A failure has to name
    // the component that failed, because the two channels fail for different reasons and
    // an anonymous "degraded" gives the user nothing to act on.
    [Fact]
    public async Task ProtectionReportsReadinessBeforeASessionStarts()
    {
        var client = new RecordingClient
        {
            Status = new StatusResponse(
                ServiceReady: true,
                ExtensionReady: true,
                SessionState: "Inactive",
                RemainingSeconds: 0,
                SessionId: null,
                ProfileId: null,
                StartUtc: null,
                EndUtc: null,
                StrictMode: false,
                EnforcementConnected: true)
        };
        var viewModel = new FocusViewModel(client, new MemoryProfileStore());
        await viewModel.RefreshAsync();

        // Ready, not Protected: nothing is being blocked yet, and saying otherwise before
        // a session starts would be a promise the app is not currently keeping.
        Assert.Equal(ProtectionState.Ready, viewModel.ProtectionState);
        Assert.False(viewModel.IsProtectionActionable);
        Assert.False(viewModel.HasProtectionDetail);
    }

    [Fact]
    public async Task ProtectionNamesTheExtensionWhenOnlyTheExtensionIsDown()
    {
        var client = new RecordingClient
        {
            Status = new StatusResponse(
                ServiceReady: true,
                ExtensionReady: false,
                SessionState: "Active",
                RemainingSeconds: 600,
                SessionId: Guid.NewGuid(),
                ProfileId: null,
                StartUtc: DateTimeOffset.UtcNow,
                EndUtc: DateTimeOffset.UtcNow.AddMinutes(10),
                StrictMode: false,
                EnforcementConnected: false)
        };
        var viewModel = new FocusViewModel(client, new MemoryProfileStore());
        await viewModel.RefreshAsync();

        Assert.Equal(ProtectionState.Degraded, viewModel.ProtectionState);
        Assert.Equal(Resources.UiStrings.ProtectionExtensionDown, viewModel.ProtectionStatusText);
        // Degraded still protects something, so the detail says what survives.
        Assert.True(viewModel.HasProtectionDetail);
        Assert.True(viewModel.IsProtectionActionable);
        Assert.False(viewModel.IsProtectionUnavailable);
    }

    [Fact]
    public async Task ProtectionIsUnavailableWhenTheServiceItselfIsSilent()
    {
        var client = new RecordingClient
        {
            Status = new StatusResponse(
                ServiceReady: false,
                ExtensionReady: false,
                SessionState: "Inactive",
                RemainingSeconds: 0,
                SessionId: null,
                ProfileId: null,
                StartUtc: null,
                EndUtc: null,
                StrictMode: false,
                EnforcementConnected: false)
        };
        var viewModel = new FocusViewModel(client, new MemoryProfileStore());
        await viewModel.RefreshAsync();

        Assert.Equal(ProtectionState.Unavailable, viewModel.ProtectionState);
        Assert.True(viewModel.IsProtectionUnavailable);
        Assert.True(viewModel.IsProtectionActionable);
        Assert.Equal(Resources.UiStrings.Get("SessionStateUnknown"), viewModel.ProtectionStatusText);
        Assert.False(viewModel.CanStart);
    }

    // A break swaps the whole rule set for the exception list, so an empty list releases
    // everything. An exception only means something as a Block rule: stored as Allow it
    // permitted what the break already permitted and silently did nothing.
    [Fact]
    public async Task BreakExceptionsArePersistedAsBlockRules()
    {
        var store = new MemoryProfileStore();
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();

        viewModel.BreakWebsiteInput = "x.com";
        await viewModel.AddBreakWebsiteAsync();

        var profileId = viewModel.SelectedProfile!.Id;
        var saved = await store.LoadProfileCycleSettingsAsync(profileId);
        var rule = Assert.Single(saved!.WebsiteRules);
        Assert.Equal(RuleAction.Block, rule.Action);
        Assert.Equal("x.com", rule.Pattern);
    }

    // The exceptions belong to the profile, not to the Focus screen's current mode, so
    // editing them must not require switching that screen to Cycles first.
    [Fact]
    public async Task BreakExceptionsAreEditableWhileTheTimerModeIsShown()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());
        await viewModel.RefreshAsync();

        Assert.False(viewModel.IsCycleMode);
        Assert.True(viewModel.CanEditBreakExceptions);
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

        // A profile saved before the Catppuccin palette carries a colour that has no
        // light-theme counterpart, so it is snapped to the nearest accent that does —
        // here Mocha green — rather than being painted literally.
        viewModel.SelectProfile(new StoredProfile(
            ProfileId.New(),
            "Estudio",
            [],
            [],
            AccentHex: "#3E9C7A"));

        Assert.NotNull(fallback);
        var accent = Assert.IsType<SolidColorBrush>(viewModel.ProfileAccentBrush);
        Assert.Equal(Color.Parse("#A6E3A1"), accent.Color);
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

    [Fact]
    public async Task IdenticalInactiveStatusDoesNotRenotifyTheVisualTree()
    {
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(client, new MemoryProfileStore());
        await viewModel.RefreshAsync();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await viewModel.RefreshAsync();

        Assert.Empty(changed);
    }

    [Fact]
    public async Task ActiveCountdownRenotifiesOnlyTimerDependentPresentation()
    {
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(client, new MemoryProfileStore());
        await viewModel.RefreshAsync();
        var start = DateTimeOffset.UtcNow;
        client.Status = new StatusResponse(
            true,
            true,
            "Active",
            60,
            Guid.NewGuid(),
            viewModel.SelectedProfile!.Id.Value,
            start,
            start.AddMinutes(1),
            StrictMode: false,
            EnforcementConnected: true);
        await viewModel.RefreshAsync();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        client.Status = client.Status with { RemainingSeconds = 59 };

        await viewModel.RefreshAsync();

        Assert.Contains(nameof(FocusViewModel.RemainingText), changed);
        Assert.Contains(nameof(FocusViewModel.RingSweepAngle), changed);
        Assert.Contains(nameof(FocusViewModel.MetricSegments), changed);
        Assert.DoesNotContain(nameof(FocusViewModel.SessionTitle), changed);
        Assert.DoesNotContain(nameof(FocusViewModel.ModeLabel), changed);
        Assert.DoesNotContain(nameof(FocusViewModel.CanStart), changed);
    }

    [Fact]
    public async Task ActiveBlockingStatusRequiresServiceConfirmation()
    {
        using var directory = new TemporaryDirectory();
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(
            client,
            new MemoryProfileStore(),
            settingsStore: new DesktopSettingsStore(Path.Combine(directory.Path, "desktop-settings.json")));
        await viewModel.RefreshAsync();
        var start = DateTimeOffset.UtcNow;
        client.Status = new StatusResponse(
            true,
            false,
            "Active",
            60,
            Guid.NewGuid(),
            viewModel.SelectedProfile!.Id.Value,
            start,
            start.AddMinutes(1),
            StrictMode: false,
            EnforcementConnected: false);

        await viewModel.RefreshAsync();

        Assert.False(viewModel.IsEnforcementConnected);
        Assert.False(viewModel.IsEnforcementDegraded);
        Assert.True(viewModel.IsTimerOnly);
        Assert.Contains("Enforcement is off", viewModel.ActiveBlockingDescription);
        Assert.Contains("Enforcement is off", viewModel.StatusMessage);

        viewModel.IsEnforcementEnabled = true;
        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsEnforcementDegraded);
        Assert.False(viewModel.IsTimerOnly);
        Assert.Contains("Degraded", viewModel.StatusMessage);

        client.Status = client.Status with { EnforcementConnected = true };
        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsEnforcementConnected);
        Assert.False(viewModel.IsEnforcementDegraded);
        Assert.False(viewModel.IsTimerOnly);
        Assert.Contains("blocked", viewModel.ActiveBlockingDescription);
        Assert.Contains("Connected", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ActiveProfileWithoutRulesIsNotPresentedAsDegradedOrProtected()
    {
        using var directory = new TemporaryDirectory();
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(
            client,
            new MemoryProfileStore(),
            settingsStore: new DesktopSettingsStore(Path.Combine(directory.Path, "desktop-settings.json")));
        await viewModel.RefreshAsync();
        viewModel.SelectProfile(new StoredProfile(ProfileId.New(), "Empty", [], []));
        viewModel.IsEnforcementEnabled = true;
        var start = DateTimeOffset.UtcNow;
        client.Status = new StatusResponse(
            true,
            true,
            "Active",
            60,
            Guid.NewGuid(),
            viewModel.SelectedProfile!.Id.Value,
            start,
            start.AddMinutes(1),
            StrictMode: false,
            EnforcementConnected: false);

        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsNoRulesConfigured);
        Assert.False(viewModel.IsEnforcementConnected);
        Assert.False(viewModel.IsEnforcementDegraded);
        Assert.False(viewModel.IsTimerOnly);
        Assert.Contains("no blocked apps or websites", viewModel.ActiveBlockingDescription);
        Assert.Equal(viewModel.ActiveBlockingDescription, viewModel.StatusMessage);
    }

    [Fact]
    public async Task FirstConnectedActiveStatusRefreshesBlockingIndicators()
    {
        using var directory = new TemporaryDirectory();
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(
            client,
            new MemoryProfileStore(),
            settingsStore: new DesktopSettingsStore(Path.Combine(directory.Path, "desktop-settings.json")));
        await viewModel.RefreshAsync();
        viewModel.IsEnforcementEnabled = true;
        var changed = new List<string>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName!);
        var start = DateTimeOffset.UtcNow;
        client.Status = new StatusResponse(
            true,
            true,
            "Active",
            60,
            Guid.NewGuid(),
            viewModel.SelectedProfile!.Id.Value,
            start,
            start.AddMinutes(1),
            StrictMode: false,
            EnforcementConnected: true);

        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsEnforcementConnected);
        Assert.False(viewModel.IsEnforcementDegraded);
        Assert.False(viewModel.IsTimerOnly);
        Assert.Contains(nameof(FocusViewModel.IsEnforcementConnected), changed);
        Assert.Contains(nameof(FocusViewModel.IsEnforcementDegraded), changed);
        Assert.Contains(nameof(FocusViewModel.IsTimerOnly), changed);
    }

    [Fact]
    public async Task StatusTransportFailureRetainsActiveSessionWithoutEndingOrAllowingMutation()
    {
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(client, new MemoryProfileStore());
        await viewModel.RefreshAsync();
        viewModel.IsEnforcementEnabled = true;
        var start = DateTimeOffset.UtcNow;
        client.Status = new(true, true, "Active", 60, Guid.NewGuid(), viewModel.SelectedProfile!.Id.Value,
            start, start.AddMinutes(1), false, true);
        await viewModel.RefreshAsync();
        var remaining = viewModel.RemainingText;
        var end = viewModel.SessionEndUtc;
        var ended = 0;
        viewModel.SessionEnded += (_, _) => ended++;
        client.StatusFailure = new IOException("Status unavailable.");

        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsActive);
        Assert.Equal(remaining, viewModel.RemainingText);
        Assert.Equal(end, viewModel.SessionEndUtc);
        Assert.Equal(0, ended);
        Assert.False(viewModel.CanStart);
        Assert.False(viewModel.CanStop);
        Assert.False(viewModel.IsEnforcementConnected);
        Assert.True(viewModel.IsProtectionUnavailable);
        viewModel.StopFocusCommand.Execute(null);
        Assert.Equal(0, client.StopCount);
    }

    [Fact]
    public async Task InitialAndIdleDisconnectDisableStartUntilFreshStatus()
    {
        var client = new RecordingClient { StatusFailure = new TimeoutException("Missing service.") };
        var viewModel = new FocusViewModel(client, new MemoryProfileStore());
        Assert.False(viewModel.CanStart);
        await viewModel.RefreshAsync();
        Assert.False(viewModel.HasConfirmedSessionState);
        Assert.False(viewModel.CanStart);
        await viewModel.StartAnotherSessionAsync(25);
        Assert.Null(client.LastActivation);
        client.StatusFailure = null;
        await viewModel.RefreshAsync();
        Assert.True(viewModel.IsSessionStateFresh);
        Assert.True(viewModel.CanStart);
        client.StatusFailure = new IOException("Disconnected after idle.");
        await viewModel.RefreshAsync();
        Assert.True(viewModel.IsSessionStateStale);
        Assert.False(viewModel.CanStart);
        Assert.False(viewModel.CanEditProfileRules);
    }

    [Fact]
    public async Task OpenProfileCreationCannotPersistWhileSessionStateIsUnknown()
    {
        var client = new RecordingClient();
        var store = new MemoryProfileStore();
        var viewModel = new FocusViewModel(client, store);
        await viewModel.RefreshAsync();
        await viewModel.BeginCreateProfileAsync();
        viewModel.NewProfileNameInput = "Disconnected draft";
        Assert.True(viewModel.CanCreateProfile);
        var count = (await store.ListProfilesAsync()).Count;
        client.StatusFailure = new IOException("Lost status.");
        await viewModel.RefreshAsync();
        Assert.False(viewModel.CanCreateProfile);
        await viewModel.CreateProfileAsync();
        Assert.Equal(count, (await store.ListProfilesAsync()).Count);
    }

    [Fact]
    public async Task ReconnectToSameSessionRestoresActionsWithoutReplayingClosure()
    {
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(client, new MemoryProfileStore());
        await viewModel.RefreshAsync();
        var now = DateTimeOffset.UtcNow;
        client.Status = new(true, true, "Active", 60, Guid.NewGuid(), viewModel.SelectedProfile!.Id.Value,
            now, now.AddMinutes(1), false, true);
        await viewModel.RefreshAsync();
        var ended = 0;
        viewModel.SessionEnded += (_, _) => ended++;
        client.StatusFailure = new TimeoutException("Lost status.");
        await viewModel.RefreshAsync();
        client.StatusFailure = null;
        client.Status = client.Status with { RemainingSeconds = 30 };
        await viewModel.RefreshAsync();
        Assert.True(viewModel.IsSessionStateFresh);
        Assert.True(viewModel.IsActive);
        Assert.True(viewModel.CanStop);
        Assert.Equal("00:30", viewModel.RemainingText);
        Assert.Equal(0, ended);
    }

    [Fact]
    public async Task OnlyFreshTerminalReconnectRaisesClosureOnce()
    {
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(client, new MemoryProfileStore());
        await viewModel.RefreshAsync();
        var now = DateTimeOffset.UtcNow;
        client.Status = new(true, true, "Active", 60, Guid.NewGuid(), viewModel.SelectedProfile!.Id.Value,
            now, now.AddMinutes(1), true, true);
        await viewModel.RefreshAsync();
        var ended = 0;
        viewModel.SessionEnded += (_, _) => ended++;
        client.StatusFailure = new IOException("Lost status.");
        await viewModel.RefreshAsync();
        await viewModel.RefreshAsync();
        Assert.True(viewModel.IsActive);
        Assert.Equal(0, ended);
        client.StatusFailure = null;
        client.Status = client.Status with { SessionState = "Expired", RemainingSeconds = 0, EnforcementConnected = false };
        await viewModel.RefreshAsync();
        await viewModel.RefreshAsync();
        Assert.False(viewModel.IsActive);
        Assert.True(viewModel.CanStart);
        Assert.Equal(1, ended);
    }

    [Fact]
    public async Task StrictReviewCannotConfirmWhileConnectionIsStaleAndCancellationStillWorks()
    {
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(client, new MemoryProfileStore()) { IsStrictMode = true };
        await viewModel.RefreshAsync();
        viewModel.StartFocusCommand.Execute(null);
        viewModel.AdvanceStrictStep();
        viewModel.TypedConfirmation = viewModel.RequiredConfirmationText;
        Assert.True(viewModel.CanConfirmStrict);
        client.StatusFailure = new IOException("Lost status during review.");
        await viewModel.RefreshAsync();
        Assert.False(viewModel.CanConfirmStrict);
        viewModel.ConfirmStrictCommand.Execute(null);
        Assert.Null(client.LastActivation);
        viewModel.CancelStrictCommand.Execute(null);
        Assert.False(viewModel.IsStrictReviewVisible);
        Assert.Empty(viewModel.RecoveryCodeDisplay);
    }

    [Fact]
    public async Task CycleBreakStaysLastKnownWithoutConfirmingReleaseOrAllowingSkip()
    {
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(client, new MemoryProfileStore());
        await viewModel.RefreshAsync();
        var now = DateTimeOffset.UtcNow;
        client.Status = new(true, true, "Active", 180, Guid.NewGuid(), viewModel.SelectedProfile!.Id.Value,
            now, now.AddMinutes(3), false, true, SessionMode.FocusCycle,
            new(1, SessionPhaseKind.ShortBreak, 1, 60, now.AddMinutes(1)));
        await viewModel.RefreshAsync();
        Assert.True(viewModel.CanSkipBreak);
        client.StatusFailure = new IOException("Lost cycle state.");
        await viewModel.RefreshAsync();
        Assert.True(viewModel.IsBreakPhase);
        Assert.False(viewModel.CanSkipBreak);
        Assert.Equal(Resources.UiStrings.Get("SessionStateStale"), viewModel.ActiveBlockingDescription);
    }

    [Fact]
    public async Task LostActivationResponseRequiresStatusReconciliationBeforeTryingAgain()
    {
        var client = new RecordingClient { ActivationFailure = new TimeoutException("Response lost.") };
        var viewModel = new FocusViewModel(client, new MemoryProfileStore());
        await viewModel.RefreshAsync();
        viewModel.StartFocusCommand.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => viewModel.IsSessionStateStale, TimeSpan.FromSeconds(2)));
        Assert.False(viewModel.CanStart);
        Assert.Equal(Resources.UiStrings.Get("SessionActionUnconfirmed"), viewModel.StatusMessage);
        await viewModel.RefreshAsync();
        Assert.True(viewModel.IsSessionStateFresh);
        Assert.True(viewModel.IsActive);
        Assert.False(viewModel.CanStart);
    }

    [Fact]
    public async Task CallerCancellationDoesNotInvalidateFreshStatus()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());
        await viewModel.RefreshAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await viewModel.RefreshAsync(cancellation.Token);
        Assert.True(viewModel.IsSessionStateFresh);
        Assert.True(viewModel.CanStart);
    }

    [Fact]
    public async Task ActivityMetricsFailureDoesNotReplaceAuthoritativeActiveStatus()
    {
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(client, new MemoryProfileStore());
        await viewModel.RefreshAsync();
        var start = DateTimeOffset.UtcNow;
        client.Status = new StatusResponse(
            true,
            true,
            "Active",
            60,
            Guid.NewGuid(),
            viewModel.SelectedProfile!.Id.Value,
            start,
            start.AddMinutes(1),
            StrictMode: false,
            EnforcementConnected: true);
        client.ActivityFailure = new TimeoutException("Activity metrics timed out.");

        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsActive);
        Assert.Equal("01:00", viewModel.RemainingText);
        Assert.True(viewModel.IsSessionStateFresh);
    }

    [Fact]
    public async Task ReduceMotionDisablesActiveAmbientMotion()
    {
        var settingsPath = Path.GetTempFileName();
        try
        {
            var client = new RecordingClient();
            var viewModel = new FocusViewModel(
                client,
                new MemoryProfileStore(),
                settingsStore: new DesktopSettingsStore(settingsPath));
            await viewModel.RefreshAsync();
            var start = DateTimeOffset.UtcNow;
            client.Status = new StatusResponse(
                true,
                false,
                "Active",
                60,
                Guid.NewGuid(),
                viewModel.SelectedProfile!.Id.Value,
                start,
                start.AddMinutes(1),
                StrictMode: false,
                EnforcementConnected: false);
            await viewModel.RefreshAsync();

            Assert.True(viewModel.IsMotionActive);

            viewModel.IsReduceMotionEnabled = true;

            Assert.False(viewModel.IsMotionActive);
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }

    [Fact]
    public async Task ActiveCycleStatusShowsAuthoritativePhaseAndNextTransition()
    {
        var client = new RecordingClient();
        var viewModel = new FocusViewModel(client, new MemoryProfileStore());
        await viewModel.RefreshAsync();
        var start = DateTimeOffset.UtcNow;
        client.Status = new StatusResponse(
            true,
            true,
            "Active",
            240,
            Guid.NewGuid(),
            viewModel.SelectedProfile!.Id.Value,
            start,
            start.AddMinutes(35),
            StrictMode: false,
            EnforcementConnected: true,
            Mode: SessionMode.FocusCycle,
            CurrentPhase: new PhaseStatus(
                1,
                SessionPhaseKind.ShortBreak,
                1,
                240,
                start.AddMinutes(30)),
            NextPhase: new PhaseStatus(
                2,
                SessionPhaseKind.Focus,
                2,
                1500,
                start.AddMinutes(55)),
            FinalEndUtc: start.AddMinutes(35));

        await viewModel.RefreshAsync();

        Assert.True(viewModel.IsCycleSession);
        Assert.NotNull(viewModel.CurrentPhaseStatus);
        Assert.Equal(SessionPhaseKind.ShortBreak, viewModel.CurrentPhaseStatus!.Kind);
        Assert.True(viewModel.IsBreakPhase);
        Assert.True(viewModel.CanSkipBreak);
        Assert.Contains("Short break", viewModel.CurrentPhaseLabel);
        Assert.Equal("Cycle 1 of 4", viewModel.CycleProgressText);
        Assert.Equal("Short break", viewModel.ActiveCycleLabel);
        Assert.Contains("Next: Focus", viewModel.NextPhaseText);
    }

    [Fact]
    public async Task CycleSelectorLeadsWithBuiltInRhythmsThenTheOwnersOwnPrograms()
    {
        var store = new MemoryProfileStore();
        store.AddPreset(new StoredCyclePreset(
            Guid.NewGuid(),
            "Zetas",
            BuiltInCyclePresets.All[0].Configuration,
            IsBuiltIn: false));
        store.AddPreset(new StoredCyclePreset(
            Guid.NewGuid(),
            "Alfas",
            BuiltInCyclePresets.All[0].Configuration,
            IsBuiltIn: false));
        var viewModel = new FocusViewModel(new RecordingClient(), store);

        await viewModel.RefreshAsync();

        Assert.Equal(5, viewModel.VisibleCyclePresets.Count);
        Assert.Equal(BuiltInCyclePresets.DeepFocusId, viewModel.VisibleCyclePresets[0].Id);
        Assert.Equal(BuiltInCyclePresets.ExtendedFlowId, viewModel.VisibleCyclePresets[1].Id);
        Assert.Equal(BuiltInCyclePresets.ClassicId, viewModel.VisibleCyclePresets[2].Id);
        Assert.Equal("Alfas", viewModel.VisibleCyclePresets[3].Name);
        Assert.Equal("Zetas", viewModel.VisibleCyclePresets[4].Name);
    }

    [Fact]
    public async Task CreatingACustomCycleSeedsFromTheSelectionAndLandsOnTheShelf()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());
        await viewModel.RefreshAsync();
        viewModel.SelectedSessionMode = viewModel.SessionModes[1];

        await viewModel.BeginCreateCyclePresetAsync();

        Assert.True(viewModel.IsCyclePresetEditorVisible);
        Assert.Equal(25, viewModel.CycleFocusMinutes);
        Assert.Equal("My cycle", viewModel.CyclePresetNameInput);

        viewModel.CyclePresetNameInput = "Escritura";
        viewModel.CycleFocusMinutes = 45;
        viewModel.CycleShortBreakMinutes = 8;
        await viewModel.CommitCyclePresetAsync();

        Assert.False(viewModel.IsCyclePresetEditorVisible);
        var created = Assert.Single(viewModel.VisibleCyclePresets, preset => !preset.IsBuiltIn);
        Assert.Equal("Escritura", created.Name);
        Assert.Equal(TimeSpan.FromMinutes(45), created.Configuration.FocusDuration);
        Assert.Equal(created.Id, viewModel.SelectedCyclePreset!.Id);
        Assert.Contains("45 min", viewModel.CycleCompositionText);
    }

    [Fact]
    public async Task CancellingTheCycleEditorLeavesTheShelfUntouched()
    {
        var viewModel = new FocusViewModel(new RecordingClient(), new MemoryProfileStore());
        await viewModel.RefreshAsync();
        viewModel.SelectedSessionMode = viewModel.SessionModes[1];

        await viewModel.BeginCreateCyclePresetAsync();
        viewModel.CycleFocusMinutes = 90;
        await viewModel.CancelCyclePresetEditAsync();

        Assert.False(viewModel.IsCyclePresetEditorVisible);
        Assert.All(viewModel.VisibleCyclePresets, preset => Assert.True(preset.IsBuiltIn));
        // The editor returns to describing the selected program rather than the abandoned draft.
        Assert.Equal(25, viewModel.CycleFocusMinutes);
    }

    [Fact]
    public async Task EditingACustomCycleRewritesItInPlace()
    {
        var store = new MemoryProfileStore();
        var original = new StoredCyclePreset(
            Guid.NewGuid(),
            "Escritura",
            BuiltInCyclePresets.All[0].Configuration,
            IsBuiltIn: false);
        store.AddPreset(original);
        store.SetCycleSettings(new ProfileCycleSettings(store.PrimaryProfileId, original.Id, [], []));
        var viewModel = new FocusViewModel(new RecordingClient(), store);
        await viewModel.RefreshAsync();
        viewModel.SelectedSessionMode = viewModel.SessionModes[1];

        await viewModel.BeginEditCyclePresetAsync();
        viewModel.CycleFocusMinutes = 35;
        await viewModel.CommitCyclePresetAsync();

        var updated = Assert.Single(viewModel.VisibleCyclePresets, preset => !preset.IsBuiltIn);
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(TimeSpan.FromMinutes(35), updated.Configuration.FocusDuration);
    }

    private sealed class RecordingStartupRegistration(bool isEnabled, bool succeeds = true)
        : IStartupRegistration
    {
        public bool IsEnabled { get; private set; } = isEnabled;

        public List<bool> Requests { get; } = [];

        public bool TrySetEnabled(bool enabled)
        {
            Requests.Add(enabled);
            if (succeeds)
            {
                IsEnabled = enabled;
            }

            return succeeds;
        }
    }

    private sealed class MemoryProfileStore : IRevisionedCyclePresetStore
    {
        private readonly List<StoredProfile> _profiles =
        [
            new(
                new ProfileId(Guid.Parse("84c70293-0738-4dde-a0d2-2cd585f054a6")),
                "Deep Work",
                [new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "chat.exe")],
                [new WebsiteRule(Guid.NewGuid(), RuleAction.Block, "example.com")])
        ];

        private readonly List<StoredCyclePreset> _presets = [.. BuiltInCyclePresets.All];
        private readonly Dictionary<ProfileId, ProfileCycleSettings> _cycleSettings = [];

        public ProfileId PrimaryProfileId => _profiles[0].Id;

        public void AddPreset(StoredCyclePreset preset) => _presets.Add(preset);

        public void SetCycleSettings(ProfileCycleSettings settings) => _cycleSettings[settings.ProfileId] = settings;

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

        public Task SaveCyclePresetAsync(
            StoredCyclePreset preset,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _presets.RemoveAll(existing => existing.Id == preset.Id);
            _presets.Add(preset);
            return Task.CompletedTask;
        }

        public Task<CyclePresetMutationResult> TrySaveCyclePresetAsync(
            StoredCyclePreset preset,
            string? expectedRevision,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (preset.Id == Guid.Empty || string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Trim().Length > 80)
                return Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.InvalidInput));
            if (preset.IsBuiltIn)
                return Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.BuiltIn));

            var current = _presets.FirstOrDefault(candidate => candidate.Id == preset.Id);
            if (expectedRevision is null && current is not null)
                return Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.Conflict));
            if (expectedRevision is not null && current is null)
                return Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.Missing));
            if (current is not null && CyclePresetRevision.Compute(current) != expectedRevision)
                return Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.Conflict));

            var canonical = preset with { Name = preset.Name.Trim() };
            if (_presets.Any(candidate => candidate.Id != preset.Id
                && string.Equals(candidate.Name.Trim(), canonical.Name, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.NameConflict));

            _presets.RemoveAll(candidate => candidate.Id == preset.Id);
            _presets.Add(canonical);
            return Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.Saved, canonical));
        }

        public Task<CyclePresetMutationResult> TryDeleteCyclePresetAsync(
            Guid id,
            string expectedRevision,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (id == Guid.Empty || string.IsNullOrWhiteSpace(expectedRevision))
                return Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.InvalidInput));
            var current = _presets.FirstOrDefault(candidate => candidate.Id == id);
            if (current is null) return Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.Missing));
            if (current.IsBuiltIn) return Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.BuiltIn));
            if (CyclePresetRevision.Compute(current) != expectedRevision)
                return Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.Conflict));
            if (_cycleSettings.Values.Any(settings => settings.DefaultPresetId == id))
                return Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.Referenced));
            _presets.Remove(current);
            return Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.Saved));
        }

        public Task<CyclePresetMutationResult> TryDuplicateCyclePresetAsync(
            Guid id,
            string expectedRevision,
            string name,
            CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (id == Guid.Empty || string.IsNullOrWhiteSpace(expectedRevision)
                || string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
                return Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.InvalidInput));
            var current = _presets.FirstOrDefault(candidate => candidate.Id == id);
            if (current is null) return Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.Missing));
            if (CyclePresetRevision.Compute(current) != expectedRevision)
                return Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.Conflict));
            var duplicateName = name.Trim();
            if (_presets.Any(candidate => string.Equals(candidate.Name.Trim(), duplicateName, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.NameConflict));
            var duplicate = current with { Id = Guid.NewGuid(), Name = duplicateName, IsBuiltIn = false };
            _presets.Add(duplicate);
            return Task.FromResult(new CyclePresetMutationResult(CyclePresetMutationStatus.Saved, duplicate));
        }

        public Task<StoredCyclePreset?> LoadCyclePresetAsync(
            Guid presetId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_presets.FirstOrDefault(preset => preset.Id == presetId));
        }

        public Task<IReadOnlyList<StoredCyclePreset>> ListCyclePresetsAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<StoredCyclePreset>>(_presets.ToArray());
        }

        public Task<bool> DeleteCyclePresetAsync(
            Guid presetId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var preset = _presets.FirstOrDefault(candidate => candidate.Id == presetId);
            return Task.FromResult(preset is { IsBuiltIn: false } && _presets.Remove(preset));
        }

        public Task SaveProfileCycleSettingsAsync(
            ProfileCycleSettings settings,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _cycleSettings[settings.ProfileId] = settings;
            return Task.CompletedTask;
        }

        public Task<ProfileCycleSettings?> LoadProfileCycleSettingsAsync(
            ProfileId profileId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_profiles.Any(profile => profile.Id == profileId)
                ? _cycleSettings.GetValueOrDefault(profileId)
                : null);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class RecordingClient : IFocusControlClient
    {
        private StatusResponse _status = Inactive();
        private readonly Guid _preparationId = Guid.NewGuid();

        public ValueTask<PrepareSessionResponse> PrepareAsync(
            PrepareSessionRequest request,
            CancellationToken cancellationToken)
        {
            LastPreparation = request;
            return ValueTask.FromResult(new PrepareSessionResponse(
                true,
                "Prepared",
                _preparationId,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(35),
                DateTimeOffset.UtcNow.AddMinutes(2),
                request.Mode,
                request.Mode == SessionMode.FocusCycle
                    ? [
                        new PreparedSessionPhase(
                            0,
                            SessionPhaseKind.Focus,
                            DateTimeOffset.UtcNow,
                            DateTimeOffset.UtcNow.AddMinutes(25),
                            1),
                        new PreparedSessionPhase(
                            1,
                            SessionPhaseKind.ShortBreak,
                            DateTimeOffset.UtcNow.AddMinutes(25),
                            DateTimeOffset.UtcNow.AddMinutes(30),
                            1)
                    ]
                    : []));
        }

        public ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(
            ActivatePreparedSessionRequest request,
            CancellationToken cancellationToken)
        {
            PreparedActivationCount++;
            return ValueTask.FromResult(new ActivatePreparedSessionResponse(
                true,
                "Activated",
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(35),
                SessionMode.FocusCycle));
        }

        public ValueTask<SkipBreakResponse> SkipBreakAsync(
            SkipBreakRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public StatusResponse Status
        {
            get => _status;
            set => _status = value;
        }

        public PrepareSessionRequest? LastPreparation { get; private set; }

        public int PreparedActivationCount { get; private set; }

        public ActivateSessionRequest? LastActivation { get; private set; }

        public int StopCount { get; private set; }

        public IReadOnlyList<FocusAuditEvent> Activity { get; init; } = [];

        public Exception? ActivityFailure { get; set; }

        public Exception? StatusFailure { get; set; }

        public Exception? ActivationFailure { get; set; }

        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (StatusFailure is not null) throw StatusFailure;
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
            if (ActivationFailure is not null) throw ActivationFailure;
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
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ActivityFailure is null
                ? ValueTask.FromResult(new ActivityResponse(Activity.Take(maximumCount).ToArray()))
                : ValueTask.FromException<ActivityResponse>(ActivityFailure);
        }

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
