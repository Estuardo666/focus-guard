using System.Security.Cryptography;
using System.Text.Json;
using FocusGuard.Devices;

namespace FocusGuard.Sync;

/// <summary>Caller owns the loaded vault material for the context lifetime. Never advances membership trust.</summary>
public static class SettingsSyncContextFactory
{
    public static SettingsSyncCryptography Create(DeviceAccountContext account, RestoredDeviceMaterial material,
        string entityId, MembershipDocument[] verifiedHistory)
    {
        ArgumentNullException.ThrowIfNull(account); ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(verifiedHistory);
        if (verifiedHistory.Length is < 1 or > 10_000) throw new CryptographicException("Invalid membership history size.");
        var history = JsonSerializer.Deserialize<MembershipDocument[]>(JsonSerializer.SerializeToUtf8Bytes(verifiedHistory))
            ?? throw new CryptographicException("Missing membership history.");
        var authors = new PinnedSettingsAuthor(history, account, material.MembershipRevision, material.MembershipDigest);
        var current = history[^1];
        var member = current.Members.SingleOrDefault(entry => entry.DeviceId == material.Identity.Public.DeviceId);
        if (current.KeyEpoch != material.KeyEpoch || member?.Status != "Active" ||
            !member.Roles.Contains("Read", StringComparer.Ordinal) ||
            member.SigningPublicKey != material.Identity.Public.SigningPublicKey ||
            member.EncryptionPublicKey != material.Identity.Public.EncryptionPublicKey || material.DeviceSession is null)
            throw new CryptographicException("Current vault device cannot read synchronized settings.");
        var historical = new Dictionary<long, byte[]>();
        try
        {
            foreach (var epoch in history.Select(entry => entry.KeyEpoch).Where(epoch => epoch < material.KeyEpoch).Distinct())
            {
                try { historical.Add(epoch, material.GetContentKey(epoch).ToArray()); }
                catch (KeyNotFoundException) { } // Missing historical keys remain unavailable; never substitute current key.
            }
            return new(account, entityId, material.KeyEpoch, material.MembershipRevision, material.Identity,
                material.GetContentKey(material.KeyEpoch).Span, authors.Resolve, historical);
        }
        finally { foreach (var key in historical.Values) CryptographicOperations.ZeroMemory(key); }
    }
}
