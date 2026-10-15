using FocusGuard.Sync;
using Xunit;

public sealed class SettingsConsentStoreTests
{
    [Fact]
    public async Task ConsentDefaultsOffRequiresConfirmationAndSurvivesRealVaultRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Consent-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "outbox.dpapi");
            var scope = new SettingsSyncScope("https://identity.example.invalid/realms/test", Guid.NewGuid().ToString("D"),
                Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
            var vault = new SettingsOutboxVault(path, scope); var consent = new SettingsConsentStore(vault);
            Assert.False(await consent.IsEnabledAsync());
            await vault.SaveAsync(new(new(0, GlobalSettings.Default)));
            Assert.False(await consent.IsEnabledAsync());
            Assert.Throws<InvalidOperationException>(() => { _ = consent.SetAsync(true, false); });
            await consent.SetAsync(true, true);
            Assert.True(await new SettingsConsentStore(new(path, scope)).IsEnabledAsync());
            await vault.UpdateAsync(state => state!.Edit(GlobalSettings.Default with { Theme = "light" }).Prepare(_ => [1]));
            var before = (await vault.LoadAsync())!;
            await consent.SetAsync(false, false);
            var after = (await vault.LoadAsync())!;
            Assert.False(after.SyncConsentEnabled); Assert.Equal(before.Draft, after.Draft); Assert.Equal(before.InFlight, after.InFlight);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AckMergeAndResolutionPreserveConsentWithoutGrantingItImplicitly()
    {
        var state = new SettingsOutbox(new(0, GlobalSettings.Default), SyncConsentEnabled: true)
            .Edit(GlobalSettings.Default with { Theme = "light" }).Prepare(_ => [1]);
        var intent = state.InFlight!.Intent;
        var acknowledged = state.Acknowledge(intent.OperationId, 1, intent.Values);
        Assert.True(acknowledged.SyncConsentEnabled);
        Assert.True(acknowledged.Receive(new(2, intent.Values with { UiLanguage = "en" })).SyncConsentEnabled);
        Assert.True(acknowledged.Resolve(GlobalSettings.Default).SyncConsentEnabled);
        Assert.False(new SettingsOutbox(new(0, GlobalSettings.Default)).Edit(GlobalSettings.Default with { Theme = "light" }).SyncConsentEnabled);
    }
}
