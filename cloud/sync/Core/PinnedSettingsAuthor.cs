using System.Security.Cryptography;
using System.Text.Json;
using FocusGuard.Devices;

namespace FocusGuard.Sync;

/// <summary>Resolve authors only from a snapshot matching a previously trusted local membership digest.
/// Does not establish trust in a new digest or verify a history transition.</summary>
public sealed class PinnedSettingsAuthor
{
    private readonly MembershipDocument membership;
    private readonly Dictionary<long, MembershipDocument> history = new();
    public PinnedSettingsAuthor(MembershipDocument snapshot, DeviceAccountContext account, long revision, string trustedDigest)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(account); account.Validate();
        membership = JsonSerializer.Deserialize<MembershipDocument>(JsonSerializer.SerializeToUtf8Bytes(snapshot))
            ?? throw new CryptographicException("Missing membership.");
        if (!MembershipProtocol.VerifyMembership(membership) || membership.AccountId != account.AccountId ||
            membership.GenerationId != account.GenerationId || membership.ProposedRevision != revision ||
            MembershipProtocol.Digest(membership) != trustedDigest)
            throw new CryptographicException("Membership does not match the trusted local anchor.");
        history.Add(revision, membership);
    }

    public PinnedSettingsAuthor(MembershipDocument[] chain, DeviceAccountContext account, long revision, string trustedDigest)
        : this(Last(chain), account, revision, trustedDigest)
    {
        if (chain.Length != revision || chain[0].BaseRevision != 0) throw new CryptographicException("Incomplete membership history.");
        var copies = JsonSerializer.Deserialize<MembershipDocument[]>(JsonSerializer.SerializeToUtf8Bytes(chain))
            ?? throw new CryptographicException("Missing membership history.");
        history.Clear(); MembershipDocument? previous = null;
        foreach (var entry in copies)
        {
            if (!MembershipProtocol.VerifyMembership(entry) || entry.AccountId != account.AccountId ||
                entry.GenerationId != account.GenerationId || entry.ProposedRevision != history.Count + 1L)
                throw new CryptographicException("Invalid membership history.");
            if (previous is not null)
            {
                var before = previous.Members.SingleOrDefault(member => member.DeviceId == entry.SignerDeviceId);
                var after = entry.Members.SingleOrDefault(member => member.DeviceId == entry.SignerDeviceId);
                if (entry.PreviousDigest != MembershipProtocol.Digest(previous) || entry.BaseRevision != previous.ProposedRevision ||
                    entry.KeyEpoch < previous.KeyEpoch || before?.Status != "Active" || after?.Status != "Active" ||
                    !before.Roles.Contains("ManageDevices", StringComparer.Ordinal) || !after.Roles.Contains("ManageDevices", StringComparer.Ordinal) ||
                    before.SigningPublicKey != after.SigningPublicKey || before.EncryptionPublicKey != after.EncryptionPublicKey)
                    throw new CryptographicException("Unauthorized membership transition.");
                foreach (var member in previous.Members)
                {
                    var retained = entry.Members.SingleOrDefault(candidate => candidate.DeviceId == member.DeviceId);
                    if (retained is not null && (retained.SigningPublicKey != member.SigningPublicKey || retained.EncryptionPublicKey != member.EncryptionPublicKey))
                        throw new CryptographicException("Retained device keys changed.");
                }
            }
            history.Add(entry.ProposedRevision, entry); previous = entry;
        }
        if (MembershipProtocol.Digest(copies[^1]) != trustedDigest) throw new CryptographicException("History anchor changed.");
    }

    private static MembershipDocument Last(MembershipDocument[] chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Length is < 1 or > 10_000 || chain.Any(entry => entry is null)) throw new CryptographicException("Invalid membership history size.");
        return chain[^1];
    }

    public DevicePublicIdentity Resolve(long revision, string deviceId)
    {
        if (!history.TryGetValue(revision, out var snapshot)) throw new CryptographicException("Historical author requires verified history.");
        var member = snapshot.Members.SingleOrDefault(member => member.DeviceId == deviceId);
        if (member is null || member.Status != "Active" || !member.Roles.Contains("WriteSettings", StringComparer.Ordinal))
            throw new CryptographicException("Author cannot write settings at this membership revision.");
        return new(member.DeviceId, member.SigningPublicKey, member.EncryptionPublicKey);
    }
}
