using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using FocusGuard.Client;
using FocusGuard.Client.Preferences;
using FocusGuard.Client.State;
using FocusGuard.Client.Workflows;
using FocusGuard.Contracts;
using FocusGuard.Desktop.Resources;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Desktop;

public enum NavigationSection
{
    Focus,
    Profiles,
    Schedule,
    Activity,
    Settings
}

/// <summary>
/// How much of the blocking machinery is actually answering. Kept as four named states
/// rather than a bool because "ready", "blocking now", "one channel down" and "nothing
/// is enforced" call for different words, different colours and different actions.
/// </summary>
public enum ProtectionState
{
    Ready,
    Protected,
    Degraded,
    Unavailable
}

/// <summary>
/// A duration choice rendered as a preset pill. The custom ("Other") choice keeps
/// its identity while its duration changes so the selection binding stays stable.
/// </summary>
public sealed class DurationPresetViewModel(
    string label,
    TimeSpan duration,
    bool isCustom = false) : INotifyPropertyChanged
{
    private TimeSpan _duration = duration;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label { get; } = label;

    public bool IsCustom { get; } = isCustom;

    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            if (_duration == value)
            {
                return;
            }

            _duration = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Duration)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShortLabel)));
        }
    }

    /// <summary>Pill copy: compact enough that four choices fit one row.</summary>
    public string ShortLabel => IsCustom
        ? UiStrings.DurationCustom
        : string.Format(UiStrings.DurationMinutesShort, (int)_duration.TotalMinutes);
}

/// <summary>One run of the header metric: either digits or a trailing unit set smaller.</summary>
public sealed record MetricSegmentViewModel(string Text, bool IsUnit);

public sealed record SessionModeChoiceViewModel(string Label, SessionMode Mode);

public sealed record CycleTerminationChoiceViewModel(string Label, CycleTerminationMode Mode);

public sealed class BreakRuleViewModel(
    string display,
    bool isWebsite,
    Guid id,
    Func<Guid, Task> remove)
{
    private readonly AsyncRuleCommand _removeCommand = new(
        () => remove(id),
        () => true);

    public string Display { get; } = display;

    public bool IsWebsite { get; } = isWebsite;

    /// <summary>Same globe and window glyphs the contextual column uses, so an
    /// exception is recognisable as the site or app it came from.</summary>
    public string Glyph => IsWebsite ? "\uE774" : "\uECAA";

    public ICommand RemoveCommand => _removeCommand;
}

/// <summary>
/// A profile shown as a 24 px accent square. The swatch row doubles as the profile
/// switcher, which is why the accent lives on the profile rather than in the theme.
/// </summary>
public sealed record ProfileSwatchViewModel(
    StoredProfile Profile,
    IBrush AccentBrush,
    IBrush IdleBrush,
    bool IsSelected)
{
    public string Name => Profile.Name;

    /// <summary>Only the active profile shows its colour; the rest read as neutral chips.</summary>
    public IBrush SwatchBrush => IsSelected ? AccentBrush : IdleBrush;
}

/// <summary>
/// One row of the contextual column: a site or an app the profile blocks, optionally
/// carrying how many times it was turned away in the running session. Sites and apps
/// share one list because the column answers a single question — what is off limits —
/// and splitting it in two only reintroduces the pair of panels this replaced.
/// </summary>
public sealed record BlockedItemViewModel(string Label, bool IsWebsite, int Attempts)
{
    /// <summary>Segoe Fluent glyph: globe for sites, app tile for processes.</summary>
    public string Glyph => IsWebsite ? "\uE774" : "\uECAA";

    /// <summary>A zero is nothing worth reading, so the count column stays empty.</summary>
    public bool HasAttempts => Attempts > 0;

    public string AttemptsText => Attempts.ToString(CultureInfo.CurrentCulture);
}

/// <summary>
/// One saved cycle, presented as a selectable card. The card carries its own summary
/// and configuration so the list can show every program at once; the previous shelf
/// could only describe whichever program happened to be selected.
/// </summary>
public sealed class CyclePresetCardViewModel(
    StoredCyclePreset preset,
    string lengthText,
    string compositionText,
    bool isSelected,
    Action<StoredCyclePreset> select) : INotifyPropertyChanged
{
    private bool _isSelected = isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public StoredCyclePreset Preset { get; } = preset;

    public Guid Id => Preset.Id;

    public string Name => Preset.Name;

    public FocusCycleConfiguration Configuration => Preset.Configuration;

    /// <summary>Only the owner's own programs can be edited, so the list says which.</summary>
    public bool IsCustom => !Preset.IsBuiltIn;

    public string LengthText { get; } = lengthText;

    public string CompositionText { get; } = compositionText;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            if (value)
            {
                select(Preset);
            }
        }
    }
}

public sealed record ActivityEventViewModel(
    string KindLabel,
    string Target,
    string Outcome,
    string LocalTimeText,
    string RelativeTimeText = "",
    bool IsWebsite = false)
{
    /// <summary>Segoe Fluent glyph: globe for sites, app tile for processes.</summary>
    public string Glyph => IsWebsite ? "\uE774" : "\uECAA";
}

public sealed class SavedApplicationRuleViewModel(
    ApplicationRule rule,
    Func<Guid, Task> remove,
    Func<bool> canRemove)
{
    private readonly AsyncRuleCommand _removeCommand = new(() => remove(rule.Id), canRemove);

    public ApplicationRule Rule { get; } = rule;
    public string DisplayName => ApplicationPresentation.Name(Rule.Value);
    public string ActionLabel => Rule.Action == RuleAction.Block ? UiStrings.RuleBlock : UiStrings.RuleAllow;
    public Avalonia.Media.Imaging.Bitmap? Icon => ApplicationIconCache.Get(ApplicationPresentation.PathFor(Rule.Value));
    public bool HasIcon => Icon is not null;
    public string MatchLabel => Rule.MatchKind.ToString();

    public ICommand RemoveCommand => _removeCommand;

    public void RefreshCanRemove() => _removeCommand.RaiseCanExecuteChanged();
}

public sealed class SavedWebsiteRuleViewModel(
    WebsiteRule rule,
    Func<Guid, Task> remove,
    Func<bool> canRemove)
{
    private readonly AsyncRuleCommand _removeCommand = new(() => remove(rule.Id), canRemove);

    public WebsiteRule Rule { get; } = rule;
    public string ActionLabel => Rule.Action == RuleAction.Block ? UiStrings.RuleBlock : UiStrings.RuleAllow;

    public ICommand RemoveCommand => _removeCommand;

    public void RefreshCanRemove() => _removeCommand.RaiseCanExecuteChanged();
}

public sealed class ProfileSuggestionViewModel(
    string label,
    IReadOnlyList<string> domains,
    Action changed) : INotifyPropertyChanged
{
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label { get; } = label;

    public IReadOnlyList<string> Domains { get; } = domains;

    public string DomainSummary => string.Join(" · ", Domains);

    public string SelectionGlyph => IsSelected ? "\uE73E" : string.Empty;

    /// <summary>Animated width of the check column; 0 collapses it out of the chip.</summary>
    public double CheckWidth => IsSelected ? 18d : 0d;

    public double CheckOpacity => IsSelected ? 1d : 0d;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionGlyph)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CheckWidth)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CheckOpacity)));
            changed();
        }
    }

    public void Reset() => IsSelected = false;
}

/// <summary>
/// One swatch in the Zen colour palette. The option owns its own selection flag so
/// the grid can show the check mark without the view re-querying the whole list.
/// </summary>
public sealed class ProfileColorOptionViewModel : INotifyPropertyChanged
{
    private readonly AsyncRuleCommand _selectCommand;
    private bool _isSelected;

    public ProfileColorOptionViewModel(string hex, Func<string, Task> select)
    {
        Hex = hex;
        Swatch = FocusViewModel.CreateFrozenBrush(hex);
        _selectCommand = new AsyncRuleCommand(() => select(hex), () => true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Hex { get; }

    public IBrush Swatch { get; }

    public ICommand SelectCommand => _selectCommand;

    public string SelectionGlyph => IsSelected ? "" : string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionGlyph)));
        }
    }
}

public sealed class NewProfileRuleDraftViewModel(
    Guid id,
    string display,
    ApplicationRule? applicationRule,
    WebsiteRule? websiteRule,
    Func<Guid, Task> remove)
{
    private readonly AsyncRuleCommand _removeCommand = new(() => remove(id), () => true);

    public Guid Id { get; } = id;
    public string Display { get; } = display;
    public ApplicationRule? ApplicationRule { get; } = applicationRule;
    public WebsiteRule? WebsiteRule { get; } = websiteRule;
    public string KindLabel => ApplicationRule is null ? UiStrings.WebsitesLabel : UiStrings.ApplicationsLabel;
    public ICommand RemoveCommand => _removeCommand;
}

public sealed partial class FocusViewModel : INotifyPropertyChanged
{
    private readonly IFocusControlClient _client;
    private readonly ISessionWorkflow _sessionWorkflow;
    private SessionConnectionSnapshot _connectionSnapshot = SessionConnectionSnapshot.Initial;
    private readonly IProfileWorkflow _profileStore;
    private readonly TimeProvider _timeProvider;
    private readonly AsyncDelegateCommand _startFocusCommand;
    private readonly AsyncDelegateCommand _confirmStrictCommand;
    private readonly AsyncDelegateCommand _cancelStrictCommand;
    private readonly AsyncDelegateCommand _stopFocusCommand;
    private readonly AsyncDelegateCommand _addWebsiteRuleCommand;
    private readonly AsyncDelegateCommand _addApplicationRuleCommand;
    private readonly AsyncDelegateCommand _duplicateProfileCommand;
    private readonly AsyncDelegateCommand _renameProfileCommand;
    private readonly AsyncDelegateCommand _deleteProfileCommand;
    private readonly AsyncDelegateCommand _confirmProfileDeleteCommand;
    private readonly AsyncDelegateCommand _cancelProfileDeleteCommand;
    private readonly AsyncDelegateCommand _newProfileCommand;
    private readonly AsyncDelegateCommand _addNewProfileCustomRuleCommand;
    private readonly AsyncDelegateCommand _retryProtectionCommand;
    private readonly AsyncDelegateCommand _createProfileCommand;
    private readonly AsyncDelegateCommand _cancelCreateProfileCommand;
    private readonly AsyncDelegateCommand _skipBreakCommand;
    private readonly AsyncDelegateCommand _createCyclePresetCommand;
    private readonly AsyncDelegateCommand _editCyclePresetCommand;
    private readonly AsyncDelegateCommand _commitCyclePresetCommand;
    private readonly AsyncDelegateCommand _cancelCyclePresetEditCommand;
    private readonly AsyncDelegateCommand _saveCyclePresetCommand;
    private readonly AsyncDelegateCommand _deleteCyclePresetCommand;
    private readonly AsyncDelegateCommand _addBreakApplicationCommand;
    private readonly AsyncDelegateCommand _addBreakWebsiteCommand;
    private DurationPresetViewModel _selectedDurationPreset;
    private StoredCyclePreset? _selectedCyclePreset;
    private bool _isCycleMode;
    private bool _isEditingCyclePreset;
    private string _cyclePresetNameInput = string.Empty;
    private int _cycleFocusMinutes = 25;
    private int _cycleShortBreakMinutes = 5;
    private int _cycleLongBreakMinutes = 15;
    private int _cycleLongBreakFrequency = 4;
    private int _cycleCount = 4;
    private int _cycleTotalDurationMinutes = 120;
    private CycleTerminationMode _cycleTerminationMode = CycleTerminationMode.CycleCount;
    private string _breakApplicationInput = string.Empty;
    private string _breakWebsiteInput = string.Empty;
    private ProfileCycleSettings? _profileCycleSettings;
    private string? _profileCycleSettingsRevision;
    private PrepareSessionResponse? _pendingPreparation;
    private Guid? _pendingPreparationId;
    private RecoveryCredential? _pendingRecoveryCredential;
    private bool _isStrictMode;
    private bool _isEnforcementEnabled;
    private bool _isStrictReviewVisible;
    private bool _isBusy;
    private bool _isActive;
    private bool _activeStrictMode;
    private bool _isEnforcementConnected;
    /// <summary>
    /// The two protection channels reported separately by the service, so a failure can
    /// name the component that failed instead of reporting a single anonymous "degraded".
    /// </summary>
    private bool _isServiceReady = true;
    private bool _isExtensionReady = true;
    private SessionMode _activeMode = SessionMode.SimpleTimer;
    private PhaseStatus? _currentPhase;
    private PhaseStatus? _nextPhase;
    private string _typedConfirmation = string.Empty;
    private string _requiredConfirmationText = string.Empty;
    private int _strictWizardStep = 1;
    private bool _isStrictLimitsVisible;
    private string _recoveryCodeDisplay = string.Empty;
    private string _statusMessage = UiStrings.Get("ConnectingService");
    private string _serviceHealthLabel = UiStrings.Get("ServiceStatusPending");
    private string _remainingText = "00:00";
    /// <summary>Blocked attempts in the running session, keyed by audit target.</summary>
    private IReadOnlyDictionary<string, int> _blockedAttemptsByTarget =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private string _exactEndText = UiStrings.NoActiveEndTime;
    private StoredProfile? _selectedProfile;
    private bool _profilesLoaded;
    private bool _isActivityVisible;
    private bool _isSettingsVisible;
    private NavigationSection _section = NavigationSection.Focus;
    private string _websiteRuleInput = string.Empty;
    private string _applicationRuleInput = string.Empty;
    private string _profileNameInput = string.Empty;
    private readonly DurationPresetViewModel _customPreset;
    private int _customDurationMinutes = 45;
    private DateTimeOffset? _sessionStart;
    private DateTimeOffset? _sessionEnd;
    private long _sessionTotalSeconds;
    private long _remainingSeconds;
    private int _activityPollTicks;
    private int _blockedAttempts;
    private Guid? _runningSessionId;
    private Guid _lastApplicationBlockId;
    private readonly DesktopSettingsStore _settingsStore;
    private readonly CyclePhaseTransitionTracker _cyclePhaseTransitionTracker = new();
    private readonly IStartupRegistration? _startupRegistration;
    private DesktopSettings _settings;
    private bool _startsWithWindows;
    private bool _showAllWebsiteRules;
    private bool _showAllApplicationRules;
    private bool _areWebsiteChipsExpanded;
    private bool _areApplicationChipsExpanded;
    private bool _isDeleteProfileConfirmationVisible;
    private bool _isCreatingProfile;
    private bool _isCyclePresetEditorVisible;
    private Guid? _editedCyclePresetId;
    private string? _editedCyclePresetRevision;
    private string _newProfileNameInput = string.Empty;
    private string _newProfileAccentHex = ZenPalette[0];
    private string _newProfileCustomRuleInput = string.Empty;
    private string _newProfileRuleValidationMessage = string.Empty;
    private IBrush _profileAccentBrush = DefaultAccentBrush;

    /// <summary>Chips shown before the overflow affordance takes over.</summary>
    private const int VisibleChipLimit = 5;

    /// <summary>
    /// Fallback accents for profiles saved before <c>AccentHex</c> existed. Picking by
    /// profile id rather than list position keeps a profile's colour stable when
    /// another profile is renamed and the alphabetical order shifts.
    /// </summary>
    private static readonly string[] AccentPalette =
    [
        "#CBA6F7",
        "#94E2D5",
        "#F9E2AF",
        "#89B4FA",
        "#F38BA8",
        "#A6E3A1"
    ];

    /// <summary>
    /// The accents a profile can be given: the fourteen Catppuccin accents, ordered
    /// around the wheel so the grid reads as a gradient rather than a random set.
    /// Stored form is always the Mocha hex; <see cref="CatppuccinAccents"/> maps it to
    /// the flavour in use before anything is painted.
    /// </summary>
    private static IReadOnlyList<string> ZenPalette => CatppuccinAccents.PickerHexes;

    private static SolidColorBrush DefaultAccentBrush { get; } = CreateFrozenBrush("#CBA6F7");

    private static SolidColorBrush DefaultMetricBrush { get; } = CreateFrozenBrush("#CDD6F4");

    private static SolidColorBrush SwatchIdleBrush { get; } = CreateFrozenBrush("#313244");

    public FocusViewModel(
        IFocusControlClient? client = null,
        IProfileStore? profileStore = null,
        TimeProvider? timeProvider = null,
        DesktopSettingsStore? settingsStore = null,
        IStartupRegistration? startupRegistration = null)
    {
        _client = client ?? new NamedPipeFocusControlClient();
        _settingsStore = settingsStore ?? new DesktopSettingsStore(persist: client is null);
        _settings = _settingsStore.Load();
        _isEnforcementEnabled = _settings.EnforcementEnabled;
        _startupRegistration = startupRegistration;
        _startsWithWindows = startupRegistration?.IsEnabled ?? false;
        _profileStore = new ProfileWorkflow(profileStore ?? new SqliteProfileStore(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FocusGuard",
                "profiles.db")));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _sessionWorkflow = new SessionWorkflow(_client, _profileStore, _timeProvider);
        DurationPresets =
        [
            new(UiStrings.Get("Duration25"), TimeSpan.FromMinutes(25)),
            new(UiStrings.Get("Duration50"), TimeSpan.FromMinutes(50)),
            new(UiStrings.Get("Duration90"), TimeSpan.FromMinutes(90)),
            new(UiStrings.Get("DurationTwoTest"), TimeSpan.FromMinutes(2))
        ];
        _selectedDurationPreset = DurationPresets[1];
        _customPreset = new(UiStrings.DurationCustom, TimeSpan.FromMinutes(_customDurationMinutes), isCustom: true);
        DurationChoices = [.. DurationPresets, _customPreset];
        CyclePresets = [.. BuiltInCyclePresets.All];
        _selectedCyclePreset = CyclePresets[0];
        RebuildCyclePresetCards();
        _startFocusCommand = new AsyncDelegateCommand(StartFocusAsync, () => CanStart);
        _confirmStrictCommand = new AsyncDelegateCommand(ConfirmStrictAsync, () => CanConfirmStrict);
        _cancelStrictCommand = new AsyncDelegateCommand(CancelStrictReviewAsync, () => CanCancelStrict);
        _stopFocusCommand = new AsyncDelegateCommand(StopFocusAsync, () => CanStop);
        _retryProtectionCommand = new AsyncDelegateCommand(
            () => RefreshAsync(CancellationToken.None),
            () => IsProtectionActionable);
        _addWebsiteRuleCommand = new AsyncDelegateCommand(AddWebsiteRuleAsync, () => SelectedProfile is not null);
        _addApplicationRuleCommand = new AsyncDelegateCommand(AddApplicationRuleAsync, () => SelectedProfile is not null);
        _duplicateProfileCommand = new AsyncDelegateCommand(
            DuplicateSelectedProfileAsync,
            () => CanEditProfileRules);
        _renameProfileCommand = new AsyncDelegateCommand(
            RenameSelectedProfileAsync,
            () => CanRenameProfile);
        _deleteProfileCommand = new AsyncDelegateCommand(
            RequestProfileDeleteAsync,
            () => CanDeleteProfile);
        _confirmProfileDeleteCommand = new AsyncDelegateCommand(
            ConfirmProfileDeleteAsync,
            () => CanConfirmProfileDelete);
        _cancelProfileDeleteCommand = new AsyncDelegateCommand(
            CancelProfileDeleteAsync,
            () => IsDeleteProfileConfirmationVisible);
        _newProfileCommand = new AsyncDelegateCommand(
            BeginCreateProfileAsync,
            () => CanEditProfileRules);
        _addNewProfileCustomRuleCommand = new AsyncDelegateCommand(
            AddNewProfileCustomRuleAsync,
            () => IsCreatingProfile && !IsBusy);
        _createProfileCommand = new AsyncDelegateCommand(
            CreateProfileAsync,
            () => CanCreateProfile);
        _cancelCreateProfileCommand = new AsyncDelegateCommand(
            CancelCreateProfileAsync,
            () => IsCreatingProfile && !IsBusy);
        _skipBreakCommand = new AsyncDelegateCommand(SkipBreakAsync, () => CanSkipBreak);
        _createCyclePresetCommand = new AsyncDelegateCommand(
            BeginCreateCyclePresetAsync,
            () => CanEditCyclePreset);
        _editCyclePresetCommand = new AsyncDelegateCommand(
            BeginEditCyclePresetAsync,
            () => CanDeleteCyclePreset);
        _commitCyclePresetCommand = new AsyncDelegateCommand(
            CommitCyclePresetAsync,
            () => CanCommitCyclePreset);
        _cancelCyclePresetEditCommand = new AsyncDelegateCommand(
            CancelCyclePresetEditAsync,
            () => IsCyclePresetEditorVisible && !IsBusy);
        _saveCyclePresetCommand = new AsyncDelegateCommand(SaveCyclePresetAsync, () => CanSaveCyclePreset);
        _deleteCyclePresetCommand = new AsyncDelegateCommand(DeleteCyclePresetAsync, () => CanDeleteCyclePreset);
        _addBreakApplicationCommand = new AsyncDelegateCommand(AddBreakApplicationAsync, () => CanEditBreakExceptions);
        _addBreakWebsiteCommand = new AsyncDelegateCommand(AddBreakWebsiteAsync, () => CanEditBreakExceptions);
        NewProfileColors =
        [
            .. ZenPalette.Select(hex => new ProfileColorOptionViewModel(hex, SelectNewProfileColorAsync))
        ];
        ProfileColors =
        [
            .. ZenPalette.Select(hex => new ProfileColorOptionViewModel(hex, ApplyProfileColorAsync))
        ];
        NewProfileSuggestions =
        [
            new("Instagram", ["instagram.com"], OnNewProfileSuggestionChanged),
            new("X / Twitter", ["x.com", "twitter.com"], OnNewProfileSuggestionChanged),
            new("YouTube", ["youtube.com"], OnNewProfileSuggestionChanged),
            new("TikTok", ["tiktok.com"], OnNewProfileSuggestionChanged),
            new("Facebook", ["facebook.com"], OnNewProfileSuggestionChanged),
            new("Reddit", ["reddit.com"], OnNewProfileSuggestionChanged),
            new("Netflix", ["netflix.com"], OnNewProfileSuggestionChanged),
            new("Twitch", ["twitch.tv"], OnNewProfileSuggestionChanged)
        ];
        InitializeWorkflows();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<StoredProfile> Profiles { get; } = [];

