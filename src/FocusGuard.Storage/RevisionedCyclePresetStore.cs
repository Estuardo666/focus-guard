using System.Security.Cryptography;
using System.Text.Json;
using FocusGuard.Domain;
using Microsoft.Data.Sqlite;

namespace FocusGuard.Storage;

public enum CyclePresetMutationStatus { Saved, Conflict, Missing, BuiltIn, NameConflict, Referenced, InvalidInput, NotSupported }
public sealed record CyclePresetMutationResult(CyclePresetMutationStatus Status, StoredCyclePreset? Preset = null);
public interface IRevisionedCyclePresetStore : IProfileStore
{
    Task<CyclePresetMutationResult> TrySaveCyclePresetAsync(StoredCyclePreset preset, string? expectedRevision, CancellationToken token = default);
    Task<CyclePresetMutationResult> TryDeleteCyclePresetAsync(Guid id, string expectedRevision, CancellationToken token = default);
    Task<CyclePresetMutationResult> TryDuplicateCyclePresetAsync(Guid id, string expectedRevision, string name, CancellationToken token = default);
}

/// <summary>Semantic conflict fingerprint; not a credential, signature, or monotonically increasing version.</summary>
public static class CyclePresetRevision
{
    public static string Compute(StoredCyclePreset preset) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
        preset.Id, Name = preset.Name.Trim(), preset.IsBuiltIn,
        Focus = preset.Configuration.FocusDuration.Ticks, Short = preset.Configuration.ShortBreakDuration.Ticks,
        Long = preset.Configuration.LongBreakDuration.Ticks, preset.Configuration.CyclesBeforeLongBreak,
        preset.Configuration.TerminationMode, preset.Configuration.CycleCount, Total = preset.Configuration.TotalDuration?.Ticks
    })));
}

public sealed partial class SqliteProfileStore
{
    public async Task<CyclePresetMutationResult> TrySaveCyclePresetAsync(StoredCyclePreset preset, string? expectedRevision, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(preset); token.ThrowIfCancellationRequested();
        if (!ValidConditionalPreset(preset) || expectedRevision is not null && !ProfileRevision.IsValid(expectedRevision)) return new(CyclePresetMutationStatus.InvalidInput);
        if (preset.IsBuiltIn) return new(CyclePresetMutationStatus.BuiltIn);
        await using var connection = await OpenAsync(token); await EnsureSchemaAsync(connection, token);
        using var transaction = connection.BeginTransaction(deferred: false);
        var current = await ReadPresetAsync(connection, transaction, preset.Id, token);
        if (current?.IsBuiltIn == true) return new(CyclePresetMutationStatus.BuiltIn);
        if (expectedRevision is null && current is not null) return new(CyclePresetMutationStatus.Conflict);
        if (expectedRevision is not null && current is null) return new(CyclePresetMutationStatus.Missing);
        if (current is not null && CyclePresetRevision.Compute(current) != expectedRevision) return new(CyclePresetMutationStatus.Conflict);
        var canonical = preset with { Name = preset.Name.Trim() };
        if (await PresetNameExistsAsync(connection, transaction, canonical.Name, canonical.Id, token)) return new(CyclePresetMutationStatus.NameConflict);
        await WritePresetAsync(connection, transaction, canonical, token); transaction.Commit();
        return new(CyclePresetMutationStatus.Saved, canonical);
    }

    public async Task<CyclePresetMutationResult> TryDeleteCyclePresetAsync(Guid id, string expectedRevision, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (id == Guid.Empty || !ProfileRevision.IsValid(expectedRevision)) return new(CyclePresetMutationStatus.InvalidInput);
        await using var connection = await OpenAsync(token); await EnsureSchemaAsync(connection, token);
        using var transaction = connection.BeginTransaction(deferred: false);
        var current = await ReadPresetAsync(connection, transaction, id, token);
        if (current is null) return new(CyclePresetMutationStatus.Missing);
        if (current.IsBuiltIn) return new(CyclePresetMutationStatus.BuiltIn);
        if (CyclePresetRevision.Compute(current) != expectedRevision) return new(CyclePresetMutationStatus.Conflict);
        await using var references = connection.CreateCommand(); references.Transaction = transaction;
        references.CommandText = "SELECT COUNT(*) FROM ProfileCycleSettings WHERE DefaultPresetId = $id;";
        references.Parameters.AddWithValue("$id", id.ToString("D"));
        if (Convert.ToInt64(await references.ExecuteScalarAsync(token)) > 0) return new(CyclePresetMutationStatus.Referenced);
        await ExecuteAsync(connection, transaction, "DELETE FROM CyclePresets WHERE Id = $id;", token, ("$id", id.ToString("D")));
        transaction.Commit(); return new(CyclePresetMutationStatus.Saved);
    }

