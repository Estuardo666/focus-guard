using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace FocusGuard.Service;

public sealed record RebootExpirationDrillState(
    int SchemaVersion,
    Guid DrillId,
    DateTimeOffset ArmedUtc,
    DateTimeOffset ExpiresUtc,
    string BootIdBeforeRestart);

public sealed record RebootExpirationDrillResult(
    int SchemaVersion,
    Guid DrillId,
    string Outcome,
    DateTimeOffset ReleasedUtc,
    bool RestartObserved,
    string BootIdBeforeRestart,
    string BootIdAfterRestart,
    string Reason);

public sealed class RebootExpirationDrillWorker : BackgroundService
{
    public const int CurrentSchemaVersion = 1;
    public const string ReleasedOutcome = "Released";
    public const string DefaultDirectory =
        @"C:\ProgramData\FocusGuard\Development\RebootExpirationDrill";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private readonly string _directory;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string> _bootIdProvider;

    public RebootExpirationDrillWorker(
        TimeProvider? timeProvider = null,
        string? directory = null,
        Func<string>? bootIdProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _directory = directory ?? DefaultDirectory;
        _bootIdProvider = bootIdProvider ?? GetCurrentBootId;
    }

    public string StatePath => Path.Combine(_directory, "state.json");

    public string ResultPath => Path.Combine(_directory, "result.json");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!File.Exists(StatePath))
        {
            return;
        }

        RebootExpirationDrillState? state;
        try
        {
            await using var stream = File.OpenRead(StatePath);
            state = await JsonSerializer.DeserializeAsync<RebootExpirationDrillState>(
                stream,
                JsonOptions,
                stoppingToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            await WriteReleasedResultAsync(
                new RebootExpirationDrillState(
                    CurrentSchemaVersion,
                    Guid.Empty,
                    _timeProvider.GetUtcNow(),
                    _timeProvider.GetUtcNow(),
                    "unknown"),
                false,
                "InvalidStateFailOpen",
                stoppingToken);
            return;
        }

        if (!IsValid(state))
        {
            await WriteReleasedResultAsync(
                state ?? new RebootExpirationDrillState(
                    CurrentSchemaVersion,
                    Guid.Empty,
                    _timeProvider.GetUtcNow(),
                    _timeProvider.GetUtcNow(),
                    "unknown"),
                false,
                "InvalidStateFailOpen",
                stoppingToken);
            return;
        }

        var validState = state!;
        while (_timeProvider.GetUtcNow() < validState.ExpiresUtc)
        {
            await Task.Delay(PollInterval, _timeProvider, stoppingToken);
        }

        var bootIdAfterRestart = _bootIdProvider();
        await WriteReleasedResultAsync(
            validState,
            !string.Equals(
                validState.BootIdBeforeRestart,
                bootIdAfterRestart,
                StringComparison.Ordinal),
            "Expired",
            stoppingToken,
            bootIdAfterRestart);
    }

    private static bool IsValid(RebootExpirationDrillState? state) =>
        state is not null
        && state.SchemaVersion == CurrentSchemaVersion
        && state.DrillId != Guid.Empty
        && state.ArmedUtc.Offset == TimeSpan.Zero
        && state.ExpiresUtc.Offset == TimeSpan.Zero
        && state.ExpiresUtc > state.ArmedUtc
        && state.ExpiresUtc - state.ArmedUtc <= TimeSpan.FromHours(1)
        && !string.IsNullOrWhiteSpace(state.BootIdBeforeRestart);

    private async Task WriteReleasedResultAsync(
        RebootExpirationDrillState state,
        bool restartObserved,
        string reason,
        CancellationToken cancellationToken,
        string? bootIdAfterRestart = null)
    {
        Directory.CreateDirectory(_directory);
        var result = new RebootExpirationDrillResult(
            CurrentSchemaVersion,
            state.DrillId,
            ReleasedOutcome,
            _timeProvider.GetUtcNow().ToUniversalTime(),
            restartObserved,
            state.BootIdBeforeRestart,
            bootIdAfterRestart ?? _bootIdProvider(),
            reason);
        var temporaryPath = ResultPath + ".tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(result, JsonOptions),
            cancellationToken);
        File.Move(temporaryPath, ResultPath, true);
    }

    public static string GetCurrentBootId()
    {
        var bootUtc = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
        var roundedTicks = bootUtc.UtcTicks
            - (bootUtc.UtcTicks % TimeSpan.TicksPerMinute);
        return new DateTimeOffset(roundedTicks, TimeSpan.Zero).ToString("O");
    }
}