    public ObservableCollection<ActivityEventViewModel> ActivityEvents { get; } = [];

    public ObservableCollection<SavedApplicationRuleViewModel> SavedApplicationRules { get; } = [];

    public ObservableCollection<SavedWebsiteRuleViewModel> SavedWebsiteRules { get; } = [];

    public ObservableCollection<ActivityEventViewModel> RecentBlocks { get; } = [];
    public ObservableCollection<NewProfileRuleDraftViewModel> NewProfileDraftRules { get; } = [];
    public IReadOnlyList<ProfileSuggestionViewModel> NewProfileSuggestions { get; }

    /// <summary>Zen palette shown while creating a profile.</summary>
    public IReadOnlyList<ProfileColorOptionViewModel> NewProfileColors { get; }

    /// <summary>Zen palette shown while editing the selected profile.</summary>
    public IReadOnlyList<ProfileColorOptionViewModel> ProfileColors { get; }

    public string NewProfileAccentHex
    {
        get => _newProfileAccentHex;
        private set
        {
            if (SetField(ref _newProfileAccentHex, value))
            {
                foreach (var option in NewProfileColors)
                {
                    option.IsSelected = string.Equals(option.Hex, value, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    public NavigationSection Section
    {
        get => _section;
        private set
        {
            if (_section == value)
            {
                return;
            }

            _section = value;
            _isActivityVisible = value == NavigationSection.Activity;
            OnPropertyChanged(nameof(Section));
            OnPropertyChanged(nameof(IsActivityVisible));
            OnPropertyChanged(nameof(IsFocusVisible));
            OnPropertyChanged(nameof(IsProfilesVisible));
            OnPropertyChanged(nameof(IsScheduleVisible));
        }
    }

    public bool IsActivityVisible => _isActivityVisible;

    public bool IsFocusVisible => Section == NavigationSection.Focus;

    public bool IsProfilesVisible => Section == NavigationSection.Profiles;

    public bool IsScheduleVisible => Section == NavigationSection.Schedule;

    public bool IsSettingsVisible
    {
        get => _isSettingsVisible;
        private set => SetField(ref _isSettingsVisible, value);
    }

    /// <summary>
    /// Profiles and Schedule are locked while a session runs: the honest way to say
    /// "edits do not apply now", instead of a warning line at the bottom of the page.
    /// </summary>
    public bool IsNavigationLocked => IsActive;

    public bool CanNavigateToEditors => !IsActive;

    public bool IsActivityEmpty => ActivityEvents.Count == 0;

    public bool HasRecentBlocks => RecentBlocks.Count > 0;

    public StoredProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetField(ref _selectedProfile, value))
            {
                OnPropertyChanged(nameof(ProfileName));
                OnPropertyChanged(nameof(SessionHeadlineText));
                OnPropertyChanged(nameof(WebsiteRuleCount));
                OnPropertyChanged(nameof(ApplicationRuleCount));
                NotifyEnforcementPresentationChanged();
                OnPropertyChanged(nameof(SessionTitle));
                OnPropertyChanged(nameof(SessionMeta));
                OnPropertyChanged(nameof(HeaderSubtitle));
                ApplyResolvedTheme();
                OnPropertyChanged(nameof(UsesProfileThemeOverride));
                OnPropertyChanged(nameof(IsProfileLightTheme));
                SyncProfileColorSelection();
                LoadSelectedRuleRows();
                IsDeleteProfileConfirmationVisible = false;
                _profileNameInput = value?.Name ?? string.Empty;
                OnPropertyChanged(nameof(ProfileNameInput));
                OnPropertyChanged(nameof(ProfileNameValidationMessage));
                OnPropertyChanged(nameof(CanRenameProfile));
                OnPropertyChanged(nameof(CanDeleteProfile));
                OnPropertyChanged(nameof(DeleteProfileConfirmationText));
                OnPropertyChanged(nameof(CanStart));
                if (_profilesLoaded && value is not null)
                {
                    _ = RefreshSelectedProfileCycleSettingsAsync(value.Id);
                }
                NotifyCommands();
            }
        }
    }

    public string ProfileName => SelectedProfile?.Name ?? UiStrings.NoProfileSelected;

    public int WebsiteRuleCount => SelectedProfile?.WebsiteRules.Count ?? 0;

    public int ApplicationRuleCount => SelectedProfile?.ApplicationRules.Count ?? 0;

    public bool HasSavedApplicationRules => SavedApplicationRules.Count > 0;

    public bool HasSavedWebsiteRules => SavedWebsiteRules.Count > 0;

    public string WebsiteRuleInput { get => _websiteRuleInput; set => SetField(ref _websiteRuleInput, value); }
    public string ApplicationRuleInput { get => _applicationRuleInput; set => SetField(ref _applicationRuleInput, value); }
    public ICommand AddWebsiteRuleCommand => _addWebsiteRuleCommand;
    public ICommand AddApplicationRuleCommand => _addApplicationRuleCommand;
    public ICommand DuplicateProfileCommand => _duplicateProfileCommand;
    public ICommand RenameProfileCommand => _renameProfileCommand;
    public ICommand DeleteProfileCommand => _deleteProfileCommand;
    public ICommand ConfirmProfileDeleteCommand => _confirmProfileDeleteCommand;
    public ICommand CancelProfileDeleteCommand => _cancelProfileDeleteCommand;
    public ICommand NewProfileCommand => _newProfileCommand;
    public ICommand AddNewProfileCustomRuleCommand => _addNewProfileCustomRuleCommand;
    public ICommand CreateProfileCommand => _createProfileCommand;
    public ICommand CancelCreateProfileCommand => _cancelCreateProfileCommand;

    public string ProfileNameInput
    {
        get => _profileNameInput;
        set
        {
            if (SetField(ref _profileNameInput, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(ProfileNameValidationMessage));
                OnPropertyChanged(nameof(CanRenameProfile));
                _renameProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ProfileNameValidationMessage
    {
        get
        {
            var result = Profile.ValidateName(ProfileNameInput);
            if (!result.IsValid)
            {
                return result.Errors[0].Code switch
                {
                    "profile.name.too_long" => UiStrings.ProfileNameTooLong,
                    _ => UiStrings.ProfileNameRequired
                };
            }

            if (SelectedProfile is null)
            {
                return UiStrings.ProfileNameRequired;
            }

            if (string.Equals(result.Value, SelectedProfile.Name, StringComparison.CurrentCulture))
            {
                return string.Empty;
            }

            return ProfileWorkflow.HasNameConflict(Profiles, result.Value!, SelectedProfile.Id)
                ? UiStrings.ProfileNameAlreadyExists
                : string.Empty;
        }
    }

    public bool CanRenameProfile
    {
        get
        {
            var result = Profile.ValidateName(ProfileNameInput);
            return CanEditProfileRules
                && result.IsValid
                && !string.Equals(result.Value, SelectedProfile?.Name, StringComparison.CurrentCulture)
                && string.IsNullOrEmpty(ProfileNameValidationMessage);
        }
    }

    public bool CanDeleteProfile =>
        CanEditProfileRules
        && Profiles.Count > 1;

    public bool IsDeleteProfileConfirmationVisible
    {
        get => _isDeleteProfileConfirmationVisible;
        private set
        {
            if (SetField(ref _isDeleteProfileConfirmationVisible, value))
            {
                OnPropertyChanged(nameof(CanConfirmProfileDelete));
                _confirmProfileDeleteCommand.RaiseCanExecuteChanged();
                _cancelProfileDeleteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanConfirmProfileDelete =>
        IsDeleteProfileConfirmationVisible
        && CanDeleteProfile;

    public string DeleteProfileConfirmationText => SelectedProfile is null
        ? string.Empty
        : string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.DeleteProfileConfirmation,
            SelectedProfile.Name,
            SelectedProfile.ApplicationRules.Count,
            SelectedProfile.WebsiteRules.Count);

    public bool IsCreatingProfile
    {
        get => _isCreatingProfile;
        private set
        {
            if (SetField(ref _isCreatingProfile, value))
            {
                OnPropertyChanged(nameof(CanCreateProfile));
                NotifyCommands();
            }
        }
    }

    public string NewProfileNameInput
    {
        get => _newProfileNameInput;
        set
        {
            if (SetField(ref _newProfileNameInput, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(NewProfileNameValidationMessage));
                OnPropertyChanged(nameof(CanCreateProfile));
                _createProfileCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewProfileNameValidationMessage
    {
        get
        {
            var result = Profile.ValidateName(NewProfileNameInput);
            if (!result.IsValid)
            {
                return result.Errors[0].Code == "profile.name.too_long"
                    ? UiStrings.ProfileNameTooLong
                    : UiStrings.ProfileNameRequired;
            }

            return ProfileWorkflow.HasNameConflict(Profiles, result.Value!)
                ? UiStrings.ProfileNameAlreadyExists
                : string.Empty;
        }
    }

    public string NewProfileCustomRuleInput
    {
        get => _newProfileCustomRuleInput;
        set
        {
            if (SetField(ref _newProfileCustomRuleInput, value ?? string.Empty))
            {
                NewProfileRuleValidationMessage = string.Empty;
            }
        }
    }

    public string NewProfileRuleValidationMessage
    {
        get => _newProfileRuleValidationMessage;
        private set => SetField(ref _newProfileRuleValidationMessage, value);
    }

    public int NewProfileWebsiteRuleCount =>
        BuildNewProfileWebsiteRules().Count;

    public int NewProfileApplicationRuleCount =>
        NewProfileDraftRules.Count(rule => rule.ApplicationRule is not null);

    public string NewProfileSummaryText => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.NewProfileSummary,
        NewProfileWebsiteRuleCount,
        NewProfileApplicationRuleCount);

    public bool CanCreateProfile =>
        IsSessionStateFresh
        && IsCreatingProfile
        && CanNavigateToEditors
        && !IsBusy
        && Profile.ValidateName(NewProfileNameInput).IsValid
        && string.IsNullOrEmpty(NewProfileNameValidationMessage);

    public IReadOnlyList<DurationPresetViewModel> DurationPresets { get; }

    /// <summary>Presets plus the custom choice; bound to the preset pill list.</summary>
    public IReadOnlyList<DurationPresetViewModel> DurationChoices { get; }

    public bool IsCustomDurationSelected => SelectedDurationPreset.IsCustom;

    public int CustomDurationMinutes
    {
        get => _customDurationMinutes;
        set
        {
            var clamped = Math.Clamp(value, 1, 12 * 60);
            if (SetField(ref _customDurationMinutes, clamped))
            {
                _customPreset.Duration = TimeSpan.FromMinutes(clamped);
                OnPropertyChanged(nameof(StrictEndTimeText));
                OnPropertyChanged(nameof(RingCenterValue));
                OnPropertyChanged(nameof(SelectedDurationMinutes));
                OnPropertyChanged(nameof(CanStart));
                NotifyCommands();
            }
        }
    }

    /// <summary>
    /// Edits the value shown below the preset row. A non-preset value selects the
    /// existing custom choice instead of mutating an immutable preset.
    /// </summary>
    public int SelectedDurationMinutes
    {
        get => (int)SelectedDurationPreset.Duration.TotalMinutes;
        set
        {
            var minutes = Math.Clamp(value, 1, 12 * 60);
            var matchingPreset = DurationPresets.FirstOrDefault(
                preset => (int)preset.Duration.TotalMinutes == minutes);
            if (matchingPreset is not null)
            {
                SelectedDurationPreset = matchingPreset;
                return;
            }

            CustomDurationMinutes = minutes;
            SelectedDurationPreset = _customPreset;
        }
    }

    public string EnforcementWarning => IsEnforcementEnabled
        ? UiStrings.Get("EnforcementOnWarning")
        : UiStrings.Get("EnforcementOffWarning");

    public bool IsEnforcementEnabled
    {
        get => _isEnforcementEnabled;
        set
        {
            if (SetField(ref _isEnforcementEnabled, value))
            {
                _settings = _settings with { EnforcementEnabled = value };
                _settingsStore.Save(_settings);
                OnPropertyChanged(nameof(EnforcementWarning));
                OnPropertyChanged(nameof(ActiveBlockingDescription));
                OnPropertyChanged(nameof(IsEnforcementDegraded));
                OnPropertyChanged(nameof(IsTimerOnly));
            }
        }
    }

    public string RequiredConfirmationText
    {
        get => _requiredConfirmationText;
        private set
        {
            if (SetField(ref _requiredConfirmationText, value))
            {
                OnPropertyChanged(nameof(StrictConfirmationPromptText));
                OnPropertyChanged(nameof(CanConfirmStrict));
            }
        }
    }

    public string StrictConfirmationPromptText =>
        string.Format(UiStrings.StrictConfirmationPrompt, RequiredConfirmationText);

    public DurationPresetViewModel SelectedDurationPreset
    {
        get => _selectedDurationPreset;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_selectedDurationPreset, value))
            {
                return;
            }

            _selectedDurationPreset = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StrictEndTimeText));
            OnPropertyChanged(nameof(StrictEndTimeDescription));
            OnPropertyChanged(nameof(IsCustomDurationSelected));
            OnPropertyChanged(nameof(RingCenterValue));
            OnPropertyChanged(nameof(RingCenterCaption));
            OnPropertyChanged(nameof(SelectedDurationMinutes));
            OnPropertyChanged(nameof(PlannedEndText));
            OnPropertyChanged(nameof(PlannedReleaseText));
            NotifyCommands();
        }
    }

    public bool IsStrictMode
    {
        get => _isStrictMode;
        set
        {
            if (_isStrictMode == value)
            {
                return;
            }

            _isStrictMode = value;
            CancelStrictReview();
            OnPropertyChanged();
            OnPropertyChanged(nameof(StrictModeDescription));
        }
    }

    public string StrictModeDescription => IsStrictMode
        ? UiStrings.Get("StrictModeDescription")
        : UiStrings.Get("NormalModeDescription");

    /// <summary>
    /// What turning the switch on will cost, in one line, stated before it is thrown
    /// rather than discovered when the stop button turns out to be missing.
    /// </summary>
    public string StrictModeConsequence => UiStrings.StrictModeConsequence;

    public bool IsStrictReviewVisible
    {
        get => _isStrictReviewVisible;
        private set => SetField(ref _isStrictReviewVisible, value);
    }

    public string RecoveryCodeDisplay
    {
        get => _recoveryCodeDisplay;
        private set => SetField(ref _recoveryCodeDisplay, value);
    }

    /// <summary>
    /// Which half of the strict wizard is showing. The two steps replace the pair of
    /// acknowledgement checkboxes that used to gate activation: a checkbox verifies
    /// nothing, whereas having to pass the recovery code before the consequences are
    /// even shown is a deliberate act in itself.
    /// </summary>
    public int StrictWizardStep
    {
        get => _strictWizardStep;
        private set
        {
            if (SetField(ref _strictWizardStep, value))
            {
                OnPropertyChanged(nameof(IsStrictStepOne));
                OnPropertyChanged(nameof(IsStrictStepTwo));
                OnPropertyChanged(nameof(CanConfirmStrict));
                NotifyCommands();
            }
        }
    }

    public bool IsStrictStepOne => StrictWizardStep <= 1;

    public bool IsStrictStepTwo => StrictWizardStep >= 2;

    public void AdvanceStrictStep() => StrictWizardStep = 2;

    /// <summary>
    /// Every line the wizard quotes is derived from the session about to start, so it
    /// is recomputed when the review opens rather than whenever the bindings happened
    /// to be evaluated first.
    /// </summary>
    private void NotifyStrictWizardCopyChanged()
    {
        OnPropertyChanged(nameof(StrictLockTitle));
        OnPropertyChanged(nameof(StrictLockSubtitle));
        OnPropertyChanged(nameof(StrictConfirmButtonText));
        OnPropertyChanged(nameof(StrictRecoveryWarning));
        OnPropertyChanged(nameof(StrictAppClosureWarning));
        OnPropertyChanged(nameof(HasStrictAppClosures));
    }

    public void ReturnToStrictStepOne() => StrictWizardStep = 1;

    /// <summary>
    /// The honest limits of strict mode, opened from step two. It is a sheet rather
    /// than permanent copy because the list is long and the claim it qualifies is
    /// already stated in one line above it.
    /// </summary>
    public bool IsStrictLimitsVisible
    {
        get => _isStrictLimitsVisible;
        set => SetField(ref _isStrictLimitsVisible, value);
    }

    // ---- Strict wizard copy -----------------------------------------------

    /// <summary>
    /// Step two leads with the consequence, not with the feature name: the decision
    /// being made is "no exit until this time", and that is what the title says.
    /// </summary>
    public string StrictLockTitle => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.StrictLockTitle,
        FormatClock(StrictEndUtc));

    public string StrictLockSubtitle => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.StrictLockSubtitle,
        StrictDurationText,
        SavedWebsiteRules.Count,
        SavedApplicationRules.Count);

    /// <summary>The commitment is written inside the button that makes it.</summary>
    public string StrictConfirmButtonText => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.StrictConfirmButton,
        StrictDurationText);

    public string StrictRecoveryWarning => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.StrictRecoveryWarning,
        FormatClock(StrictEndUtc));

