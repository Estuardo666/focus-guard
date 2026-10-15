using System.Text.Json;
using System.Security.Cryptography;
using FocusGuard.Client.Preferences;
using FocusGuard.Contracts;
using FocusGuard.Domain;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed record PreferenceCommand(bool IsRead, string? UiLanguage = null, string? Theme = null,
    bool ReduceMotion = false, string? SessionEnd = null, bool CyclePhaseNotifications = false,
    bool EnforcementEnabled = false, int LastDurationMinutes = 50, string? LastSessionMode = null,
    Guid? LastCyclePresetId = null);
internal sealed record BridgePreferences(string UiLanguage, string Theme, bool ReduceMotion, string SessionEnd,
    bool CyclePhaseNotifications, bool EnforcementEnabled, int LastDurationMinutes, string LastSessionMode,
    string? LastCyclePresetId);

internal sealed partial class BridgeRequestParser
{
    private static bool TryParsePreferenceCommand(string? method, JsonElement parameters, out PreferenceCommand? command)
    {
        command = null;
        if (method == "preferences.get" && Exact(parameters)) { command = new(true); return true; }
        if (method != "preferences.set" || !Exact(parameters, "uiLanguage", "theme", "reduceMotion", "sessionEnd",
            "cyclePhaseNotifications", "enforcementEnabled", "lastDurationMinutes", "lastSessionMode", "lastCyclePresetId")) return false;
        if (!Text(parameters, "uiLanguage", 2, out var language) || language is not ("es" or "en")
            || !Text(parameters, "theme", 8, out var theme) || theme is not ("Dark" or "Light")
            || parameters.GetProperty("reduceMotion").ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || !Text(parameters, "sessionEnd", 16, out var sessionEnd) || sessionEnd is not ("Window" or "Notification" or "DimmedWindow" or "None")
            || parameters.GetProperty("cyclePhaseNotifications").ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || parameters.GetProperty("enforcementEnabled").ValueKind is not JsonValueKind.True and not JsonValueKind.False
            || !parameters.GetProperty("lastDurationMinutes").TryGetInt32(out var duration) || duration is < 1 or > 720
            || !Text(parameters, "lastSessionMode", 16, out var mode) || mode is not ("SimpleTimer" or "FocusCycle")) return false;
        Guid? preset = null; var presetValue = parameters.GetProperty("lastCyclePresetId");
        if (presetValue.ValueKind != JsonValueKind.Null)
        {
            if (presetValue.ValueKind != JsonValueKind.String || !Guid.TryParseExact(presetValue.GetString(), "N", out var parsed) || parsed == Guid.Empty) return false;
            preset = parsed;
        }
        command = new(false, language, theme, parameters.GetProperty("reduceMotion").GetBoolean(), sessionEnd,
            parameters.GetProperty("cyclePhaseNotifications").GetBoolean(), parameters.GetProperty("enforcementEnabled").GetBoolean(),
            duration, mode, preset);
        return true;
    }
}

internal sealed partial class NativeBridgeDispatcher
{
    private async ValueTask<(string? Error, object? Result)> HandlePreferenceCommandAsync(BridgeRequest request, CancellationToken token)
    {
        if (settingsStore is null) return ("PreferencesUnavailable", null);
        token.ThrowIfCancellationRequested();
        if (!documentLifetime.TryGetCurrent(request.DocumentToken, out _)) return ("StaleDocument", null);
        var current = settingsStore.Load();
        if (request.Preferences!.IsRead) return (null, ProjectPreferences(current));
        var update = request.Preferences;
        var saved = current with
        {
            UiLanguage = update.UiLanguage!,
            Theme = Enum.Parse<AppTheme>(update.Theme!, false),
            ReduceMotion = update.ReduceMotion,
            SessionEnd = Enum.Parse<SessionEndPresentation>(update.SessionEnd!, false),
            CyclePhaseNotifications = update.CyclePhaseNotifications,
            EnforcementEnabled = update.EnforcementEnabled,
            LastDurationMinutes = update.LastDurationMinutes,
            LastSessionMode = Enum.Parse<SessionMode>(update.LastSessionMode!, false),
            LastCyclePresetId = update.LastCyclePresetId
        };
        var synchronized = false;
        try
        {
            synchronized = accountService is not null &&
                await accountService.TrySyncSharedSettingsEditAsync(current, saved, token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException
            or CryptographicException or UnauthorizedAccessException or System.Security.SecurityException
            or HttpRequestException or TimeoutException or ArgumentException)
        {
            // Local preferences remain usable if the optional cloud journal needs attention.
            synchronized = false;
        }
        try
        {
            if (synchronized) settingsStore.SaveVerified(saved);
            else settingsStore.Save(saved);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException
            or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return ("PreferencesSaveFailed", null);
        }
        token.ThrowIfCancellationRequested();
        if (!documentLifetime.TryGetCurrent(request.DocumentToken, out _)) return ("StaleDocument", null);
        var observed = settingsStore.Load();
        if (observed.UiLanguage != saved.UiLanguage || observed.Theme != saved.Theme || observed.ReduceMotion != saved.ReduceMotion
            || observed.SessionEnd != saved.SessionEnd || observed.CyclePhaseNotifications != saved.CyclePhaseNotifications
            || observed.EnforcementEnabled != saved.EnforcementEnabled || observed.LastDurationMinutes != saved.LastDurationMinutes
            || observed.LastSessionMode != saved.LastSessionMode || observed.LastCyclePresetId != saved.LastCyclePresetId)
            return ("PreferencesSaveFailed", null);
        return (null, ProjectPreferences(observed));
    }

    private static BridgePreferences ProjectPreferences(DesktopSettings settings) => new(settings.UiLanguage,
        settings.Theme.ToString(), settings.ReduceMotion, settings.SessionEnd.ToString(), settings.CyclePhaseNotifications,
        settings.EnforcementEnabled, settings.LastDurationMinutes, settings.LastSessionMode.ToString(),
        settings.LastCyclePresetId?.ToString("N"));
}
