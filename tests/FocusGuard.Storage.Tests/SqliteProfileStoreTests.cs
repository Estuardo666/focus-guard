using FocusGuard.Domain;
using FocusGuard.Storage;
using Microsoft.Data.Sqlite;

namespace FocusGuard.Storage.Tests;

public sealed class SqliteProfileStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsProfileAndRulesInRealSqlite()
    {
        using var temporary = TemporaryDatabase.Create();
        var profileId = ProfileId.New();
        var applicationRule = new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.ExactPath, "C:\\Apps\\Focus.exe");
        var websiteRule = new WebsiteRule(Guid.NewGuid(), RuleAction.Allow, "example.com/work/*");
        var profile = new StoredProfile(profileId, "Deep work 'quoted'", [applicationRule], [websiteRule]);
        var store = new SqliteProfileStore(temporary.DatabasePath);

        await store.InitializeAsync();
        await store.SaveProfileAsync(profile);
        var loaded = await store.LoadProfileAsync(profileId);

        Assert.NotNull(loaded);
        Assert.Equal(profile.Name, loaded!.Name);
        Assert.Equal(profileId, loaded.Id);
        Assert.Equal(applicationRule, Assert.Single(loaded.ApplicationRules));
        Assert.Equal(websiteRule, Assert.Single(loaded.WebsiteRules));
    }

    [Fact]
    public async Task SaveProfile_ReplacesRulesAtomicallyAndPersistsAcrossStoreInstances()
    {
        using var temporary = TemporaryDatabase.Create();
        var profileId = ProfileId.New();
        var first = new StoredProfile(
            profileId,
            "Focus",
            [new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "old.exe")],
            []);
        var second = first with
        {
            Name = "Updated",
            ApplicationRules = [new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "new.exe")]
        };
        var store = new SqliteProfileStore(temporary.DatabasePath);

        await store.SaveProfileAsync(first);
        await store.SaveProfileAsync(second);
        var loaded = await new SqliteProfileStore(temporary.DatabasePath).LoadProfileAsync(profileId);

        Assert.Equal("Updated", loaded!.Name);
        Assert.Equal("new.exe", Assert.Single(loaded.ApplicationRules).Value);
    }

    [Fact]
    public async Task LoadMissingProfile_ReturnsNull()
    {
        using var temporary = TemporaryDatabase.Create();
        var store = new SqliteProfileStore(temporary.DatabasePath);

        Assert.Null(await store.LoadProfileAsync(ProfileId.New()));
    }

    [Fact]
    public async Task ListAndDelete_UseDeterministicNamesAndCascadeRules()
    {
        using var temporary = TemporaryDatabase.Create();
        var store = new SqliteProfileStore(temporary.DatabasePath);
        var zulu = new StoredProfile(ProfileId.New(), "Zulu", [], []);
        var alpha = new StoredProfile(
            ProfileId.New(),
            "alpha",
            [new ApplicationRule(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "chat.exe")],
            []);

        await store.SaveProfileAsync(zulu);
        await store.SaveProfileAsync(alpha);
        var listed = await store.ListProfilesAsync();
        var deleted = await store.DeleteProfileAsync(alpha.Id);

        Assert.Equal(["alpha", "Zulu"], listed.Select(profile => profile.Name));
        Assert.True(deleted);
        Assert.Null(await store.LoadProfileAsync(alpha.Id));
        var remaining = Assert.Single(await store.ListProfilesAsync());
        Assert.Equal(zulu.Id, remaining.Id);
        Assert.Equal(zulu.Name, remaining.Name);
        Assert.Empty(remaining.ApplicationRules);
        Assert.Empty(remaining.WebsiteRules);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsDescriptionAndAccent()
    {
        using var temporary = TemporaryDatabase.Create();
        var profile = new StoredProfile(
            ProfileId.New(),
            "Estudio",
            [],
            [],
            Description: "Solo video y streaming fuera.",
            AccentHex: "#3E9C7A");
        var store = new SqliteProfileStore(temporary.DatabasePath);

        await store.SaveProfileAsync(profile);
        var loaded = await new SqliteProfileStore(temporary.DatabasePath).LoadProfileAsync(profile.Id);

        Assert.Equal("Solo video y streaming fuera.", loaded!.Description);
        Assert.Equal("#3E9C7A", loaded.AccentHex);
    }

    [Fact]
    public async Task Load_DropsAccentThatIsNotSixDigitHex()
    {
        using var temporary = TemporaryDatabase.Create();
        var profile = new StoredProfile(ProfileId.New(), "Focus", [], [], AccentHex: "not-a-color");
        var store = new SqliteProfileStore(temporary.DatabasePath);

        await store.SaveProfileAsync(profile);
        var loaded = await store.LoadProfileAsync(profile.Id);

        Assert.Null(loaded!.AccentHex);
    }

    // The installed build already has a v1 database on disk, so widening the table has
    // to keep the existing rows readable rather than starting from an empty schema.
    [Fact]
    public async Task Initialize_AddsAccentColumnsToAnExistingVersionOneDatabase()
    {
        using var temporary = TemporaryDatabase.Create();
        var profileId = ProfileId.New();
        // Pooling off, otherwise the handle outlives the connection and the temp directory cannot be deleted.
        await using (var legacy = new SqliteConnection($"Data Source={temporary.DatabasePath};Pooling=False"))
        {
            await legacy.OpenAsync();
            await using var command = legacy.CreateCommand();
            command.CommandText =
                """
                PRAGMA user_version = 1;
                CREATE TABLE Profiles (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Name TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL
                );
                INSERT INTO Profiles (Id, Name, UpdatedUtc) VALUES ($id, 'Legacy', '2026-01-01T00:00:00.0000000Z');
                """;
            command.Parameters.AddWithValue("$id", profileId.Value.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }

        var store = new SqliteProfileStore(temporary.DatabasePath);
        await store.InitializeAsync();
        var loaded = await store.LoadProfileAsync(profileId);

        Assert.Equal("Legacy", loaded!.Name);
        Assert.Null(loaded.Description);
        Assert.Null(loaded.AccentHex);
    }

    [Fact]
    public async Task CanceledInitialization_StopsBeforeSchemaWork()
    {
        using var temporary = TemporaryDatabase.Create();
        var store = new SqliteProfileStore(temporary.DatabasePath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.InitializeAsync(cancellation.Token));
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly DirectoryInfo _directory;

        private TemporaryDatabase(DirectoryInfo directory)
        {
            _directory = directory;
            DatabasePath = Path.Combine(directory.FullName, "focusguard.db");
        }

        public string DatabasePath { get; }

        public static TemporaryDatabase Create() =>
            new(Directory.CreateTempSubdirectory("FocusGuard-sqlite-"));

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
