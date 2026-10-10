using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FocusGuard.Domain;
using Microsoft.Data.Sqlite;

namespace FocusGuard.Storage;

public enum ProfileMutationStatus { Saved, Conflict, ProfileMissing, NameConflict, LastProfile, InvalidInput, RuleMissing }
public sealed record ProfileMutationResult(ProfileMutationStatus Status, StoredProfile? Profile = null);

public interface IRevisionedProfileStore : IProfileStore
{
    Task<ProfileMutationResult> TrySaveProfileAsync(StoredProfile profile, string? expectedRevision, CancellationToken token = default);
    Task<ProfileMutationResult> TryDeleteProfileAsync(ProfileId id, string expectedRevision, CancellationToken token = default);
    Task<ProfileMutationResult> TryDuplicateProfileAsync(ProfileId id, string expectedRevision, string copyNamePrefix, CancellationToken token = default);
    Task<ProfileMutationResult> TryDuplicateProfileAsync(ProfileId id, string expectedRevision,
        string copyNameFormat, string numberSuffixFormat, CancellationToken token = default) =>
        throw new NotSupportedException("Formatted atomic profile copy is unavailable.");
}

public enum ProfileCycleSettingsMutationStatus { Saved, Conflict, ProfileMissing, PresetMissing, InvalidInput, NotSupported }
public sealed record ProfileCycleSettingsSnapshot(ProfileCycleSettings Settings, string Revision);
public sealed record ProfileCycleSettingsMutationResult(ProfileCycleSettingsMutationStatus Status,
    ProfileCycleSettingsSnapshot? Snapshot = null);

public interface IRevisionedProfileCycleSettingsStore : IProfileStore
{
    Task<ProfileCycleSettingsSnapshot?> LoadRevisionedProfileCycleSettingsAsync(ProfileId id, CancellationToken token = default);
    Task<ProfileCycleSettingsMutationResult> TrySaveProfileCycleSettingsAsync(
        ProfileCycleSettings settings, string expectedRevision, CancellationToken token = default);
}

/// <summary>A conflict token, never an authorization credential or a policy signature.</summary>
public static class ProfileRevision
{
    public static string Compute(StoredProfile profile) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Canonical(profile))));

    public static StoredProfile Canonical(StoredProfile profile) => profile with
    {
        Name = profile.Name.Trim(),
        Description = string.IsNullOrWhiteSpace(profile.Description) ? null : profile.Description.Trim(),
        AccentHex = Profile.IsAccentHex(profile.AccentHex) ? profile.AccentHex!.ToUpperInvariant() : null,
        ApplicationRules = profile.ApplicationRules.OrderBy(rule => rule.Id).ToArray(),
        WebsiteRules = profile.WebsiteRules.OrderBy(rule => rule.Id).ToArray()
    };

    public static bool IsValid(string? revision) => revision is { Length: 64 }
        && revision.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');
}

public sealed partial class SqliteProfileStore
{
    public async Task<ProfileMutationResult> TrySaveProfileAsync(
        StoredProfile profile, string? expectedRevision, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        token.ThrowIfCancellationRequested();
        ValidateProfile(profile);
        if (expectedRevision is not null && !ProfileRevision.IsValid(expectedRevision)) return new(ProfileMutationStatus.InvalidInput);
        await using var connection = await OpenAsync(token);
        await EnsureSchemaAsync(connection, token);
        using var transaction = connection.BeginTransaction();
        var current = await ReadProfileAsync(connection, transaction, profile.Id, token);
        if (expectedRevision is null && current is not null) return new(ProfileMutationStatus.Conflict);
        if (expectedRevision is not null && current is null) return new(ProfileMutationStatus.ProfileMissing);
        if (current is not null && ProfileRevision.Compute(current) != expectedRevision) return new(ProfileMutationStatus.Conflict);
        var canonical = ProfileRevision.Canonical(profile);
        var names = await ReadProfileNamesAsync(connection, transaction, token);
        if ((current is null || !string.Equals(current.Name, canonical.Name, StringComparison.Ordinal))
            && names.Any(pair => pair.Id != profile.Id.Value && string.Equals(pair.Name, canonical.Name, StringComparison.CurrentCultureIgnoreCase)))
            return new(ProfileMutationStatus.NameConflict);
        await WriteProfileAsync(connection, transaction, canonical, token);
        token.ThrowIfCancellationRequested();
        transaction.Commit();
        return new(ProfileMutationStatus.Saved, canonical);
    }

    public async Task<ProfileMutationResult> TryDeleteProfileAsync(
        ProfileId id, string expectedRevision, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        token.ThrowIfCancellationRequested();
        ValidateId(id.Value, nameof(id));
        if (!ProfileRevision.IsValid(expectedRevision)) return new(ProfileMutationStatus.InvalidInput);
        await using var connection = await OpenAsync(token);
        await EnsureSchemaAsync(connection, token);
        using var transaction = connection.BeginTransaction();
        var current = await ReadProfileAsync(connection, transaction, id, token);
        if (current is null) return new(ProfileMutationStatus.ProfileMissing);
        if (ProfileRevision.Compute(current) != expectedRevision) return new(ProfileMutationStatus.Conflict);
        if ((await ReadProfileNamesAsync(connection, transaction, token)).Count <= 1) return new(ProfileMutationStatus.LastProfile);
        await ExecuteAsync(connection, transaction, "DELETE FROM Profiles WHERE Id = $id;", token, ("$id", id.Value.ToString("D")));
        token.ThrowIfCancellationRequested();
        transaction.Commit();
        return new(ProfileMutationStatus.Saved);
    }

