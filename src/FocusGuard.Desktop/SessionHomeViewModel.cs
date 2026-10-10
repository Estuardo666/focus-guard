using System.Globalization;
using System.Windows.Input;
using Avalonia.Media;
using FocusGuard.Desktop.Resources;
using FocusGuard.Storage;

namespace FocusGuard.Desktop;

public sealed class HomeProfileViewModel(StoredProfile profile, IBrush accent, Action open)
{
    public StoredProfile Profile => profile;
    public string Name => profile.Name;
    public IBrush Accent => accent;
    public string Scope => SessionHomeText.ProfileScope(profile.WebsiteRules.Count, profile.ApplicationRules.Count);
    public ICommand OpenCommand { get; } = new AsyncRuleCommand(() => { open(); return Task.CompletedTask; }, () => true);
}

public sealed partial class FocusViewModel
{
    private bool _sessionSetupOpen;
    private bool _homeSchedulesUnavailable;
    public bool IsSessionSetupOpen { get => _sessionSetupOpen; private set => SetField(ref _sessionSetupOpen, value); }
    public void OpenSessionSetup()
    {
        if (IsActive || IsBusy) return;
        ShowFocus();
        IsSessionSetupOpen = true;
    }
    public void CloseSessionSetup() => IsSessionSetupOpen = false;
    public IReadOnlyList<HomeProfileViewModel> HomeProfiles => Profiles.Select(p => new HomeProfileViewModel(p, WorkflowProfileBrush(p.Id.Value), () =>
    {
        if (IsActive || IsBusy) return;
        SelectProfile(p);
        OpenSessionSetup();
    })).ToArray();
    public IReadOnlyList<ScheduleRowViewModel> HomeUpcoming => Schedules.Where(s => s.Schedule.Enabled && s.Schedule.NextStartUtc is not null)
        .OrderBy(s => s.Schedule.NextStartUtc).Take(3).ToArray();
    public bool HasHomeUpcoming => HomeUpcoming.Count > 0;
    public string HomeUpcomingEmptyTitle => _homeSchedulesUnavailable ? UiStrings.ScheduleUnavailable : UiStrings.HomeNoUpcoming;
    public string HomeUpcomingEmptyHint => _homeSchedulesUnavailable ? "" : UiStrings.HomeNoUpcomingHint;
    private void SetHomeScheduleAvailability(bool available)
    {
        _homeSchedulesUnavailable = !available;
        OnPropertyChanged(nameof(HomeUpcomingEmptyTitle));
        OnPropertyChanged(nameof(HomeUpcomingEmptyHint));
    }
    public string HomeTodaySummary => IsTodayLoaded
        ? string.Format(CultureInfo.CurrentCulture, UiStrings.HomeTodaySummary, TodayFocusMinutes, TodayCompletedSessions)
        : UiStrings.TodayTitle;
    public string HomeCurrentTitle => IsActive ? ActiveProfileName : ProfileName;
    public string HomeProfileScope => SelectedProfile is { } profile
        ? SessionHomeText.ProfileScope(profile.WebsiteRules.Count, profile.ApplicationRules.Count) : "";
}

internal static class SessionHomeText
{
    internal static string ProfileScope(int sites, int apps) =>
        string.Format(CultureInfo.CurrentCulture, sites == 1 ? UiStrings.HomeSiteCount : UiStrings.HomeSitesCount, sites)
        + " · " + string.Format(CultureInfo.CurrentCulture, apps == 1 ? UiStrings.HomeAppCount : UiStrings.HomeAppsCount, apps);
}
