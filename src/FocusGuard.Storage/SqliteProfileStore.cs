using System.Globalization;
using FocusGuard.Domain;
using Microsoft.Data.Sqlite;

namespace FocusGuard.Storage;

public sealed record StoredProfile(
    ProfileId Id,
    string Name,
    IReadOnlyList<ApplicationRule> ApplicationRules,
    IReadOnlyList<WebsiteRule> WebsiteRules,
    string? Description = null,
    string? AccentHex = null);

public interface IProfileStore
{
    Task SaveProfileAsync(StoredProfile profile, CancellationToken cancellationToken = default);

    Task<StoredProfile?> LoadProfileAsync(ProfileId profileId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredProfile>> ListProfilesAsync(CancellationToken cancellationToken = default);

    Task<bool> DeleteProfileAsync(ProfileId profileId, CancellationToken cancellationToken = default);

    Task SaveCyclePresetAsync(StoredCyclePreset preset, CancellationToken cancellationToken = default);

    Task<StoredCyclePreset?> LoadCyclePresetAsync(Guid presetId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredCyclePreset>> ListCyclePresetsAsync(CancellationToken cancellationToken = default);

    Task<bool> DeleteCyclePresetAsync(Guid presetId, CancellationToken cancellationToken = default);

    Task SaveProfileCycleSettingsAsync(
        ProfileCycleSettings settings,
        CancellationToken cancellationToken = default);

    Task<ProfileCycleSettings?> LoadProfileCycleSettingsAsync(
        ProfileId profileId,
        CancellationToken cancellationToken = default);

    async Task<Guid?> LoadDefaultCyclePresetIdAsync(ProfileId profileId, CancellationToken cancellationToken = default) =>
        (await LoadProfileCycleSettingsAsync(profileId, cancellationToken).ConfigureAwait(false))?.DefaultPresetId;
}

public sealed partial class SqliteProfileStore : IRevisionedProfileStore, IRevisionedCyclePresetStore, IRevisionedProfileCycleSettingsStore
{
    private const int CurrentSchemaVersion = 3;

    private readonly string _connectionString;
    private readonly TimeProvider _timeProvider;

    public SqliteProfileStore(string databasePath, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
    }

    public async Task SaveProfileAsync(StoredProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateProfile(profile);

        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        using var transaction = connection.BeginTransaction();

        await WriteProfileAsync(connection, transaction, profile, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    private async Task WriteProfileAsync(SqliteConnection connection, SqliteTransaction transaction,
        StoredProfile profile, CancellationToken cancellationToken)
    {

        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO Profiles (Id, Name, UpdatedUtc, Description, AccentHex)
            VALUES ($id, $name, $updatedUtc, $description, $accentHex)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                UpdatedUtc = excluded.UpdatedUtc,
                Description = excluded.Description,
                AccentHex = excluded.AccentHex;
            """,
            cancellationToken,
            ("$id", profile.Id.Value.ToString("D")),
            ("$name", profile.Name.Trim()),
            ("$updatedUtc", _timeProvider.GetUtcNow().ToUniversalTime().ToString("O")),
            ("$description", NormalizeText(profile.Description) ?? (object)DBNull.Value),
            ("$accentHex", NormalizeAccent(profile.AccentHex) ?? (object)DBNull.Value));

        await ExecuteAsync(
            connection,
            transaction,
            "DELETE FROM ApplicationRules WHERE ProfileId = $profileId;",
            cancellationToken,
            ("$profileId", profile.Id.Value.ToString("D")));
        await ExecuteAsync(
            connection,
            transaction,
            "DELETE FROM WebsiteRules WHERE ProfileId = $profileId;",
            cancellationToken,
            ("$profileId", profile.Id.Value.ToString("D")));

        foreach (var rule in profile.ApplicationRules)
        {
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO ApplicationRules (Id, ProfileId, Action, MatchKind, Value) VALUES ($id, $profileId, $action, $matchKind, $value);",
                cancellationToken,
                ("$id", rule.Id.ToString("D")),
                ("$profileId", profile.Id.Value.ToString("D")),
                ("$action", (int)rule.Action),
                ("$matchKind", (int)rule.MatchKind),
                ("$value", rule.Value));
        }

        foreach (var rule in profile.WebsiteRules)
        {
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO WebsiteRules (Id, ProfileId, Action, Pattern) VALUES ($id, $profileId, $action, $pattern);",
                cancellationToken,
                ("$id", rule.Id.ToString("D")),
                ("$profileId", profile.Id.Value.ToString("D")),
                ("$action", (int)rule.Action),
                ("$pattern", rule.Pattern));
        }

    }

    public async Task<StoredProfile?> LoadProfileAsync(ProfileId profileId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        if (profileId.Value == Guid.Empty)
        {
            throw new ArgumentException("Profile ID must not be empty.", nameof(profileId));
        }

        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        using var transaction = connection.BeginTransaction(deferred: true);
        var profile = await ReadProfileAsync(connection, transaction, profileId, cancellationToken);
        transaction.Commit();
        return profile;
    }

    private async Task<StoredProfile?> ReadProfileAsync(SqliteConnection connection, SqliteTransaction transaction,
        ProfileId profileId, CancellationToken cancellationToken)
    {

        string? name = null;
        string? description = null;
        string? accentHex = null;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT Name, Description, AccentHex FROM Profiles WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", profileId.Value.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                name = reader.GetString(0);
                description = reader.IsDBNull(1) ? null : reader.GetString(1);
                accentHex = reader.IsDBNull(2) ? null : reader.GetString(2);
            }
        }

        if (name is null)
        {
            return null;
        }

        var applicationRules = await LoadApplicationRulesAsync(connection, profileId, cancellationToken, transaction);
        var websiteRules = await LoadWebsiteRulesAsync(connection, profileId, cancellationToken, transaction);
        return new StoredProfile(
            profileId,
            name,
            applicationRules,
            websiteRules,
            NormalizeText(description),
            // A row written before accents existed, or hand-edited, must not crash the UI.
            Profile.IsAccentHex(accentHex) ? accentHex : null);
    }

    public async Task<IReadOnlyList<StoredProfile>> ListProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        var profileIds = new List<ProfileId>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id FROM Profiles ORDER BY Name COLLATE NOCASE, Id;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                profileIds.Add(new ProfileId(ParseGuid(reader.GetString(0), "profile ID")));
            }
        }

        var profiles = new List<StoredProfile>(profileIds.Count);
        foreach (var profileId in profileIds)
        {
            var profile = await LoadProfileAsync(profileId, cancellationToken);
            if (profile is not null) profiles.Add(profile);
        }

        return profiles.AsReadOnly();
    }

    public async Task<bool> DeleteProfileAsync(
        ProfileId profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        if (profileId.Value == Guid.Empty)
        {
            throw new ArgumentException("Profile ID must not be empty.", nameof(profileId));
        }

        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Profiles WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", profileId.Value.ToString("D"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task SaveCyclePresetAsync(
        StoredCyclePreset preset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ValidateCyclePreset(preset);

        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO CyclePresets
                (Id, Name, FocusSeconds, ShortBreakSeconds, LongBreakSeconds,
                 CyclesBeforeLongBreak, TerminationMode, CycleCount, TotalDurationSeconds, IsBuiltIn)
            VALUES
                ($id, $name, $focusSeconds, $shortBreakSeconds, $longBreakSeconds,
                 $frequency, $terminationMode, $cycleCount, $totalDurationSeconds, $isBuiltIn)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                FocusSeconds = excluded.FocusSeconds,
                ShortBreakSeconds = excluded.ShortBreakSeconds,
                LongBreakSeconds = excluded.LongBreakSeconds,
                CyclesBeforeLongBreak = excluded.CyclesBeforeLongBreak,
                TerminationMode = excluded.TerminationMode,
                CycleCount = excluded.CycleCount,
                TotalDurationSeconds = excluded.TotalDurationSeconds
            WHERE CyclePresets.IsBuiltIn = 0;
            """;
        command.Parameters.AddWithValue("$id", preset.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", preset.Name.Trim());
        command.Parameters.AddWithValue("$focusSeconds", (long)preset.Configuration.FocusDuration.TotalSeconds);
        command.Parameters.AddWithValue("$shortBreakSeconds", (long)preset.Configuration.ShortBreakDuration.TotalSeconds);
        command.Parameters.AddWithValue("$longBreakSeconds", (long)preset.Configuration.LongBreakDuration.TotalSeconds);
        command.Parameters.AddWithValue("$frequency", preset.Configuration.CyclesBeforeLongBreak);
        command.Parameters.AddWithValue("$terminationMode", preset.Configuration.TerminationMode.ToString());
        command.Parameters.AddWithValue("$cycleCount", (object?)preset.Configuration.CycleCount ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$totalDurationSeconds",
            (object?)(preset.Configuration.TotalDuration is { } total ? (long)total.TotalSeconds : null) ?? DBNull.Value);
        command.Parameters.AddWithValue("$isBuiltIn", preset.IsBuiltIn ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredCyclePreset?> LoadCyclePresetAsync(
        Guid presetId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(presetId, nameof(presetId));
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Name, FocusSeconds, ShortBreakSeconds, LongBreakSeconds,
                   CyclesBeforeLongBreak, TerminationMode, CycleCount,
                   TotalDurationSeconds, IsBuiltIn
            FROM CyclePresets
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", presetId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadCyclePreset(reader, presetId)
            : null;
    }

    public async Task<IReadOnlyList<StoredCyclePreset>> ListCyclePresetsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, FocusSeconds, ShortBreakSeconds, LongBreakSeconds, CyclesBeforeLongBreak, TerminationMode, CycleCount, TotalDurationSeconds, IsBuiltIn FROM CyclePresets ORDER BY IsBuiltIn DESC, Name COLLATE NOCASE, Id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var presets = new List<StoredCyclePreset>();
        while (await reader.ReadAsync(cancellationToken))
        {
            presets.Add(ReadCyclePreset(reader, ParseGuid(reader.GetString(0), "cycle preset ID"), 1));
        }

        return presets.AsReadOnly();
    }

    public async Task<bool> DeleteCyclePresetAsync(
        Guid presetId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(presetId, nameof(presetId));
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CyclePresets WHERE Id = $id AND IsBuiltIn = 0;";
        command.Parameters.AddWithValue("$id", presetId.ToString("D"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task SaveProfileCycleSettingsAsync(
        ProfileCycleSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateProfileCycleSettings(settings);

        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var transaction = connection.BeginTransaction();
        if (!await ExistsAsync(connection, transaction, "Profiles", settings.ProfileId.Value, cancellationToken))
        {
            throw new InvalidOperationException("Profile must exist before saving cycle settings.");
        }

        if (settings.DefaultPresetId is { } presetId
            && !await ExistsAsync(connection, transaction, "CyclePresets", presetId, cancellationToken))
        {
            throw new InvalidOperationException("Default cycle preset does not exist.");
        }

        await ExecuteAsync(
            connection,
            transaction,
            "INSERT INTO ProfileCycleSettings (ProfileId, DefaultPresetId) VALUES ($profileId, $presetId) ON CONFLICT(ProfileId) DO UPDATE SET DefaultPresetId = excluded.DefaultPresetId;",
            cancellationToken,
            ("$profileId", settings.ProfileId.Value.ToString("D")),
            ("$presetId", (object?)settings.DefaultPresetId?.ToString("D") ?? DBNull.Value));
        await ExecuteAsync(
            connection,
            transaction,
            "DELETE FROM BreakApplicationRules WHERE ProfileId = $profileId;",
            cancellationToken,
            ("$profileId", settings.ProfileId.Value.ToString("D")));
        await ExecuteAsync(
            connection,
            transaction,
            "DELETE FROM BreakWebsiteRules WHERE ProfileId = $profileId;",
            cancellationToken,
            ("$profileId", settings.ProfileId.Value.ToString("D")));

        foreach (var rule in settings.ApplicationRules)
        {
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO BreakApplicationRules (Id, ProfileId, MatchKind, Value) VALUES ($id, $profileId, $matchKind, $value);",
                cancellationToken,
                ("$id", rule.Id.ToString("D")),
                ("$profileId", settings.ProfileId.Value.ToString("D")),
                ("$matchKind", (int)rule.MatchKind),
                ("$value", rule.Value.Trim()));
        }

        foreach (var rule in settings.WebsiteRules)
        {
            await ExecuteAsync(
                connection,
                transaction,
                "INSERT INTO BreakWebsiteRules (Id, ProfileId, Pattern) VALUES ($id, $profileId, $pattern);",
                cancellationToken,
                ("$id", rule.Id.ToString("D")),
                ("$profileId", settings.ProfileId.Value.ToString("D")),
                ("$pattern", rule.Pattern.Trim().ToLowerInvariant()));
        }

        transaction.Commit();
    }

    public async Task<ProfileCycleSettings?> LoadProfileCycleSettingsAsync(
        ProfileId profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ValidateId(profileId.Value, nameof(profileId));
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        if (!await ExistsAsync(connection, null, "Profiles", profileId.Value, cancellationToken))
        {
            return null;
        }

        Guid? defaultPresetId = null;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT DefaultPresetId FROM ProfileCycleSettings WHERE ProfileId = $profileId;";
            command.Parameters.AddWithValue("$profileId", profileId.Value.ToString("D"));
            var value = await command.ExecuteScalarAsync(cancellationToken);
            if (value is string text && Guid.TryParse(text, out var parsed) && parsed != Guid.Empty)
            {
                defaultPresetId = parsed;
            }
        }

        var applications = new List<BreakApplicationRule>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, MatchKind, Value FROM BreakApplicationRules WHERE ProfileId = $profileId ORDER BY Id;";
            command.Parameters.AddWithValue("$profileId", profileId.Value.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                // The action is not a stored column: every row in this table means the
                // same thing, so it is synthesized rather than trusted from disk.
                applications.Add(new BreakApplicationRule(
                    ParseGuid(reader.GetString(0), "break application rule ID"),
                    RuleAction.Block,
                    ParseEnum<ApplicationMatchKind>(reader.GetInt32(1), "break application match kind"),
                    reader.GetString(2)));
            }
        }

        var websites = new List<BreakWebsiteRule>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Pattern FROM BreakWebsiteRules WHERE ProfileId = $profileId ORDER BY Id;";
            command.Parameters.AddWithValue("$profileId", profileId.Value.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var pattern = reader.GetString(1);
                if (string.IsNullOrWhiteSpace(pattern) || pattern.Contains('?', StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Stored break website pattern is invalid.");
                }

                websites.Add(new BreakWebsiteRule(
                    ParseGuid(reader.GetString(0), "break website rule ID"),
                    RuleAction.Block,
                    pattern));
            }
        }

        return new ProfileCycleSettings(profileId, defaultPresetId, applications.AsReadOnly(), websites.AsReadOnly());
    }

    public async Task<Guid?> LoadDefaultCyclePresetIdAsync(ProfileId profileId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ValidateId(profileId.Value, nameof(profileId));
        await using var connection = await OpenAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        if (!await ExistsAsync(connection, null, "Profiles", profileId.Value, cancellationToken).ConfigureAwait(false)) return null;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DefaultPresetId FROM ProfileCycleSettings WHERE ProfileId = $profileId;";
        command.Parameters.AddWithValue("$profileId", profileId.Value.ToString("D"));
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string text && Guid.TryParse(text, out var id) && id != Guid.Empty ? id : null;
    }

    private async Task<IReadOnlyList<ApplicationRule>> LoadApplicationRulesAsync(
        SqliteConnection connection,
        ProfileId profileId,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        var rules = new List<ApplicationRule>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id, Action, MatchKind, Value FROM ApplicationRules WHERE ProfileId = $profileId ORDER BY Id;";
        command.Parameters.AddWithValue("$profileId", profileId.Value.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = ParseGuid(reader.GetString(0), "application rule ID");
            var action = ParseEnum<RuleAction>(reader.GetInt32(1), "application rule action");
            var matchKind = ParseEnum<ApplicationMatchKind>(reader.GetInt32(2), "application match kind");
            var value = reader.GetString(3);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException("Application rule value cannot be empty.");
            }

            rules.Add(new ApplicationRule(id, action, matchKind, value));
        }

        return rules;
    }

    private async Task<IReadOnlyList<WebsiteRule>> LoadWebsiteRulesAsync(
        SqliteConnection connection,
        ProfileId profileId,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        var rules = new List<WebsiteRule>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id, Action, Pattern FROM WebsiteRules WHERE ProfileId = $profileId ORDER BY Id;";
        command.Parameters.AddWithValue("$profileId", profileId.Value.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = ParseGuid(reader.GetString(0), "website rule ID");
            var action = ParseEnum<RuleAction>(reader.GetInt32(1), "website rule action");
            var pattern = reader.GetString(2);
            if (string.IsNullOrWhiteSpace(pattern) || pattern.Contains('?', StringComparison.Ordinal))
            {
                throw new InvalidDataException("Website rule pattern is invalid.");
            }

            rules.Add(new WebsiteRule(id, action, pattern));
        }

        return rules;
    }

    private static void ValidateProfile(StoredProfile profile)
    {
        if (profile.Id.Value == Guid.Empty)
        {
            throw new ArgumentException("Profile ID must not be empty.", nameof(profile));
        }

        var name = profile.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
        {
            throw new ArgumentException("Profile name must be between one and 80 characters.", nameof(profile));
        }

        foreach (var rule in profile.ApplicationRules)
        {
            if (rule.Id == Guid.Empty || !Enum.IsDefined(rule.Action) || !Enum.IsDefined(rule.MatchKind) || string.IsNullOrWhiteSpace(rule.Value))
            {
                throw new ArgumentException("Application rule is invalid.", nameof(profile));
            }
        }

        foreach (var rule in profile.WebsiteRules)
        {
            if (rule.Id == Guid.Empty || !Enum.IsDefined(rule.Action) || string.IsNullOrWhiteSpace(rule.Pattern) || rule.Pattern.Contains('?', StringComparison.Ordinal))
            {
                throw new ArgumentException("Website rule is invalid.", nameof(profile));
            }
        }
    }

    private static void ValidateCyclePreset(StoredCyclePreset preset)
    {
        ValidateId(preset.Id, nameof(preset));
        if (string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Trim().Length > 80)
        {
            throw new ArgumentException("Cycle preset name must be between one and 80 characters.", nameof(preset));
        }

        var validation = FocusCycleConfiguration.Create(
            preset.Configuration.FocusDuration,
            preset.Configuration.ShortBreakDuration,
            preset.Configuration.LongBreakDuration,
            preset.Configuration.CyclesBeforeLongBreak,
            preset.Configuration.TerminationMode,
            preset.Configuration.CycleCount,
            preset.Configuration.TotalDuration);
        if (!validation.IsValid)
        {
            throw new ArgumentException("Cycle preset configuration is invalid.", nameof(preset));
        }
    }

    private static void ValidateProfileCycleSettings(ProfileCycleSettings settings)
    {
        ValidateId(settings.ProfileId.Value, nameof(settings));
        if (settings.ApplicationRules.Count > 10000 || settings.WebsiteRules.Count > 10000)
        {
            throw new ArgumentException("Break exception count exceeds the maximum.", nameof(settings));
        }
        if (settings.DefaultPresetId == Guid.Empty)
        {
            throw new ArgumentException("Default preset ID must not be empty.", nameof(settings));
        }

        if (settings.ApplicationRules.Select(rule => rule.Id).Distinct().Count() != settings.ApplicationRules.Count
            || settings.WebsiteRules.Select(rule => rule.Id).Distinct().Count() != settings.WebsiteRules.Count)
        {
            throw new ArgumentException("Break exception IDs must be unique within each rule set.", nameof(settings));
        }

        // Break rules are exceptions to the release, not permissions. During a break the
        // extension swaps the whole rule set for this one, so an empty list releases
        // everything — which is the point of a break. An entry here therefore has to be
        // a Block rule to mean "this one stays blocked anyway"; stored as Allow it
        // permitted what was already permitted and the exception did nothing.
        foreach (var rule in settings.ApplicationRules)
        {
            if (rule.Id == Guid.Empty
                || rule.Action != RuleAction.Block
                || !Enum.IsDefined(rule.MatchKind)
                || string.IsNullOrWhiteSpace(rule.Value))
            {
                throw new ArgumentException("Break application rules must be non-empty Block rules.", nameof(settings));
            }
        }

        foreach (var rule in settings.WebsiteRules)
        {
            if (rule.Id == Guid.Empty
                || rule.Action != RuleAction.Block
                || string.IsNullOrWhiteSpace(rule.Pattern)
                || rule.Pattern.Contains('?', StringComparison.Ordinal))
            {
                throw new ArgumentException("Break website rules must be non-empty Block rules without query parameters.", nameof(settings));
            }
        }
    }

    private static void ValidateId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("ID must not be empty.", parameterName);
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
            await foreignKeys.ExecuteNonQueryAsync(cancellationToken);
        }

        int schemaVersion;
        await using (var versionProbe = connection.CreateCommand())
        {
            versionProbe.CommandText = "PRAGMA user_version;";
            schemaVersion = Convert.ToInt32(
                await versionProbe.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
        }

        if (schemaVersion > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Database schema version {schemaVersion} is newer than supported version {CurrentSchemaVersion}.");
        }

        if (schemaVersion == CurrentSchemaVersion)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS Profiles (
                Id TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ApplicationRules (
                Id TEXT NOT NULL PRIMARY KEY,
                ProfileId TEXT NOT NULL,
                Action INTEGER NOT NULL,
                MatchKind INTEGER NOT NULL,
                Value TEXT NOT NULL,
                FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS WebsiteRules (
                Id TEXT NOT NULL PRIMARY KEY,
                ProfileId TEXT NOT NULL,
                Action INTEGER NOT NULL,
                Pattern TEXT NOT NULL,
                FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS CyclePresets (
                Id TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                FocusSeconds INTEGER NOT NULL,
                ShortBreakSeconds INTEGER NOT NULL,
                LongBreakSeconds INTEGER NOT NULL,
                CyclesBeforeLongBreak INTEGER NOT NULL,
                TerminationMode TEXT NOT NULL,
                CycleCount INTEGER NULL,
                TotalDurationSeconds INTEGER NULL,
                IsBuiltIn INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ProfileCycleSettings (
                ProfileId TEXT NOT NULL PRIMARY KEY,
                DefaultPresetId TEXT NULL,
                FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE,
                FOREIGN KEY (DefaultPresetId) REFERENCES CyclePresets(Id) ON DELETE SET NULL
            );
            CREATE TABLE IF NOT EXISTS BreakApplicationRules (
                Id TEXT NOT NULL PRIMARY KEY,
                ProfileId TEXT NOT NULL,
                MatchKind INTEGER NOT NULL,
                Value TEXT NOT NULL,
                FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE
            );
            CREATE TABLE IF NOT EXISTS BreakWebsiteRules (
                Id TEXT NOT NULL PRIMARY KEY,
                ProfileId TEXT NOT NULL,
                Pattern TEXT NOT NULL,
                FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        // CREATE TABLE IF NOT EXISTS never widens a table that already exists, so the
        // v1 -> v2 columns are added separately for databases written before accents.
        await AddColumnIfMissingAsync(connection, "Profiles", "Description", "TEXT", cancellationToken);
        await AddColumnIfMissingAsync(connection, "Profiles", "AccentHex", "TEXT", cancellationToken);

        await SeedBuiltInPresetsAsync(connection, cancellationToken);

        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion};";
        await versionCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SeedBuiltInPresetsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        foreach (var preset in BuiltInCyclePresets.All)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT OR IGNORE INTO CyclePresets
                    (Id, Name, FocusSeconds, ShortBreakSeconds, LongBreakSeconds,
                     CyclesBeforeLongBreak, TerminationMode, CycleCount, TotalDurationSeconds, IsBuiltIn)
                VALUES
                    ($id, $name, $focusSeconds, $shortBreakSeconds, $longBreakSeconds,
                     $frequency, $terminationMode, $cycleCount, $totalDurationSeconds, 1);
                """;
            command.Parameters.AddWithValue("$id", preset.Id.ToString("D"));
            command.Parameters.AddWithValue("$name", preset.Name);
            command.Parameters.AddWithValue("$focusSeconds", (long)preset.Configuration.FocusDuration.TotalSeconds);
            command.Parameters.AddWithValue("$shortBreakSeconds", (long)preset.Configuration.ShortBreakDuration.TotalSeconds);
            command.Parameters.AddWithValue("$longBreakSeconds", (long)preset.Configuration.LongBreakDuration.TotalSeconds);
            command.Parameters.AddWithValue("$frequency", preset.Configuration.CyclesBeforeLongBreak);
            command.Parameters.AddWithValue("$terminationMode", preset.Configuration.TerminationMode.ToString());
            command.Parameters.AddWithValue("$cycleCount", (object?)preset.Configuration.CycleCount ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$totalDurationSeconds",
                (object?)(preset.Configuration.TotalDuration is { } total ? (long)total.TotalSeconds : null) ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task AddColumnIfMissingAsync(
        SqliteConnection connection,
        string table,
        string column,
        string type,
        CancellationToken cancellationToken)
    {
        await using (var probe = connection.CreateCommand())
        {
            probe.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $column;";
            probe.Parameters.AddWithValue("$column", column);
            var existing = Convert.ToInt64(await probe.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            if (existing > 0)
            {
                return;
            }
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeAccent(string? value)
    {
        var normalized = NormalizeText(value)?.ToUpperInvariant();
        return Profile.IsAccentHex(normalized) ? normalized : null;
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> ExistsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string table,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT EXISTS (SELECT 1 FROM {table} WHERE Id = $id);";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture) == 1;
    }

    private static StoredCyclePreset ReadCyclePreset(
        Microsoft.Data.Sqlite.SqliteDataReader reader,
        Guid id,
        int offset = 0)
    {
        var name = reader.GetString(offset);
        var focusDuration = TimeSpan.FromSeconds(reader.GetInt64(offset + 1));
        var shortBreakDuration = TimeSpan.FromSeconds(reader.GetInt64(offset + 2));
        var longBreakDuration = TimeSpan.FromSeconds(reader.GetInt64(offset + 3));
        var frequency = reader.GetInt32(offset + 4);
        var terminationModeText = reader.GetString(offset + 5);
        if (!Enum.TryParse<CycleTerminationMode>(terminationModeText, ignoreCase: false, out var terminationMode))
        {
            throw new InvalidDataException("Stored cycle preset termination mode is invalid.");
        }

        int? cycleCount = reader.IsDBNull(offset + 6) ? null : reader.GetInt32(offset + 6);
        TimeSpan? totalDuration = reader.IsDBNull(offset + 7)
            ? null
            : TimeSpan.FromSeconds(reader.GetInt64(offset + 7));
        var configuration = FocusCycleConfiguration.Create(
            focusDuration,
            shortBreakDuration,
            longBreakDuration,
            frequency,
            terminationMode,
            cycleCount,
            totalDuration);
        if (!configuration.IsValid)
        {
            throw new InvalidDataException("Stored cycle preset configuration is invalid.");
        }

        return new StoredCyclePreset(id, name, configuration.Value!, reader.GetInt32(offset + 8) != 0);
    }

    private static Guid ParseGuid(string value, string description) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed
            : throw new InvalidDataException($"Stored {description} is invalid.");

    private static TEnum ParseEnum<TEnum>(int value, string description)
        where TEnum : struct, Enum =>
        Enum.IsDefined(typeof(TEnum), value)
            ? (TEnum)(object)value
            : throw new InvalidDataException($"Stored {description} is invalid.");
}
