using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace FocusGuard.Ipc.Windows;

/// <summary>Reads installed files under service identity. Never trusts caller-provided hashes or paths.</summary>
[SupportedOSPlatform("windows")]
public static class ProtectedIpcDeployment
{
    private static readonly HashSet<string> TrustedWriters =
    [
        "S-1-5-18", "S-1-5-32-544",
        "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464" // Windows TrustedInstaller
    ];
    private const FileSystemRights WriteRights = FileSystemRights.WriteData | FileSystemRights.AppendData
        | FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes
        | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
    private const FileSystemRights AncestorReplacementRights = FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions
        | FileSystemRights.TakeOwnership | FileSystemRights.WriteAttributes;

    [SupportedOSPlatform("windows")]
    public static IpcPeerEvidence Observe(ObservedPipeClient peer, string installRoot)
    {
        ArgumentNullException.ThrowIfNull(peer);
        var root = IpcDirectoryPath.Normalize(installRoot);
        var image = Path.GetFullPath(peer.ImagePath);
        var parent = Path.GetDirectoryName(image);
        if (parent is null || !string.Equals(IpcDirectoryPath.Normalize(parent), root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Pipe image is outside the installed root.");
        EnsureProtectedAncestors(root);
        EnsureProtectedFile(image);
        var managed = Path.ChangeExtension(image, ".dll");
        EnsureProtectedFile(managed);
        // Both .NET apphost and managed entrypoint are version-bound; apphost alone is insufficient.
        using var file = new FileStream(image, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var managedFile = new FileStream(managed, FileMode.Open, FileAccess.Read, FileShare.Read);
        var finalImage = FinalPath(file.SafeFileHandle);
        if (!string.Equals(finalImage, image, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(FinalPath(managedFile.SafeFileHandle), managed, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Opened image alias differs from native deployment path.");
        var hash = Convert.ToHexString(SHA256.HashData(file));
        var managedHash = Convert.ToHexString(SHA256.HashData(managedFile));
        return new(peer.ProcessId, peer.UserSid, peer.LogonSid, peer.SessionId, finalImage, hash,
            WindowsPipeIdentity.IsAlive(peer), true, true, managedHash, true);
    }

    public static (string FinalImagePath, string Sha256, string ManagedSha256) ObserveServiceImage(string imagePath)
    {
        var image = Path.GetFullPath(imagePath);
        var root = Path.GetDirectoryName(image) ?? throw new UnauthorizedAccessException("Missing service root.");
        // Full dependency/resource ACL validation is done once while loading the
        // protected deployment manifest. Per exchange, pin and recheck the service
        // entrypoint and managed image without rescanning the entire runtime tree.
        EnsureProtectedAncestors(root);
        var managed = Path.ChangeExtension(image, ".dll");
        EnsureProtectedFile(image);
        EnsureProtectedFile(managed);
        using var file = new FileStream(image, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var assembly = new FileStream(managed, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!string.Equals(FinalPath(file.SafeFileHandle), image, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(FinalPath(assembly.SafeFileHandle), managed, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Service image alias denied.");
        return (image, Convert.ToHexString(SHA256.HashData(file)), Convert.ToHexString(SHA256.HashData(assembly)));
    }

    public static void EnsureProtectedFile(string path)
    {
        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Reparse image denied.");
        EnsureProtectedAcl(info.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner), WriteRights);
    }

    public static void EnsureProtectedRuntimeFiles(string installRoot)
    {
        // A protected apphost/entrypoint cannot authorize user-writable DLLs,
        // runtime configuration or UI resources loaded from the same deployment.
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(installRoot));
        var entries = 0;
        while (pending.TryPop(out var directory))
        {
            EnsureProtectedAncestors(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++entries > 8192) throw new InvalidDataException("Deployment file count exceeded.");
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("Deployment reparse entry denied.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                else EnsureProtectedFile(entry);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    public static void EnsureProtectedAncestors(string directory)
    {
        var isInstallRoot = true;
        for (var info = new DirectoryInfo(Path.GetFullPath(directory)); info is not null; info = info.Parent)
        {
            if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Missing or reparse deployment directory.");
            // Adding a new sibling to an ancestor does not replace the protected installation.
            // The installation directory itself must deny all untrusted mutation/child creation.
            EnsureProtectedAcl(info.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner),
                isInstallRoot ? WriteRights : AncestorReplacementRights);
            isInstallRoot = false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void EnsureProtectedAcl(FileSystemSecurity security, FileSystemRights dangerousRights)
    {
        var owner = security.GetOwner(typeof(SecurityIdentifier))?.Value;
        if (owner is null || !TrustedWriters.Contains(owner)) throw new UnauthorizedAccessException("Untrusted deployment owner.");
        if (!security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Any())
            throw new UnauthorizedAccessException("Empty deployment ACL denied.");
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            // InheritOnly ACEs apply to descendants, not to the current object.
            if (rule.AccessControlType == AccessControlType.Allow
                && (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0
                && (rule.FileSystemRights & dangerousRights) != 0
                && !TrustedWriters.Contains(rule.IdentityReference.Value))
                throw new UnauthorizedAccessException("Deployment has an untrusted write grant.");
        }
    }

    private static string FinalPath(SafeFileHandle file)
    {
        var buffer = new char[32768];
        var count = GetFinalPathNameByHandle(file, buffer, (uint)buffer.Length, 0);
        if (count == 0 || count >= buffer.Length) throw new UnauthorizedAccessException("Final image path unavailable.");
        var path = new string(buffer, 0, (int)count);
        if (!path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Nonlocal image path denied.");
        return Path.GetFullPath(path[4..]);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, [Out] char[] path, uint size, uint flags);
}
