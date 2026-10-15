using System.Security.Cryptography;
using System.Text.Json;
using FocusGuard.Devices;
using Npgsql;

namespace FocusGuard.Cloud.Api;

public sealed record RenewDeviceSessionResult(DateTimeOffset ExpiresAtUtc, MembershipSnapshotResponse Snapshot);

public sealed partial class BootstrapChallengeLedger
{
    public async Task<RenewDeviceSessionResult> RenewDeviceSessionAsync(string issuer, string subject,
        string accessToken, string requestTarget, string requestId, long timestampUnixMilliseconds,
        string signature, RenewDeviceSessionRequest body, CancellationToken cancellationToken)
    {
        ValidateCaller(issuer, subject); DeviceEncoding.RequireId(requestId);
        if (requestTarget != "/v1/bootstrap/sessions/renew" ||
            Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestampUnixMilliseconds) > 60_000 ||
            string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 16384)
            throw new InvalidDeviceProofException();
        var bodyBytes = MembershipProtocol.CanonicalRenewDeviceSessionBody(body);
        string bodyDigest;
        try { bodyDigest = DeviceEncoding.Encode(SHA256.HashData(bodyBytes)); }
        finally { CryptographicOperations.ZeroMemory(bodyBytes); }
        var sessionBytes = DeviceEncoding.Decode(body.DeviceSession, 32);
        var sessionHash = SHA256.HashData(sessionBytes);
        CryptographicOperations.ZeroMemory(sessionBytes);
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var accountQuery = new NpgsqlCommand("""
                SELECT account_id,generation_id,membership_revision,membership_digest,membership,key_envelopes
                FROM focusguard_device.accounts WHERE subject_hash=$1 FOR UPDATE
                """, connection, transaction);
            accountQuery.Parameters.AddWithValue(SubjectHash(issuer, subject));
            await using var reader = await accountQuery.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidBootstrapProofException();
            var accountId = reader.GetGuid(0); var generationId = reader.GetGuid(1); var revision = reader.GetInt64(2);
            var digest = DeviceEncoding.Encode((byte[])reader[3]);
            var membership = JsonSerializer.Deserialize<MembershipDocument>(reader.GetString(4))
                ?? throw new InvalidBootstrapProofException();
            var envelopes = JsonSerializer.Deserialize<SignedKeyEnvelope[]>(reader.GetString(5))
                ?? throw new InvalidBootstrapProofException();
            await reader.DisposeAsync();
            if (body.AccountId != accountId.ToString("D") || body.GenerationId != generationId.ToString("D"))
                throw new InvalidDeviceProofException();
            if (!MembershipProtocol.VerifyMembership(membership) || membership.AccountId != body.AccountId ||
                membership.GenerationId != body.GenerationId || membership.ProposedRevision != revision ||
                MembershipProtocol.Digest(membership) != digest) throw new InvalidBootstrapProofException();
            var member = membership.Members.SingleOrDefault(item => item.DeviceId == body.DeviceId);
            if (member?.Status != "Active" || !member.Roles.Contains("Read", StringComparer.Ordinal))
                throw new InvalidDeviceProofException();
            var proof = new DeviceRequestProof("POST", requestTarget, body.AccountId, body.GenerationId,
                body.DeviceId, requestId, timestampUnixMilliseconds, TokenDigest(accessToken), bodyDigest);
            var proofBytes = MembershipProtocol.CanonicalDeviceRequestProof(proof);
            try
            {
                if (!DeviceIdentity.Verify(new DevicePublicIdentity(member.DeviceId, member.SigningPublicKey,
                    member.EncryptionPublicKey), "FG-DEVICE-SESSION-RENEW-1", proofBytes, signature))
                    throw new InvalidDeviceProofException();
            }
            finally { CryptographicOperations.ZeroMemory(proofBytes); }
            if (body.MembershipDigest != digest) throw new StaleDeviceSessionException();
            await RecordProofAsync(connection, transaction, accountId, Guid.ParseExact(body.DeviceId, "D"),
                Guid.ParseExact(requestId, "D"), cancellationToken);

            DateTimeOffset? existingExpiry = null;
            await using (var existing = new NpgsqlCommand("""
                SELECT account_id,generation_id,device_id,membership_revision,expires_at,idle_until,revoked_at
                FROM focusguard_device.device_sessions WHERE session_hash=$1 FOR UPDATE
                """, connection, transaction))
            {
                existing.Parameters.AddWithValue(sessionHash);
                await using var sessionReader = await existing.ExecuteReaderAsync(cancellationToken);
                if (await sessionReader.ReadAsync(cancellationToken))
                {
                    var expiry = sessionReader.GetFieldValue<DateTimeOffset>(4);
                    if (sessionReader.GetGuid(0) != accountId || sessionReader.GetGuid(1) != generationId ||
                        sessionReader.GetGuid(2) != Guid.ParseExact(body.DeviceId, "D") ||
                        sessionReader.GetInt64(3) != revision || !sessionReader.IsDBNull(6) ||
                        expiry <= DateTimeOffset.UtcNow || sessionReader.GetFieldValue<DateTimeOffset>(5) <= DateTimeOffset.UtcNow)
                        throw new InvalidDeviceProofException();
                    existingExpiry = expiry;
                }
            }
            var now = DateTimeOffset.UtcNow;
            var expires = existingExpiry ?? new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond,
                TimeSpan.Zero).AddHours(8);
            if (existingExpiry is null)
            {
                await using var revoke = new NpgsqlCommand("""
                    UPDATE focusguard_device.device_sessions SET revoked_at=NOW()
                    WHERE account_id=$1 AND generation_id=$2 AND device_id=$3 AND revoked_at IS NULL
                    """, connection, transaction);
                revoke.Parameters.AddWithValue(accountId); revoke.Parameters.AddWithValue(generationId);
                revoke.Parameters.AddWithValue(Guid.ParseExact(body.DeviceId, "D"));
                await revoke.ExecuteNonQueryAsync(cancellationToken);
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO focusguard_device.device_sessions(session_hash,account_id,generation_id,device_id,
                        membership_revision,expires_at,idle_until)
                    VALUES($1,$2,$3,$4,$5,$6,NOW()+INTERVAL '30 minutes')
                    """, connection, transaction);
                insert.Parameters.AddWithValue(sessionHash); insert.Parameters.AddWithValue(accountId);
                insert.Parameters.AddWithValue(generationId); insert.Parameters.AddWithValue(Guid.ParseExact(body.DeviceId, "D"));
                insert.Parameters.AddWithValue(revision); insert.Parameters.AddWithValue(expires);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            var membershipChain = await ReadMembershipChainAsync(connection, transaction, accountId,
                generationId, revision, digest, cancellationToken);
            if (membershipChain is null) throw new InvalidBootstrapProofException();
            await transaction.CommitAsync(cancellationToken);
            return new(expires, new MembershipSnapshotResponse(membership, digest, envelopes)
            { MembershipChain = membershipChain });
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(sessionHash); }
    }
}
