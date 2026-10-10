using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusGuard.Ipc.Windows;

public sealed record IpcServiceManifest(string ServiceName, string FileName, string Sha256,
    string ManagedFileName, string ManagedSha256);
public sealed record IpcDeploymentManifest(int SchemaVersion, string OwnerUserSid, string InstallRoot,
    IpcServiceManifest Service, IReadOnlyList<TrustedIpcBinary> Binaries, bool ClientMetadataQueryConsent = false)
{
    public const int MaximumBytes = 65536;
    public const string FileName = "ipc-policy.json";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<IpcClientRole>(allowIntegerValues: false) },
        MaxDepth = 8
    };

    public static IpcDeploymentManifest Parse(ReadOnlySpan<byte> payload, string expectedInstallRoot)
    {
        if (payload.Length is 0 or > MaximumBytes) throw new InvalidDataException("IPC policy size denied.");
        using var document = JsonDocument.Parse(payload.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
        EnsureUniqueProperties(document.RootElement);
        var manifest = JsonSerializer.Deserialize<IpcDeploymentManifest>(payload, Options)
            ?? throw new InvalidDataException("IPC policy missing.");
        if (manifest.SchemaVersion != 1 || manifest.Service is null || manifest.Binaries is null
            || !string.Equals(manifest.Service.ServiceName, "FocusGuard", StringComparison.Ordinal)
            || manifest.Service.FileName != "FocusGuard.Service.exe"
            || manifest.Service.ManagedFileName != "FocusGuard.Service.dll"
            || !Hash(manifest.Service.Sha256) || !Hash(manifest.Service.ManagedSha256)
            || !Path.IsPathFullyQualified(manifest.InstallRoot)
            || !string.Equals(IpcDirectoryPath.Normalize(manifest.InstallRoot), IpcDirectoryPath.Normalize(expectedInstallRoot), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("IPC service deployment denied.");
        manifest = manifest with { InstallRoot = IpcDirectoryPath.Normalize(manifest.InstallRoot) };
        // Re-use owner/binary validation without persisting a boot-specific logon scope.
        _ = new IpcAuthorizationPolicy(new(manifest.OwnerUserSid, "S-1-5-5-1-1", 1), manifest.InstallRoot, manifest.Binaries);
        foreach (var binary in manifest.Binaries)
        {
            if (binary.ManagedFileName != Path.GetFileNameWithoutExtension(binary.FileName) + ".dll"
                || !Hash(binary.ManagedSha256)) throw new InvalidDataException("Managed image proof missing.");
        }
        return manifest;
    }

    [SupportedOSPlatform("windows")]
    public static IpcDeploymentManifest ReadProtected(string installRoot)
    {
        ProtectedIpcDeployment.EnsureProtectedAncestors(installRoot);
        ProtectedIpcDeployment.EnsureProtectedRuntimeFiles(installRoot);
        var path = Path.Combine(installRoot, FileName);
        ProtectedIpcDeployment.EnsureProtectedFile(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is 0 or > MaximumBytes) throw new InvalidDataException("IPC policy size denied.");
        var data = new byte[(int)file.Length];
        file.ReadExactly(data);
        return Parse(data, installRoot);
    }

    private static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static void EnsureUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate IPC policy property.");
                EnsureUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) EnsureUniqueProperties(item);
    }
}
