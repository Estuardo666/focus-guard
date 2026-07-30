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
}

public sealed class SqliteProfileStore : IProfileStore
{
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

        transaction.Commit();
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

        string? name = null;
        string? description = null;
        string? accentHex = null;
        await using (var command = connection.CreateCommand())
        {
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

        var applicationRules = await LoadApplicationRulesAsync(connection, profileId, cancellationToken);
        var websiteRules = await LoadWebsiteRulesAsync(connection, profileId, cancellationToken);
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
            profiles.Add((await LoadProfileAsync(profileId, cancellationToken))!);
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

    private async Task<IReadOnlyList<ApplicationRule>> LoadApplicationRulesAsync(
        SqliteConnection connection,
        ProfileId profileId,
        CancellationToken cancellationToken)
    {
        var rules = new List<ApplicationRule>();
        await using var command = connection.CreateCommand();
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
        CancellationToken cancellationToken)
    {
        var rules = new List<WebsiteRule>();
        await using var command = connection.CreateCommand();
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
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA foreign_keys = ON;
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
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        // CREATE TABLE IF NOT EXISTS never widens a table that already exists, so the
        // v1 -> v2 columns are added separately for databases written before accents.
        await AddColumnIfMissingAsync(connection, "Profiles", "Description", "TEXT", cancellationToken);
        await AddColumnIfMissingAsync(connection, "Profiles", "AccentHex", "TEXT", cancellationToken);

        await using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version = 2;";
        await version.ExecuteNonQueryAsync(cancellationToken);
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
