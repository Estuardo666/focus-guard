using System.Security;
using System.Text.Json;

namespace FocusGuard.Sync;

/// <summary>Reconciles the encrypted outbox, then projects the current committed/draft allowlist locally.</summary>
public sealed class SettingsSyncDesktopTick(SettingsSyncWorker worker, SettingsOutboxVault vault,
    DesktopSettingsSyncStore preferences, LocalSettingsOverrides overrides)
{
    public async Task<SettingsSyncStatus> RunAsync(CancellationToken token = default)
    {
        var status = await worker.TickAsync(token);
        if (status == SettingsSyncStatus.Disabled) return status;

        try
        {
            await vault.ProjectCurrentAsync(async (state, cancellation) =>
            {
                var current = state ?? throw new InvalidOperationException("Settings outbox is not initialized.");
                await preferences.ApplyAsync(current, overrides, cancellation);
                return true;
            }, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (IsProjectionFailure(error))
        {
            throw new SettingsTransportException(SettingsTransportFailure.Storage);
        }

        return status;
    }

    private static bool IsProjectionFailure(Exception error) => error is IOException or JsonException or InvalidDataException or
        ArgumentException or InvalidOperationException or UnauthorizedAccessException or SecurityException;
}
