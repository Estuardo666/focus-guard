using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FocusGuard.Devices;

namespace FocusGuard.Sync;

public interface ISettingsSyncCryptography
{
    DeviceAccountContext Account { get; }
    string DeviceId { get; }
    byte[] Prepare(SettingsWriteIntent intent);
    SettingsSnapshot Open(ReadOnlyMemory<byte> document);
    SettingsOutbox Confirm(SettingsOutbox pending, ReadOnlyMemory<byte> document);
}

/// <summary>Pins routing and key epoch. Author lookup must enforce WriteSettings in verified membership history.</summary>
public sealed class SettingsSyncCryptography : ISettingsSyncCryptography, IDisposable
{
    private readonly string entityId;
    private readonly long keyEpoch;
    private readonly long membershipRevision;
    private readonly DeviceIdentity author;
    private readonly byte[] key;
    private readonly Dictionary<long, byte[]> historicalKeys = new();
    private readonly Func<long, string, DevicePublicIdentity> verifiedAuthors;
    private readonly object sync = new();
    private bool disposed;
    public DeviceAccountContext Account { get; }
    public string DeviceId => author.Public.DeviceId;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public SettingsSyncCryptography(DeviceAccountContext account, string entityId, long keyEpoch,
        long membershipRevision, DeviceIdentity author, ReadOnlySpan<byte> contentKey,
        Func<long, string, DevicePublicIdentity> verifiedAuthors, IReadOnlyDictionary<long, byte[]>? historicalContentKeys = null)
    {
        ArgumentNullException.ThrowIfNull(account); account.Validate();
        ArgumentNullException.ThrowIfNull(author); ArgumentNullException.ThrowIfNull(verifiedAuthors);
        new ContentDocumentContext(account.AccountId, account.GenerationId, entityId, GlobalSettings.DocumentKind,
            0, 1, keyEpoch, membershipRevision).Validate();
        if (contentKey.Length != 32) throw new ArgumentException("Invalid account content key.");
        if (historicalContentKeys is not null && (historicalContentKeys.Count > 64 || historicalContentKeys.Any(entry =>
            entry.Key < 1 || entry.Key >= keyEpoch || entry.Value is null || entry.Value.Length != 32)))
            throw new ArgumentException("Invalid historical content keys.");
        Account = account; this.entityId = entityId; this.keyEpoch = keyEpoch;
        this.membershipRevision = membershipRevision; this.author = author;
        this.verifiedAuthors = verifiedAuthors; key = contentKey.ToArray();
        if (historicalContentKeys is not null)
            foreach (var entry in historicalContentKeys) historicalKeys.Add(entry.Key, entry.Value.ToArray());
    }

    public byte[] Prepare(SettingsWriteIntent intent)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (verifiedAuthors(membershipRevision, DeviceId) != author.Public)
                throw new CryptographicException("Local author is not authorized by current membership.");
            ObjectDisposedException.ThrowIf(disposed, this);
            return SettingsEnvelopeCodec.Prepare(intent, new(Account.AccountId, Account.GenerationId, entityId,
                GlobalSettings.DocumentKind, intent.BaseRevision, intent.ProposedRevision, keyEpoch, membershipRevision), key, author);
        }
    }

    public SettingsSnapshot Open(ReadOnlyMemory<byte> document)
    {
        lock (sync)
        {
            var (header, context) = Inspect(document);
            var verified = verifiedAuthors(header.MembershipRevision, header.AuthorDeviceId);
            ObjectDisposedException.ThrowIf(disposed, this);
            return SettingsEnvelopeCodec.Open(document, context, KeyFor(context.KeyEpoch), verified);
        }
    }

    public SettingsOutbox Confirm(SettingsOutbox pending, ReadOnlyMemory<byte> document)
    {
        lock (sync)
        {
            var (header, context) = Inspect(document);
            if (pending.InFlight is null || header.AuthorDeviceId != DeviceId ||
                context.BaseRevision != pending.InFlight.Intent.BaseRevision ||
                context.ProposedRevision != pending.InFlight.Intent.ProposedRevision) return pending;
            var verified = verifiedAuthors(header.MembershipRevision, header.AuthorDeviceId);
            ObjectDisposedException.ThrowIf(disposed, this);
            return SettingsEnvelopeCodec.ConfirmOwnWrite(pending, document, context, KeyFor(context.KeyEpoch), verified);
        }
    }

    private (EncryptedContentDocument Header, ContentDocumentContext Context) Inspect(ReadOnlyMemory<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (bytes.Length is < 2 or > 65_536) throw new JsonException("Invalid settings envelope size.");
        var normalized = ContentDocumentCodec.CanonicalizeJson(bytes.Span);
        CryptographicOperations.ZeroMemory(normalized);
        var header = JsonSerializer.Deserialize<EncryptedContentDocument>(bytes.Span, Json)
            ?? throw new JsonException("Missing document.");
        if (header.AccountId != Account.AccountId || header.GenerationId != Account.GenerationId ||
            header.EntityId != entityId || header.Kind != GlobalSettings.DocumentKind || header.KeyEpoch < 1 || header.KeyEpoch > keyEpoch ||
            header.MembershipRevision < 1 || header.MembershipRevision > membershipRevision)
            throw new CryptographicException("Settings routing or membership context rejected.");
        var context = new ContentDocumentContext(Account.AccountId, Account.GenerationId, entityId,
            GlobalSettings.DocumentKind, header.BaseRevision, header.ProposedRevision, header.KeyEpoch, header.MembershipRevision);
        context.Validate(); return (header, context);
    }

    private byte[] KeyFor(long epoch) => epoch == keyEpoch ? key : historicalKeys.TryGetValue(epoch, out var historical)
        ? historical : throw new CryptographicException("Historical content key is unavailable.");
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            CryptographicOperations.ZeroMemory(key);
            foreach (var historical in historicalKeys.Values) CryptographicOperations.ZeroMemory(historical);
            historicalKeys.Clear(); disposed = true;
        }
    }
}