    public async Task<CyclePresetMutationResult> TryDuplicateCyclePresetAsync(Guid id, string expectedRevision, string name, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (id == Guid.Empty || !ProfileRevision.IsValid(expectedRevision) || string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80) return new(CyclePresetMutationStatus.InvalidInput);
        await using var connection = await OpenAsync(token); await EnsureSchemaAsync(connection, token);
        using var transaction = connection.BeginTransaction(deferred: false);
        var current = await ReadPresetAsync(connection, transaction, id, token);
        if (current is null) return new(CyclePresetMutationStatus.Missing);
        if (CyclePresetRevision.Compute(current) != expectedRevision) return new(CyclePresetMutationStatus.Conflict);
        var copy = current with { Id = Guid.NewGuid(), Name = name.Trim(), IsBuiltIn = false };
        if (!ValidConditionalPreset(copy)) return new(CyclePresetMutationStatus.InvalidInput);
        if (await PresetNameExistsAsync(connection, transaction, copy.Name, copy.Id, token)) return new(CyclePresetMutationStatus.NameConflict);
        await WritePresetAsync(connection, transaction, copy, token); transaction.Commit();
        return new(CyclePresetMutationStatus.Saved, copy);
    }

    private static bool ValidConditionalPreset(StoredCyclePreset preset)
    {
        if (preset.Configuration is null) return false;
        try { ValidateCyclePreset(preset); } catch (ArgumentException) { return false; }
        var config = preset.Configuration;
        return new[] { config.FocusDuration, config.ShortBreakDuration, config.LongBreakDuration, config.TotalDuration ?? TimeSpan.Zero }
            .All(value => value.Ticks % TimeSpan.TicksPerSecond == 0)
            && FocusCyclePlanBuilder.Build(config, new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)).IsValid;
    }

    private static async Task<StoredCyclePreset?> ReadPresetAsync(SqliteConnection connection, SqliteTransaction transaction, Guid id, CancellationToken token)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT Name, FocusSeconds, ShortBreakSeconds, LongBreakSeconds, CyclesBeforeLongBreak, TerminationMode, CycleCount, TotalDurationSeconds, IsBuiltIn FROM CyclePresets WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D")); await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? ReadCyclePreset(reader, id) : null;
    }

    private static async Task<bool> PresetNameExistsAsync(SqliteConnection connection, SqliteTransaction transaction, string name, Guid except, CancellationToken token)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT Name FROM CyclePresets WHERE Id <> $id;";
        command.Parameters.AddWithValue("$id", except.ToString("D")); await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) if (string.Equals(reader.GetString(0), name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static Task WritePresetAsync(SqliteConnection connection, SqliteTransaction transaction, StoredCyclePreset preset, CancellationToken token) =>
        ExecuteAsync(connection, transaction,
            "INSERT INTO CyclePresets (Id, Name, FocusSeconds, ShortBreakSeconds, LongBreakSeconds, CyclesBeforeLongBreak, TerminationMode, CycleCount, TotalDurationSeconds, IsBuiltIn) VALUES ($id,$name,$focus,$short,$long,$frequency,$mode,$count,$total,0) ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name,FocusSeconds=excluded.FocusSeconds,ShortBreakSeconds=excluded.ShortBreakSeconds,LongBreakSeconds=excluded.LongBreakSeconds,CyclesBeforeLongBreak=excluded.CyclesBeforeLongBreak,TerminationMode=excluded.TerminationMode,CycleCount=excluded.CycleCount,TotalDurationSeconds=excluded.TotalDurationSeconds WHERE CyclePresets.IsBuiltIn=0;",
            token, ("$id", preset.Id.ToString("D")), ("$name", preset.Name), ("$focus", (long)preset.Configuration.FocusDuration.TotalSeconds),
            ("$short", (long)preset.Configuration.ShortBreakDuration.TotalSeconds), ("$long", (long)preset.Configuration.LongBreakDuration.TotalSeconds),
            ("$frequency", preset.Configuration.CyclesBeforeLongBreak), ("$mode", preset.Configuration.TerminationMode.ToString()),
            ("$count", (object?)preset.Configuration.CycleCount ?? DBNull.Value), ("$total", (object?)(long?)preset.Configuration.TotalDuration?.TotalSeconds ?? DBNull.Value));
}