    /// <summary>
    /// Which of the profile's applications are running right now and will be closed.
    /// Naming them beats a generic "save your work", which nobody can act on.
    /// </summary>
    public string StrictAppClosureWarning => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.StrictAppClosureWarning,
        string.Join(", ", SavedApplicationRules.Select(rule => rule.Rule.Value)));

    public bool HasStrictAppClosures => SavedApplicationRules.Count > 0;

    private DateTimeOffset StrictEndUtc =>
        BuildCyclePreview()?.EndUtc
        ?? _timeProvider.GetUtcNow().Add(SelectedDurationPreset.Duration);

    /// <summary>The commitment as a length, rendered "2 h 45 min".</summary>
    private string StrictDurationText
    {
        get
        {
            var minutes = (int)Math.Round((StrictEndUtc - _timeProvider.GetUtcNow()).TotalMinutes);
            return minutes >= 60
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    UiStrings.FocusedHoursMinutes,
                    minutes / 60,
                    minutes % 60)
                : string.Format(CultureInfo.CurrentCulture, UiStrings.FocusedMinutesOnly, minutes);
        }
    }

    public string TypedConfirmation
    {
        get => _typedConfirmation;
        set
        {
            if (SetField(ref _typedConfirmation, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanConfirmStrict));
                NotifyCommands();
            }
        }
    }

    public string StrictEndTimeText =>
        (BuildCyclePreview()?.EndUtc.ToLocalTime()
            ?? _timeProvider.GetLocalNow().Add(SelectedDurationPreset.Duration))
        .ToString("g", CultureInfo.CurrentCulture);

    public string StrictEndTimeDescription =>
        string.Format(UiStrings.StrictReviewEndsExactly, StrictEndTimeText);

    public bool CanConfirmStrict =>
        IsStrictReviewVisible
        && IsStrictStepTwo
        && string.Equals(TypedConfirmation.Trim(), RequiredConfirmationText, StringComparison.Ordinal)
        && IsSessionStateFresh
        && (!IsCycleMode || _pendingPreparationId is not null)
        && !IsBusy;

    private FocusCyclePlan? BuildCyclePreview()
    {
        if (!_isCycleMode || _selectedCyclePreset is null)
        {
            return null;
        }

        var result = FocusCyclePlanBuilder.Build(
            _selectedCyclePreset.Configuration,
            _timeProvider.GetUtcNow());
        return result.IsValid ? result.Value : null;
    }

    private void LoadCycleEditor(StoredCyclePreset? preset)
    {
        if (preset is null)
        {
            IsEditingCyclePreset = false;
            return;
        }

        CyclePresetNameInput = preset.Name;
        LoadCycleEditorValues(preset.Configuration);
        IsEditingCyclePreset = !preset.IsBuiltIn;
    }

    private void SetCycleEditorValue(ref int field, int value, string propertyName)
    {
        if (SetField(ref field, value, propertyName))
        {
            OnPropertyChanged(nameof(CanSaveCyclePreset));
            OnPropertyChanged(nameof(CyclePresetDraftSummary));
            OnPropertyChanged(nameof(CycleCompositionText));
            OnPropertyChanged(nameof(CycleLongBreakText));
            OnPropertyChanged(nameof(CycleLengthText));
            OnPropertyChanged(nameof(CycleEndText));
            OnPropertyChanged(nameof(StrictEndTimeText));
            NotifyCommands();
        }
    }

    public FocusCycleConfiguration? SelectedCycleConfiguration => SelectedCyclePreset?.Configuration;

    private FocusCycleConfiguration? BuildEditedCycleConfiguration()
    {
        var result = FocusCycleConfiguration.Create(
            TimeSpan.FromMinutes(CycleFocusMinutes),
            TimeSpan.FromMinutes(CycleShortBreakMinutes),
            TimeSpan.FromMinutes(CycleLongBreakMinutes),
            CycleLongBreakFrequency,
            CycleTerminationMode,
            IsCycleCountTermination ? CycleCount : null,
            IsTotalDurationTermination ? TimeSpan.FromMinutes(CycleTotalDurationMinutes) : null);
        if (!result.IsValid)
        {
            StatusMessage = result.Errors[0].Message;
            return null;
        }

        return result.Value;
    }

    /// <summary>
    /// Opens the cycle editor on a fresh program. The currently selected preset seeds the
    /// numbers so building a variant of a built-in is a matter of changing one field
    /// rather than re-entering all six.
    /// </summary>
    public Task BeginCreateCyclePresetAsync()
    {
        if (!CanEditCyclePreset)
        {
            return Task.CompletedTask;
        }

        _editedCyclePresetId = null;
        _editedCyclePresetRevision = null;
        LoadCycleEditorValues(_selectedCyclePreset?.Configuration ?? DefaultCycleConfiguration);
        CyclePresetNameInput = UnusedCyclePresetName();
        IsCyclePresetEditorVisible = true;
        return Task.CompletedTask;
    }

    public Task BeginEditCyclePresetAsync()
    {
        if (!CanDeleteCyclePreset || SelectedCyclePreset is not { IsBuiltIn: false } selected)
        {
            return Task.CompletedTask;
        }

        _editedCyclePresetId = selected.Id;
        _editedCyclePresetRevision = CyclePresetRevision.Compute(selected);
        LoadCycleEditorValues(selected.Configuration);
        CyclePresetNameInput = selected.Name;
        IsCyclePresetEditorVisible = true;
        return Task.CompletedTask;
    }

    public Task CancelCyclePresetEditAsync()
    {
        IsCyclePresetEditorVisible = false;
        _editedCyclePresetId = null;
        _editedCyclePresetRevision = null;
        LoadCycleEditor(_selectedCyclePreset);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes the editor back, whether it was opened on a new program or an existing
    /// custom one, and leaves that program selected so the choice is its own preview.
    /// </summary>
    public async Task CommitCyclePresetAsync()
    {
        if (!CanCommitCyclePreset)
        {
            return;
        }

        var configuration = BuildEditedCycleConfiguration();
        if (configuration is null)
        {
            return;
        }

        var preset = new StoredCyclePreset(
            _editedCyclePresetId ?? Guid.NewGuid(),
            CyclePresetNameInput.Trim(),
            configuration,
            IsBuiltIn: false);
        var isNew = _editedCyclePresetId is null;

        IsBusy = true;
        try
        {
            var result = await _profileStore.TrySaveCyclePresetAsync(preset, isNew ? null : _editedCyclePresetRevision);
            if (result.Status != CyclePresetMutationStatus.Saved)
            {
                StatusMessage = CyclePresetMutationMessage(result.Status);
                if (result.Status is CyclePresetMutationStatus.Conflict or CyclePresetMutationStatus.Missing)
                {
                    await ReloadCyclePresetsAsync(preset.Id);
                    IsCyclePresetEditorVisible = false;
                    _editedCyclePresetId = null;
                    _editedCyclePresetRevision = null;
                    LoadCycleEditor(SelectedCyclePreset);
                }
                return;
            }
            await ReloadCyclePresetsAsync(preset.Id);
            await SaveCurrentProfileCycleSettingsAsync();
            StatusMessage = isNew ? UiStrings.CyclePresetCreated : UiStrings.CyclePresetSaved;
            IsCyclePresetEditorVisible = false;
            _editedCyclePresetId = null;
            _editedCyclePresetRevision = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static FocusCycleConfiguration DefaultCycleConfiguration { get; } =
        FocusCycleConfiguration.Create(
            TimeSpan.FromMinutes(25),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            4,
            CycleTerminationMode.CycleCount,
            cycleCount: 4).Value!;

    private void LoadCycleEditorValues(FocusCycleConfiguration configuration)
    {
        CycleFocusMinutes = (int)configuration.FocusDuration.TotalMinutes;
        CycleShortBreakMinutes = (int)configuration.ShortBreakDuration.TotalMinutes;
        CycleLongBreakMinutes = (int)configuration.LongBreakDuration.TotalMinutes;
        CycleLongBreakFrequency = configuration.CyclesBeforeLongBreak;
        CycleCount = configuration.CycleCount ?? 4;
        CycleTotalDurationMinutes = (int)(configuration.TotalDuration?.TotalMinutes ?? 120);
        CycleTerminationMode = configuration.TerminationMode;
    }

    /// <summary>
    /// "My cycle", then "My cycle 2", and so on: a new program never lands on a name
    /// the shelf already shows, which would make the two impossible to tell apart.
    /// </summary>
    private string UnusedCyclePresetName()
    {
        var baseName = UiStrings.NewCyclePresetDefaultName;
        var taken = CyclePresets
            .Select(preset => preset.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!taken.Contains(baseName))
        {
            return baseName;
        }

        for (var suffix = 2; suffix < 100; suffix++)
        {
            var candidate = $"{baseName} {suffix.ToString(CultureInfo.CurrentCulture)}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return baseName;
    }

    private async Task SaveCyclePresetAsync()
    {
        if (!CanSaveCyclePreset || SelectedCyclePreset is not { IsBuiltIn: false } selected)
        {
            return;
        }

        var configuration = BuildEditedCycleConfiguration();
        if (configuration is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var updated = selected with
            {
                Name = CyclePresetNameInput.Trim(),
                Configuration = configuration
            };
            var result = await _profileStore.TrySaveCyclePresetAsync(updated, CyclePresetRevision.Compute(selected));
            if (result.Status != CyclePresetMutationStatus.Saved)
            {
                StatusMessage = CyclePresetMutationMessage(result.Status);
                if (result.Status is CyclePresetMutationStatus.Conflict or CyclePresetMutationStatus.Missing)
                {
                    await ReloadCyclePresetsAsync(selected.Id);
                    LoadCycleEditor(SelectedCyclePreset);
                }
                return;
            }
            await ReloadCyclePresetsAsync(updated.Id);
            await SaveCurrentProfileCycleSettingsAsync();
            StatusMessage = UiStrings.CyclePresetSaved;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteCyclePresetAsync()
    {
        if (!CanDeleteCyclePreset || SelectedCyclePreset is not { IsBuiltIn: false } selected)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _profileStore.TryDeleteCyclePresetAsync(selected.Id, CyclePresetRevision.Compute(selected));
            if (result.Status != CyclePresetMutationStatus.Saved)
            {
                StatusMessage = CyclePresetMutationMessage(result.Status);
                if (result.Status is CyclePresetMutationStatus.Conflict or CyclePresetMutationStatus.Missing)
                    await ReloadCyclePresetsAsync(selected.Id);
                return;
            }
            await ReloadCyclePresetsAsync(null);
            await SaveCurrentProfileCycleSettingsAsync();
            StatusMessage = UiStrings.CyclePresetDeleted;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string CyclePresetMutationMessage(CyclePresetMutationStatus status) => status switch
    {
        CyclePresetMutationStatus.Conflict or CyclePresetMutationStatus.Missing => UiStrings.CyclePresetConflict,
        CyclePresetMutationStatus.NameConflict => UiStrings.CyclePresetNameConflict,
        CyclePresetMutationStatus.Referenced => UiStrings.CyclePresetReferenced,
        CyclePresetMutationStatus.BuiltIn => UiStrings.CyclePresetBuiltIn,
        _ => UiStrings.CyclePresetWriteUnavailable
    };

    private async Task ReloadCyclePresetsAsync(Guid? preferredId)
    {
        var presets = await _profileStore.ListCyclePresetsAsync();
        CyclePresets.Clear();
        foreach (var preset in presets.Count > 0 ? presets : BuiltInCyclePresets.All)
        {
            CyclePresets.Add(preset);
        }

        OnPropertyChanged(nameof(VisibleCyclePresets));
        RebuildCyclePresetCards();

        SelectedCyclePreset = preferredId is { } id
            ? CyclePresets.FirstOrDefault(preset => preset.Id == id) ?? CyclePresets.FirstOrDefault()
            : CyclePresets.FirstOrDefault();
    }

    private async Task AddBreakApplicationAsync()
    {
        if (!CanEditBreakExceptions || SelectedProfile is null)
        {
            return;
        }

        // Block, not Allow: a break exception is something that stays blocked while the
        // rest of the list is released, so the rule has to deny rather than permit.
        var result = ApplicationRule.Create(
            RuleAction.Block,
            ApplicationMatchKind.FileName,
            BreakApplicationInput);
        if (!result.IsValid)
        {
            StatusMessage = result.Errors[0].Message;
            return;
        }

        var rules = (_profileCycleSettings?.ApplicationRules ?? [])
            .Append(new BreakApplicationRule(
                result.Value!.Id,
                RuleAction.Block,
                ApplicationMatchKind.FileName,
                result.Value.Value))
            .ToArray();
        _profileCycleSettings = new ProfileCycleSettings(
            SelectedProfile.Id,
            SelectedCyclePreset?.Id,
            rules,
            _profileCycleSettings?.WebsiteRules ?? []);
        BreakApplicationInput = string.Empty;
        await SaveCurrentProfileCycleSettingsAsync();
        RebuildBreakRules();
    }

    public async Task AddBreakWebsiteAsync()
    {
        if (!CanEditBreakExceptions || SelectedProfile is null)
        {
            return;
        }

        var result = WebsiteRule.Create(RuleAction.Block, BreakWebsiteInput);
        if (!result.IsValid)
        {
            StatusMessage = result.Errors[0].Message;
            return;
        }

        var rules = (_profileCycleSettings?.WebsiteRules ?? [])
            .Append(new BreakWebsiteRule(result.Value!.Id, RuleAction.Block, result.Value.Pattern))
            .ToArray();
        _profileCycleSettings = new ProfileCycleSettings(
            SelectedProfile.Id,
            SelectedCyclePreset?.Id,
            _profileCycleSettings?.ApplicationRules ?? [],
            rules);
        BreakWebsiteInput = string.Empty;
        await SaveCurrentProfileCycleSettingsAsync();
        RebuildBreakRules();
    }

    private async Task RemoveBreakApplicationAsync(Guid id)
    {
        if (!CanEditBreakExceptions || SelectedProfile is null || _profileCycleSettings is null)
        {
            return;
        }

        _profileCycleSettings = _profileCycleSettings with
        {
            ApplicationRules = _profileCycleSettings.ApplicationRules.Where(rule => rule.Id != id).ToArray()
        };
        await SaveCurrentProfileCycleSettingsAsync();
        RebuildBreakRules();
    }

    private async Task RemoveBreakWebsiteAsync(Guid id)
    {
        if (!CanEditBreakExceptions || SelectedProfile is null || _profileCycleSettings is null)
        {
            return;
        }

        _profileCycleSettings = _profileCycleSettings with
        {
            WebsiteRules = _profileCycleSettings.WebsiteRules.Where(rule => rule.Id != id).ToArray()
        };
        await SaveCurrentProfileCycleSettingsAsync();
        RebuildBreakRules();
    }

    private async Task SaveCurrentProfileCycleSettingsAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        // Break exceptions can be edited from the profile screen while the Focus screen
        // sits in timer mode, where no preset is selected. Falling back to the stored
        // default keeps that edit from silently clearing the profile's chosen cycle.
        _profileCycleSettings = new ProfileCycleSettings(
            SelectedProfile.Id,
            SelectedCyclePreset?.Id ?? _profileCycleSettings?.DefaultPresetId,
            _profileCycleSettings?.ApplicationRules ?? [],
            _profileCycleSettings?.WebsiteRules ?? []);
        if (_profileCycleSettingsRevision is null)
        {
            StatusMessage = UiStrings.CycleSettingsLoadFailed;
            return;
        }

        var result = await _profileStore.TrySaveProfileCycleSettingsAsync(
            _profileCycleSettings, _profileCycleSettingsRevision);
        if (result.Status == ProfileCycleSettingsMutationStatus.Saved && result.Snapshot is { } saved)
        {
            _profileCycleSettings = saved.Settings;
            _profileCycleSettingsRevision = saved.Revision;
            return;
        }

        if (result.Status == ProfileCycleSettingsMutationStatus.NotSupported)
        {
            await _profileStore.SaveProfileCycleSettingsAsync(_profileCycleSettings);
            var reloaded = await _profileStore.LoadRevisionedProfileCycleSettingsAsync(SelectedProfile.Id);
            _profileCycleSettings = reloaded?.Settings;
            _profileCycleSettingsRevision = reloaded?.Revision;
            return;
        }

        if (result.Status == ProfileCycleSettingsMutationStatus.Conflict)
        {
            var profileId = SelectedProfile.Id;
            await RefreshSelectedProfileCycleSettingsAsync(profileId);
            StatusMessage = UiStrings.CyclePresetConflict;
            return;
        }

        StatusMessage = string.Format(UiStrings.Get("CycleSettingsLoadFailed"), result.Status);
    }

    public bool CanCancelStrict => IsStrictReviewVisible && !IsBusy;

    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (SetField(ref _isActive, value))
            {
                OnPropertyChanged(nameof(IsInactive));
                OnPropertyChanged(nameof(IsMotionActive));
                OnPropertyChanged(nameof(CanStart));
                NotifyStopPresentationChanged();
                OnPropertyChanged(nameof(ModeLabel));
                OnPropertyChanged(nameof(CanEditProfileRules));
                OnPropertyChanged(nameof(CanRenameProfile));
                OnPropertyChanged(nameof(CanDeleteProfile));
                if (value)
                {
                    IsDeleteProfileConfirmationVisible = false;
                    IsCreatingProfile = false;
                }
                RaiseSessionPresentationChanged();
                RefreshRuleCommands();
                NotifyCommands();
                if (value && Section is NavigationSection.Profiles or NavigationSection.Schedule)
                {
                    Section = NavigationSection.Focus;
                }
            }
        }
    }

    public bool IsInactive => !IsActive;

    /// <summary>
    /// The ambient ring may breathe only while a session is active and the owner has
    /// not requested reduced motion. This keeps the visual state declarative.
    /// </summary>
    public bool IsMotionActive => IsSessionStateFresh && IsActive && !IsReduceMotionEnabled;

    public IReadOnlyList<SessionModeChoiceViewModel> SessionModes { get; } =
    [
        new(UiStrings.TimerMode, SessionMode.SimpleTimer),
        new(UiStrings.CyclesMode, SessionMode.FocusCycle)
    ];

    public SessionModeChoiceViewModel SelectedSessionMode
    {
        get => SessionModes.First(mode => mode.Mode == (_isCycleMode
            ? SessionMode.FocusCycle
            : SessionMode.SimpleTimer));
        set
        {
            if (value is null || _isCycleMode == (value.Mode == SessionMode.FocusCycle))
            {
                return;
            }

            _isCycleMode = value.Mode == SessionMode.FocusCycle;
            OnPropertyChanged(nameof(SelectedSessionMode));
            OnPropertyChanged(nameof(IsCycleMode));
            OnPropertyChanged(nameof(IsSimpleTimerMode));
            OnPropertyChanged(nameof(CycleCompositionText));
            OnPropertyChanged(nameof(CycleLongBreakText));
            OnPropertyChanged(nameof(CycleLengthText));
            OnPropertyChanged(nameof(CycleEndText));
            OnPropertyChanged(nameof(SelectedCycleConfiguration));
            OnPropertyChanged(nameof(ActiveCycleLabel));
            OnPropertyChanged(nameof(StrictEndTimeText));
            OnPropertyChanged(nameof(StrictEndTimeDescription));
            OnPropertyChanged(nameof(RingCenterValue));
            OnPropertyChanged(nameof(RingCenterCaption));
            OnPropertyChanged(nameof(CanEditCyclePreset));
            OnPropertyChanged(nameof(CanEditBreakExceptions));
            // Only cycles have breaks, so the contextual column's exception section
            // appears and disappears with the mode rather than with the rule set.
            NotifyBreakExceptionsChanged();
            NotifyCommands();
        }
    }

    public bool IsCycleMode => _isCycleMode;

    public bool IsSimpleTimerMode => !_isCycleMode;

    public ObservableCollection<StoredCyclePreset> CyclePresets { get; }

    /// <summary>
    /// The built-in programs keep their curated order and lead, so the shelf reads the
    /// same on every install; anything the owner built themselves follows, alphabetically.
    /// </summary>
    public IReadOnlyList<StoredCyclePreset> VisibleCyclePresets =>
    [
        .. CyclePresets
            .Where(preset => preset.IsBuiltIn)
            .OrderBy(preset => preset.Id == BuiltInCyclePresets.DeepFocusId
                ? 0
                : preset.Id == BuiltInCyclePresets.ExtendedFlowId
                    ? 1
                    : 2),
        .. CyclePresets
            .Where(preset => !preset.IsBuiltIn)
            .OrderBy(preset => preset.Name, StringComparer.CurrentCultureIgnoreCase)
    ];

    public IReadOnlyList<CycleTerminationChoiceViewModel> CycleTerminationChoices { get; } =
    [
        new(UiStrings.CycleCountTermination, CycleTerminationMode.CycleCount),
        new(UiStrings.TotalDurationTermination, CycleTerminationMode.TotalDuration)
    ];

    public CycleTerminationChoiceViewModel SelectedCycleTerminationChoice
    {
        get => CycleTerminationChoices.First(choice => choice.Mode == CycleTerminationMode);
        set
        {
            if (value is not null)
            {
                CycleTerminationMode = value.Mode;
            }
        }
    }

    public ObservableCollection<BreakRuleViewModel> BreakRules { get; } = [];

    public bool IsEditingCyclePreset
    {
        get => _isEditingCyclePreset;
        private set
        {
            if (SetField(ref _isEditingCyclePreset, value))
            {
                NotifyCommands();
            }
        }
    }

    public bool CanEditCyclePreset =>
        IsSessionStateFresh && IsCycleMode && IsInactive && !IsBusy && SelectedProfile is not null;

    /// <summary>
    /// Break exceptions belong to the profile, not to whichever mode the Focus screen
    /// happens to be showing, so editing them does not require the segmented control to
    /// be on Cycles first. A running session still locks them, like every other rule.
    /// </summary>
    public bool CanEditBreakExceptions =>
        IsSessionStateFresh && IsInactive && !IsBusy && SelectedProfile is not null;

    public bool IsCyclePresetEditorVisible
    {
        get => _isCyclePresetEditorVisible;
        private set
        {
            if (SetField(ref _isCyclePresetEditorVisible, value))
            {
                OnPropertyChanged(nameof(CyclePresetEditorTitle));
                OnPropertyChanged(nameof(CanCommitCyclePreset));
                NotifyCommands();
            }
        }
    }

    public string CyclePresetEditorTitle => _editedCyclePresetId is null
        ? UiStrings.NewCyclePresetTitle
        : UiStrings.EditCyclePresetTitle;

    public bool CanCommitCyclePreset =>
        IsSessionStateFresh
        && IsCyclePresetEditorVisible
        && !IsBusy
        && !string.IsNullOrWhiteSpace(CyclePresetNameInput);

    public string CyclePresetNameValidationMessage =>
        IsCyclePresetEditorVisible && string.IsNullOrWhiteSpace(CyclePresetNameInput)
            ? UiStrings.CyclePresetNameRequired
            : string.Empty;

    /// <summary>
    /// The editor previews itself: the same three sentences the shelf shows, recomputed
    /// from the numbers currently in the fields rather than from the saved program.
    /// </summary>
    public string CyclePresetDraftSummary
    {
        get
        {
            var configuration = FocusCycleConfiguration.Create(
                TimeSpan.FromMinutes(CycleFocusMinutes),
                TimeSpan.FromMinutes(CycleShortBreakMinutes),
                TimeSpan.FromMinutes(CycleLongBreakMinutes),
                CycleLongBreakFrequency,
                CycleTerminationMode,
                IsCycleCountTermination ? CycleCount : null,
                IsTotalDurationTermination ? TimeSpan.FromMinutes(CycleTotalDurationMinutes) : null);
            if (!configuration.IsValid)
            {
                return configuration.Errors[0].Message;
            }

            var value = configuration.Value!;
            return string.Join(
                " · ",
                string.Format(
                    CultureInfo.CurrentCulture,
                    UiStrings.CycleCompositionLine,
                    (int)value.FocusDuration.TotalMinutes,
                    (int)value.ShortBreakDuration.TotalMinutes),
                string.Format(
                    CultureInfo.CurrentCulture,
                    UiStrings.CycleLongBreakLine,
                    value.CyclesBeforeLongBreak,
                    (int)value.LongBreakDuration.TotalMinutes),
                value.CycleCount is { } count
                    ? string.Format(CultureInfo.CurrentCulture, UiStrings.CycleLengthByCount, count)
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        UiStrings.CycleLengthByDuration,
                        (int)(value.TotalDuration?.TotalMinutes ?? 0)));
        }
    }

    public bool CanSaveCyclePreset =>
        CanEditCyclePreset
        && IsEditingCyclePreset
        && !string.IsNullOrWhiteSpace(CyclePresetNameInput)
        && SelectedCyclePreset is { IsBuiltIn: false };

    public bool CanDeleteCyclePreset =>
        CanEditCyclePreset && SelectedCyclePreset is { IsBuiltIn: false };

    public string CyclePresetNameInput
    {
        get => _cyclePresetNameInput;
        set
        {
            if (SetField(ref _cyclePresetNameInput, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(CanSaveCyclePreset));
                OnPropertyChanged(nameof(CanCommitCyclePreset));
                OnPropertyChanged(nameof(CyclePresetNameValidationMessage));
                _saveCyclePresetCommand.RaiseCanExecuteChanged();
                _commitCyclePresetCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int CycleFocusMinutes
    {
        get => _cycleFocusMinutes;
        set => SetCycleEditorValue(ref _cycleFocusMinutes, value, nameof(CycleFocusMinutes));
    }

    public int CycleShortBreakMinutes
    {
        get => _cycleShortBreakMinutes;
        set => SetCycleEditorValue(ref _cycleShortBreakMinutes, value, nameof(CycleShortBreakMinutes));
    }

    public int CycleLongBreakMinutes
    {
        get => _cycleLongBreakMinutes;
        set => SetCycleEditorValue(ref _cycleLongBreakMinutes, value, nameof(CycleLongBreakMinutes));
    }

    public int CycleLongBreakFrequency
    {
        get => _cycleLongBreakFrequency;
        set => SetCycleEditorValue(ref _cycleLongBreakFrequency, value, nameof(CycleLongBreakFrequency));
    }

    public int CycleCount
    {
        get => _cycleCount;
        set => SetCycleEditorValue(ref _cycleCount, value, nameof(CycleCount));
    }

    public int CycleTotalDurationMinutes
    {
        get => _cycleTotalDurationMinutes;
        set => SetCycleEditorValue(ref _cycleTotalDurationMinutes, value, nameof(CycleTotalDurationMinutes));
    }

    public CycleTerminationMode CycleTerminationMode
    {
        get => _cycleTerminationMode;
        set
        {
            if (SetField(ref _cycleTerminationMode, value))
            {
                OnPropertyChanged(nameof(IsCycleCountTermination));
                OnPropertyChanged(nameof(IsTotalDurationTermination));
                OnPropertyChanged(nameof(CanSaveCyclePreset));
                OnPropertyChanged(nameof(CyclePresetDraftSummary));
                OnPropertyChanged(nameof(SelectedCycleTerminationChoice));
                NotifyCommands();
            }
        }
    }

    public bool IsCycleCountTermination => CycleTerminationMode == CycleTerminationMode.CycleCount;

    public bool IsTotalDurationTermination => CycleTerminationMode == CycleTerminationMode.TotalDuration;

    public string BreakApplicationInput
    {
        get => _breakApplicationInput;
        set => SetField(ref _breakApplicationInput, value ?? string.Empty);
    }

    public string BreakWebsiteInput
    {
        get => _breakWebsiteInput;
        set => SetField(ref _breakWebsiteInput, value ?? string.Empty);
    }

    public bool HasBreakRules => BreakRules.Count > 0;

    public string PreparedCyclePlanText => _pendingPreparation?.Phases is not { Count: > 0 } phases
        ? string.Empty
        : string.Join(
            Environment.NewLine,
            phases.Select(phase => string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.PreparedPhase,
                phase.FocusCycleNumber,
                PhaseLabel(phase.Kind),
                phase.StartUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                phase.EndUtc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture))));

    public bool HasPreparedCyclePlan => !string.IsNullOrEmpty(PreparedCyclePlanText);

    public ICommand CreateCyclePresetCommand => _createCyclePresetCommand;
    public ICommand EditCyclePresetCommand => _editCyclePresetCommand;
    public ICommand CommitCyclePresetCommand => _commitCyclePresetCommand;
    public ICommand CancelCyclePresetEditCommand => _cancelCyclePresetEditCommand;
    public ICommand SaveCyclePresetCommand => _saveCyclePresetCommand;
    public ICommand DeleteCyclePresetCommand => _deleteCyclePresetCommand;
    public ICommand AddBreakApplicationCommand => _addBreakApplicationCommand;
    public ICommand AddBreakWebsiteCommand => _addBreakWebsiteCommand;

    public StoredCyclePreset? SelectedCyclePreset
    {
        get => _selectedCyclePreset;
        set
        {
            if (ReferenceEquals(_selectedCyclePreset, value))
            {
                return;
            }

            _selectedCyclePreset = value;
            LoadCycleEditor(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(CycleCompositionText));
            OnPropertyChanged(nameof(CycleLongBreakText));
            OnPropertyChanged(nameof(CycleLengthText));
            OnPropertyChanged(nameof(CycleEndText));
            OnPropertyChanged(nameof(SelectedCycleConfiguration));
            OnPropertyChanged(nameof(ActiveCycleLabel));
            OnPropertyChanged(nameof(StrictEndTimeText));
            OnPropertyChanged(nameof(StrictEndTimeDescription));
            OnPropertyChanged(nameof(RingCenterValue));
            OnPropertyChanged(nameof(CanSaveCyclePreset));
            OnPropertyChanged(nameof(CanDeleteCyclePreset));
            OnPropertyChanged(nameof(HasBreakExceptions));
            OnPropertyChanged(nameof(BreakExceptionItems));
            SyncCyclePresetCardSelection();
            NotifyCommands();
        }
    }

    // The old one-line summary crammed five numbers into a single row of separators,
    // which read as a spec sheet rather than an explanation. The same numbers now arrive
    // as three plain sentences in the order the session will actually play out.
    public string CycleCompositionText
    {
        get
        {
            var configuration = _selectedCyclePreset?.Configuration;
            return !_isCycleMode || configuration is null
                ? string.Empty
                : string.Format(
                    CultureInfo.CurrentCulture,
                    UiStrings.CycleCompositionLine,
                    (int)configuration.FocusDuration.TotalMinutes,
                    (int)configuration.ShortBreakDuration.TotalMinutes);
        }
    }

    public string CycleLongBreakText
    {
        get
        {
            var configuration = _selectedCyclePreset?.Configuration;
            return !_isCycleMode || configuration is null
                ? string.Empty
                : string.Format(
                    CultureInfo.CurrentCulture,
                    UiStrings.CycleLongBreakLine,
                    configuration.CyclesBeforeLongBreak,
                    (int)configuration.LongBreakDuration.TotalMinutes);
        }
    }

    public string CycleLengthText
    {
        get
        {
            var configuration = _selectedCyclePreset?.Configuration;
            if (!_isCycleMode || configuration is null)
            {
                return string.Empty;
            }

            return configuration.CycleCount is { } count
                ? string.Format(CultureInfo.CurrentCulture, UiStrings.CycleLengthByCount, count)
                : string.Format(
                    CultureInfo.CurrentCulture,
                    UiStrings.CycleLengthByDuration,
                    (int)(configuration.TotalDuration?.TotalMinutes ?? 0));
        }
    }

    public string CycleEndText
    {
        get
        {
            var end = BuildCyclePreview()?.EndUtc;
            return end is null
                ? string.Empty
                : string.Format(
                    CultureInfo.CurrentCulture,
                    UiStrings.CycleEndsAt,
                    end.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        }
    }

    public bool CanEditProfileRules => IsSessionStateFresh && IsInactive && !IsBusy && SelectedProfile is not null;

    public string ModeLabel => _activeStrictMode ? UiStrings.StrictMode : UiStrings.NormalMode;

    public bool CanStop => IsSessionStateFresh && IsActive && !_activeStrictMode && !IsBusy;

    public bool IsCycleSession => IsActive && _activeMode == SessionMode.FocusCycle;

    /// <summary>
    /// True only after the service confirms every requested policy channel is intact.
    /// The Desktop never infers protection merely from a running countdown.
    /// </summary>
    public bool IsEnforcementConnected =>
        IsSessionStateFresh && IsActive && HasConfiguredBlockingRules && _isEnforcementConnected;

    public bool HasConfiguredBlockingRules =>
        (SelectedProfile?.ApplicationRules.Count ?? 0) > 0
        || (SelectedProfile?.WebsiteRules.Count ?? 0) > 0
        || (_profileCycleSettings?.ApplicationRules.Count ?? 0) > 0
        || (_profileCycleSettings?.WebsiteRules.Count ?? 0) > 0;

    public bool IsEnforcementDegraded =>
        IsActive && IsEnforcementEnabled && HasConfiguredBlockingRules && !IsEnforcementConnected;

    public bool IsTimerOnly => IsActive && !IsEnforcementEnabled;

    public bool IsNoRulesConfigured =>
        IsActive && IsEnforcementEnabled && !HasConfiguredBlockingRules;

    // A break is the one moment the app deliberately stops blocking, so it says so
    // outright. Reading the ordinary "you are protected" line through a break is what
    // makes the release look like a failure rather than the point of the cycle.
    public string ActiveBlockingDescription => !IsSessionStateFresh
        ? ConnectionNotice
        : IsBreakPhase
        ? UiStrings.BreakReleasedNotice
        : IsNoRulesConfigured
            ? UiStrings.Get("NoBlockingRulesActive")
            : IsEnforcementConnected
                ? SessionMeta
                : EnforcementWarning;

    public bool IsBreakPhase => IsCycleSession
        && _currentPhase?.Kind is SessionPhaseKind.ShortBreak or SessionPhaseKind.LongBreak;

    public bool CanSkipBreak => IsSessionStateFresh && IsBreakPhase && !_activeStrictMode && !IsBusy;

    public string CurrentPhaseLabel => _currentPhase?.Kind switch
    {
        SessionPhaseKind.Focus => UiStrings.FocusPhase,
        SessionPhaseKind.ShortBreak => UiStrings.ShortBreakPhase,
        SessionPhaseKind.LongBreak => UiStrings.LongBreakPhase,
        _ => UiStrings.SimpleTimerPhase
    };

    public string CycleProgressText => _currentPhase is null
        ? string.Empty
        : string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.CycleProgress,
            _currentPhase.FocusCycleNumber,
            SelectedCycleConfiguration?.CycleCount ?? _currentPhase.FocusCycleNumber);

    public string ActiveCycleLabel => IsBreakPhase
        ? CurrentPhaseLabel
        : SelectedCyclePreset?.Name ?? CurrentPhaseLabel;

    public PhaseStatus? CurrentPhaseStatus => _currentPhase;

    public string NextPhaseText => _nextPhase is null
        ? UiStrings.NoNextPhase
        : string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.NextPhase,
            _nextPhase.Kind switch
            {
                SessionPhaseKind.Focus => UiStrings.FocusPhase,
                SessionPhaseKind.ShortBreak => UiStrings.ShortBreakPhase,
                SessionPhaseKind.LongBreak => UiStrings.LongBreakPhase,
                _ => UiStrings.SimpleTimerPhase
            },
            _nextPhase.EndUtc.ToLocalTime().ToString("t", CultureInfo.CurrentCulture));

    // ---- Header ---------------------------------------------------------

    public string SessionEyebrow => IsActive
        ? UiStrings.SessionEyebrowActive
        : UiStrings.SessionEyebrowIdle;

    public string SessionTitle => IsActive ? ProfileName : UiStrings.ReadyTitle;

    public string SessionMeta => IsActive
        ? string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.SessionMetaActive,
            FormatClock(_sessionStart),
            FormatClock(_sessionEnd),
            WebsiteRuleCount,
            ApplicationRuleCount)
        : string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.SessionMetaIdle,
            WebsiteRuleCount,
            ApplicationRuleCount);

    public string StatusPillText => IsActive ? UiStrings.StatusProtected : UiStrings.StatusIdle;

    /// <summary>
    /// Repeated in the title bar so the remaining time is readable from the taskbar
    /// hover or when the window sits behind other apps.
    /// </summary>
    public string TitleBarStatus => IsActive
        ? string.Format(CultureInfo.CurrentCulture, UiStrings.TitleBarRemaining, RemainingText)
        : string.Empty;

    public bool HasTitleBarStatus => IsActive;

    // ---- Profile identity ------------------------------------------------

    /// <summary>
    /// Every profile as a coloured swatch. These sit under the profile name instead of
    /// beside the metric so they never collide with the figure, and they disappear
    /// outright during a session rather than being dimmed.
    /// </summary>
    public ObservableCollection<ProfileSwatchViewModel> ProfileSwatches { get; } = [];

    public bool HasProfileSwatches => ProfileSwatches.Count > 0 && IsInactive;

    /// <summary>
    /// The profile's own description. When the user left it empty the rule counts stand
    /// in, so the second line is never blank.
    /// </summary>
    public string HeaderSubtitle
    {
        get
        {
            if (IsActive)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    UiStrings.HeaderSubtitleActive,
                    FormatClock(_sessionEnd));
            }

            var description = SelectedProfile?.Description;
            return string.IsNullOrWhiteSpace(description)
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    UiStrings.HeaderSubtitleFallback,
                    WebsiteRuleCount,
                    ApplicationRuleCount)
                : description;
        }
    }

    /// <summary>
    /// The accent is the only thing that changes colour between profiles. It is also
    /// mirrored into the application resources so the ring and the sidebar follow.
    /// </summary>
    public IBrush ProfileAccentBrush => _profileAccentBrush;

    // ---- Metric ----------------------------------------------------------

    /// <summary>
    /// One value that mutates rather than three stacked controls: the countdown while a
    /// session runs, the day's focused total otherwise.
    /// </summary>
    public string MetricValue => IsActive
        ? RemainingText
        : HasHistory
            ? FormatFocusedTotal(FocusedTodayMinutes)
            : string.Empty;

    /// <summary>
    /// The value split into number and unit runs so "2h 18m" can set its h and m smaller
    /// than the digits. A countdown is a single run, which is why this is one collection
    /// rather than a fixed set of labelled fields.
    /// </summary>
    public IReadOnlyList<MetricSegmentViewModel> MetricSegments
    {
        get
        {
            if (IsActive)
            {
                return [new(RemainingText, IsUnit: false)];
            }

            if (!HasHistory)
            {
                return [];
            }

            var whole = (int)Math.Round(FocusedTodayMinutes);
            if (whole < 60)
            {
                return
                [
                    new(whole.ToString(CultureInfo.CurrentCulture), IsUnit: false),
                    new(UiStrings.MetricUnitMinutes, IsUnit: true)
                ];
            }

            return
            [
                new((whole / 60).ToString(CultureInfo.CurrentCulture), IsUnit: false),
                new(UiStrings.MetricUnitHours, IsUnit: true),
                new(" " + (whole % 60).ToString(CultureInfo.CurrentCulture), IsUnit: false),
                new(UiStrings.MetricUnitMinutes, IsUnit: true)
            ];
        }
    }

    /// <summary>
    /// While running the caption carries the session length too, so the countdown is
    /// legible without hunting for how long the session was set for.
    /// </summary>
    public string MetricLabel => IsActive
        ? string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.MetricRemainingOfTotal,
            (int)Math.Round(_sessionTotalSeconds / 60d))
        : HasHistory
            ? UiStrings.MetricFocusedToday
            : string.Empty;

    /// <summary>Accent while counting down, normal foreground for a past-tense total.</summary>
    public IBrush MetricBrush => IsActive ? _profileAccentBrush : DefaultMetricBrush;

    public bool HasMetric => MetricValue.Length > 0;

    /// <summary>
    /// First run shows a sentence, not a figure: zero hours set in 30 px type is a
    /// depressing thing to open an app to.
    /// </summary>
    public bool HasMetricPlaceholder => !IsActive && !HasHistory;

    /// <summary>False until session history exists in the backend; the sparkline hides with it.</summary>
    public bool HasHistory => WeekMinutes is { Count: > 0 };

    public IReadOnlyList<double>? WeekMinutes { get; private set; }

    private double FocusedTodayMinutes { get; set; }

    /// <summary>
    /// The seam session history will feed once the service records finished sessions.
    /// Until then nothing calls it, the header reports its first-run state, and the
    /// sparkline stays hidden rather than drawing a fabricated week.
    /// </summary>
    public void ApplyFocusHistory(IReadOnlyList<double>? weekMinutes, double focusedTodayMinutes)
    {
        WeekMinutes = weekMinutes is { Count: > 0 } ? weekMinutes : null;
        FocusedTodayMinutes = Math.Max(0d, focusedTodayMinutes);
        RaiseSessionPresentationChanged();
    }

    // ---- Progress ring --------------------------------------------------

    public double RingSweepAngle
        => CalculateRingSweepAngle(IsActive, PhaseDurationSeconds, _currentPhase?.RemainingSeconds ?? _remainingSeconds);

    private long PhaseDurationSeconds => _currentPhase is { StartUtc: { } start } phase
        ? (long)(phase.EndUtc - start).TotalSeconds : _currentPhase?.RemainingSeconds ?? _sessionTotalSeconds;

    internal static double CalculateRingSweepAngle(
        bool isActive,
        long totalSeconds,
        long remainingSeconds) =>
        isActive && totalSeconds > 0
            ? 360d * (1d - Math.Clamp(remainingSeconds / (double)totalSeconds, 0d, 1d))
            : 0d;

    public string RingCenterValue => IsActive
        ? _currentPhase is { } phase ? FormatRemaining(phase.RemainingSeconds) : RemainingText
        : ((int)SelectedDurationPreset.Duration.TotalMinutes).ToString(CultureInfo.CurrentCulture);

    public string RingCenterCaption => IsActive
        ? string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.RemainingOfTotal,
            (int)Math.Round(PhaseDurationSeconds / 60d))
        : UiStrings.MinutesLower;

    // ---- Running session copy ---------------------------------------------

    /// <summary>
    /// The running session named in one line: which profile, and which shape of
    /// session. It replaces the idle screen's title without moving it, so starting a
    /// session transforms the screen rather than navigating away from it.
    /// </summary>
    public string SessionHeadlineText => !IsActive
        ? ProfileName
        : string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.SessionHeadline,
            ActiveProfileName,
            IsCycleSession
                ? SelectedCyclePreset?.Name ?? UiStrings.CycleSessionKind
                : UiStrings.TimerSessionKind);

    /// <summary>
    /// What follows the block currently on the ring. During a cycle the ring measures
    /// one block, not the session, so without this the countdown reads as the whole
    /// thing being nearly over.
    /// </summary>
    public string RingNextPhaseHint
    {
        get
        {
            if (!IsCycleSession || IsBreakPhase || _nextPhase is null || _currentPhase is null)
            {
                return string.Empty;
            }

            var minutes = (int)Math.Round((_nextPhase.EndUtc - _currentPhase.EndUtc).TotalMinutes);
            return minutes <= 0
                ? string.Empty
                : string.Format(CultureInfo.CurrentCulture, UiStrings.ThenBreakOf, minutes);
        }
    }

    public bool HasRingNextPhaseHint => RingNextPhaseHint.Length > 0;

    /// <summary>A break is a state of the session, never the end of it.</summary>
    public bool IsFocusPhaseActive => IsActive && !IsBreakPhase;

    /// <summary>
    /// When the block after this break begins, which is simply when the break ends.
    /// </summary>
    public string NextBlockStartText => _currentPhase is null
        ? string.Empty
        : string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.NextBlockStartsAt,
            FormatClock(_currentPhase.EndUtc));

    /// <summary>
    /// What the break actually released. Stated as a count rather than a list because
    /// the exceptions — the part that stays blocked — are the surprising half, and
    /// they get their own place in the contextual column.
    /// </summary>
    public string BreakReleasedSummaryText
    {
        get
        {
            var exceptionSites = _profileCycleSettings?.WebsiteRules.Count ?? 0;
            var exceptionApps = _profileCycleSettings?.ApplicationRules.Count ?? 0;
            return string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.BreakReleasedSummary,
                Math.Max(0, SavedWebsiteRules.Count - exceptionSites),
                Math.Max(0, SavedApplicationRules.Count - exceptionApps));
        }
    }

    /// <summary>The one canonical statement of when everything ends.</summary>
    public string SessionEndSummaryText => IsCycleSession
        ? string.Format(CultureInfo.CurrentCulture, UiStrings.WholeCycleEndsAt, ExactEndText)
        : string.Format(CultureInfo.CurrentCulture, UiStrings.TimerEndsAndReleases, ExactEndText);

    // ---- Strict mode ----------------------------------------------------

    /// <summary>
    /// In strict mode the stop button is removed rather than disabled: a greyed
    /// button invites retrying something that cannot happen.
    /// </summary>
    public bool IsStrictSessionActive => IsActive && _activeStrictMode;

    public string StrictActiveNotice => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.StrictActiveNotice,
        FormatClock(_sessionEnd));

    // ---- Rule chips -----------------------------------------------------

    public string WebsitesHeader => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.WebsitesWithCount,
        SavedWebsiteRules.Count);

    public string ApplicationsHeader => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.ApplicationsWithCount,
        SavedApplicationRules.Count);

    public IEnumerable<SavedWebsiteRuleViewModel> VisibleWebsiteRules =>
        _showAllWebsiteRules ? SavedWebsiteRules : SavedWebsiteRules.Take(VisibleChipLimit);

    public IEnumerable<SavedApplicationRuleViewModel> VisibleApplicationRules =>
        _showAllApplicationRules ? SavedApplicationRules : SavedApplicationRules.Take(VisibleChipLimit);

    public bool HasWebsiteOverflow =>
        !_showAllWebsiteRules && SavedWebsiteRules.Count > VisibleChipLimit;

    public bool HasApplicationOverflow =>
        !_showAllApplicationRules && SavedApplicationRules.Count > VisibleChipLimit;

    /// <summary>
    /// The primary action's label. It reports the one state the button can be in
    /// besides idle, so a slow service handshake looks like work in progress rather
    /// than a dead button.
    /// </summary>
    public string StartFocusButtonText => IsBusy ? UiStrings.StartingFocus : string.Format(CultureInfo.CurrentCulture, UiStrings.QuickStartMinutes, QuickStartDurationMinutes);

    /// <summary>
    /// Whether the chip list under a summary header is showing. Collapsed by default:
    /// the header already states the count, and on a short window an expanded pair of
    /// lists pushes the primary action out of view.
    /// </summary>
    public bool AreWebsiteChipsExpanded
    {
        get => _areWebsiteChipsExpanded;
        set
        {
            if (SetField(ref _areWebsiteChipsExpanded, value))
            {
                OnPropertyChanged(nameof(HasVisibleWebsiteOverflow));
            }
        }
    }

    public bool AreApplicationChipsExpanded
    {
        get => _areApplicationChipsExpanded;
        set
        {
            if (SetField(ref _areApplicationChipsExpanded, value))
            {
                OnPropertyChanged(nameof(HasVisibleApplicationOverflow));
            }
        }
    }

    /// <summary>The "+N more" affordance only makes sense next to visible chips.</summary>
    public bool HasVisibleWebsiteOverflow => AreWebsiteChipsExpanded && HasWebsiteOverflow;

    public bool HasVisibleApplicationOverflow => AreApplicationChipsExpanded && HasApplicationOverflow;

    public string WebsiteOverflowText => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.MoreChips,
        Math.Max(0, SavedWebsiteRules.Count - VisibleChipLimit));

    public string ApplicationOverflowText => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.MoreChips,
        Math.Max(0, SavedApplicationRules.Count - VisibleChipLimit));

    // ---- Contextual column ----------------------------------------------

    /// <summary>
    /// How many blocked items the contextual column lists before it stops and counts
    /// the rest. Five keeps the column shorter than the decision beside it, which is
    /// the point: it is context, not the subject of the screen.
    /// </summary>
    private const int BlockedItemLimit = 5;

    /// <summary>
    /// Sites first, then apps, in the order the profile stores them. During a session
    /// each row carries its own blocked-attempt count; outside one the counts are all
    /// zero and the column reads as a plain list of what is about to be blocked.
    /// </summary>
    public IReadOnlyList<BlockedItemViewModel> BlockedItems =>
    [
        .. BlockingWebsiteRules
            .Take(BlockedItemLimit)
            .Select(rule => new BlockedItemViewModel(
                rule.Rule.Pattern,
                IsWebsite: true,
                AttemptsFor(rule.Rule.Pattern))),
        .. BlockingApplicationRules
            .Take(Math.Max(0, BlockedItemLimit - BlockingWebsiteRules.Count))
            .Select(rule => new BlockedItemViewModel(
                rule.DisplayName,
                IsWebsite: false,
                AttemptsFor(rule.Rule.Value))),
    ];

    public IReadOnlyList<SavedWebsiteRuleViewModel> BlockingWebsiteRules => SavedWebsiteRules.Where(r => r.Rule.Action == RuleAction.Block).ToArray();

    public IReadOnlyList<SavedApplicationRuleViewModel> BlockingApplicationRules => SavedApplicationRules.Where(r => r.Rule.Action == RuleAction.Block).ToArray();

    private int BlockedItemTotal => BlockingWebsiteRules.Count + BlockingApplicationRules.Count;

    public bool HasBlockedItemOverflow => BlockedItemTotal > BlockedItemLimit;

    public string BlockedItemOverflowText => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.MoreChips,
        Math.Max(0, BlockedItemTotal - BlockedItemLimit));

    public bool HasBlockedItems => BlockedItemTotal > 0;

    /// <summary>
    /// The service reports a blocked attempt against a target string. Matching is by
    /// exact target because that is what the audit records; anything cleverer would
    /// invent counts the log does not support.
    /// </summary>
    private int AttemptsFor(string target) =>
        _blockedAttemptsByTarget.GetValueOrDefault(target, 0);

    /// <summary>
    /// The profile's reach in one line: the canonical place these two counts appear.
    /// Nothing else on the Focus screen repeats them.
    /// </summary>
    public string ProfileScopeText => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.ProfileScope,
        SavedWebsiteRules.Count,
        SavedApplicationRules.Count);

    /// <summary>
    /// The unit and the finish time for the duration being configured, sitting beside
    /// the number itself so the screen never has to state the end time twice.
    /// </summary>
    public string PlannedEndText => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.PlannedEnd,
        FormatClock(_timeProvider.GetUtcNow().AddMinutes(SelectedDurationMinutes)));

    public string PlannedReleaseText => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.PlannedRelease,
        FormatClock(_timeProvider.GetUtcNow().AddMinutes(SelectedDurationMinutes)));

    // ---- Cycle cards ------------------------------------------------------

    /// <summary>
    /// Every saved program, each carrying its own length and composition. Rebuilt
    /// whenever the set or the selection changes; the card list is the only place a
    /// cycle is chosen, so selection lives on the cards themselves.
    /// </summary>
    public ObservableCollection<CyclePresetCardViewModel> CyclePresetCards { get; } = [];

    private void SyncCyclePresetCardSelection()
    {
        foreach (var card in CyclePresetCards)
        {
            card.IsSelected = card.Id == _selectedCyclePreset?.Id;
        }
    }

    private void RebuildCyclePresetCards()
    {
        CyclePresetCards.Clear();
        foreach (var preset in VisibleCyclePresets)
        {
            CyclePresetCards.Add(new CyclePresetCardViewModel(
                preset,
                DescribeCycleLength(preset.Configuration),
                DescribeCycleComposition(preset.Configuration),
                preset.Id == _selectedCyclePreset?.Id,
                selected => SelectedCyclePreset = selected));
        }
    }

    /// <summary>
    /// How long the whole program runs, as "2 h 50 min". A block count answers a
    /// different question than the one a card is asked; the finish time is deliberately
    /// absent because it belongs once, under the primary action, to the chosen program.
    /// </summary>
    private static string DescribeCycleLength(FocusCycleConfiguration configuration)
    {
        var minutes = (int)Math.Round(TotalCycleMinutes(configuration));
        return minutes >= 60
            ? string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.FocusedHoursMinutes,
                minutes / 60,
                minutes % 60)
            : string.Format(CultureInfo.CurrentCulture, UiStrings.FocusedMinutesOnly, minutes);
    }

    /// <summary>
    /// Total wall-clock length: every focus block, the short break between each pair,
    /// and the long break that replaces the last short one when the program has one.
    /// </summary>
    private static double TotalCycleMinutes(FocusCycleConfiguration configuration)
    {
        if (configuration.TotalDuration is { } total)
        {
            return total.TotalMinutes;
        }

        var blocks = configuration.CycleCount ?? configuration.CyclesBeforeLongBreak;
        var focus = blocks * configuration.FocusDuration.TotalMinutes;
        var gaps = Math.Max(0, blocks - 1);
        var hasLongBreak = blocks >= configuration.CyclesBeforeLongBreak
            && configuration.LongBreakDuration > configuration.ShortBreakDuration;
        var shortGaps = hasLongBreak ? Math.Max(0, gaps - 1) : gaps;
        return focus
            + (shortGaps * configuration.ShortBreakDuration.TotalMinutes)
            + (hasLongBreak ? configuration.LongBreakDuration.TotalMinutes : 0);
    }

    /// <summary>
    /// The program in one sentence, in the order it plays out. The long break is only
    /// mentioned when it actually differs from the short one, so a flat program does
    /// not carry a clause about a break it never takes.
    /// </summary>
    private static string DescribeCycleComposition(FocusCycleConfiguration configuration)
    {
        var blocks = configuration.CycleCount ?? configuration.CyclesBeforeLongBreak;
        var line = string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.CycleCardComposition,
            blocks,
            (int)configuration.FocusDuration.TotalMinutes,
            (int)configuration.ShortBreakDuration.TotalMinutes);

        return configuration.LongBreakDuration > configuration.ShortBreakDuration
            && blocks >= configuration.CyclesBeforeLongBreak
                ? line + string.Format(
                    CultureInfo.CurrentCulture,
                    UiStrings.CycleCardLastBreak,
                    (int)configuration.LongBreakDuration.TotalMinutes)
                : line;
    }

    // ---- Protection status ------------------------------------------------

    /// <summary>
    /// Whether every protection channel the session asked for is answering. Before a
    /// session this reports readiness, so a degraded extension is discovered while the
    /// choice can still be changed rather than after the countdown has started.
    /// </summary>
    /// While idle this is judged from the channels themselves, not from
    /// EnforcementConnected: that flag describes a live policy, so with no session
    /// running it is always false and the column reported a permanent false alarm
    /// before every session.
    public bool IsProtectionHealthy => IsSessionStateFresh && (IsActive
        ? IsEnforcementConnected
        : !IsEnforcementEnabled || (_isServiceReady && _isExtensionReady));

    public bool IsProtectionDegraded => !IsProtectionHealthy;

    /// <summary>
    /// The four states the column can report. Ready and Protected are the same health
    /// before and during a session, kept apart because "ready to block" and "blocking
    /// right now" are different promises. Degraded means one channel is down and the
    /// other still holds; Unavailable means the service itself is not answering, so
    /// the Desktop cannot confirm the current enforcement state.
    /// </summary>
    public ProtectionState ProtectionState => !IsSessionStateFresh || !_isServiceReady
        ? ProtectionState.Unavailable
        : IsProtectionDegraded
            ? ProtectionState.Degraded
            : IsActive
                ? ProtectionState.Protected
                : ProtectionState.Ready;

    public bool IsProtectionUnavailable => ProtectionState == ProtectionState.Unavailable;

    /// <summary>Never colour alone: the glyph carries the state as well.</summary>
    public string ProtectionGlyph => ProtectionState switch
    {
        ProtectionState.Unavailable => "\uEA39",
        ProtectionState.Degraded => "\uE7BA",
        ProtectionState.Protected => "\uEA18",
        _ => "\uE73E",
    };

    /// <summary>
    /// The contextual column's line about the protection channels. A failure names the
    /// component that failed: "something is degraded" gives the user nothing to act on,
    /// and the two channels fail for entirely different reasons.
    /// </summary>
    public string ProtectionStatusText => !IsSessionStateFresh ? ConnectionNotice : ProtectionState switch
    {
        ProtectionState.Unavailable => UiStrings.ProtectionServiceUnavailable,
        ProtectionState.Degraded => _isServiceReady && !_isExtensionReady
            ? UiStrings.ProtectionExtensionDown
            : EnforcementWarning,
        ProtectionState.Protected => UiStrings.ServiceAndExtensionActive,
        _ => UiStrings.ProtectionReady,
    };

    /// <summary>
    /// What still works while a channel is down, so the state is actionable rather than
    /// merely alarming.
    /// </summary>
    public string ProtectionDetailText => !IsSessionStateFresh ? UiStrings.Get("ConnectionUnconfirmedDetail") : ProtectionState switch
    {
        ProtectionState.Unavailable => UiStrings.ProtectionServiceUnavailableDetail,
        ProtectionState.Degraded => _isServiceReady && !_isExtensionReady
            ? UiStrings.ProtectionExtensionDownDetail
            : string.Empty,
        _ => string.Empty,
    };

    public bool HasProtectionDetail => ProtectionDetailText.Length > 0;

    /// <summary>Both failing states offer the same two ways out.</summary>
    public bool IsProtectionActionable => IsProtectionDegraded || IsProtectionUnavailable;

    /// <summary>
    /// The running session's own status pill. It names the scope it is protecting so
    /// the pill is a statement rather than a badge.
    /// </summary>
    public string ProtectionSummaryText => !IsSessionStateFresh
        ? ConnectionNotice
        : IsBreakPhase
        ? UiStrings.BreakActivePill
        : IsProtectionDegraded
            ? UiStrings.StatusDegraded
            : string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.ProtectedWithScope,
                SavedWebsiteRules.Count,
                SavedApplicationRules.Count);

    public string BlockedAttemptsSummaryText => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.BlockedAttemptsToday,
        _blockedAttempts);

    /// <summary>A plain timer session is the only one whose Stop is a single button.</summary>
    public bool CanStopSimpleSession => CanStop && !IsCycleSession;

    public bool IsNormalSessionActive => IsActive && !_activeStrictMode;

    /// <summary>
    /// What strict mode actually is, kept behind an info affordance so the screen does
    /// not carry a permanent paragraph explaining a switch that is usually off.
    /// </summary>
    public string StrictModeInfoText => UiStrings.StrictModeInfo;

    // ---- Break exceptions -------------------------------------------------

    /// <summary>
    /// The rules that survive a break. A cycle's breaks release the profile's whole
    /// list, and these are the exceptions to that release, so the column can say what
    /// will still be blocked before the first pause arrives.
    /// </summary>
    public IReadOnlyList<BlockedItemViewModel> BreakExceptionItems =>
    [
        .. (_profileCycleSettings?.WebsiteRules ?? [])
            .Select(rule => new BlockedItemViewModel(rule.Pattern, IsWebsite: true, Attempts: 0)),
        .. (_profileCycleSettings?.ApplicationRules ?? [])
            .Select(rule => new BlockedItemViewModel(rule.Value, IsWebsite: false, Attempts: 0)),
    ];

    /// <summary>Shown only in cycle mode, and only when the profile defines any.</summary>
    public bool HasBreakExceptions => (IsCycleMode || IsCycleSession) && BreakExceptionItems.Count > 0;

    public void ExpandWebsiteRules()
    {
        _showAllWebsiteRules = true;
        OnPropertyChanged(nameof(VisibleWebsiteRules));
        OnPropertyChanged(nameof(HasWebsiteOverflow));
    }

    public void ExpandApplicationRules()
    {
        _showAllApplicationRules = true;
        OnPropertyChanged(nameof(VisibleApplicationRules));
        OnPropertyChanged(nameof(HasApplicationOverflow));
    }

    /// <summary>
    /// Swatch click. Refused while a session runs for the same reason the Profiles
    /// screen is padlocked: the running policy cannot be swapped underneath it.
    /// </summary>
    public void SelectProfile(StoredProfile? profile)
    {
        if (profile is null || profile.Id == SelectedProfile?.Id)
        {
            return;
        }

        if (IsNavigationLocked)
        {
            StatusMessage = UiStrings.RulesLocked;
            return;
        }

        SelectedProfile = profile;
        RebuildProfileSwatches();
    }

    // ---- Session metrics ------------------------------------------------

    /// <summary>Blocked attempts recorded by the service since the session started.</summary>
    public string BlockedAttemptsText =>
        _blockedAttempts.ToString(CultureInfo.CurrentCulture);

    public void ShowSection(NavigationSection section)
    {
        if (section == NavigationSection.Profiles && !_applicationsLoaded && !IsActive) _ = LoadApplicationsAsync();
        if (section == NavigationSection.Settings)
        {
            IsSettingsVisible = true;
            return;
        }

        if (IsNavigationLocked
            && section is NavigationSection.Profiles or NavigationSection.Schedule)
        {
            StatusMessage = UiStrings.RulesLocked;
            return;
        }

        IsSettingsVisible = false;
        Section = section;
    }

    public void CloseSettings() => IsSettingsVisible = false;

    private static ActivityEventViewModel ToActivityViewModel(FocusAuditEvent item) =>
        new(
            item.Kind switch
            {
                FocusAuditEventKind.ApplicationBlocked => UiStrings.ActivityApplicationBlocked,
                FocusAuditEventKind.WebsiteBlocked => UiStrings.ActivityWebsiteBlocked,
                FocusAuditEventKind.EnforcementReleased => UiStrings.ActivityEnforcementReleased,
                FocusAuditEventKind.RecoveryCompleted => UiStrings.ActivityRecoveryCompleted,
                _ => UiStrings.ActivityGeneric
            },
            item.Target,
            item.Outcome,
            item.TimestampUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            FormatRelative(item.TimestampUtc),
            item.Kind == FocusAuditEventKind.WebsiteBlocked);

    private static string FormatRelative(DateTimeOffset timestampUtc)
    {
        var elapsed = DateTimeOffset.UtcNow - timestampUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return UiStrings.RelativeJustNow;
        }

        return elapsed < TimeSpan.FromHours(1)
            ? string.Format(CultureInfo.CurrentCulture, UiStrings.RelativeMinutes, (int)elapsed.TotalMinutes)
            : string.Format(CultureInfo.CurrentCulture, UiStrings.RelativeHours, (int)elapsed.TotalHours);
    }

    private static string FormatClock(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("t", CultureInfo.CurrentCulture) ?? "--:--";

    public string RemainingText
    {
        get => _remainingText;
        private set => SetField(ref _remainingText, value);
    }

    public string ExactEndText
    {
        get => _exactEndText;
        private set => SetField(ref _exactEndText, value);
    }

    public string ServiceHealthLabel
    {
        get => _serviceHealthLabel;
        private set => SetField(ref _serviceHealthLabel, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public bool CanStart =>
        IsSessionStateFresh
        && !IsActive
        && !IsBusy
        && SelectedProfile is not null
        && SelectedDurationPreset.Duration >= TimeSpan.FromMinutes(1);

    /// <summary>
    /// How the end of a session is announced. Owned here rather than by the window so the
    /// Settings screen can bind to it and the shell can read it when a session closes.
    /// </summary>
    public IReadOnlyList<SessionEndOptionViewModel> SessionEndOptions { get; } =
    [
        new(SessionEndPresentation.Window, UiStrings.SettingsSessionEndWindow),
        new(SessionEndPresentation.Notification, UiStrings.SettingsSessionEndToast),
        new(SessionEndPresentation.DimmedWindow, UiStrings.SettingsSessionEndDimmed),
        new(SessionEndPresentation.None, UiStrings.SettingsSessionEndOff)
    ];

    public SessionEndOptionViewModel SelectedSessionEndOption
    {
        get => SessionEndOptions.FirstOrDefault(option => option.Value == _settings.SessionEnd)
            ?? SessionEndOptions[0];
        set
        {
            if (value is null || value.Value == _settings.SessionEnd)
            {
                return;
            }

            _settings = _settings with { SessionEnd = value.Value };
            _settingsStore.Save(_settings);
            OnPropertyChanged();
        }
    }

    public SessionEndPresentation SessionEndPresentation => _settings.SessionEnd;

    /// <summary>
    /// White Zen when on, Dark Zen when off. The palette is applied immediately rather
    /// than on the next launch: a theme you cannot see change is a theme you cannot judge.
    /// </summary>
    public bool IsLightTheme
    {
        get => _settings.Theme == AppTheme.Light;
        set
        {
            var theme = value ? AppTheme.Light : AppTheme.Dark;
            if (theme == _settings.Theme)
            {
                return;
            }

            _settings = _settings with { Theme = theme };
            _settingsStore.Save(_settings);
            ApplyResolvedTheme();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsProfileLightTheme));
        }
    }

    public bool UsesProfileThemeOverride
    {
        get => SelectedProfile is not null
            && _settings.ProfileThemes?.ContainsKey(SelectedProfile.Id.Value) == true;
        set
        {
            if (SelectedProfile is null || value == UsesProfileThemeOverride)
            {
                return;
            }

            var themes = _settings.ProfileThemes is null
                ? []
                : new Dictionary<Guid, AppTheme>(_settings.ProfileThemes);
            if (value)
            {
                themes[SelectedProfile.Id.Value] = _settings.Theme;
            }
            else
            {
                themes.Remove(SelectedProfile.Id.Value);
            }

            _settings = _settings with { ProfileThemes = themes };
            _settingsStore.Save(_settings);
            ApplyResolvedTheme();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsProfileLightTheme));
        }
    }

    public bool IsProfileLightTheme
    {
        get => ResolveTheme() == AppTheme.Light;
        set
        {
            if (SelectedProfile is null)
            {
                return;
            }

            var requested = value ? AppTheme.Light : AppTheme.Dark;
            var themes = _settings.ProfileThemes is null
                ? []
                : new Dictionary<Guid, AppTheme>(_settings.ProfileThemes);
            if (themes.GetValueOrDefault(SelectedProfile.Id.Value) == requested
                && UsesProfileThemeOverride)
            {
                return;
            }

            themes[SelectedProfile.Id.Value] = requested;
            _settings = _settings with { ProfileThemes = themes };
            _settingsStore.Save(_settings);
            ApplyResolvedTheme();
            OnPropertyChanged(nameof(UsesProfileThemeOverride));
            OnPropertyChanged();
        }
    }

    public bool IsReduceMotionEnabled
    {
        get => _settings.ReduceMotion;
        set
        {
            if (value == _settings.ReduceMotion)
            {
                return;
            }

            _settings = _settings with { ReduceMotion = value };
            _settingsStore.Save(_settings);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMotionActive));
        }
    }

    public bool AreCyclePhaseNotificationsEnabled
    {
        get => _settings.CyclePhaseNotifications;
        set
        {
            if (value == _settings.CyclePhaseNotifications)
            {
                return;
            }

            _settings = _settings with { CyclePhaseNotifications = value };
            _settingsStore.Save(_settings);
            OnPropertyChanged();
        }
    }

    public string AppearanceTitle => UiStrings.SettingsAppearanceTitle;

    public string AppearanceDescription => UiStrings.SettingsAppearanceDescription;

    public string AppearanceLightLabel => UiStrings.SettingsAppearanceLight;

    public bool StartsWithWindows
    {
        get => _startsWithWindows;
        set
        {
            if (value == _startsWithWindows || _startupRegistration is null)
            {
                return;
            }

            if (_startupRegistration.TrySetEnabled(value))
            {
                _startsWithWindows = value;
                OnPropertyChanged();
            }
        }
    }

    public string StartupTitle => UiStrings.SettingsStartupTitle;

    public string StartupDescription => UiStrings.SettingsStartupDescription;

    public string StartupToggleLabel => UiStrings.SettingsStartupToggle;

    public DateTimeOffset? SessionStartUtc => _sessionStart;

    public DateTimeOffset? SessionEndUtc => _sessionEnd;

    public ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(
        Guid sessionId,
        CancellationToken cancellationToken) =>
        _client.GetSessionSummaryAsync(sessionId, cancellationToken);

    public string SessionEndTitle => UiStrings.SettingsSessionEndTitle;

    public string SessionEndDescription => UiStrings.SettingsSessionEndDescription;

    /// <summary>
    /// Chains straight into another session of the given length from the summary window.
    /// Strict mode still goes through its own review: skipping that would let the summary
    /// commit the user to something irreversible with one click.
    /// </summary>
    public async Task StartAnotherSessionAsync(int minutes)
    {
        if (minutes < 1 || IsActive)
        {
            return;
        }

        SelectedDurationPreset = DurationChoices.FirstOrDefault(
                preset => !preset.IsCustom && (int)preset.Duration.TotalMinutes == minutes)
            ?? _customPreset;
        if (SelectedDurationPreset.IsCustom)
        {
            CustomDurationMinutes = minutes;
        }

        await StartFocusAsync();
    }

    public ICommand StartFocusCommand => _startFocusCommand;

    public ICommand RetryProtectionCommand => _retryProtectionCommand;

    public ICommand ConfirmStrictCommand => _confirmStrictCommand;

    public ICommand CancelStrictCommand => _cancelStrictCommand;

    public ICommand StopFocusCommand => _stopFocusCommand;

    public ICommand SkipBreakCommand => _skipBreakCommand;

    private async Task SkipBreakAsync()
    {
        if (!CanSkipBreak || _currentPhase is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var response = await _sessionWorkflow.SkipBreakAsync(
                _currentPhase.Index,
                CancellationToken.None);
            StatusMessage = response.Reason;
            await RefreshAsync();
        }
        catch (Exception exception) when (
            exception is IOException
            or TimeoutException
            or InvalidDataException
            or OperationCanceledException)
        {
            StatusMessage = string.Format(UiStrings.Get("SkipBreakFailed"), exception.Message);
            MarkUnconfirmedMutation(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_profilesLoaded)
            {
                await LoadProfilesAsync(cancellationToken);
            }

            var status = await _sessionWorkflow.GetStatusAsync(cancellationToken);
            UpdateConnectionSnapshot();
            if (!IsSessionStateFresh) { PresentUnavailableConnection(); return; }
            ApplyStatus(status);

            // Status is polled every second; blocked-attempt counters change far more
            // slowly, so the second pipe round trip runs at a fifth of that rate.
            if (IsActive && _activityPollTicks++ % 5 == 0)
            {
                await TryRefreshSessionMetricsAsync(cancellationToken);
            }
            else if (!IsActive)
            {
                _activityPollTicks = 0;
                // Re-baseline so the first metrics poll of the next session treats the
                // blocks it inherits as history rather than as something just happening.
                _lastApplicationBlockId = Guid.Empty;
            }
        }
        catch (Exception exception) when (
            exception is IOException
            or TimeoutException
            or OperationCanceledException
            or InvalidDataException)
        {
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                return;
            }

            UpdateConnectionSnapshot();
            PresentUnavailableConnection();
        }
    }

    private async Task TryRefreshSessionMetricsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshSessionMetricsAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException
            or TimeoutException
            or OperationCanceledException
            or InvalidDataException)
        {
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            // Activity counters are supplemental. Keep their last known values and let
            // the next scheduled poll retry without overriding authoritative session state.
        }
    }

    public async Task ShowActivityAsync(CancellationToken cancellationToken = default)
    {
        Section = NavigationSection.Activity;
        await LoadTodayAsync(cancellationToken);
        ActivityEvents.Clear();
        try
        {
            var response = await _client.GetActivityAsync(200, cancellationToken);
            foreach (var item in response.Events) ActivityEvents.Add(ToActivityViewModel(item));
            TechnicalLogError = "";
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidDataException or OperationCanceledException)
        { TechnicalLogError = UiStrings.TechnicalLogUnavailable; }
        OnPropertyChanged(nameof(IsActivityEmpty));
        OnPropertyChanged(nameof(TechnicalLogError));
    }

    public void ShowFocus() => ShowSection(NavigationSection.Focus);

    /// <summary>
    /// Blocked attempts and the three most recent blocks come from the audit events
    /// the enforcement service already emits, filtered to the running session.
    /// </summary>
    private async Task RefreshSessionMetricsAsync(CancellationToken cancellationToken)
    {
        var response = await _client.GetActivityAsync(200, cancellationToken);
        var sessionStart = _sessionStart ?? DateTimeOffset.MinValue;
        var blocks = response.Events
            .Where(item => item.TimestampUtc >= sessionStart)
            .Where(item => item.Kind is FocusAuditEventKind.ApplicationBlocked
                or FocusAuditEventKind.WebsiteBlocked)
            .OrderByDescending(item => item.TimestampUtc)
            .ToArray();

        _blockedAttempts = blocks.Length;
        OnPropertyChanged(nameof(BlockedAttemptsText));

        // The contextual column reports attempts per row, so the same set of events is
        // also tallied by target. Grouping here keeps the column a pure projection.
        _blockedAttemptsByTarget = blocks
            .GroupBy(item => item.Target, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        OnPropertyChanged(nameof(BlockedItems));

        // Only application blocks raise the desktop block screen: a blocked site is
        // already answered by the extension's own page, inside the tab the user was in.
        var latestApplicationBlock = blocks
            .FirstOrDefault(item => item.Kind == FocusAuditEventKind.ApplicationBlocked);
        if (latestApplicationBlock is not null && latestApplicationBlock.Id != _lastApplicationBlockId)
        {
            var isFirstObservation = _lastApplicationBlockId == Guid.Empty;
            _lastApplicationBlockId = latestApplicationBlock.Id;
            // The first poll of a session sees history, not a fresh block; announcing it
            // would throw a full-screen window at someone who did nothing.
            if (!isFirstObservation)
            {
                ApplicationBlocked?.Invoke(
                    this,
                    new ApplicationBlockedEventArgs(latestApplicationBlock.Target));
            }
        }

        RecentBlocks.Clear();
        foreach (var item in blocks.Take(3))
        {
            RecentBlocks.Add(ToActivityViewModel(item));
        }

        OnPropertyChanged(nameof(HasRecentBlocks));
    }

    private async Task StartFocusAsync()
    {
        if (!CanStart) return;
        if (IsStrictMode)
        {
            var generated = RecoveryCodeService.Generate();
            _pendingRecoveryCredential = generated.Credential;
            RecoveryCodeDisplay = generated.DisplayCode;
            // Typing the profile's own name is a confirmation the user can read off the
            // screen they came from. A random two-word phrase tested nothing except
            // transcription, and taught them to copy whatever the dialog offered.
            RequiredConfirmationText = ProfileName;
            StrictWizardStep = 1;
            if (IsCycleMode && !await PrepareCycleForReviewAsync(recoveryDelaySeconds: 15 * 60))
            {
                CancelStrictReview();
                return;
            }

            IsStrictReviewVisible = true;
            StatusMessage = UiStrings.Get("SaveRecoveryNow");
            OnPropertyChanged(nameof(StrictEndTimeText));
            OnPropertyChanged(nameof(StrictEndTimeDescription));
            NotifyStrictWizardCopyChanged();
            OnPropertyChanged(nameof(CanCancelStrict));
            NotifyCommands();
            return;
        }

        await ActivateAsync(null, 0);
    }

    private async Task ConfirmStrictAsync()
    {
        if (!CanConfirmStrict || _pendingRecoveryCredential is null)
        {
            return;
        }

        if (IsCycleMode && _pendingPreparationId is { } preparationId)
        {
            IsBusy = true;
            try
            {
                var activated = await _sessionWorkflow.ActivatePreparedAsync(
                    new ActivatePreparedSessionRequest(preparationId, _pendingRecoveryCredential),
                    CancellationToken.None);
                StatusMessage = activated.Reason;
                if (activated.Accepted)
                {
                    RememberAcceptedSession(activated.SessionId);
                    CancelStrictReview();
                    await RefreshAsync();
                }
            }
            catch (Exception exception) when (
                exception is IOException
                or TimeoutException
                or InvalidDataException
                or OperationCanceledException)
            {
                StatusMessage = string.Format(UiStrings.Get("ActivationFailed"), exception.Message);
                MarkUnconfirmedMutation(exception);
            }
            finally
            {
                IsBusy = false;
            }

            return;
        }

        await ActivateAsync(_pendingRecoveryCredential, recoveryDelaySeconds: 15 * 60);
    }

    private Task CancelStrictReviewAsync()
    {
        CancelStrictReview();
        return Task.CompletedTask;
    }

    private async Task<bool> PrepareCycleForReviewAsync(int recoveryDelaySeconds)
    {
        if (SelectedProfile is null || SelectedCyclePreset is null)
        {
            StatusMessage = UiStrings.Get("CyclePresetRequired");
            return false;
        }

        try
        {
            var prepared = await _sessionWorkflow.PrepareCycleAsync(
                CreateCycleSessionInput(recoveryDelaySeconds),
                CancellationToken.None);
            StatusMessage = prepared.Reason;
            if (!prepared.Accepted || prepared.PreparationId is not { } preparationId)
            {
                return false;
            }

            _pendingPreparation = prepared;
            _pendingPreparationId = preparationId;
            OnPropertyChanged(nameof(PreparedCyclePlanText));
            OnPropertyChanged(nameof(HasPreparedCyclePlan));
            OnPropertyChanged(nameof(CanConfirmStrict));
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or TimeoutException
            or InvalidDataException
            or OperationCanceledException)
        {
            StatusMessage = string.Format(UiStrings.Get("ActivationFailed"), exception.Message);
            MarkUnconfirmedMutation(exception);
            return false;
        }
    }

    private CycleSessionInput CreateCycleSessionInput(int recoveryDelaySeconds) =>
        new(SelectedProfile!, SelectedCyclePreset!.Configuration, _profileCycleSettings,
            IsStrictMode, recoveryDelaySeconds, IsEnforcementEnabled);
    private async Task ActivateAsync(
        RecoveryCredential? credential,
        int recoveryDelaySeconds)
    {
        IsBusy = true;
        try
        {
            if (IsCycleMode)
            {
                if (SelectedCyclePreset is null)
                {
                    StatusMessage = UiStrings.Get("CyclePresetRequired");
                    return;
                }

                var started = await _sessionWorkflow.StartNormalCycleAsync(
                    CreateCycleSessionInput(recoveryDelaySeconds), CancellationToken.None);
                StatusMessage = started.Activation?.Reason ?? started.Preparation.Reason;
                if (started.Activation is { Accepted: true } activated)
                {
                    RememberAcceptedSession(activated.SessionId);
                    CancelStrictReview();
                    await RefreshAsync();
                }
                return;
            }

            var response = IsStrictMode
                ? await _sessionWorkflow.StartStrictTimerAsync(
                    new StrictTimerInput(SelectedProfile!, checked((int)SelectedDurationPreset.Duration.TotalSeconds),
                        recoveryDelaySeconds, credential, IsEnforcementEnabled), CancellationToken.None)
                : await _sessionWorkflow.StartNormalAsync(
                    new StartNormalInput(
                        SelectedProfile!.Id.Value,
                        checked((int)SelectedDurationPreset.Duration.TotalSeconds),
                        IsEnforcementEnabled),
                    CancellationToken.None);
            StatusMessage = response.Reason == SessionWorkflow.ProfileMissingReason
                ? UiStrings.Get("SessionProfileUnavailable")
                : response.Reason;
            if (response.Accepted)
            {
                RememberAcceptedSession(response.SessionId);
                CancelStrictReview();
                await RefreshAsync();
            }
        }
        catch (Exception exception) when (
            exception is IOException
            or TimeoutException
            or InvalidDataException
            or OperationCanceledException
            or Microsoft.Data.Sqlite.SqliteException)
        {
            StatusMessage = string.Format(UiStrings.Get("ActivationFailed"), exception.Message);
            MarkUnconfirmedMutation(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopFocusAsync()
    {
        if (!CanStop) return;
        IsBusy = true;
        try
        {
            var response = await _sessionWorkflow.StopNormalAsync(CancellationToken.None);
            StatusMessage = response.Reason;
            await RefreshAsync();
        }
        catch (Exception exception) when (
            exception is IOException
            or TimeoutException
            or InvalidDataException
            or OperationCanceledException)
        {
            StatusMessage = string.Format(UiStrings.Get("StopFailed"), exception.Message);
            MarkUnconfirmedMutation(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Raised once, when a session that was running stops running. Carries the session ID
    /// so the shell can ask the service for that exact summary rather than the latest.
    /// </summary>
    public event EventHandler<SessionEndedEventArgs>? SessionEnded;

    /// <summary>
    /// Raised when the service reports that it kept an application closed. The block
    /// screen is shown by the shell, not by the service, so the shell has to hear it.
    /// </summary>
    public event EventHandler<ApplicationBlockedEventArgs>? ApplicationBlocked;

    /// <summary>
    /// Raised only when a service-authoritative focus/short-break boundary changes.
    /// </summary>
    public event EventHandler<CyclePhaseTransitionEventArgs>? CyclePhaseChanged;

    private void ApplyStatus(StatusResponse status)
    {
        var cyclePhaseTransition = _cyclePhaseTransitionTracker.Observe(status);
        DetectSessionClosure(status);
        var previousStart = _sessionStart;
        var previousEnd = _sessionEnd;
        var previousRemainingSeconds = _remainingSeconds;
        var previousTotalSeconds = _sessionTotalSeconds;
        var previousStrictMode = _activeStrictMode;
        var previousEnforcementConnected = _isEnforcementConnected;
        var previousServiceReady = _isServiceReady;
        var previousExtensionReady = _isExtensionReady;
        var previousMode = _activeMode;
        var previousCurrentPhase = _currentPhase;
        var previousNextPhase = _nextPhase;
        var wasActive = IsActive;

        ServiceHealthLabel = status.ServiceReady
            ? status.ExtensionReady
                ? UiStrings.Get("ServiceAndExtensionReady")
                : UiStrings.Get("ServiceReadyExtensionUnavailable")
            : UiStrings.Get("ServiceUnavailable");
        _sessionStart = status.StartUtc;
        _sessionEnd = status.EndUtc;
        _activeMode = status.Mode;
        _currentPhase = status.CurrentPhase;
        _nextPhase = status.NextPhase;
        _remainingSeconds = Math.Max(0, status.RemainingSeconds);
        _sessionTotalSeconds = status.StartUtc is { } start && status.EndUtc is { } end && end > start
            ? (long)(end - start).TotalSeconds
            : 0;
        _isEnforcementConnected = status.EnforcementConnected;
        _isServiceReady = status.ServiceReady;
        _isExtensionReady = status.ExtensionReady;
        IsActive = status.SessionState is "Activating" or "Active";
        _activeStrictMode = status.StrictMode;
        RemainingText = FormatRemaining(status.RemainingSeconds);
        ExactEndText = status.EndUtc?.ToLocalTime().ToString("g") ?? UiStrings.NoActiveEndTime;
        ObserveWorkflowStatus(status);

        var sessionShapeChanged = previousStart != _sessionStart
            || previousEnd != _sessionEnd
            || previousTotalSeconds != _sessionTotalSeconds;
        var timerChanged = previousRemainingSeconds != _remainingSeconds;
        var strictModeChanged = previousStrictMode != _activeStrictMode;
        // A channel can drop without the aggregate connection flag moving, so the two
        // per-channel signals count as changes in their own right.
        var enforcementChanged = previousEnforcementConnected != _isEnforcementConnected
            || previousServiceReady != _isServiceReady
            || previousExtensionReady != _isExtensionReady;
        var phaseChanged = previousMode != _activeMode
            || previousCurrentPhase != _currentPhase
            || previousNextPhase != _nextPhase;
        if (wasActive == IsActive)
        {
            if (sessionShapeChanged)
            {
                NotifySessionShapeChanged();
            }
            if (timerChanged)
            {
                NotifyTimerPresentationChanged();
            }
            if (strictModeChanged)
            {
                NotifyStrictPresentationChanged();
            }
            if (enforcementChanged)
            {
                OnPropertyChanged(nameof(IsEnforcementConnected));
                OnPropertyChanged(nameof(IsEnforcementDegraded));
                OnPropertyChanged(nameof(IsTimerOnly));
                OnPropertyChanged(nameof(IsNoRulesConfigured));
                OnPropertyChanged(nameof(ActiveBlockingDescription));
                NotifyProtectionStateChanged();
            }
        }
        else if (strictModeChanged)
        {
            // IsActive already raised the complete presentation set, but it observed
            // the preceding strict flag. Refresh only the strict-dependent values.
            NotifyStrictPresentationChanged();
        }

        StatusMessage = status.SessionState == "Activating"
            ? UiStrings.Get("StateActivating")
            : IsActive
            ? !IsEnforcementEnabled
                ? UiStrings.Get("EnforcementOffWarning")
                : !HasConfiguredBlockingRules
                    ? UiStrings.Get("NoBlockingRulesActive")
                    : status.EnforcementConnected
                    ? UiStrings.Get("StateConnected")
                    : UiStrings.Get("StateDegraded")
            : status.SessionState is "Expired" or "Stopped" or "Recovered"
                ? status.SessionState == "Expired"
                    ? UiStrings.Get("StateExpired")
                    : string.Format(UiStrings.Get("StateReleased"), status.SessionState.ToLowerInvariant())
                : UiStrings.Get("NoActiveSession");
        if (phaseChanged)
        {
            OnPropertyChanged(nameof(IsCycleSession));
            OnPropertyChanged(nameof(IsBreakPhase));
            OnPropertyChanged(nameof(CurrentPhaseLabel));
            OnPropertyChanged(nameof(ActiveCycleLabel));
            OnPropertyChanged(nameof(CycleProgressText));
            OnPropertyChanged(nameof(CurrentPhaseStatus));
            OnPropertyChanged(nameof(NextPhaseText));
            OnPropertyChanged(nameof(CanSkipBreak));
            OnPropertyChanged(nameof(ActiveBlockingDescription));
        }

        if (cyclePhaseTransition is not null)
        {
            CyclePhaseChanged?.Invoke(this, new CyclePhaseTransitionEventArgs(cyclePhaseTransition));
        }
    }

    private void NotifyTimerPresentationChanged()
    {
        OnPropertyChanged(nameof(TitleBarStatus));
        OnPropertyChanged(nameof(RingSweepAngle));
        OnPropertyChanged(nameof(RingCenterValue));
        OnPropertyChanged(nameof(MetricValue));
        OnPropertyChanged(nameof(MetricSegments));
        OnPropertyChanged(nameof(HasMetric));
        OnPropertyChanged(nameof(CanSkipBreak));
    }

    private void NotifySessionShapeChanged()
    {
        OnPropertyChanged(nameof(SessionMeta));
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(MetricLabel));
        OnPropertyChanged(nameof(RingSweepAngle));
        OnPropertyChanged(nameof(RingCenterCaption));
        OnPropertyChanged(nameof(StrictActiveNotice));
        OnPropertyChanged(nameof(IsCycleSession));
        OnPropertyChanged(nameof(IsBreakPhase));
        OnPropertyChanged(nameof(CurrentPhaseLabel));
        OnPropertyChanged(nameof(ActiveCycleLabel));
        OnPropertyChanged(nameof(CycleProgressText));
        OnPropertyChanged(nameof(CurrentPhaseStatus));
        OnPropertyChanged(nameof(NextPhaseText));
        NotifyStopPresentationChanged();
        OnPropertyChanged(nameof(ActiveBlockingDescription));
    }

    /// <summary>
    /// Every predicate the running-session action band binds to. They all derive from
    /// <see cref="CanStop"/>, so raising CanStop alone left the Stop button bound to a
    /// value computed once while the session was still inactive — permanently false, and
    /// with no strict mode there was then no way at all to end a session from the UI.
    /// </summary>
    private void NotifyStopPresentationChanged()
    {
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanStopSimpleSession));
        OnPropertyChanged(nameof(CanSkipBreak));
        OnPropertyChanged(nameof(IsFocusPhaseActive));
        OnPropertyChanged(nameof(IsNormalSessionActive));
        OnPropertyChanged(nameof(IsStrictSessionActive));
    }

    private void NotifyStrictPresentationChanged()
    {
        OnPropertyChanged(nameof(ModeLabel));
        NotifyStopPresentationChanged();
        OnPropertyChanged(nameof(IsEnforcementConnected));
        OnPropertyChanged(nameof(IsEnforcementDegraded));
        OnPropertyChanged(nameof(IsTimerOnly));
        OnPropertyChanged(nameof(ActiveBlockingDescription));
        OnPropertyChanged(nameof(StrictActiveNotice));
        _stopFocusCommand.RaiseCanExecuteChanged();
        _skipBreakCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// A session is "closed" the first poll after it stops being Activating or Active.
    /// Tracking the ID rather than the flag keeps a reconnect from replaying the summary.
    /// </summary>
    private void DetectSessionClosure(StatusResponse status)
    {
        var isRunning = status.SessionState is "Activating" or "Active";
        if (isRunning && status.SessionId is { } running)
        {
            _runningSessionId = running;
            return;
        }

        if (isRunning || _runningSessionId is not { } finished)
        {
            return;
        }

        _runningSessionId = null;
        SessionEnded?.Invoke(this, new SessionEndedEventArgs(finished));
    }

    private void CancelStrictReview()
    {
        _pendingPreparation = null;
        _pendingPreparationId = null;
        _pendingRecoveryCredential = null;
        RecoveryCodeDisplay = string.Empty;
        RequiredConfirmationText = string.Empty;
        StrictWizardStep = 1;
        IsStrictLimitsVisible = false;
        IsStrictReviewVisible = false;
        TypedConfirmation = string.Empty;
        OnPropertyChanged(nameof(PreparedCyclePlanText));
        OnPropertyChanged(nameof(HasPreparedCyclePlan));
        OnPropertyChanged(nameof(CanCancelStrict));
        NotifyCommands();
    }

    private bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(StartFocusButtonText));
                NotifyStopPresentationChanged();
                OnPropertyChanged(nameof(CanConfirmStrict));
                OnPropertyChanged(nameof(CanEditProfileRules));
                OnPropertyChanged(nameof(CanRenameProfile));
                OnPropertyChanged(nameof(CanDeleteProfile));
                OnPropertyChanged(nameof(CanConfirmProfileDelete));
                RefreshRuleCommands();
                NotifyCommands();
            }
        }
    }

    private static string FormatRemaining(long remainingSeconds)
    {
        var remaining = TimeSpan.FromSeconds(Math.Max(0, remainingSeconds));
        return remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    private static string PhaseLabel(SessionPhaseKind kind) => kind switch
    {
        SessionPhaseKind.Focus => UiStrings.FocusPhase,
        SessionPhaseKind.ShortBreak => UiStrings.ShortBreakPhase,
        SessionPhaseKind.LongBreak => UiStrings.LongBreakPhase,
        _ => UiStrings.SimpleTimerPhase
    };

    private void RaiseSessionPresentationChanged()
    {
        OnPropertyChanged(nameof(SessionEyebrow));
        OnPropertyChanged(nameof(SessionTitle));
        OnPropertyChanged(nameof(SessionMeta));
        OnPropertyChanged(nameof(StatusPillText));
        OnPropertyChanged(nameof(TitleBarStatus));
        OnPropertyChanged(nameof(HasTitleBarStatus));
        OnPropertyChanged(nameof(RingSweepAngle));
        OnPropertyChanged(nameof(RingCenterValue));
        OnPropertyChanged(nameof(RingCenterCaption));
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(HasProfileSwatches));
        OnPropertyChanged(nameof(MetricValue));
        OnPropertyChanged(nameof(MetricSegments));
        OnPropertyChanged(nameof(MetricLabel));
        OnPropertyChanged(nameof(MetricBrush));
        OnPropertyChanged(nameof(HasMetric));
        OnPropertyChanged(nameof(HasMetricPlaceholder));
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(WeekMinutes));
        OnPropertyChanged(nameof(IsStrictSessionActive));
        OnPropertyChanged(nameof(IsEnforcementConnected));
        OnPropertyChanged(nameof(IsEnforcementDegraded));
        OnPropertyChanged(nameof(IsTimerOnly));
        OnPropertyChanged(nameof(ActiveBlockingDescription));
        OnPropertyChanged(nameof(StrictActiveNotice));
        OnPropertyChanged(nameof(IsNavigationLocked));
        OnPropertyChanged(nameof(CanNavigateToEditors));
    }

    private void NotifyCommands()
    {
        _startFocusCommand.RaiseCanExecuteChanged();
        _confirmStrictCommand.RaiseCanExecuteChanged();
        _cancelStrictCommand.RaiseCanExecuteChanged();
        _stopFocusCommand.RaiseCanExecuteChanged();
        _skipBreakCommand.RaiseCanExecuteChanged();
        _addWebsiteRuleCommand.RaiseCanExecuteChanged();
        _addApplicationRuleCommand.RaiseCanExecuteChanged();
        _duplicateProfileCommand.RaiseCanExecuteChanged();
        _renameProfileCommand.RaiseCanExecuteChanged();
        _deleteProfileCommand.RaiseCanExecuteChanged();
        _confirmProfileDeleteCommand.RaiseCanExecuteChanged();
        _cancelProfileDeleteCommand.RaiseCanExecuteChanged();
        _newProfileCommand.RaiseCanExecuteChanged();
        _addNewProfileCustomRuleCommand.RaiseCanExecuteChanged();
        _createProfileCommand.RaiseCanExecuteChanged();
        _cancelCreateProfileCommand.RaiseCanExecuteChanged();
        _createCyclePresetCommand.RaiseCanExecuteChanged();
        _editCyclePresetCommand.RaiseCanExecuteChanged();
        _commitCyclePresetCommand.RaiseCanExecuteChanged();
        _cancelCyclePresetEditCommand.RaiseCanExecuteChanged();
        _saveCyclePresetCommand.RaiseCanExecuteChanged();
        _deleteCyclePresetCommand.RaiseCanExecuteChanged();
        _addBreakApplicationCommand.RaiseCanExecuteChanged();
        _addBreakWebsiteCommand.RaiseCanExecuteChanged();
    }

    private async Task AddWebsiteRuleAsync()
    {
        if (!CanEditProfileRules || SelectedProfile is null) return;
        await RunProfileMutationAsync(async () =>
        {
            var result = await _profileStore.AddWebsiteRuleAsync(SelectedProfile, WebsiteAction.Action, WebsiteRuleInput);
            if (!result.IsValid) { StatusMessage = result.Errors[0].Message; return; }
            WebsiteRuleInput = string.Empty;
            await LoadProfilesAsync(CancellationToken.None);
            StatusMessage = UiStrings.Get("WebsiteRuleSaved");
        });
    }

    private async Task AddApplicationRuleAsync()
    {
        if (!CanEditProfileRules || SelectedProfile is null) return;
        await RunProfileMutationAsync(async () =>
        {
            var result = await _profileStore.AddApplicationRuleAsync(SelectedProfile, ApplicationAction.Action, Path.IsPathFullyQualified(ApplicationRuleInput) ? ApplicationMatchKind.ExactPath : ApplicationMatchKind.FileName, ApplicationRuleInput);
            if (!result.IsValid) { StatusMessage = result.Errors[0].Message; return; }
            ApplicationRuleInput = string.Empty;
            await LoadProfilesAsync(CancellationToken.None);
            StatusMessage = UiStrings.Get("ApplicationRuleSaved");
        });
    }

    public async Task RemoveApplicationRuleAsync(Guid ruleId)
    {
        if (!CanEditProfileRules || SelectedProfile is null)
        {
            StatusMessage = UiStrings.RulesLocked;
            return;
        }

        var profileId = SelectedProfile.Id;
        await RunProfileMutationAsync(async () =>
        {
            if (!await _profileStore.RemoveApplicationRuleAsync(SelectedProfile, ruleId)) return;
            await LoadProfilesAsync(CancellationToken.None, profileId);
            StatusMessage = UiStrings.ApplicationRuleRemoved;
        });
    }

    public async Task RemoveWebsiteRuleAsync(Guid ruleId)
    {
        if (!CanEditProfileRules || SelectedProfile is null)
        {
            StatusMessage = UiStrings.RulesLocked;
            return;
        }

        var profileId = SelectedProfile.Id;
        await RunProfileMutationAsync(async () =>
        {
            if (!await _profileStore.RemoveWebsiteRuleAsync(SelectedProfile, ruleId)) return;
            await LoadProfilesAsync(CancellationToken.None, profileId);
            StatusMessage = UiStrings.WebsiteRuleRemoved;
        });
    }

    public async Task DuplicateSelectedProfileAsync()
    {
        if (!CanEditProfileRules || SelectedProfile is null)
        {
            StatusMessage = UiStrings.RulesLocked;
            return;
        }

        var source = SelectedProfile;
        IsBusy = true;
        try
        {
            var duplicate = await _profileStore.DuplicateAsync(
                source, UiStrings.ProfileCopyName, UiStrings.ProfileCopyNumberSuffix);
            await LoadProfilesAsync(CancellationToken.None, duplicate.Id);
            StatusMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.ProfileDuplicated, duplicate.Name);
        }
        catch (ProfileWriteConflictException exception)
        {
            await ReloadProfileConflictAsync(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }
    public async Task RenameSelectedProfileAsync()
    {
        if (!CanEditProfileRules || SelectedProfile is null)
        {
            StatusMessage = UiStrings.RulesLocked;
            return;
        }

        var nameResult = Profile.ValidateName(ProfileNameInput);
        if (!nameResult.IsValid || !CanRenameProfile)
        {
            StatusMessage = string.Equals(
                nameResult.Value,
                SelectedProfile.Name,
                StringComparison.CurrentCulture)
                ? UiStrings.ProfileNameUnchanged
                : ProfileNameValidationMessage;
            return;
        }

        var profileId = SelectedProfile.Id;

        IsBusy = true;
        try
        {
            var result = await _profileStore.RenameAsync(SelectedProfile, nameResult.Value!);
            if (!result.IsValid) { StatusMessage = result.Errors[0].Message; return; }
            var renamed = result.Value!;
            await LoadProfilesAsync(CancellationToken.None, profileId);
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.ProfileRenamed,
                renamed.Name);
        }
        catch (ProfileWriteConflictException exception)
        {
            await ReloadProfileConflictAsync(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task RequestProfileDeleteAsync()
    {
        if (!CanEditProfileRules || SelectedProfile is null)
        {
            StatusMessage = UiStrings.RulesLocked;
            return Task.CompletedTask;
        }

        if (Profiles.Count <= 1)
        {
            StatusMessage = UiStrings.LastProfileRequired;
            return Task.CompletedTask;
        }

        IsDeleteProfileConfirmationVisible = true;
        return Task.CompletedTask;
    }

    public Task CancelProfileDeleteAsync()
    {
        IsDeleteProfileConfirmationVisible = false;
        return Task.CompletedTask;
    }

    public async Task ConfirmProfileDeleteAsync()
    {
        if (!CanConfirmProfileDelete || SelectedProfile is null)
        {
            if (!CanEditProfileRules)
            {
                StatusMessage = UiStrings.RulesLocked;
            }
            else if (Profiles.Count <= 1)
            {
                StatusMessage = UiStrings.LastProfileRequired;
            }

            return;
        }

        var profile = SelectedProfile;
        IsBusy = true;
        try
        {
            if (!await _profileStore.DeleteReviewedAsync(profile))
            {
                StatusMessage = UiStrings.ProfileDeleteFailed;
                return;
            }

            IsDeleteProfileConfirmationVisible = false;
            await LoadProfilesAsync(CancellationToken.None);
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.ProfileDeleted,
                profile.Name);
        }
        catch (ProfileWriteConflictException exception)
        {
            IsDeleteProfileConfirmationVisible = false;
            await ReloadProfileConflictAsync(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task BeginCreateProfileAsync()
    {
        if (!CanEditProfileRules)
        {
            StatusMessage = UiStrings.RulesLocked;
            return Task.CompletedTask;
        }

        IsDeleteProfileConfirmationVisible = false;
        NewProfileNameInput = string.Empty;
        NewProfileAccentHex = NextUnusedZenAccent();
        NewProfileCustomRuleInput = string.Empty;
        NewProfileRuleValidationMessage = string.Empty;
        NewProfileDraftRules.Clear();
        foreach (var suggestion in NewProfileSuggestions)
        {
            suggestion.Reset();
        }

        IsCreatingProfile = true;
        RaiseNewProfileSummaryChanged();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Pre-selects a colour no existing profile already wears, so a fresh profile is
    /// distinguishable at a glance without the user having to pick.
    /// </summary>
    private string NextUnusedZenAccent()
    {
        var taken = Profiles
            .Select(AccentHexFor)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ZenPalette.FirstOrDefault(hex => !taken.Contains(hex)) ?? ZenPalette[0];
    }

    public Task SelectNewProfileColorAsync(string hex)
    {
        NewProfileAccentHex = hex;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Recolouring an existing profile writes through immediately: there is nothing
    /// to confirm, and the header repaints so the choice is its own preview.
    /// </summary>
    public async Task ApplyProfileColorAsync(string hex)
    {
        if (!CanEditProfileRules || SelectedProfile is null)
        {
            StatusMessage = UiStrings.RulesLocked;
            return;
        }

        if (string.Equals(SelectedProfile.AccentHex, hex, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var profileId = SelectedProfile.Id;
        var original = SelectedProfile;

        IsBusy = true;
        try
        {
            var result = await _profileStore.SetAccentAsync(original, hex);
            if (!result.IsValid) { StatusMessage = result.Errors[0].Message; return; }
            await LoadProfilesAsync(CancellationToken.None, profileId);
        }
        catch (ProfileWriteConflictException exception)
        {
            await ReloadProfileConflictAsync(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SyncProfileColorSelection()
    {
        var current = SelectedProfile is null ? null : AccentHexFor(SelectedProfile);
        foreach (var option in ProfileColors)
        {
            option.IsSelected = string.Equals(option.Hex, current, StringComparison.OrdinalIgnoreCase);
        }
    }

    public Task CancelCreateProfileAsync()
    {
        IsCreatingProfile = false;
        NewProfileDraftRules.Clear();
        NewProfileRuleValidationMessage = string.Empty;
        return Task.CompletedTask;
    }

    public Task AddNewProfileCustomRuleAsync()
    {
        var input = NewProfileCustomRuleInput.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            NewProfileRuleValidationMessage = UiStrings.NewProfileRuleRequired;
            return Task.CompletedTask;
        }

        if (input.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var result = ApplicationRule.Create(
                RuleAction.Block,
                ApplicationMatchKind.FileName,
                input);
            if (!result.IsValid)
            {
                NewProfileRuleValidationMessage = result.Errors[0].Message;
                return Task.CompletedTask;
            }

            if (NewProfileDraftRules.Any(rule =>
                rule.ApplicationRule is not null
                && string.Equals(
                    rule.ApplicationRule.Value,
                    result.Value!.Value,
                    StringComparison.OrdinalIgnoreCase)))
            {
                NewProfileRuleValidationMessage = UiStrings.NewProfileRuleAlreadyAdded;
                return Task.CompletedTask;
            }

            var applicationRule = result.Value!;
            NewProfileDraftRules.Add(new NewProfileRuleDraftViewModel(
                applicationRule.Id,
                applicationRule.Value,
                applicationRule,
                null,
                RemoveNewProfileDraftRuleAsync));
        }
        else
        {
            var result = WebsiteRule.Create(RuleAction.Block, input);
            if (!result.IsValid)
            {
                NewProfileRuleValidationMessage = result.Errors[0].Message;
                return Task.CompletedTask;
            }

            var websiteRule = result.Value!;
            if (BuildNewProfileWebsiteRules().Any(rule =>
                string.Equals(rule.Pattern, websiteRule.Pattern, StringComparison.OrdinalIgnoreCase)))
            {
                NewProfileRuleValidationMessage = UiStrings.NewProfileRuleAlreadyAdded;
                return Task.CompletedTask;
            }

            NewProfileDraftRules.Add(new NewProfileRuleDraftViewModel(
                websiteRule.Id,
                websiteRule.Pattern,
                null,
                websiteRule,
                RemoveNewProfileDraftRuleAsync));
        }

        NewProfileCustomRuleInput = string.Empty;
        NewProfileRuleValidationMessage = string.Empty;
        RaiseNewProfileSummaryChanged();
        return Task.CompletedTask;
    }

    public async Task CreateProfileAsync()
    {
        if (!CanCreateProfile)
        {
            StatusMessage = NewProfileNameValidationMessage;
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _profileStore.CreateAsync(
                NewProfileNameInput,
                NewProfileDraftRules.Where(rule => rule.ApplicationRule is not null).Select(rule => rule.ApplicationRule!).ToArray(),
                BuildNewProfileWebsiteRules(), NewProfileAccentHex);
            if (!result.IsValid) { StatusMessage = result.Errors[0].Message; return; }
            var stored = result.Value!;
            IsCreatingProfile = false;
            await LoadProfilesAsync(CancellationToken.None, stored.Id);
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.ProfileCreated,
                stored.Name);
        }
        catch (ProfileWriteConflictException exception)
        {
            await ReloadProfileConflictAsync(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task RemoveNewProfileDraftRuleAsync(Guid id)
    {
        var rule = NewProfileDraftRules.FirstOrDefault(item => item.Id == id);
        if (rule is not null)
        {
            NewProfileDraftRules.Remove(rule);
            RaiseNewProfileSummaryChanged();
        }

        return Task.CompletedTask;
    }

    private IReadOnlyList<WebsiteRule> BuildNewProfileWebsiteRules()
    {
        var rules = new Dictionary<string, WebsiteRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var pattern in NewProfileSuggestions
                     .Where(suggestion => suggestion.IsSelected)
                     .SelectMany(suggestion => suggestion.Domains))
        {
            var rule = WebsiteRule.Create(RuleAction.Block, pattern).Value!;
            rules.TryAdd(rule.Pattern, rule);
        }

        foreach (var draft in NewProfileDraftRules.Where(rule => rule.WebsiteRule is not null))
        {
            rules.TryAdd(draft.WebsiteRule!.Pattern, draft.WebsiteRule);
        }

        return rules.Values.ToArray();
    }

    private void OnNewProfileSuggestionChanged()
    {
        NewProfileRuleValidationMessage = string.Empty;
        RaiseNewProfileSummaryChanged();
    }

    private void RaiseNewProfileSummaryChanged()
    {
        OnPropertyChanged(nameof(NewProfileWebsiteRuleCount));
        OnPropertyChanged(nameof(NewProfileApplicationRuleCount));
        OnPropertyChanged(nameof(NewProfileSummaryText));
    }

    private async Task LoadProfilesAsync(
        CancellationToken cancellationToken,
        ProfileId? preferredProfileId = null)
    {
        preferredProfileId ??= SelectedProfile?.Id ?? (_settings.LastProfileId is { } last ? new ProfileId(last) : null);
        var profiles = await _profileStore.ListProfilesAsync(cancellationToken);
        if (profiles.Count == 0)
        {
            try
            {
                var created = await _profileStore.CreateAsync(UiStrings.Get("DefaultProfileName"), [], [], null, cancellationToken);
                if (!created.IsValid) throw new InvalidDataException("Default profile validation failed.");
            }
            catch (ProfileWriteConflictException exception) when (exception.Status == ProfileMutationStatus.NameConflict)
            {
                // Another owner UI created the default first. Read it; never replay creation.
            }
            profiles = await _profileStore.ListProfilesAsync(cancellationToken);
        }

        Profiles.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
        }

        SelectedProfile = Profiles.FirstOrDefault(profile => profile.Id == preferredProfileId) ?? Profiles.First();
        await LoadCycleConfigurationAsync(cancellationToken);
        RestoreWorkflowPreferences();
        RebuildProfileSwatches();
        _profilesLoaded = true;
        RefreshRuleSearch();
        OnPropertyChanged(nameof(HomeProfiles));
        OnPropertyChanged(nameof(CanDeleteProfile));
        NotifyCommands();
    }

    private async Task RunProfileMutationAsync(Func<Task> mutation)
    {
        try { await mutation(); }
        catch (ProfileWriteConflictException exception) { await ReloadProfileConflictAsync(exception); }
    }

    private async Task ReloadProfileConflictAsync(ProfileWriteConflictException exception)
    {
        await LoadProfilesAsync(CancellationToken.None);
        StatusMessage = exception.Status switch
        {
            ProfileMutationStatus.NameConflict => UiStrings.ProfileNameAlreadyExists,
            ProfileMutationStatus.LastProfile => UiStrings.LastProfileRequired,
            _ => UiStrings.Get("ProfileChangedReload")
        };
    }

    private async Task LoadCycleConfigurationAsync(CancellationToken cancellationToken)
    {
        var storedPresets = await _profileStore.ListCyclePresetsAsync(cancellationToken);
        var presets = storedPresets.Count == 0
            ? BuiltInCyclePresets.All
            : storedPresets;

        CyclePresets.Clear();
        foreach (var preset in presets)
        {
            CyclePresets.Add(preset);
        }

        OnPropertyChanged(nameof(VisibleCyclePresets));
        RebuildCyclePresetCards();

        var cycleSnapshot = SelectedProfile is null
            ? null
            : await _profileStore.LoadRevisionedProfileCycleSettingsAsync(SelectedProfile.Id, cancellationToken);
        _profileCycleSettings = cycleSnapshot?.Settings;
        _profileCycleSettingsRevision = cycleSnapshot?.Revision;
        NotifyEnforcementPresentationChanged();
        var preferredId = _profileCycleSettings?.DefaultPresetId;
        SelectedCyclePreset = preferredId is { } id
            ? CyclePresets.FirstOrDefault(preset => preset.Id == id) ?? CyclePresets.FirstOrDefault()
            : CyclePresets.FirstOrDefault();
        RebuildBreakRules();
        NotifyBreakExceptionsChanged();
    }

    private void NotifyBreakExceptionsChanged()
    {
        OnPropertyChanged(nameof(BreakExceptionItems));
        OnPropertyChanged(nameof(HasBreakExceptions));
    }

    private async Task RefreshSelectedProfileCycleSettingsAsync(ProfileId profileId)
    {
        try
        {
            var snapshot = await _profileStore.LoadRevisionedProfileCycleSettingsAsync(profileId);
            if (SelectedProfile?.Id != profileId)
            {
                return;
            }

            _profileCycleSettings = snapshot?.Settings;
            _profileCycleSettingsRevision = snapshot?.Revision;
            NotifyEnforcementPresentationChanged();
            var preferredId = snapshot?.Settings.DefaultPresetId;
            SelectedCyclePreset = preferredId is { } id
                ? CyclePresets.FirstOrDefault(preset => preset.Id == id) ?? CyclePresets.FirstOrDefault()
                : CyclePresets.FirstOrDefault();
            RebuildBreakRules();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            StatusMessage = string.Format(UiStrings.Get("CycleSettingsLoadFailed"), exception.Message);
        }
    }

    private void RebuildBreakRules()
    {
        BreakRules.Clear();
        if (_profileCycleSettings is null)
        {
            OnPropertyChanged(nameof(HasBreakRules));
            NotifyBreakExceptionsChanged();
            return;
        }

        foreach (var rule in _profileCycleSettings.ApplicationRules)
        {
            BreakRules.Add(new BreakRuleViewModel(
                rule.Value,
                isWebsite: false,
                rule.Id,
                RemoveBreakApplicationAsync));
        }

        foreach (var rule in _profileCycleSettings.WebsiteRules)
        {
            BreakRules.Add(new BreakRuleViewModel(
                rule.Pattern,
                isWebsite: true,
                rule.Id,
                RemoveBreakWebsiteAsync));
        }

        OnPropertyChanged(nameof(HasBreakRules));
        // The editor's chips and the contextual column read the same set, so every
        // rebuild refreshes both rather than leaving the right column stale.
        NotifyBreakExceptionsChanged();
    }

    private void NotifyEnforcementPresentationChanged()
    {
        OnPropertyChanged(nameof(HasConfiguredBlockingRules));
        OnPropertyChanged(nameof(IsEnforcementConnected));
        OnPropertyChanged(nameof(IsEnforcementDegraded));
        OnPropertyChanged(nameof(IsTimerOnly));
        OnPropertyChanged(nameof(IsNoRulesConfigured));
        OnPropertyChanged(nameof(ActiveBlockingDescription));
        NotifyProtectionStateChanged();
    }

    /// <summary>
    /// The contextual column's protection block is derived from the channel flags, so
    /// every one of its faces refreshes together. Without this the icon, the colour and
    /// the sentence could disagree about which state the app is in.
    /// </summary>
    private void NotifyProtectionStateChanged()
    {
        OnPropertyChanged(nameof(ProtectionState));
        OnPropertyChanged(nameof(IsProtectionHealthy));
        OnPropertyChanged(nameof(IsProtectionDegraded));
        OnPropertyChanged(nameof(IsProtectionUnavailable));
        OnPropertyChanged(nameof(IsProtectionActionable));
        OnPropertyChanged(nameof(ProtectionGlyph));
        OnPropertyChanged(nameof(ProtectionStatusText));
        OnPropertyChanged(nameof(ProtectionDetailText));
        OnPropertyChanged(nameof(HasProtectionDetail));
        OnPropertyChanged(nameof(ProtectionSummaryText));
    }

    private void RebuildProfileSwatches()
    {
        ProfileSwatches.Clear();
        foreach (var profile in Profiles)
        {
            ProfileSwatches.Add(new ProfileSwatchViewModel(
                profile,
                AccentBrushFor(profile),
                SwatchIdleBrush,
                profile.Id == SelectedProfile?.Id));
        }

        OnPropertyChanged(nameof(HasProfileSwatches));
    }

    /// <summary>
    /// Switching profile recolours the ring and the handful of indicators that say
    /// which profile is in use, from a single resource assignment. Everything else on
    /// screen belongs to the theme and is left alone.
    /// </summary>
    private void ApplyProfileAccent()
    {
        _profileAccentBrush = AccentBrushFor(SelectedProfile);
        OnPropertyChanged(nameof(ProfileAccentBrush));
        OnPropertyChanged(nameof(MetricBrush));

        // Null in unit tests, where no Avalonia application is running.
        var resources = Application.Current?.Resources;
        if (resources is null || _profileAccentBrush is not SolidColorBrush accent)
        {
            return;
        }

        var color = accent.Color;
        resources["ProfileAccentColor"] = color;
        resources["ProfileAccent"] = accent;
        resources["ProfileAccentSoft"] = new SolidColorBrush(Color.FromArgb(0x18, color.R, color.G, color.B));
        resources["ProfileAccentGradient"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Lighten(color, 0.22), 0),
                new GradientStop(Darken(color, 0.18), 1)
            }
        };
        resources["ProfileAccentSelectionGradient"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x52, color.R, color.G, color.B), 0),
                new GradientStop(Color.FromArgb(0x28, color.R, color.G, color.B), 1)
            }
        };
    }

    private AppTheme ResolveTheme()
    {
        AppTheme? profileTheme = null;
        if (SelectedProfile is not null
            && _settings.ProfileThemes?.TryGetValue(SelectedProfile.Id.Value, out var configured) == true)
        {
            profileTheme = configured;
        }

        return ThemePreferenceResolver.Resolve(profileTheme, _settings.Theme);
    }

    /// <summary>
    /// Applies the theme and then the accent, in that order: the accent's painted value
    /// depends on which flavour is active, so resolving it first would leave the ring a
    /// frame behind on every profile switch.
    /// </summary>
    private void ApplyResolvedTheme()
    {
        App.ApplyTheme(ResolveTheme());
        ApplyProfileAccent();
    }

    private IBrush AccentBrushFor(StoredProfile? profile) =>
        CreateFrozenBrush(
            CatppuccinAccents.ResolveHex(
                profile is null ? null : AccentHexFor(profile),
                ResolveTheme() == AppTheme.Light));

    /// <summary>
    /// The profile's stored accent in canonical (Mocha) form. Profiles saved before
    /// <c>AccentHex</c> existed get one derived from their id rather than their list
    /// position, so a profile's colour survives a rename shifting the alphabetical order.
    /// </summary>
    private static string AccentHexFor(StoredProfile profile)
    {
        if (Profile.IsAccentHex(profile.AccentHex))
        {
            return CatppuccinAccents.Match(profile.AccentHex).Mocha;
        }

        var slot = (int)((uint)profile.Id.Value.GetHashCode() % (uint)AccentPalette.Length);
        return AccentPalette[slot];
    }

    internal static SolidColorBrush CreateFrozenBrush(string hex) =>
        new(Color.Parse(hex));

    private static Color Lighten(Color color, double amount) => Color.FromArgb(
        color.A,
        (byte)Math.Clamp(color.R + (255 - color.R) * amount, 0, 255),
        (byte)Math.Clamp(color.G + (255 - color.G) * amount, 0, 255),
        (byte)Math.Clamp(color.B + (255 - color.B) * amount, 0, 255));

    private static Color Darken(Color color, double amount) => Color.FromArgb(
        color.A,
        (byte)Math.Clamp(color.R * (1 - amount), 0, 255),
        (byte)Math.Clamp(color.G * (1 - amount), 0, 255),
        (byte)Math.Clamp(color.B * (1 - amount), 0, 255));

    /// <summary>Renders a day total as "2h 18m", dropping the hour part below an hour.</summary>
    private static string FormatFocusedTotal(double minutes)
    {
        var whole = (int)Math.Round(minutes);
        return whole >= 60
            ? string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.FocusedHoursMinutes,
                whole / 60,
                whole % 60)
            : string.Format(CultureInfo.CurrentCulture, UiStrings.FocusedMinutesOnly, whole);
    }

    private void LoadSelectedRuleRows()
    {
        SavedApplicationRules.Clear();
        SavedWebsiteRules.Clear();
        if (SelectedProfile is not null)
        {
            foreach (var rule in SelectedProfile.ApplicationRules)
            {
                SavedApplicationRules.Add(new SavedApplicationRuleViewModel(
                    rule,
                    RemoveApplicationRuleAsync,
                    () => CanEditProfileRules));
            }

            foreach (var rule in SelectedProfile.WebsiteRules)
            {
                SavedWebsiteRules.Add(new SavedWebsiteRuleViewModel(
                    rule,
                    RemoveWebsiteRuleAsync,
                    () => CanEditProfileRules));
            }
        }

        OnPropertyChanged(nameof(HasSavedApplicationRules));
        OnPropertyChanged(nameof(HasSavedWebsiteRules));
        OnPropertyChanged(nameof(VisibleApplicationRules));
        OnPropertyChanged(nameof(VisibleWebsiteRules));
        OnPropertyChanged(nameof(HasApplicationOverflow));
        OnPropertyChanged(nameof(HasWebsiteOverflow));
        OnPropertyChanged(nameof(ApplicationOverflowText));
        OnPropertyChanged(nameof(WebsiteOverflowText));
        OnPropertyChanged(nameof(WebsitesHeader));
        OnPropertyChanged(nameof(ApplicationsHeader));
        NotifyBlockedContextChanged();
    }

    /// <summary>
    /// The contextual column and the profile's one-line scope both project the saved
    /// rules, so they refresh together whenever that set changes.
    /// </summary>
    private void NotifyBlockedContextChanged()
    {
        OnPropertyChanged(nameof(BlockingWebsiteRules));
        OnPropertyChanged(nameof(BlockingApplicationRules));
        OnPropertyChanged(nameof(BlockedItems));
        OnPropertyChanged(nameof(HasBlockedItems));
        OnPropertyChanged(nameof(HasBlockedItemOverflow));
        OnPropertyChanged(nameof(BlockedItemOverflowText));
        OnPropertyChanged(nameof(ProfileScopeText));
    }

    private void RefreshRuleCommands()
    {
        foreach (var row in SavedApplicationRules)
        {
            row.RefreshCanRemove();
        }

        foreach (var row in SavedWebsiteRules)
        {
            row.RefreshCanRemove();
        }
    }

    private bool SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        NotifyWorkflowConfiguration(propertyName);
    }

    private sealed class AsyncDelegateCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public async void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                await execute();
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class AsyncRuleCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute();

    public async void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            await execute();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
