using FocusGuard.Client.Preferences;

namespace FocusGuard.Sync;

/// <summary>Explicit bridge to desktop preferences; never copies policy or touches an active session.</summary>
public static class DesktopSettingsProjection
{
    /// <summary>Preserve a pending local draft (including edits after send); never display stale ACK values over it.</summary>
    public static DesktopSettings ApplyOutbox(DesktopSettings current, SettingsOutbox verifiedState, LocalSettingsOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(verifiedState); verifiedState.Validate();
        ArgumentNullException.ThrowIfNull(overrides);
        return Apply(current, verifiedState.Draft ?? verifiedState.Confirmed.EffectiveValues,
            verifiedState.LocalOverrides.WithExplicit(overrides));
    }

    public static GlobalSettings Extract(DesktopSettings local)
    {
        ArgumentNullException.ThrowIfNull(local);
        if (!Enum.IsDefined(local.Theme)) throw new ArgumentException("Invalid local theme.");
        var shared = new GlobalSettings(local.LastDurationMinutes, local.Theme == AppTheme.Light ? "light" : "dark", local.UiLanguage);
        shared.Validate(); return shared;
    }

    public static DesktopSettings Apply(DesktopSettings current, GlobalSettings verifiedGlobal, LocalSettingsOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(current); ArgumentNullException.ThrowIfNull(overrides);
        var effective = overrides.Apply(verifiedGlobal);
        return current with
        {
            Theme = effective.Theme == "light" ? AppTheme.Light : AppTheme.Dark,
            UiLanguage = effective.UiLanguage,
            LastDurationMinutes = effective.DefaultDurationMinutes
        };
    }
}
