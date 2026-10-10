using System.Security.Cryptography;
using System.Text.Json;
using FocusGuard.Domain;
using Microsoft.Data.Sqlite;

namespace FocusGuard.Storage;

/// <summary>A conflict token for one profile's cycle exceptions, never an authorization credential.</summary>
public static class ProfileCycleSettingsRevision
{
    public static string Compute(ProfileCycleSettings settings) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Canonical(settings))));

    public static ProfileCycleSettings Canonical(ProfileCycleSettings settings) => settings with
    {
        ApplicationRules = settings.ApplicationRules.OrderBy(rule => rule.Id)
            .Select(rule => rule with { Value = rule.Value.Trim() }).ToArray(),
        WebsiteRules = settings.WebsiteRules.OrderBy(rule => rule.Id)
            .Select(rule => rule with { Pattern = rule.Pattern.Trim().ToLowerInvariant() }).ToArray()
    };
}

public sealed partial class SqliteProfileStore
{
    public async Task<ProfileCycleSettingsSnapshot?> LoadRevisionedProfileCycleSettingsAsync(
        ProfileId id, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        token.ThrowIfCancellationRequested();
        ValidateId(id.Value, nameof(id));
        await using var connection = await OpenAsync(token);
        await EnsureSchemaAsync(connection, token);
        using var transaction = connection.BeginTransaction();
        var settings = await ReadProfileCycleSettingsAsync(connection, transaction, id, token).ConfigureAwait(false);
        if (settings is null) return null;
        token.ThrowIfCancellationRequested();
        transaction.Commit();
        var canonical = ProfileCycleSettingsRevision.Canonical(settings);
        return new(canonical, ProfileCycleSettingsRevision.Compute(canonical));
    }

    public async Task<ProfileCycleSettingsMutationResult> TrySaveProfileCycleSettingsAsync(
        ProfileCycleSettings settings, string expectedRevision, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        token.ThrowIfCancellationRequested();
        if (!ProfileRevision.IsValid(expectedRevision))
            return new(ProfileCycleSettingsMutationStatus.InvalidInput);
        try { ValidateProfileCycleSettings(settings); }
        catch (ArgumentException) { return new(ProfileCycleSettingsMutationStatus.InvalidInput); }

        await using var connection = await OpenAsync(token);
        await EnsureSchemaAsync(connection, token);
        using var transaction = connection.BeginTransaction(deferred: false);
        var current = await ReadProfileCycleSettingsAsync(connection, transaction, settings.ProfileId, token).ConfigureAwait(false);
        if (current is null) return new(ProfileCycleSettingsMutationStatus.ProfileMissing);
        if (ProfileCycleSettingsRevision.Compute(current) != expectedRevision)
            return new(ProfileCycleSettingsMutationStatus.Conflict);
        if (settings.DefaultPresetId is { } presetId
            && !await ExistsAsync(connection, transaction, "CyclePresets", presetId, token).ConfigureAwait(false))
            return new(ProfileCycleSettingsMutationStatus.PresetMissing);

        var canonical = ProfileCycleSettingsRevision.Canonical(settings);
        await WriteProfileCycleSettingsAsync(connection, transaction, canonical, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        transaction.Commit();
        return new(ProfileCycleSettingsMutationStatus.Saved,
            new(canonical, ProfileCycleSettingsRevision.Compute(canonical)));
    }

    private async Task<ProfileCycleSettings?> ReadProfileCycleSettingsAsync(
        SqliteConnection connection, SqliteTransaction transaction, ProfileId id, CancellationToken token)
    {
        if (!await ExistsAsync(connection, transaction, "Profiles", id.Value, token).ConfigureAwait(false)) return null;
        Guid? defaultPresetId = null;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT DefaultPresetId FROM ProfileCycleSettings WHERE ProfileId = $profileId;";
            command.Parameters.AddWithValue("$profileId", id.Value.ToString("D"));
            var value = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
            if (value is string text && Guid.TryParse(text, out var parsed) && parsed != Guid.Empty) defaultPresetId = parsed;
        }

        var applications = new List<BreakApplicationRule>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT Id, MatchKind, Value FROM BreakApplicationRules WHERE ProfileId = $profileId ORDER BY Id;";
            command.Parameters.AddWithValue("$profileId", id.Value.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                applications.Add(new(ParseGuid(reader.GetString(0), "break application rule ID"), RuleAction.Block,
                    ParseEnum<ApplicationMatchKind>(reader.GetInt32(1), "break application match kind"), reader.GetString(2)));
        }

        var websites = new List<BreakWebsiteRule>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT Id, Pattern FROM BreakWebsiteRules WHERE ProfileId = $profileId ORDER BY Id;";
            command.Parameters.AddWithValue("$profileId", id.Value.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var pattern = reader.GetString(1);
                if (string.IsNullOrWhiteSpace(pattern) || pattern.Contains('?', StringComparison.Ordinal))
                    throw new InvalidDataException("Stored break website pattern is invalid.");
                websites.Add(new(ParseGuid(reader.GetString(0), "break website rule ID"), RuleAction.Block, pattern));
            }
        }
        return new(id, defaultPresetId, applications.AsReadOnly(), websites.AsReadOnly());
    }

    private static async Task WriteProfileCycleSettingsAsync(
        SqliteConnection connection, SqliteTransaction transaction, ProfileCycleSettings settings, CancellationToken token)
    {
        await ExecuteAsync(connection, transaction,
            "INSERT INTO ProfileCycleSettings (ProfileId, DefaultPresetId) VALUES ($profileId, $presetId) ON CONFLICT(ProfileId) DO UPDATE SET DefaultPresetId = excluded.DefaultPresetId;",
            token, ("$profileId", settings.ProfileId.Value.ToString("D")),
            ("$presetId", (object?)settings.DefaultPresetId?.ToString("D") ?? DBNull.Value));
        await ExecuteAsync(connection, transaction, "DELETE FROM BreakApplicationRules WHERE ProfileId = $profileId;", token,
            ("$profileId", settings.ProfileId.Value.ToString("D")));
        await ExecuteAsync(connection, transaction, "DELETE FROM BreakWebsiteRules WHERE ProfileId = $profileId;", token,
            ("$profileId", settings.ProfileId.Value.ToString("D")));
        foreach (var rule in settings.ApplicationRules)
            await ExecuteAsync(connection, transaction,
                "INSERT INTO BreakApplicationRules (Id, ProfileId, MatchKind, Value) VALUES ($id, $profileId, $matchKind, $value);", token,
                ("$id", rule.Id.ToString("D")), ("$profileId", settings.ProfileId.Value.ToString("D")),
                ("$matchKind", (int)rule.MatchKind), ("$value", rule.Value));
        foreach (var rule in settings.WebsiteRules)
            await ExecuteAsync(connection, transaction,
                "INSERT INTO BreakWebsiteRules (Id, ProfileId, Pattern) VALUES ($id, $profileId, $pattern);", token,
                ("$id", rule.Id.ToString("D")), ("$profileId", settings.ProfileId.Value.ToString("D")), ("$pattern", rule.Pattern));
    }
}
