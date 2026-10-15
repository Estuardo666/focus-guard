using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace FocusGuard.Sync;

/// <summary>Stable, opaque, owner-scoped local storage location for settings sync metadata.</summary>
[SupportedOSPlatform("windows")]
public static class SettingsSyncLocalStorage
{
    public static string GetCurrentUserRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData)) throw new InvalidOperationException("Local application data is unavailable.");
        return Path.Combine(localAppData, "FocusGuard", "Sync");
    }

    public static string PrepareCurrentUserDirectory(SettingsSyncScope scope)
    {
        return PrepareScopedDirectory(GetCurrentUserRoot(), scope);
    }

    /// <summary>Prepare a dedicated sync root; intended for owner-controlled app data and isolated tests.</summary>
    public static string PrepareScopedDirectory(string syncRoot, SettingsSyncScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncRoot);
        ArgumentNullException.ThrowIfNull(scope);
        scope.Validate();

        var root = Path.GetFullPath(syncRoot);
        Directory.CreateDirectory(root);
        ApplyOwnerOnlyAcl(root);

        var scopeDirectory = GetScopedDirectory(root, scope);
        Directory.CreateDirectory(scopeDirectory);
        ApplyOwnerOnlyAcl(scopeDirectory);
        return scopeDirectory;
    }

    /// <summary>Resolve the stable opaque path without creating it; safe to use before explicit sync opt-in.</summary>
    public static string GetScopedDirectory(string syncRoot, SettingsSyncScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncRoot);
        ArgumentNullException.ThrowIfNull(scope);
        scope.Validate();

        var root = Path.GetFullPath(syncRoot);

        var pathDigest = SHA256.HashData(Encoding.UTF8.GetBytes("FG-SYNC-LOCAL-PATH-1\0" + scope.Issuer + "\0" +
            scope.AccountId + "\0" + scope.GenerationId + "\0" + scope.DeviceId));
        return Path.Combine(root, Convert.ToHexString(pathDigest).ToLowerInvariant());
    }

    private static void ApplyOwnerOnlyAcl(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Sync storage directory cannot be a reparse point.");

        var directory = new DirectoryInfo(path);
        var security = directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        var currentUser = WindowsIdentity.GetCurrent().User ??
            throw new UnauthorizedAccessException("Current Windows user SID is unavailable.");
        if (owner is null || !currentUser.Equals(owner))
            throw new UnauthorizedAccessException("Sync storage directory is not owned by the current user.");

        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true,
            targetType: typeof(SecurityIdentifier)))
            security.RemoveAccessRuleSpecific(rule);

        var allowed = new[]
        {
            currentUser,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
        };
        foreach (var sid in allowed)
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));

        directory.SetAccessControl(security);
        var verified = directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        if (!verified.AreAccessRulesProtected || !currentUser.Equals(verified.GetOwner(typeof(SecurityIdentifier))))
            throw new UnauthorizedAccessException("Sync storage directory ACL verification failed.");

        foreach (FileSystemAccessRule rule in verified.GetAccessRules(includeExplicit: true, includeInherited: true,
            targetType: typeof(SecurityIdentifier)))
            if (rule.AccessControlType != AccessControlType.Allow || !allowed.Any(sid => sid.Equals(rule.IdentityReference)))
                throw new UnauthorizedAccessException("Sync storage directory grants access outside its owner/system administrators.");
    }
}
