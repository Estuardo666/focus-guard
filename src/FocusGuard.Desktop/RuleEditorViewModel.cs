using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using FocusGuard.Desktop.Resources;
using FocusGuard.Domain;

namespace FocusGuard.Desktop;

public sealed record RuleActionChoice(RuleAction Action, string Label) { public override string ToString() => Label; }
public sealed class ApplicationChoiceViewModel(InstalledApplication app, Func<InstalledApplication, Task> add, Func<bool> canEdit)
{
    public string Name => app.Name;
    public string Path => app.Path;
    public Bitmap? Icon => ApplicationIconCache.Get(app.Path);
    public bool HasIcon => Icon is not null;
    public ICommand AddCommand { get; } = new AsyncRuleCommand(() => add(app), canEdit);
}

public sealed partial class FocusViewModel
{
    private string _applicationSearch = "";
    private string _websiteSearch = "";
    private bool _loadingApplications;
    private bool _applicationsLoaded;
    private IReadOnlyList<InstalledApplication> _installedApplications = [];
    private RuleActionChoice? _websiteAction;
    private RuleActionChoice? _applicationAction;
    public IApplicationCatalog ApplicationCatalog { private get; set; } = new WindowsApplicationCatalog();
    public string ApplicationSearch { get => _applicationSearch; set { if (SetField(ref _applicationSearch, value ?? "")) RefreshRuleSearch(); } }
    public string WebsiteSearch { get => _websiteSearch; set { if (SetField(ref _websiteSearch, value ?? "")) RefreshRuleSearch(); } }
    public ObservableCollection<ApplicationChoiceViewModel> MatchingApplications { get; } = [];
    public ObservableCollection<SavedApplicationRuleViewModel> FilteredApplicationRules { get; } = [];
    public ObservableCollection<SavedWebsiteRuleViewModel> FilteredWebsiteRules { get; } = [];
    public bool IsLoadingApplications => _loadingApplications;
    public bool NoApplicationMatches => !_loadingApplications && MatchingApplications.Count == 0;
    public ICommand RefreshApplicationsCommand { get; private set; } = null!;
    public IReadOnlyList<RuleActionChoice> WebsiteActions { get; } = [new(RuleAction.Block, UiStrings.RuleBlock), new(RuleAction.Allow, UiStrings.RuleAllow)];
    public IReadOnlyList<RuleActionChoice> ApplicationActions { get; } = [new(RuleAction.Block, UiStrings.RuleBlock), new(RuleAction.Allow, UiStrings.RuleAllow)];
    public RuleActionChoice ApplicationAction { get => _applicationAction ?? ApplicationActions[0]; set { _applicationAction = value; OnPropertyChanged(); } }
    public RuleActionChoice WebsiteAction { get => _websiteAction ?? WebsiteActions[0]; set { _websiteAction = value; OnPropertyChanged(); OnPropertyChanged(nameof(WebsiteValidationText)); } }
    public bool IsWebsiteValid => !string.IsNullOrWhiteSpace(WebsiteRuleInput) && WebsiteRule.Create(WebsiteAction.Action, WebsiteRuleInput).IsValid;
    public bool HasWebsiteValidationError => !string.IsNullOrWhiteSpace(WebsiteRuleInput) && !IsWebsiteValid;
    public string WebsiteValidationText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(WebsiteRuleInput)) return UiStrings.DomainHint;
            var validation = WebsiteRule.Create(WebsiteAction.Action, WebsiteRuleInput);
            return validation.IsValid ? string.Format(CultureInfo.CurrentCulture, UiStrings.DomainValidated, validation.Value!.Pattern, WebsiteAction.Label) : UiStrings.DomainInvalid;
        }
    }
    private void RefreshRuleSearch()
    {
        FilteredApplicationRules.Clear();
        foreach (var rule in SavedApplicationRules.Where(r => r.DisplayName.Contains(ApplicationSearch, StringComparison.CurrentCultureIgnoreCase) || r.Rule.Value.Contains(ApplicationSearch, StringComparison.OrdinalIgnoreCase))) FilteredApplicationRules.Add(rule);
        FilteredWebsiteRules.Clear();
        foreach (var rule in SavedWebsiteRules.Where(r => r.Rule.Pattern.Contains(WebsiteSearch, StringComparison.OrdinalIgnoreCase))) FilteredWebsiteRules.Add(rule);
        MatchingApplications.Clear();
        foreach (var app in _installedApplications.Where(a => a.Name.Contains(ApplicationSearch, StringComparison.CurrentCultureIgnoreCase))
            .Where(a => !SavedApplicationRules.Any(r => string.Equals(r.Rule.Value, a.Path, StringComparison.OrdinalIgnoreCase))).Take(20))
            MatchingApplications.Add(new(app, AddInstalledApplicationAsync, () => CanEditProfileRules));
        OnPropertyChanged(nameof(NoApplicationMatches));
    }
    public async Task LoadApplicationsAsync()
    {
        if (_loadingApplications || IsActive) return;
        _loadingApplications = true; OnPropertyChanged(nameof(IsLoadingApplications)); OnPropertyChanged(nameof(NoApplicationMatches));
        try { _installedApplications = await ApplicationCatalog.ListAsync(CancellationToken.None); ApplicationPresentation.Register(_installedApplications); _applicationsLoaded = true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or System.Reflection.TargetInvocationException)
        { StatusMessage = UiStrings.ApplicationCatalogUnavailable; }
        finally { _loadingApplications = false; OnPropertyChanged(nameof(IsLoadingApplications)); RefreshRuleSearch(); }
    }
    private async Task AddInstalledApplicationAsync(InstalledApplication app)
    {
        if (!CanEditProfileRules || SelectedProfile is null) return;
        await RunProfileMutationAsync(async () =>
        {
            var rule = await _profileStore.AddApplicationRuleAsync(SelectedProfile, ApplicationAction.Action, ApplicationMatchKind.ExactPath, app.Path);
            if (!rule.IsValid) { StatusMessage = rule.Errors[0].Message; return; }
            await LoadProfilesAsync(CancellationToken.None);
            StatusMessage = UiStrings.Get("ApplicationRuleSaved");
        });
    }
    private void NotifyWorkflowConfiguration(string? name)
    {
        if (name == nameof(ProfileScopeText)) OnPropertyChanged(nameof(HomeProfileScope));
        if (name == nameof(Section) && Section != NavigationSection.Focus) CloseSessionSetup();
        if (name is nameof(IsActive) or nameof(IsStrictReviewVisible))
        {
            if (IsActive || IsStrictReviewVisible) CloseSessionSetup();
        }
        if (name is nameof(ActiveProfileName) or nameof(ProfileName) or nameof(IsActive)) OnPropertyChanged(nameof(HomeCurrentTitle));
        if (name == nameof(HasSchedules)) { OnPropertyChanged(nameof(HomeUpcoming)); OnPropertyChanged(nameof(HasHomeUpcoming)); }
        if (name is nameof(IsTodayLoaded) or nameof(TodayFocusMinutes) or nameof(TodayCompletedSessions)) OnPropertyChanged(nameof(HomeTodaySummary));

        if (name is nameof(ExactEndText) or nameof(IsCycleSession)) OnPropertyChanged(nameof(SessionEndSummaryText));
        if (name is nameof(RingCenterValue) or nameof(IsActive)) { OnPropertyChanged(nameof(CompactRemainingText)); OnPropertyChanged(nameof(CompactPhaseLabel)); }
        if (name is nameof(SelectedDurationMinutes) or nameof(SelectedDurationPreset) or nameof(SelectedSessionMode) or nameof(SelectedCyclePreset) or nameof(IsCycleMode) or nameof(ProfileName))
        {
            OnPropertyChanged(nameof(QuickStartDurationMinutes)); OnPropertyChanged(nameof(QuickStartSummary)); OnPropertyChanged(nameof(StartFocusButtonText)); OnPropertyChanged(nameof(ScheduleEndPreview));
        }
        if (name is nameof(WebsiteRuleInput) or nameof(WebsiteAction)) { OnPropertyChanged(nameof(WebsiteValidationText)); OnPropertyChanged(nameof(IsWebsiteValid)); OnPropertyChanged(nameof(HasWebsiteValidationError)); }
        if (name is nameof(ServiceHealthLabel) or nameof(IsProtectionActionable) or nameof(ProtectionSummaryText)) { OnPropertyChanged(nameof(CompactHasWarning)); OnPropertyChanged(nameof(CompactWarningText)); }
        if (name == nameof(IsEnforcementEnabled)) OnPropertyChanged(nameof(ScheduleEnforcementPreview));
        if (name is nameof(IsActive) or nameof(IsBusy) or nameof(IsSessionStateFresh))
        {
            (SaveScheduleCommand as AsyncDelegateCommand)?.RaiseCanExecuteChanged();
            (RefreshApplicationsCommand as AsyncDelegateCommand)?.RaiseCanExecuteChanged();
        }
    }
}
