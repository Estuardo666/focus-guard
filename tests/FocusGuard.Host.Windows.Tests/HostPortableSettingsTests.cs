using System.Reflection;
using FocusGuard.Client.Preferences;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Identity;
using FocusGuard.Sync;
using Xunit;

namespace FocusGuard.Host.Windows.Tests;

public sealed class HostPortableSettingsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptPreferencesArePreservedAndCannotBeExportedAsDefaults(bool importing)
    {
        using var fixture = new Fixture();
        fixture.Service.SetPortableSettingsPresenters(
            (_, _) => Task.FromResult<PortableSettingsFileSelection?>(new(fixture.Export,
                SettingsFields.Theme | SettingsFields.Language | SettingsFields.Duration)),
            _ => Task.FromResult<string?>(fixture.Import),
            (_, _) => Task.FromResult<SettingsFields?>(SettingsFields.Theme));
        await File.WriteAllBytesAsync(fixture.Import, SettingsExport.Create(new(90, "light", "en"), true));
        await File.WriteAllTextAsync(fixture.Store.FilePath, "corrupt-preferences");
        var result = await fixture.Run(importing ? HostAccountAction.ImportPortableSettings : HostAccountAction.ExportPortableSettings);
        Assert.NotNull(result.Error);
        Assert.Equal("corrupt-preferences", await File.ReadAllTextAsync(fixture.Store.FilePath));
        Assert.False(File.Exists(fixture.Export));
    }

    [Fact]
    public async Task SignedOutHostExportsAndImportsOnlySelectedSettings()
    {
        using var fixture = new Fixture();
        fixture.Service.SetPortableSettingsPresenters(
            (_, _) => Task.FromResult<PortableSettingsFileSelection?>(new(fixture.Export,
                SettingsFields.Theme | SettingsFields.Language | SettingsFields.Duration)),
            _ => Task.FromResult<string?>(fixture.Import),
            (_, _) => Task.FromResult<SettingsFields?>(SettingsFields.Theme | SettingsFields.Duration));
        await fixture.Run(HostAccountAction.ExportPortableSettings);
        var bytes = await File.ReadAllBytesAsync(fixture.Export);
        var preview = SettingsExport.Preview(bytes, GlobalSettings.Default,
            SettingsFields.Theme | SettingsFields.Language | SettingsFields.Duration);
        Assert.Equal(DesktopSettingsProjection.Extract(fixture.Original), preview.Proposed);
        await File.WriteAllBytesAsync(fixture.Import, SettingsExport.Create(new(90, "light", "en"), true));
        var result = await fixture.Run(HostAccountAction.ImportPortableSettings);
        Assert.Null(result.Error);
        Assert.Equal("PortableImported", result.SettingsTransferStatus);
        var local = fixture.Store.Load();
        Assert.Equal(fixture.Original with { Theme = AppTheme.Light, LastDurationMinutes = 90 }, local);
        Assert.False(result.SignedIn);
        Assert.False(result.SettingsSyncEnabled);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("invalid")]
    [InlineData("oversize")]
    [InlineData("stale")]
    public async Task RejectedImportPreservesLocalState(string scenario)
    {
        using var fixture = new Fixture();
        var expected = fixture.Original;
        fixture.Service.SetPortableSettingsPresenters(
            (_, _) => Task.FromResult<PortableSettingsFileSelection?>(null),
            _ => Task.FromResult<string?>(fixture.Import),
            (_, _) =>
            {
                if (scenario == "stale")
                {
                    expected = expected with { UiLanguage = "en" };
                    fixture.Store.SaveVerified(expected);
                }
                return Task.FromResult<SettingsFields?>(scenario == "cancel" ? null : SettingsFields.Theme);
            });
        var bytes = scenario switch
        {
            "invalid" => "{\"unexpected\":true}"u8.ToArray(),
            "oversize" => new byte[8193],
            _ => SettingsExport.Create(new(90, "light", "en"), true)
        };
        await File.WriteAllBytesAsync(fixture.Import, bytes);
        var result = await fixture.Run(HostAccountAction.ImportPortableSettings);
        Assert.Equal(expected, fixture.Store.Load());
        Assert.Equal(scenario switch
        {
            "cancel" => null,
            "stale" => "PortableSettingsStale",
            _ => "PortableSettingsInvalid"
        }, result.Error);
        Assert.False(result.SettingsSyncEnabled);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "FocusGuard-Portable-Test-" + Guid.NewGuid().ToString("N"));
        internal DesktopSettings Original { get; } = new(Theme: AppTheme.Dark, UiLanguage: "es", LastDurationMinutes: 45,
            ReduceMotion: true, EnforcementEnabled: true, CyclePhaseNotifications: false);
        internal DesktopSettingsStore Store { get; }
        internal HostAccountService Service { get; }
        internal string Export => Path.Combine(root, "export.json");
        internal string Import => Path.Combine(root, "import.json");

        internal Fixture()
        {
            Directory.CreateDirectory(root);
            Store = new(Path.Combine(root, "preferences.json"));
            Store.SaveVerified(Original);
            var configuration = new IdentityConfiguration(new Uri("https://identity.example.invalid/realms/test"),
                "focusguard-native", new Uri("http://127.0.0.1:18444/callback/"), LocalTest: true);
            Service = new(new IdentitySession(configuration, TimeProvider.System),
                new UserTokenVault(Path.Combine(root, "identity.dpapi")), configuration,
                Path.Combine(root, "devices"), Store, settingsSyncTestRoot: Path.Combine(root, "sync"));
        }

        internal async Task<HostAccountSnapshot> Run(HostAccountAction action)
        {
            _ = await Service.ExecuteAsync(action, CancellationToken.None);
            var operation = (Task)typeof(HostAccountService).GetField("settingsTransferTask",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Service)!;
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            return await Service.ExecuteAsync(HostAccountAction.Get, CancellationToken.None);
        }

        public void Dispose()
        {
            Service.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }
}
