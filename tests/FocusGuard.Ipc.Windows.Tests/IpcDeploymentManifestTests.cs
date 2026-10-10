using System.Text.Json;

namespace FocusGuard.Ipc.Windows.Tests;

public sealed class IpcDeploymentManifestTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EquivalentDirectoryRootsAcceptEitherTrailingSeparator(bool manifestHasSeparator, bool expectedHasSeparator)
    {
        var manifestRoot = Root + (manifestHasSeparator ? Path.DirectorySeparatorChar : "");
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            ownerUserSid = "S-1-5-21-1-2-3-1001",
            installRoot = manifestRoot,
            service = new
            {
                serviceName = "FocusGuard",
                fileName = "FocusGuard.Service.exe",
                sha256 = Hash,
                managedFileName = "FocusGuard.Service.dll",
                managedSha256 = Hash
            },
            binaries = new[] { new { role = "Desktop", fileName = "FocusGuard.Desktop.exe", sha256 = Hash,
                managedFileName = "FocusGuard.Desktop.dll", managedSha256 = Hash } }
        });
        var parsed = IpcDeploymentManifest.Parse(payload, Root + (expectedHasSeparator ? Path.DirectorySeparatorChar : ""));
        Assert.Equal(Root, parsed.InstallRoot);
    }

    [Theory]
    [InlineData("FocusGuard")]
    [InlineData("C:/Program Files/FocusGuard2/")]
    public void RelativeAndSiblingExpectedRootsRemainRejected(string expectedRoot) =>
        Assert.Throws<InvalidDataException>(() => IpcDeploymentManifest.Parse(System.Text.Encoding.UTF8.GetBytes(Valid()), expectedRoot));

    [Fact]
    public void DirectoryNormalizationPreservesDriveRootsAndCanonicalizesRepeatedSeparators()
    {
        var driveRoot = Path.GetPathRoot(Root)!;
        Assert.Equal(driveRoot, IpcDirectoryPath.Normalize(driveRoot));
        Assert.Equal(Root, IpcDirectoryPath.Normalize(Root + Path.DirectorySeparatorChar + Path.DirectorySeparatorChar));
        var manifest = IpcDeploymentManifest.Parse(System.Text.Encoding.UTF8.GetBytes(Valid()), Root + Path.DirectorySeparatorChar);
        var policy = new IpcAuthorizationPolicy(new(manifest.OwnerUserSid, "S-1-5-5-1-1", 1), Root + Path.DirectorySeparatorChar, manifest.Binaries);
        var peer = new IpcPeerEvidence(42, manifest.OwnerUserSid, "S-1-5-5-1-1", 1,
            Path.Combine(Root, "FocusGuard.Desktop.exe"), Hash, true, true, true, Hash, true);
        Assert.True(policy.Authenticate(peer).Allowed);
    }

    [Fact]
    public void EquivalentLexicalManifestRootIsCanonicalBeforeTrustedPathsAreBuilt()
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            ownerUserSid = "S-1-5-21-1-2-3-1001",
            installRoot = Path.Combine(Root, "nested", ".."),
            service = new
            {
                serviceName = "FocusGuard",
                fileName = "FocusGuard.Service.exe",
                sha256 = Hash,
                managedFileName = "FocusGuard.Service.dll",
                managedSha256 = Hash
            },
            binaries = new[] { new { role = "Desktop", fileName = "FocusGuard.Desktop.exe", sha256 = Hash,
                managedFileName = "FocusGuard.Desktop.dll", managedSha256 = Hash } }
        });
        Assert.Equal(Root, IpcDeploymentManifest.Parse(payload, Root + Path.DirectorySeparatorChar).InstallRoot);
    }

    private static readonly string Root = Path.GetFullPath("C:/Program Files/FocusGuard");
    private static readonly string Hash = new('A', 64);
    private static string Valid() => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        ownerUserSid = "S-1-5-21-1-2-3-1001",
        installRoot = Root,
        service = new
        {
            serviceName = "FocusGuard",
            fileName = "FocusGuard.Service.exe",
            sha256 = Hash,
            managedFileName = "FocusGuard.Service.dll",
            managedSha256 = Hash
        },
        binaries = new[] { new { role = "Desktop", fileName = "FocusGuard.Desktop.exe", sha256 = Hash,
            managedFileName = "FocusGuard.Desktop.dll", managedSha256 = Hash } }
    });

    [Fact]
    public void ExplicitOwnerAndBothBinaryHashesAreRequiredForVersionedManifest()
    {
        var parsed = IpcDeploymentManifest.Parse(System.Text.Encoding.UTF8.GetBytes(Valid()), Root);
        Assert.Equal("S-1-5-21-1-2-3-1001", parsed.OwnerUserSid);
        Assert.Equal(Hash, Assert.Single(parsed.Binaries).ManagedSha256);
        Assert.Equal(IpcClientRole.Desktop, Assert.Single(parsed.Binaries).Role);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("unknown-property")]
    [InlineData("duplicate")]
    [InlineData("role-integer")]
    [InlineData("owner-service")]
    [InlineData("managed-file")]
    [InlineData("managed-hash")]
    public void MalformedManifestCannotCreateAuthorization(string kind)
    {
        var payload = kind switch
        {
            "version" => Valid().Replace("\"schemaVersion\":1", "\"schemaVersion\":2"),
            "unknown-property" => Valid().Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"allowAll\":true"),
            "duplicate" => Valid().Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"),
            "role-integer" => Valid().Replace("\"role\":\"Desktop\"", "\"role\":0"),
            "owner-service" => Valid().Replace("S-1-5-21-1-2-3-1001", "S-1-5-19"),
            "managed-file" => Valid().Replace("FocusGuard.Desktop.dll", "../FocusGuard.Desktop.dll"),
            _ => Valid().Replace("\"managedSha256\":\"" + Hash + "\"", "\"managedSha256\":\"bad\"")
        };
        var exception = Record.Exception(() => IpcDeploymentManifest.Parse(System.Text.Encoding.UTF8.GetBytes(payload), Root));
        Assert.True(exception is JsonException or InvalidDataException or ArgumentException);
    }

    [Fact]
    public void ManifestFromAnotherRootAndOversizedPolicyAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => IpcDeploymentManifest.Parse(System.Text.Encoding.UTF8.GetBytes(Valid()), Path.GetTempPath()));
        Assert.Throws<InvalidDataException>(() => IpcDeploymentManifest.Parse(new byte[IpcDeploymentManifest.MaximumBytes + 1], Root));
    }

    [Fact]
    public void ModifiedManagedEntryPointCannotUseTheApphostHashAlone()
    {
        var manifest = IpcDeploymentManifest.Parse(System.Text.Encoding.UTF8.GetBytes(Valid()), Root);
        var policy = new IpcAuthorizationPolicy(new(manifest.OwnerUserSid, "S-1-5-5-1-1", 1), Root, manifest.Binaries);
        var peer = new IpcPeerEvidence(42, manifest.OwnerUserSid, "S-1-5-5-1-1", 1,
            Path.Combine(Root, "FocusGuard.Desktop.exe"), Hash, true, true, true, new string('B', 64), true);
        Assert.False(policy.Authenticate(peer).Allowed);
        Assert.True(policy.Authenticate(peer with { ManagedSha256 = Hash }).Allowed);
    }
}
