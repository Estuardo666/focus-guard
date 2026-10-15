using FocusGuard.Sync;
using FocusGuard.Host.Windows.Hosting;
using Xunit;

public sealed class SettingsLocalOverridesDialogTests
{
    [Fact]
    public void SelectionUsesOnlyReviewedEffectiveValuesAndNullMeansFollowGlobal()
    {
        var state = new SettingsOutbox(new(4, new(45, "dark", "es")),
            Draft: new(60, "dark", "en"))
        {
            LocalOverrides = new(Theme: "light", DefaultDurationMinutes: 90)
        };

        var selected = SettingsLocalOverridesDialog.CreateSelection(state,
            keepTheme: true, keepLanguage: false, keepDuration: true);

        Assert.Equal("light", selected.Theme);
        Assert.Null(selected.UiLanguage);
        Assert.Equal(90, selected.DefaultDurationMinutes);
    }

    [Fact]
    public void ClearingAllChoicesReturnsNoOverrides()
    {
        var state = new SettingsOutbox(new(2, GlobalSettings.Default))
        {
            LocalOverrides = new(Theme: "light", UiLanguage: "en", DefaultDurationMinutes: 90)
        };

        Assert.Equal(new LocalSettingsOverrides(), SettingsLocalOverridesDialog.CreateSelection(state,
            keepTheme: false, keepLanguage: false, keepDuration: false));
    }
}
