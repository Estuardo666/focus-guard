using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
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

    public ICommand RemoveCommand => _removeCommand;

    public void RefreshCanRemove() => _removeCommand.RaiseCanExecuteChanged();
}

public sealed class FocusViewModel : INotifyPropertyChanged
{
    private readonly IFocusControlClient _client;
    private readonly IProfileStore _profileStore;
    private readonly StrictConfirmationPhraseGenerator _strictPhraseGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly AsyncDelegateCommand _startFocusCommand;
    private readonly AsyncDelegateCommand _confirmStrictCommand;
    private readonly AsyncDelegateCommand _cancelStrictCommand;
    private readonly AsyncDelegateCommand _stopFocusCommand;
    private readonly AsyncDelegateCommand _addWebsiteRuleCommand;
    private readonly AsyncDelegateCommand _addApplicationRuleCommand;
    private readonly AsyncDelegateCommand _duplicateProfileCommand;
    private DurationPresetViewModel _selectedDurationPreset;
    private RecoveryCredential? _pendingRecoveryCredential;
    private bool _isStrictMode;
    private bool _isEnforcementEnabled;
    private bool _isStrictReviewVisible;
    private bool _acknowledgeRecoverySaved;
    private bool _acknowledgeUnsavedWork;
    private bool _isBusy;
    private bool _isActive;
    private bool _activeStrictMode;
    private string _typedConfirmation = string.Empty;
    private string _requiredConfirmationText = string.Empty;
    private string _recoveryCodeDisplay = string.Empty;
    private string _statusMessage = UiStrings.Get("ConnectingService");
    private string _serviceHealthLabel = UiStrings.Get("ServiceStatusPending");
    private string _remainingText = "00:00";
    private string _exactEndText = UiStrings.NoActiveEndTime;
    private StoredProfile? _selectedProfile;
    private bool _profilesLoaded;
    private bool _isActivityVisible;
    private NavigationSection _section = NavigationSection.Focus;
    private string _websiteRuleInput = string.Empty;
    private string _applicationRuleInput = string.Empty;
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
    private DesktopSettings _settings;
    private bool _showAllWebsiteRules;
    private bool _showAllApplicationRules;
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
        "#667DFF",
        "#63B7B0",
        "#D8A657",
        "#9073E8",
        "#D87878",
        "#76A9D6"
    ];

    private static SolidColorBrush DefaultAccentBrush { get; } = CreateFrozenBrush("#667DFF");

    private static SolidColorBrush DefaultMetricBrush { get; } = CreateFrozenBrush("#EDF2F5");

    private static SolidColorBrush SwatchIdleBrush { get; } = CreateFrozenBrush("#1D2631");

    public FocusViewModel(
        IFocusControlClient? client = null,
        IProfileStore? profileStore = null,
        StrictConfirmationPhraseGenerator? strictPhraseGenerator = null,
        TimeProvider? timeProvider = null,
        DesktopSettingsStore? settingsStore = null)
    {
        _client = client ?? new NamedPipeFocusControlClient();
        _settingsStore = settingsStore ?? new DesktopSettingsStore();
        _settings = _settingsStore.Load();
        _profileStore = profileStore ?? new SqliteProfileStore(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FocusGuard",
                "profiles.db"));
        _strictPhraseGenerator = strictPhraseGenerator ?? new StrictConfirmationPhraseGenerator();
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        _startFocusCommand = new AsyncDelegateCommand(StartFocusAsync, () => CanStart);
        _confirmStrictCommand = new AsyncDelegateCommand(ConfirmStrictAsync, () => CanConfirmStrict);
        _cancelStrictCommand = new AsyncDelegateCommand(CancelStrictReviewAsync, () => CanCancelStrict);
        _stopFocusCommand = new AsyncDelegateCommand(StopFocusAsync, () => CanStop);
        _addWebsiteRuleCommand = new AsyncDelegateCommand(AddWebsiteRuleAsync, () => SelectedProfile is not null);
        _addApplicationRuleCommand = new AsyncDelegateCommand(AddApplicationRuleAsync, () => SelectedProfile is not null);
        _duplicateProfileCommand = new AsyncDelegateCommand(
            DuplicateSelectedProfileAsync,
            () => CanEditProfileRules);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<StoredProfile> Profiles { get; } = [];

    public ObservableCollection<ActivityEventViewModel> ActivityEvents { get; } = [];

    public ObservableCollection<SavedApplicationRuleViewModel> SavedApplicationRules { get; } = [];

    public ObservableCollection<SavedWebsiteRuleViewModel> SavedWebsiteRules { get; } = [];

    public ObservableCollection<ActivityEventViewModel> RecentBlocks { get; } = [];

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
            OnPropertyChanged(nameof(IsSettingsVisible));
        }
    }

    public bool IsActivityVisible => _isActivityVisible;

    public bool IsFocusVisible => Section == NavigationSection.Focus;

    public bool IsProfilesVisible => Section == NavigationSection.Profiles;

    public bool IsScheduleVisible => Section == NavigationSection.Schedule;

    public bool IsSettingsVisible => Section == NavigationSection.Settings;

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
                OnPropertyChanged(nameof(WebsiteRuleCount));
                OnPropertyChanged(nameof(ApplicationRuleCount));
                OnPropertyChanged(nameof(SessionTitle));
                OnPropertyChanged(nameof(SessionMeta));
                OnPropertyChanged(nameof(HeaderSubtitle));
                ApplyProfileAccent();
                LoadSelectedRuleRows();
                OnPropertyChanged(nameof(CanStart));
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
                OnPropertyChanged(nameof(CanStart));
                NotifyCommands();
            }
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
                OnPropertyChanged(nameof(EnforcementWarning));
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

    public bool AcknowledgeRecoverySaved
    {
        get => _acknowledgeRecoverySaved;
        set
        {
            if (SetField(ref _acknowledgeRecoverySaved, value))
            {
                OnPropertyChanged(nameof(CanConfirmStrict));
                NotifyCommands();
            }
        }
    }

    public bool AcknowledgeUnsavedWork
    {
        get => _acknowledgeUnsavedWork;
        set
        {
            if (SetField(ref _acknowledgeUnsavedWork, value))
            {
                OnPropertyChanged(nameof(CanConfirmStrict));
                NotifyCommands();
            }
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
        _timeProvider.GetLocalNow().Add(SelectedDurationPreset.Duration).ToString("g");

    public string StrictEndTimeDescription =>
        string.Format(UiStrings.StrictReviewEndsExactly, StrictEndTimeText);

    public bool CanConfirmStrict =>
        IsStrictReviewVisible
        && AcknowledgeRecoverySaved
        && AcknowledgeUnsavedWork
        && string.Equals(TypedConfirmation.Trim(), RequiredConfirmationText, StringComparison.Ordinal)
        && !IsBusy;

    public bool CanCancelStrict => IsStrictReviewVisible && !IsBusy;

    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (SetField(ref _isActive, value))
            {
                OnPropertyChanged(nameof(IsInactive));
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(ModeLabel));
                OnPropertyChanged(nameof(CanEditProfileRules));
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

    public bool CanEditProfileRules => IsInactive && !IsBusy && SelectedProfile is not null;

    public string ModeLabel => _activeStrictMode ? UiStrings.StrictMode : UiStrings.NormalMode;

    public bool CanStop => IsActive && !_activeStrictMode && !IsBusy;

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
    {
        get
        {
            if (!IsActive)
            {
                return 360d;
            }

            return _sessionTotalSeconds > 0
                ? 360d * Math.Clamp(_remainingSeconds / (double)_sessionTotalSeconds, 0d, 1d)
                : 0d;
        }
    }

    public string RingCenterValue => IsActive
        ? RemainingText
        : ((int)SelectedDurationPreset.Duration.TotalMinutes).ToString(CultureInfo.CurrentCulture);

    public string RingCenterCaption => IsActive
        ? string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.RemainingOfTotal,
            (int)Math.Round(_sessionTotalSeconds / 60d))
        : UiStrings.MinutesLower;

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

    public string WebsiteOverflowText => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.MoreChips,
        Math.Max(0, SavedWebsiteRules.Count - VisibleChipLimit));

    public string ApplicationOverflowText => string.Format(
        CultureInfo.CurrentCulture,
        UiStrings.MoreChips,
        Math.Max(0, SavedApplicationRules.Count - VisibleChipLimit));

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
        if (IsNavigationLocked
            && section is NavigationSection.Profiles or NavigationSection.Schedule)
        {
            StatusMessage = UiStrings.RulesLocked;
            return;
        }

        Section = section;
    }

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
        !IsActive
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

    public ICommand ConfirmStrictCommand => _confirmStrictCommand;

    public ICommand CancelStrictCommand => _cancelStrictCommand;

    public ICommand StopFocusCommand => _stopFocusCommand;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_profilesLoaded)
            {
                await LoadProfilesAsync(cancellationToken);
            }

            var status = await _client.GetStatusAsync(cancellationToken);
            ApplyStatus(status);

            // Status is polled every second; blocked-attempt counters change far more
            // slowly, so the second pipe round trip runs at a fifth of that rate.
            if (IsActive && _activityPollTicks++ % 5 == 0)
            {
                await RefreshSessionMetricsAsync(cancellationToken);
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

            IsActive = false;
            ServiceHealthLabel = UiStrings.Get("ServiceUnavailable");
            StatusMessage = UiStrings.Get("ServiceUnreachable");
        }
    }

    public async Task ShowActivityAsync(CancellationToken cancellationToken = default)
    {
        var response = await _client.GetActivityAsync(200, cancellationToken);
        ActivityEvents.Clear();
        foreach (var item in response.Events)
        {
            ActivityEvents.Add(ToActivityViewModel(item));
        }

        Section = NavigationSection.Activity;
        OnPropertyChanged(nameof(IsActivityEmpty));
    }

    public void ShowFocus() => Section = NavigationSection.Focus;

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
        if (IsStrictMode)
        {
            var generated = RecoveryCodeService.Generate();
            _pendingRecoveryCredential = generated.Credential;
            RecoveryCodeDisplay = generated.DisplayCode;
            RequiredConfirmationText = _strictPhraseGenerator.Generate();
            IsStrictReviewVisible = true;
            StatusMessage = UiStrings.Get("SaveRecoveryNow");
            OnPropertyChanged(nameof(StrictEndTimeText));
            OnPropertyChanged(nameof(StrictEndTimeDescription));
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

        await ActivateAsync(_pendingRecoveryCredential, recoveryDelaySeconds: 15 * 60);
    }

    private Task CancelStrictReviewAsync()
    {
        CancelStrictReview();
        return Task.CompletedTask;
    }

    private async Task ActivateAsync(
        RecoveryCredential? credential,
        int recoveryDelaySeconds)
    {
        IsBusy = true;
        try
        {
            var response = await _client.ActivateAsync(
                new ActivateSessionRequest(
                    SelectedProfile!.Id.Value,
                    checked((int)SelectedDurationPreset.Duration.TotalSeconds),
                    IsStrictMode,
                    recoveryDelaySeconds,
                    credential,
                    SelectedProfile.ApplicationRules
                        .Select(rule => new ApplicationPolicyRule(
                            rule.Id,
                            rule.Action.ToString(),
                            rule.MatchKind.ToString(),
                            rule.Value))
                        .ToArray(),
                    SelectedProfile.WebsiteRules
                        .Select(rule => new WebsitePolicyRule(
                            rule.Id,
                            rule.Action.ToString(),
                            rule.Pattern))
                        .ToArray(),
                    EnforcementEnabled: IsEnforcementEnabled,
                    ProfileName: SelectedProfile.Name),
                CancellationToken.None);
            StatusMessage = response.Reason;
            if (response.Accepted)
            {
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
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopFocusAsync()
    {
        IsBusy = true;
        try
        {
            var response = await _client.StopAsync(CancellationToken.None);
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

    private void ApplyStatus(StatusResponse status)
    {
        DetectSessionClosure(status);
        ServiceHealthLabel = status.ServiceReady
            ? status.ExtensionReady
                ? UiStrings.Get("ServiceAndExtensionReady")
                : UiStrings.Get("ServiceReadyExtensionUnavailable")
            : UiStrings.Get("ServiceUnavailable");
        _sessionStart = status.StartUtc;
        _sessionEnd = status.EndUtc;
        _remainingSeconds = Math.Max(0, status.RemainingSeconds);
        _sessionTotalSeconds = status.StartUtc is { } start && status.EndUtc is { } end && end > start
            ? (long)(end - start).TotalSeconds
            : 0;
        IsActive = status.SessionState is "Activating" or "Active";
        _activeStrictMode = status.StrictMode;
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(CanStop));
        RemainingText = FormatRemaining(status.RemainingSeconds);
        ExactEndText = status.EndUtc?.ToLocalTime().ToString("g") ?? UiStrings.NoActiveEndTime;
        RaiseSessionPresentationChanged();
        StatusMessage = status.SessionState == "Activating"
            ? UiStrings.Get("StateActivating")
            : IsActive
            ? status.EnforcementConnected
                ? UiStrings.Get("StateConnected")
                : UiStrings.Get("StateDegraded")
            : status.SessionState is "Expired" or "Stopped" or "Recovered"
                ? status.SessionState == "Expired"
                    ? UiStrings.Get("StateExpired")
                    : string.Format(UiStrings.Get("StateReleased"), status.SessionState.ToLowerInvariant())
                : UiStrings.Get("NoActiveSession");
        NotifyCommands();
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
        _pendingRecoveryCredential = null;
        RecoveryCodeDisplay = string.Empty;
        RequiredConfirmationText = string.Empty;
        IsStrictReviewVisible = false;
        AcknowledgeRecoverySaved = false;
        AcknowledgeUnsavedWork = false;
        TypedConfirmation = string.Empty;
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
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(CanConfirmStrict));
                OnPropertyChanged(nameof(CanEditProfileRules));
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
        _addWebsiteRuleCommand.RaiseCanExecuteChanged();
        _addApplicationRuleCommand.RaiseCanExecuteChanged();
        _duplicateProfileCommand.RaiseCanExecuteChanged();
    }

    private async Task AddWebsiteRuleAsync()
    {
        if (SelectedProfile is null) return;
        var result = WebsiteRule.Create(RuleAction.Block, WebsiteRuleInput);
        if (!result.IsValid) { StatusMessage = result.Errors[0].Message; return; }
        await _profileStore.SaveProfileAsync(SelectedProfile with { WebsiteRules = [.. SelectedProfile.WebsiteRules, result.Value!] });
        WebsiteRuleInput = string.Empty;
        await LoadProfilesAsync(CancellationToken.None);
        StatusMessage = UiStrings.Get("WebsiteRuleSaved");
    }

    private async Task AddApplicationRuleAsync()
    {
        if (SelectedProfile is null) return;
        var result = ApplicationRule.Create(RuleAction.Block, ApplicationMatchKind.FileName, ApplicationRuleInput);
        if (!result.IsValid) { StatusMessage = result.Errors[0].Message; return; }
        await _profileStore.SaveProfileAsync(SelectedProfile with { ApplicationRules = [.. SelectedProfile.ApplicationRules, result.Value!] });
        ApplicationRuleInput = string.Empty;
        await LoadProfilesAsync(CancellationToken.None);
        StatusMessage = UiStrings.Get("ApplicationRuleSaved");
    }

    public async Task RemoveApplicationRuleAsync(Guid ruleId)
    {
        if (!CanEditProfileRules || SelectedProfile is null)
        {
            StatusMessage = UiStrings.RulesLocked;
            return;
        }

        var updatedRules = SelectedProfile.ApplicationRules.Where(rule => rule.Id != ruleId).ToArray();
        if (updatedRules.Length == SelectedProfile.ApplicationRules.Count)
        {
            return;
        }

        var profileId = SelectedProfile.Id;
        await _profileStore.SaveProfileAsync(SelectedProfile with { ApplicationRules = updatedRules });
        await LoadProfilesAsync(CancellationToken.None, profileId);
        StatusMessage = UiStrings.ApplicationRuleRemoved;
    }

    public async Task RemoveWebsiteRuleAsync(Guid ruleId)
    {
        if (!CanEditProfileRules || SelectedProfile is null)
        {
            StatusMessage = UiStrings.RulesLocked;
            return;
        }

        var updatedRules = SelectedProfile.WebsiteRules.Where(rule => rule.Id != ruleId).ToArray();
        if (updatedRules.Length == SelectedProfile.WebsiteRules.Count)
        {
            return;
        }

        var profileId = SelectedProfile.Id;
        await _profileStore.SaveProfileAsync(SelectedProfile with { WebsiteRules = updatedRules });
        await LoadProfilesAsync(CancellationToken.None, profileId);
        StatusMessage = UiStrings.WebsiteRuleRemoved;
    }

    public async Task DuplicateSelectedProfileAsync()
    {
        if (!CanEditProfileRules || SelectedProfile is null)
        {
            StatusMessage = UiStrings.RulesLocked;
            return;
        }

        var source = SelectedProfile;
        var duplicateId = ProfileId.New();
        var duplicate = new StoredProfile(
            duplicateId,
            CreateDuplicateProfileName(source.Name),
            source.ApplicationRules
                .Select(rule => rule with { Id = Guid.NewGuid() })
                .ToArray(),
            source.WebsiteRules
                .Select(rule => rule with { Id = Guid.NewGuid() })
                .ToArray(),
            source.Description,
            source.AccentHex);

        IsBusy = true;
        try
        {
            await _profileStore.SaveProfileAsync(duplicate);
            await LoadProfilesAsync(CancellationToken.None, duplicateId);
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.ProfileDuplicated,
                duplicate.Name);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string CreateDuplicateProfileName(string sourceName)
    {
        const int maximumLength = 80;
        var baseName = string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.ProfileCopyName,
            sourceName);
        var existingNames = Profiles
            .Select(profile => profile.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        var candidate = baseName[..Math.Min(baseName.Length, maximumLength)].TrimEnd();
        if (!existingNames.Contains(candidate))
        {
            return candidate;
        }

        for (var copyNumber = 2; ; copyNumber++)
        {
            var suffix = string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.ProfileCopyNumberSuffix,
                copyNumber);
            var prefixLength = Math.Max(1, maximumLength - suffix.Length);
            candidate = $"{baseName[..Math.Min(baseName.Length, prefixLength)].TrimEnd()}{suffix}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private async Task LoadProfilesAsync(
        CancellationToken cancellationToken,
        ProfileId? preferredProfileId = null)
    {
        preferredProfileId ??= SelectedProfile?.Id;
        var profiles = await _profileStore.ListProfilesAsync(cancellationToken);
        if (profiles.Count == 0)
        {
            var created = Profile.Create(UiStrings.Get("DefaultProfileName")).Value!;
            await _profileStore.SaveProfileAsync(
                new StoredProfile(created.Id, created.Name, [], []),
                cancellationToken);
            profiles = await _profileStore.ListProfilesAsync(cancellationToken);
        }

        Profiles.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
        }

        SelectedProfile = Profiles.FirstOrDefault(profile => profile.Id == preferredProfileId) ?? Profiles.First();
        RebuildProfileSwatches();
        _profilesLoaded = true;
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
    /// Switching profile recolours the header bar, sidebar selection and sparkline
    /// from a single resource assignment. The timer keeps the stable Zen gradient.
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
    }

    private static IBrush AccentBrushFor(StoredProfile? profile)
    {
        if (profile is null)
        {
            return DefaultAccentBrush;
        }

        if (Profile.IsAccentHex(profile.AccentHex))
        {
            return CreateFrozenBrush(profile.AccentHex!);
        }

        var slot = (int)((uint)profile.Id.Value.GetHashCode() % (uint)AccentPalette.Length);
        return CreateFrozenBrush(AccentPalette[slot]);
    }

    private static SolidColorBrush CreateFrozenBrush(string hex) =>
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

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

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
