using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FocusGuard.Client;
using FocusGuard.Client.Preferences;
using FocusGuard.Contracts;
using FocusGuard.Desktop;
using FocusGuard.Domain;
using FocusGuard.Service;
using FocusGuard.Storage;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(FocusGuard.Desktop.UiTests.TestAppBuilder))]

namespace FocusGuard.Desktop.UiTests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        CultureInfo.DefaultThreadCurrentUICulture = new("es");
        CultureInfo.CurrentUICulture = new("es");
        CultureInfo.CurrentCulture = new("es-EC");
        CultureInfo.DefaultThreadCurrentCulture = new("es-EC");
        return AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}

public sealed class DesktopWorkflowUiTests
{
    [AvaloniaTheory]
    [InlineData(1920, 1040, 1, 1440, 840)]
    [InlineData(1366, 720, 1.25, 1044, 528)]
    [InlineData(1920, 1040, 1.25, 1440, 784)]
    public async Task FixedWindowUsesMonitorSpaceWithoutClippingQuickStart(int width, int height, double scaling, int expectedWidth, int expectedHeight)
    {
        using var fixture = await Fixture.CreateAsync(sampleLibrary: true);
        fixture.ViewModel.ScheduleName = "Lectura de la tarde";
        fixture.ViewModel.ScheduleTime = "17:00";
        fixture.ViewModel.ScheduleEnabled = true;
        await ExecuteAsync(fixture.ViewModel.SaveScheduleCommand, () => fixture.ViewModel.Schedules.Count == 1);
        Assert.True(fixture.ViewModel.HasHomeUpcoming);
        var window = new MainWindow(fixture.ViewModel);
        window.ApplyWorkspaceArea(new PixelRect(0, 0, width, height), scaling);
        window.SetRenderScaling(scaling); window.Show();
        Dispatcher.UIThread.RunJobs();
        var workspace = window.GetVisualDescendants().OfType<FocusWorkspaceView>().Single();
        Assert.False(window.CanResize); Assert.False(window.CanMaximize);
        Assert.Equal(expectedWidth, window.Bounds.Width); Assert.Equal(expectedHeight, window.Bounds.Height);
        Assert.Empty(workspace.GetVisualDescendants().OfType<ClockDial>());
        Assert.False(workspace.FindControl<Border>("SessionSetupPanel")!.IsEffectivelyVisible);
        var start = workspace.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("startAction"));
        Assert.True(start.IsEffectivelyVisible);
        AssertWithinWindow(start, window);
        Assert.Contains("50", fixture.ViewModel.StartFocusButtonText);
        Assert.Equal(2, fixture.ViewModel.HomeProfiles.Count);
        SaveFrame(window, "session-home-" + width + "-" + scaling.ToString(CultureInfo.InvariantCulture));
        fixture.ViewModel.OpenSessionSetup(); Dispatcher.UIThread.RunJobs();
        Assert.False(workspace.FindControl<Grid>("SessionHome")!.IsEffectivelyEnabled);
        Assert.False(window.FindControl<Border>("NavigationSidebar")!.IsEffectivelyEnabled);
        var setupStart = workspace.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("setupStartAction"));
        Assert.True(setupStart.IsEffectivelyVisible); AssertWithinWindow(setupStart, window);
        SaveFrame(window, "session-setup-" + width + "-" + scaling.ToString(CultureInfo.InvariantCulture));
        window.KeyPress(Key.Escape, Avalonia.Input.RawInputModifiers.None, PhysicalKey.Escape, "Escape"); Dispatcher.UIThread.RunJobs();
        Assert.False(fixture.ViewModel.IsSessionSetupOpen);
        var reading = fixture.ViewModel.HomeProfiles.Single(p => p.Name == "Lectura");
        reading.OpenCommand.Execute(null); Dispatcher.UIThread.RunJobs();
        Assert.Equal("Lectura", fixture.ViewModel.ProfileName);
        Assert.True(fixture.ViewModel.IsSessionSetupOpen);
        fixture.ViewModel.ShowSection(NavigationSection.Profiles);
        Assert.False(fixture.ViewModel.IsSessionSetupOpen);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ActiveSessionShowsObjectiveAndCompactWindowKeepsStatusWhenCollapsed()
    {
        using var fixture = await Fixture.CreateAsync();
        var vm = fixture.ViewModel;
        vm.ObjectiveInput = "Terminar la propuesta";
        await ExecuteAsync(vm.StartFocusCommand, () => vm.IsActive && vm.CanStop);
        var window = new MainWindow(vm) { Width = 760, Height = 620 };
        window.ApplyWorkspaceArea(new PixelRect(0, 0, 1366, 720), 1.25); window.SetRenderScaling(1.25); window.Show();
        Assert.Equal("Terminar la propuesta", vm.ActiveObjective);
        Dispatcher.UIThread.RunJobs();
        var endSummary = window.GetVisualDescendants().OfType<FocusWorkspaceView>().Single().FindControl<TextBlock>("ActiveEndSummary")!;
        Assert.Equal(vm.SessionEndSummaryText, endSummary.Text);
        Assert.True(endSummary.IsEffectivelyVisible);
        Assert.True(endSummary.TranslatePoint(default, window)!.Value.Y + endSummary.Bounds.Height <= window.Bounds.Height);
        SaveFrame(window, "active-small");
        window.ApplyWorkspaceArea(new PixelRect(0, 0, 1920, 1040), 1);
        window.SetRenderScaling(1); Dispatcher.UIThread.RunJobs();
        SaveFrame(window, "active-1920-100");
        var compact = new CompactFocusWindow { DataContext = vm };
        compact.Show();
        vm.CompactTopmost = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(compact.Topmost);
        SaveFrame(compact, "compact");
        var collapse = compact.FindControl<Button>("CollapseButton")!;
        collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(compact.FindControl<StackPanel>("Details")!.IsVisible);
        Assert.Contains(compact.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == vm.RemainingText && t.IsEffectivelyVisible);
        Assert.Contains(compact.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == vm.ActiveProfileName && t.IsEffectivelyVisible);
        Assert.Contains(compact.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == vm.CompactPhaseLabel && t.IsEffectivelyVisible);
        SaveFrame(compact, "compact-collapsed");
        compact.Close(); window.Close();
        Assert.True((await fixture.Service.GetStatusAsync(default)).SessionState is "Active" or "Activating");
    }

    [AvaloniaFact]
    public async Task ApplicationSearchAndDomainValidationWorkWithKeyboardFocus()
    {
        using var fixture = await Fixture.CreateAsync();
        var vm = fixture.ViewModel;
        await vm.LoadApplicationsAsync();
        vm.ShowSection(NavigationSection.Profiles);
        var window = new MainWindow(vm) { Width = 1100, Height = 760 };
        window.ApplyWorkspaceArea(new PixelRect(0, 0, 1366, 720), 1.25); window.SetRenderScaling(1.25); window.Show();
        var rules = window.FindControl<RuleEditorView>("ProfileRules")!;
        rules.FocusSearch();
        window.KeyTextInput("Discord");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Discord", vm.ApplicationSearch);
        Assert.Single(vm.MatchingApplications);
        vm.WebsiteRuleInput = "https://youtube.com/watch?v=private";
        Assert.False(vm.IsWebsiteValid);
        vm.WebsiteRuleInput = "youtube.com";
        Assert.True(vm.IsWebsiteValid);
        await ExecuteAsync(vm.AddWebsiteRuleCommand, () => vm.SavedWebsiteRules.Any(r => r.Rule.Pattern == "youtube.com"));
        Assert.Contains(vm.FilteredWebsiteRules, r => r.Rule.Pattern == "youtube.com");
        vm.WebsiteRuleInput = "docs.youtube.com";
        vm.WebsiteAction = vm.WebsiteActions.Single(a => a.Action == RuleAction.Allow);
        await ExecuteAsync(vm.AddWebsiteRuleCommand, () => vm.SavedWebsiteRules.Any(r => r.Rule.Pattern == "docs.youtube.com"));
        Assert.Contains(vm.FilteredWebsiteRules, r => r.Rule.Pattern == "docs.youtube.com" && r.Rule.Action == RuleAction.Allow);
        Assert.DoesNotContain(vm.BlockingWebsiteRules, r => r.Rule.Pattern == "docs.youtube.com");
        Assert.DoesNotContain(vm.BlockedItems, r => r.Label == "docs.youtube.com");
        vm.ApplicationSearch = string.Empty;
        vm.ApplicationAction = vm.ApplicationActions.Single(a => a.Action == RuleAction.Allow);
        vm.ApplicationRuleInput = "FocusGuard.TestTarget.exe";
        await ExecuteAsync(vm.AddApplicationRuleCommand, () => vm.SavedApplicationRules.Any(r => r.Rule.Value == "FocusGuard.TestTarget.exe"));
        Dispatcher.UIThread.RunJobs();
        var applicationPanel = rules.FindControl<Border>("Applications")!;
        Assert.Contains(applicationPanel.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == FocusGuard.Desktop.Resources.UiStrings.RuleAllow && text.IsEffectivelyVisible);
        Assert.Contains(applicationPanel.GetVisualDescendants().OfType<ComboBox>(),
            selector => selector.SelectedItem is RuleActionChoice action && action.Action == RuleAction.Allow && selector.IsEffectivelyVisible);
        SaveFrame(window, "rule-editor");
        window.ApplyWorkspaceArea(new PixelRect(0, 0, 1920, 1040), 1);
        window.SetRenderScaling(1);
        Dispatcher.UIThread.RunJobs();
        SaveFrame(window, "rule-editor-wide");
        window.Close();
    }

    [AvaloniaFact]
    public async Task CycleCountdownMeasuresTheCurrentPhaseAndAdvancedControlsKeepStartReachable()
    {
        using var fixture = await Fixture.CreateAsync();
        var vm = fixture.ViewModel;
        vm.SelectedSessionMode = vm.SessionModes.Single(m => m.Mode == SessionMode.FocusCycle);
        vm.SelectedCyclePreset = vm.CyclePresets.Single(p => p.Id == BuiltInCyclePresets.ClassicId);
        var window = new MainWindow(vm) { Width = 760, Height = 560 };
        window.ApplyWorkspaceArea(new PixelRect(0, 0, 1366, 720), 1.25); window.SetRenderScaling(1.25); window.Show();
        var workspace = window.GetVisualDescendants().OfType<FocusWorkspaceView>().Single();
        vm.OpenSessionSetup(); Dispatcher.UIThread.RunJobs();
        var advanced = workspace.GetVisualDescendants().OfType<Expander>().Single();
        advanced.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        var cycleCard = workspace.GetVisualDescendants().OfType<RadioButton>().First(b => b.Classes.Contains("cycleCard"));
        cycleCard.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var start = workspace.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("setupStartAction"));
        Assert.True(start.TranslatePoint(default, window)!.Value.Y + start.Bounds.Height <= window.Bounds.Height);
        SaveFrame(window, "advanced-small");
        await ExecuteAsync(vm.StartFocusCommand, () => vm.IsActive && vm.CanStop);
        Assert.Equal("25:00", vm.RingCenterValue);
        Assert.Equal("25:00", vm.CompactRemainingText);
        fixture.Clock.Advance(TimeSpan.FromMinutes(10)); await vm.RefreshAsync();
        Assert.Equal("15:00", vm.RingCenterValue);
        Assert.Equal(144, vm.RingSweepAngle, precision: 2);
        SaveFrame(window, "active-cycle");
        fixture.Clock.Advance(TimeSpan.FromMinutes(15)); await vm.RefreshAsync();
        Assert.True(vm.IsBreakPhase);
        Assert.Equal("05:00", vm.RingCenterValue);
        Assert.Contains("5", vm.RingCenterCaption);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(vm.SessionEndSummaryText, workspace.FindControl<TextBlock>("ActiveEndSummary")!.Text);
        Assert.DoesNotContain(FocusGuard.Desktop.Resources.UiStrings.NoActiveEndTime, vm.SessionEndSummaryText);
        SaveFrame(window, "active-break");
        var restored = new FocusViewModel(fixture.Client, fixture.Profiles, fixture.Clock, fixture.Settings);
        await restored.RefreshAsync();
        Assert.True(restored.IsCycleMode);
        window.Close();
    }

    [AvaloniaFact]
    public async Task StrictRepeatRequiresFreshReviewAndNeverActivatesFromOneClick()
    {
        using var fixture = await Fixture.CreateAsync();
        var vm = fixture.ViewModel;
        vm.IsStrictMode = true;
        await ExecuteAsync(vm.StartFocusCommand, () => vm.IsStrictReviewVisible);
        var firstCode = vm.RecoveryCodeDisplay;
        vm.AdvanceStrictStep(); vm.TypedConfirmation = vm.RequiredConfirmationText;
        await ExecuteAsync(vm.ConfirmStrictCommand, () => vm.IsActive);
        var compact = new CompactFocusWindow { DataContext = vm };
        compact.Show();
        compact.FindControl<Button>("CollapseButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Contains(compact.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == vm.StrictActiveNotice && t.IsEffectivelyVisible);
        SaveFrame(compact, "compact-strict-collapsed");
        compact.Close();
        var id = (await fixture.Service.GetStatusAsync(default)).SessionId;
        fixture.Clock.Advance(TimeSpan.FromMinutes(50)); await vm.RefreshAsync();
        await vm.RepeatSessionAsync(id, 50);
        Assert.True(vm.IsStrictReviewVisible);
        Assert.True(vm.IsStrictStepOne);
        Assert.False(vm.IsActive);
        Assert.NotEqual(firstCode, vm.RecoveryCodeDisplay);
        Assert.False(vm.ConfirmStrictCommand.CanExecute(null));
        Assert.True((await fixture.Service.GetStatusAsync(default)).SessionState is "Expired" or "Recovered");
    }

    [AvaloniaFact]
    public async Task StrictReviewAndSettingsFitFixedWindowAt125Percent()
    {
        using var fixture = await Fixture.CreateAsync();
        var vm = fixture.ViewModel;
        vm.IsReduceMotionEnabled = true;
        var window = new MainWindow(vm);
        window.ApplyWorkspaceArea(new PixelRect(0, 0, 1366, 720), 1.25); window.SetRenderScaling(1.25); window.Show();
        await Task.Delay(50); Dispatcher.UIThread.RunJobs();
        vm.ShowSection(NavigationSection.Settings);
        await Task.Delay(50); Dispatcher.UIThread.RunJobs();
        SaveFrame(window, "settings");
        var settingsTabs = window.FindControl<TabControl>("SettingsTabs")!;
        for (var index = 1; index < settingsTabs.Items.Count; index++)
        {
            settingsTabs.SelectedIndex = index;
            Dispatcher.UIThread.RunJobs();
            AssertWithinWindow(window.FindControl<Border>("SettingsDialog")!, window);
            SaveFrame(window, "settings-" + index);
        }
        vm.ShowFocus();
        vm.IsStrictMode = true;
        vm.OpenSessionSetup();
        await ExecuteAsync(vm.StartFocusCommand, () => vm.IsStrictReviewVisible);
        Assert.False(vm.IsSessionSetupOpen);
        await Task.Delay(50); Dispatcher.UIThread.RunJobs();
        var dialog = window.FindControl<Border>("StrictReviewDialog")!;
        Assert.True(dialog.IsEffectivelyVisible);
        AssertWithinWindow(dialog, window);
        SaveFrame(window, "strict-review-one");
        vm.AdvanceStrictStep();
        await Task.Delay(50); Dispatcher.UIThread.RunJobs();
        AssertWithinWindow(dialog, window);
        SaveFrame(window, "strict-review-two");
        vm.CancelStrictCommand.Execute(null);
        window.Close();
    }

    [AvaloniaFact]
    public async Task DisconnectedUiShowsVisibleWarningAndDisabledStopWhileServiceSessionContinues()
    {
        using var fixture = await Fixture.CreateAsync();
        var vm = fixture.ViewModel;
        await ExecuteAsync(vm.StartFocusCommand, () => vm.IsActive && vm.CanStop);
        var before = await fixture.Service.GetStatusAsync(default);
        var window = new MainWindow(vm);
        window.Show();
        fixture.Client.FailStatusRead = true;
        await vm.RefreshAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.IsActive);
        Assert.False(vm.CanStop);
        Assert.False(vm.IsSessionStateFresh);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text =>
            text.IsEffectivelyVisible && text.Text == vm.CompactWarningText);
        var actual = await fixture.Service.GetStatusAsync(default);
        Assert.Equal(before.SessionId, actual.SessionId);
        Assert.Equal(before.EndUtc, actual.EndUtc);
        Assert.Equal("Active", actual.SessionState);
        fixture.Client.FailStatusRead = false;
        await vm.RefreshAsync();
        Assert.True(vm.CanStop);
        Assert.True(vm.IsSessionStateFresh);
        window.Close();
    }

    [AvaloniaFact]
    public async Task UnavailableOverviewDoesNotChangeAuthoritativeSessionState()
    {
        using var fixture = await Fixture.CreateAsync();
        var vm = fixture.ViewModel;
        await ExecuteAsync(vm.StartFocusCommand, () => vm.IsActive && vm.CanStop);
        fixture.Client.FailScheduleRead = true;
        await vm.LoadSchedulesAsync();
        Assert.Equal(FocusGuard.Desktop.Resources.UiStrings.ScheduleUnavailable, vm.HomeUpcomingEmptyTitle);
        Assert.Empty(vm.HomeUpcomingEmptyHint);
        Assert.True(vm.IsActive);
        fixture.Client.FailScheduleRead = false;
        await vm.LoadSchedulesAsync();
        Assert.Equal(FocusGuard.Desktop.Resources.UiStrings.HomeNoUpcoming, vm.HomeUpcomingEmptyTitle);
        Assert.True(vm.IsActive);
    }

    [AvaloniaFact]
    public async Task LongProfilesAndEditorDialogsRemainReachableInFixedWindow()
    {
        using var fixture = await Fixture.CreateAsync();
        var vm = fixture.ViewModel;
        vm.IsReduceMotionEnabled = true;
        var name = new string('W', 80);
        vm.ProfileNameInput = name;
        await ExecuteAsync(vm.RenameProfileCommand, () => vm.ProfileName == name);
        var window = new MainWindow(vm);
        window.ApplyWorkspaceArea(new PixelRect(0, 0, 1366, 720), 1.25); window.SetRenderScaling(1.25); window.Show();
        await Task.Delay(50); Dispatcher.UIThread.RunJobs();
        SaveFrame(window, "long-profile");
        vm.SelectedSessionMode = vm.SessionModes.Single(m => m.Mode == SessionMode.FocusCycle);
        Assert.True(vm.CreateCyclePresetCommand.CanExecute(null));
        vm.CreateCyclePresetCommand.Execute(null);
        await Task.Delay(50); Dispatcher.UIThread.RunJobs();
        var save = window.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.CommitCyclePresetCommand);
        Assert.True(save.IsEffectivelyVisible);
        AssertWithinWindow(save, window);
        SaveFrame(window, "cycle-editor");
        vm.CancelCyclePresetEditCommand.Execute(null);
        vm.NewProfileCommand.Execute(null);
        await Task.Delay(50); Dispatcher.UIThread.RunJobs();
        AssertWithinWindow(window.FindControl<Border>("NewProfileDialog")!, window);
        SaveFrame(window, "profile-creation");
        window.Close();
    }

    private static void AssertWithinWindow(Control control, Window window)
    {
        var origin = control.TranslatePoint(default, window)!.Value;
        Assert.True(origin.X >= 0 && origin.Y >= 0 && origin.X + control.Bounds.Width <= window.Bounds.Width + 1 && origin.Y + control.Bounds.Height <= window.Bounds.Height + 1,
            $"Control outside fixed window: {origin}, {control.Bounds}, {window.Bounds}");
    }

    [AvaloniaFact]
    public void ExecutableIconsUseTheRealWindowsShell()
    {
        if (!OperatingSystem.IsWindows()) return;
        var app = new ApplicationChoiceViewModel(new("Test host", Environment.ProcessPath!), _ => Task.CompletedTask, () => true);
        Assert.NotNull(app.Icon);
        Assert.InRange(app.Icon.PixelSize.Width, 1, 256);
    }

    [AvaloniaFact]
    public async Task TrayCompactEntryKeepsExitReachable()
    {
        using var fixture = await Fixture.CreateAsync();
        var tray = new TrayFlyoutWindow { DataContext = fixture.ViewModel };
        tray.Show(); Dispatcher.UIThread.RunJobs();
        var exit = tray.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == FocusGuard.Desktop.Resources.UiStrings.ExitFocusGuard);
        Assert.True(exit.TranslatePoint(default, tray)!.Value.Y + exit.Bounds.Height <= tray.Bounds.Height);
        SaveFrame(tray, "tray");
        tray.Close();
    }

    [AvaloniaFact]
    public async Task ScheduleAndTodayRenderRealServiceDataAndRespectExplicitEnable()
    {
        using var fixture = await Fixture.CreateAsync();
        var vm = fixture.ViewModel;
        vm.ScheduleName = "Escritura";
        vm.ScheduleTime = "11:00";
        vm.ScheduleDuration = 50;
        Assert.False(vm.ScheduleEnabled);
        await ExecuteAsync(vm.SaveScheduleCommand, () => vm.Schedules.Count == 1);
        Assert.False(Assert.Single(vm.Schedules).Schedule.Enabled);
        vm.ShowSection(NavigationSection.Schedule);
        var window = new MainWindow(vm) { Width = 1100, Height = 760 };
        window.ApplyWorkspaceArea(new PixelRect(0, 0, 1366, 720), 1.25); window.SetRenderScaling(1.25); window.Show();
        Assert.Equal(7, vm.ScheduleWeek.Count);
        SaveFrame(window, "weekly-schedule");
        vm.ShowFocus(); vm.SelectedDurationMinutes = 2;
        await ExecuteAsync(vm.StartFocusCommand, () => vm.IsActive && vm.CanStop);
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        await vm.RefreshAsync();
        await vm.ShowActivityAsync();
        Assert.Equal(2, vm.TodayFocusMinutes);
        Assert.Equal(1, vm.TodayCompletedSessions);
        Assert.Single(vm.TodayTimeline);
        SaveFrame(window, "today");
        Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        SaveFrame(window, "today-light");
        window.Close();
    }

    [AvaloniaFact]
    public async Task AcceptedSessionRestoresLastDurationAndObjectiveRepeatsOriginalProfile()
    {
        using var fixture = await Fixture.CreateAsync();
        var vm = fixture.ViewModel;
        vm.SelectedDurationMinutes = 47;
        vm.ObjectiveInput = "Draft";
        await ExecuteAsync(vm.StartFocusCommand, () => vm.IsActive && vm.CanStop);
        var id = (await fixture.Service.GetStatusAsync(default)).SessionId;
        var saved = fixture.Settings.Load();
        Assert.Equal(47, saved.LastDurationMinutes);
        Assert.Equal(vm.SelectedProfile!.Id.Value, saved.LastProfileId);
        var reloaded = new FocusViewModel(fixture.Client, fixture.Profiles, fixture.Clock, fixture.Settings);
        await reloaded.RefreshAsync();
        Assert.Equal(47, reloaded.SelectedDurationMinutes);
        Assert.Equal("Draft", reloaded.ActiveObjective);
        await fixture.Service.StopNormalSessionAsync(default); await vm.RefreshAsync();
        vm.ObjectiveInput = "Changed";
        vm.SelectedDurationMinutes = 25;
        await vm.RepeatSessionAsync(id, 47);
        Assert.Equal("Draft", vm.ActiveObjective);
        Assert.Equal(47, vm.SelectedDurationMinutes);
        var summary = await fixture.Service.GetSessionSummaryAsync(new(id), default);
        var model = new SessionSummaryViewModel(summary, fixture.Client, "Draft", saveReflection: value => vm.SaveReflection(id, value));
        var summaryWindow = new SessionSummaryWindow(model);
        summaryWindow.Show(); model.Reflection = "Completed the outline";
        SaveFrame(summaryWindow, "session-summary");
        summaryWindow.Close();
        Assert.Equal("Completed the outline", vm.GetIntention(id)!.Reflection);
    }

    private static async Task ExecuteAsync(System.Windows.Input.ICommand command, Func<bool> completed)
    {
        Assert.True(command.CanExecute(null)); command.Execute(null);
        for (var i = 0; i < 100 && !completed(); i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
        Assert.True(completed());
    }
    private static void SaveFrame(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var output = Environment.GetEnvironmentVariable("FOCUSGUARD_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        frame.Save(Path.Combine(output, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        if (window is MainWindow)
        {
            Assert.Contains(window.RenderScaling, new[] { 1.0, 1.25 });
            Assert.Equal(new PixelSize((int)(window.Bounds.Width * window.RenderScaling), (int)(window.Bounds.Height * window.RenderScaling)), frame.PixelSize);
            frame.Save(Path.Combine(output, name + (window.RenderScaling == 1.25 ? "-125.png" : "-100.png")), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "focusguard-ui-" + Guid.NewGuid());
        public Clock Clock { get; } = new();
        public InMemoryFocusService Service { get; private set; } = null!;
        public ServiceClient Client { get; private set; } = null!;
        public SqliteProfileStore Profiles { get; private set; } = null!;
        public DesktopSettingsStore Settings { get; private set; } = null!;
        public FocusViewModel ViewModel { get; private set; } = null!;
        public static async Task<Fixture> CreateAsync(bool sampleLibrary = false)
        {
            CultureInfo.CurrentUICulture = new("es");
            var f = new Fixture();
            Directory.CreateDirectory(f._directory);
            f.Profiles = new(Path.Combine(f._directory, "profiles.db"));
            var history = new SqliteSessionHistoryStore(Path.Combine(f._directory, "history.db"));
            f.Service = new(f.Clock, strictSessionStore: new TestStrictStore(), historyStore: history);
            f.Client = new(f.Service, new(f.Service, history, Path.Combine(f._directory, "schedule.json"), f.Clock));
            f.Settings = new(Path.Combine(f._directory, "settings.json"));
            f.Settings.Save(new(SessionEnd: SessionEndPresentation.None, ReduceMotion: true));
            await f.Profiles.SaveProfileAsync(new(new(Guid.NewGuid()), "Deep work", [], [WebsiteRule.Create(RuleAction.Block, "instagram.com").Value!], AccentHex: "#89B4FA"));
            if (sampleLibrary)
                await f.Profiles.SaveProfileAsync(new(new(Guid.NewGuid()), "Lectura", [], [WebsiteRule.Create(RuleAction.Block, "youtube.com").Value!], AccentHex: "#F9E2AF"));
            f.ViewModel = new(f.Client, f.Profiles, f.Clock, f.Settings) { ApplicationCatalog = new Catalog() };
            await f.ViewModel.RefreshAsync();
            Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            return f;
        }
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }
    private sealed class Catalog : IApplicationCatalog
    {
        public Task<IReadOnlyList<InstalledApplication>> ListAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<InstalledApplication>>([new("Discord", "C:\\Apps\\Discord.exe"), new("Steam", "C:\\Apps\\Steam.exe")]);
    }
    private sealed class TestStrictStore : IStrictSessionStore
    {
        public ValueTask ActivateAsync(Session session, EnforcementPolicy policy, RecoveryCredential credential, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask<StrictPolicyLoadResult> LoadAsync(DateTimeOffset nowUtc, CancellationToken token) => ValueTask.FromResult(new StrictPolicyLoadResult(StrictPolicyLoadStatus.Absent, null, "Absent"));
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public void Advance(TimeSpan value) => _now += value;
    }
    private sealed class ServiceClient(InMemoryFocusService service, DesktopWorkflowService workflows) : IFocusControlClient
    {
        public bool FailScheduleRead { get; set; }
        public bool FailStatusRead { get; set; }
        public async ValueTask<PrepareSessionResponse> PrepareAsync(PrepareSessionRequest r, CancellationToken t) => (await service.PrepareSessionAsync(r, t)).Value!;
        public async ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(ActivatePreparedSessionRequest r, CancellationToken t) => (await service.ActivatePreparedSessionAsync(r, t)).Value!;
        public async ValueTask<SkipBreakResponse> SkipBreakAsync(SkipBreakRequest r, CancellationToken t) => (await service.SkipBreakAsync(r, t)).Value!;
        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken t) => FailStatusRead
            ? throw new IOException("Status unavailable.") : service.GetStatusAsync(t);
        public async ValueTask<ActivateSessionResponse> ActivateAsync(ActivateSessionRequest r, CancellationToken t) => (await service.ActivateAsync(r, t)).Value!;
        public ValueTask<StopSessionResponse> StopAsync(CancellationToken t) => service.StopNormalSessionAsync(t);
        public ValueTask<ActivityResponse> GetActivityAsync(int n, CancellationToken t) => service.GetActivityAsync(new(n), t);
        public ValueTask<CaptureResponse> CaptureAsync(CaptureRequest r, CancellationToken t) => service.CaptureAsync(r, t);
        public ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(Guid? id, CancellationToken t) => service.GetSessionSummaryAsync(new(id), t);
        public ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(ResolveCapturesRequest r, CancellationToken t) => service.ResolveCapturesAsync(r, t);
        public async ValueTask<TodayResponse> GetTodayAsync(TodayRequest r, CancellationToken t) => await workflows.TodayAsync(r, t);
        public async ValueTask<ScheduleResponse> GetSchedulesAsync(CancellationToken t)
        {
            if (FailScheduleRead) throw new IOException("Service unavailable.");
            return await workflows.ListAsync(t);
        }
        public async ValueTask<ScheduleResponse> ChangeScheduleAsync(ScheduleChangeRequest r, CancellationToken t) => await workflows.ChangeAsync(r, t);
    }
}