    public Task<ProfileMutationResult> TryDuplicateProfileAsync(
        ProfileId id, string expectedRevision, string copyNamePrefix, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(copyNamePrefix) || copyNamePrefix.Length > 24)
            return Task.FromResult(new ProfileMutationResult(ProfileMutationStatus.InvalidInput));
        return TryDuplicateProfileAsync(id, expectedRevision, copyNamePrefix.Trim() + " {0}", " ({0})", token);
    }

    public async Task<ProfileMutationResult> TryDuplicateProfileAsync(
        ProfileId id, string expectedRevision, string copyNameFormat, string numberSuffixFormat, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        token.ThrowIfCancellationRequested();
        ValidateId(id.Value, nameof(id));
        if (!ProfileRevision.IsValid(expectedRevision) || string.IsNullOrWhiteSpace(copyNameFormat) || copyNameFormat.Length > 120
            || string.IsNullOrEmpty(numberSuffixFormat) || numberSuffixFormat.Length > 24
            || !copyNameFormat.Contains("{0}", StringComparison.Ordinal) || !numberSuffixFormat.Contains("{0}", StringComparison.Ordinal))
            return new(ProfileMutationStatus.InvalidInput);
        await using var connection = await OpenAsync(token);
        await EnsureSchemaAsync(connection, token);
        using var transaction = connection.BeginTransaction();
        var current = await ReadProfileAsync(connection, transaction, id, token);
        if (current is null) return new(ProfileMutationStatus.ProfileMissing);
        if (ProfileRevision.Compute(current) != expectedRevision) return new(ProfileMutationStatus.Conflict);
        var names = (await ReadProfileNamesAsync(connection, transaction, token)).Select(pair => pair.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var baseName = string.Format(CultureInfo.CurrentCulture, copyNameFormat, current.Name).Trim();
        var name = baseName[..Math.Min(baseName.Length, 80)].TrimEnd();
        for (var number = 2; names.Contains(name); number++)
        {
            var suffix = string.Format(CultureInfo.CurrentCulture, numberSuffixFormat, number);
            if (suffix.Length >= 80) return new(ProfileMutationStatus.InvalidInput);
            name = baseName[..Math.Min(baseName.Length, 80 - suffix.Length)].TrimEnd() + suffix;
        }
        var duplicate = ProfileRevision.Canonical(current with
        {
            Id = ProfileId.New(),
            Name = name,
            ApplicationRules = current.ApplicationRules.Select(rule => rule with { Id = Guid.NewGuid() }).ToArray(),
            WebsiteRules = current.WebsiteRules.Select(rule => rule with { Id = Guid.NewGuid() }).ToArray()
        });
        await WriteProfileAsync(connection, transaction, duplicate, token);
        await ExecuteAsync(connection, transaction,
            "INSERT INTO ProfileCycleSettings (ProfileId, DefaultPresetId) SELECT $newId, DefaultPresetId FROM ProfileCycleSettings WHERE ProfileId = $id;",
            token, ("$newId", duplicate.Id.Value.ToString("D")), ("$id", id.Value.ToString("D")));
        await CopyBreakRulesAsync(connection, transaction, id, duplicate.Id, token);
        token.ThrowIfCancellationRequested();
        transaction.Commit();
        return new(ProfileMutationStatus.Saved, duplicate);
    }

    private static async Task<List<(Guid Id, string Name)>> ReadProfileNamesAsync(
        SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        var result = new List<(Guid, string)>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id, Name FROM Profiles;";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add((ParseGuid(reader.GetString(0), "profile ID"), reader.GetString(1)));
        return result;
    }

    private static async Task CopyBreakRulesAsync(SqliteConnection connection, SqliteTransaction transaction,
        ProfileId source, ProfileId target, CancellationToken token)
    {
        // Fixed tables/columns only. The caller never supplies SQL or a table name.
        var apps = new List<(int Kind, string Value)>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT MatchKind, Value FROM BreakApplicationRules WHERE ProfileId = $id;";
            command.Parameters.AddWithValue("$id", source.Value.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var kind = ParseEnum<ApplicationMatchKind>(reader.GetInt32(0), "break application match kind");
                var value = reader.GetString(1);
                if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Stored break application value is invalid.");
                apps.Add(((int)kind, value));
            }
        }
        foreach (var app in apps)
            await ExecuteAsync(connection, transaction,
                "INSERT INTO BreakApplicationRules (Id, ProfileId, MatchKind, Value) VALUES ($id, $profile, $kind, $value);", token,
                ("$id", Guid.NewGuid().ToString("D")), ("$profile", target.Value.ToString("D")), ("$kind", app.Kind), ("$value", app.Value));
        var sites = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT Pattern FROM BreakWebsiteRules WHERE ProfileId = $id;";
            command.Parameters.AddWithValue("$id", source.Value.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var pattern = reader.GetString(0);
                if (string.IsNullOrWhiteSpace(pattern) || pattern.Contains('?', StringComparison.Ordinal))
                    throw new InvalidDataException("Stored break website pattern is invalid.");
                sites.Add(pattern);
            }
        }
        foreach (var site in sites)
            await ExecuteAsync(connection, transaction,
                "INSERT INTO BreakWebsiteRules (Id, ProfileId, Pattern) VALUES ($id, $profile, $pattern);", token,
                ("$id", Guid.NewGuid().ToString("D")), ("$profile", target.Value.ToString("D")), ("$pattern", site));
    }
}
