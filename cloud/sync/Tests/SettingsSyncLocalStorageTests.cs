using System.Security.AccessControl;
using System.Security.Principal;
using FocusGuard.Sync;
using Xunit;

public sealed class SettingsSyncLocalStorageTests
{
    [Fact]
    public async Task ScopePathIsStableOpaqueAndProtectedByOwnerSystemAndAdminAcl()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusGuard-Scoped-Store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var scope = Scope();
            var path = SettingsSyncLocalStorage.PrepareScopedDirectory(root, scope);
            Assert.Equal(path, SettingsSyncLocalStorage.PrepareScopedDirectory(root, scope));
            Assert.NotEqual(path, SettingsSyncLocalStorage.PrepareScopedDirectory(root,
                scope with { AccountId = Guid.NewGuid().ToString("D") }));
            Assert.NotEqual(path, SettingsSyncLocalStorage.PrepareScopedDirectory(root,
                scope with { GenerationId = Guid.NewGuid().ToString("D") }));
            Assert.NotEqual(path, SettingsSyncLocalStorage.PrepareScopedDirectory(root,
                scope with { DeviceId = Guid.NewGuid().ToString("D") }));
            Assert.NotEqual(path, SettingsSyncLocalStorage.PrepareScopedDirectory(root,
                scope with { Issuer = "https://other.identity.example.invalid/realm" }));
            Assert.DoesNotContain(scope.AccountId, path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(scope.DeviceId, path, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("identity.example", path, StringComparison.OrdinalIgnoreCase);

            var directory = new DirectoryInfo(path);
            var security = directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            var currentUser = WindowsIdentity.GetCurrent().User!;
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            Assert.True(security.AreAccessRulesProtected);
            Assert.Equal(currentUser, security.GetOwner(typeof(SecurityIdentifier)));
            var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true,
                targetType: typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
            Assert.NotEmpty(rules);
            Assert.All(rules, rule =>
            {
                Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
                Assert.Contains(rule.IdentityReference, new IdentityReference[] { currentUser, system, administrators });
            });
            Assert.Contains(rules, rule => currentUser.Equals(rule.IdentityReference));
            Assert.Contains(rules, rule => system.Equals(rule.IdentityReference));
            Assert.Contains(rules, rule => administrators.Equals(rule.IdentityReference));

            var rootSecurity = new DirectoryInfo(root).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            Assert.True(rootSecurity.AreAccessRulesProtected);

            var store = new SettingsRetryCheckpointStore(Path.Combine(path, "retry.dpapi"), scope);
            var checkpoint = new SettingsRetryCheckpoint(2, TimeSpan.FromMinutes(3).Ticks);
            await store.SaveAsync(checkpoint);
            Assert.Equal(checkpoint, await store.LoadAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static SettingsSyncScope Scope() => new("https://identity.example.invalid/realms/test",
        Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
}
