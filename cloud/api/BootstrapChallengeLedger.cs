using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using FocusGuard.Devices;
namespace FocusGuard.Cloud.Api;

public sealed record BootstrapChallengeResponse(string ChallengeId, string AccountId, string GenerationId,
    string DeviceId, string Nonce, DateTimeOffset ExpiresAt, string Purpose);
/// <summary>Atomic first-device bootstrap only. Pairing and operational sessions remain separate flows.</summary>
public sealed partial class BootstrapChallengeLedger(NpgsqlDataSource database)
{
    private const int MaximumMembershipHistoryLength = 256;

    public async Task<DeviceAccountContextResponse> GetDeviceAccountContextAsync(string issuer, string subject,
        CancellationToken cancellationToken)
    {
        ValidateCaller(issuer, subject);
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT account_id,generation_id,membership_revision
            FROM focusguard_device.accounts WHERE subject_hash=$1
            """, connection);
        command.Parameters.AddWithValue(SubjectHash(issuer, subject));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return new(false, null, null, null);
        return new(true, reader.GetGuid(0).ToString("D"), reader.GetGuid(1).ToString("D"), reader.GetInt64(2));
    }

    public async Task<MembershipSnapshotResponse> GetCurrentMembershipAsync(string issuer, string subject,
        string accessToken, string requestTarget, string deviceId, string deviceSession, string requestId,
        long timestampUnixMilliseconds, string proofSignature, CancellationToken cancellationToken)
    {
        ValidateCaller(issuer, subject);
        DeviceEncoding.RequireId(deviceId);
        DeviceEncoding.RequireId(requestId);
        if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestampUnixMilliseconds) > 60_000 ||
            string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 16384)
            throw new InvalidDeviceProofException();
        var sessionBytes = DeviceEncoding.Decode(deviceSession, 32);
        var sessionHash = SHA256.HashData(sessionBytes);
        CryptographicOperations.ZeroMemory(sessionBytes);

        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var subjectHash = SubjectHash(issuer, subject);
            await using var accountQuery = new NpgsqlCommand("""
                SELECT account_id,generation_id,membership_revision,membership_digest,membership,key_envelopes
                FROM focusguard_device.accounts WHERE subject_hash=$1 FOR SHARE
                """, connection, transaction);
            accountQuery.Parameters.AddWithValue(subjectHash);
            await using var reader = await accountQuery.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidBootstrapProofException();
            var accountId = reader.GetGuid(0);
            var generationId = reader.GetGuid(1);
            var revision = reader.GetInt64(2);
            var digestBytes = (byte[])reader[3];
            var membershipJson = reader.GetString(4);
            var envelopesJson = reader.GetString(5);
            await reader.DisposeAsync();

            var membership = JsonSerializer.Deserialize<MembershipDocument>(membershipJson)
                ?? throw new InvalidBootstrapProofException();
            var envelopes = JsonSerializer.Deserialize<SignedKeyEnvelope[]>(envelopesJson)
                ?? throw new InvalidBootstrapProofException();
            var digest = DeviceEncoding.Encode(digestBytes);
            if (!MembershipProtocol.VerifyMembership(membership) || membership.AccountId != accountId.ToString("D") ||
                membership.GenerationId != generationId.ToString("D") || membership.ProposedRevision != revision ||
                MembershipProtocol.Digest(membership) != digest)
                throw new InvalidBootstrapProofException();

            var member = membership.Members.SingleOrDefault(candidate => candidate.DeviceId == deviceId);
            if (member is null || member.Status != "Active" ||
                !member.Roles.Contains("Read", StringComparer.Ordinal))
                throw new InvalidDeviceProofException();

            var requestProof = new DeviceRequestProof("GET", requestTarget, accountId.ToString("D"),
                generationId.ToString("D"), deviceId, requestId, timestampUnixMilliseconds,
                TokenDigest(accessToken), DeviceEncoding.Encode(SHA256.HashData(ReadOnlySpan<byte>.Empty)));
            var canonicalProof = MembershipProtocol.CanonicalDeviceRequestProof(requestProof);
            try
            {
                if (!DeviceIdentity.Verify(new DevicePublicIdentity(member.DeviceId, member.SigningPublicKey,
                    member.EncryptionPublicKey), "FG-REQUEST-1", canonicalProof, proofSignature))
                    throw new InvalidDeviceProofException();
            }
            finally { CryptographicOperations.ZeroMemory(canonicalProof); }
            await RecordProofAsync(connection, transaction, accountId, Guid.ParseExact(deviceId, "D"),
                Guid.ParseExact(requestId, "D"), cancellationToken);

            await using var sessionQuery = new NpgsqlCommand("""
                SELECT membership_revision FROM focusguard_device.device_sessions
                WHERE session_hash=$1 AND account_id=$2 AND generation_id=$3 AND device_id=$4
                  AND revoked_at IS NULL AND expires_at>NOW() AND idle_until>NOW()
                FOR UPDATE
                """, connection, transaction);
            sessionQuery.Parameters.AddWithValue(sessionHash);
            sessionQuery.Parameters.AddWithValue(accountId);
            sessionQuery.Parameters.AddWithValue(generationId);
            sessionQuery.Parameters.AddWithValue(Guid.ParseExact(deviceId, "D"));
            var sessionRevision = await sessionQuery.ExecuteScalarAsync(cancellationToken);
            if (sessionRevision is not long activeRevision) throw new InvalidDeviceProofException();
            if (activeRevision != revision) throw new StaleDeviceSessionException();
            await using var touchSession = new NpgsqlCommand("""
                UPDATE focusguard_device.device_sessions
                SET idle_until=LEAST(expires_at,NOW()+INTERVAL '30 minutes')
                WHERE session_hash=$1 AND revoked_at IS NULL
                """, connection, transaction);
            touchSession.Parameters.AddWithValue(sessionHash);
            if (await touchSession.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidDeviceProofException();

            var membershipChain = await ReadMembershipChainAsync(connection, transaction, accountId,
                generationId, revision, digest, cancellationToken);
            if (membershipChain is null) throw new InvalidBootstrapProofException();
            await transaction.CommitAsync(cancellationToken);
            return new MembershipSnapshotResponse(membership, digest, envelopes)
            { MembershipChain = membershipChain };
        }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(cancellationToken);
            throw new InvalidDeviceProofException();
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(sessionHash); }
    }

    public async Task<bool> IsDeviceLinkedAsync(string issuer, string subject, CancellationToken cancellationToken)
    {
        ValidateCaller(issuer, subject);
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1 FROM focusguard_device.accounts WHERE subject_hash=$1", connection);
        command.Parameters.AddWithValue(SubjectHash(issuer, subject));
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public const string Schema = """
        CREATE SCHEMA IF NOT EXISTS focusguard_device;
        CREATE TABLE IF NOT EXISTS focusguard_device.accounts (
            account_id uuid PRIMARY KEY,
            issuer text NOT NULL CHECK (length(issuer) <= 2048),
            subject_hash char(64) NOT NULL UNIQUE,
            generation_id uuid NOT NULL,
            membership_revision bigint NOT NULL,
            membership_digest bytea NOT NULL CHECK (octet_length(membership_digest) = 32),
            membership jsonb NOT NULL,
            key_envelopes jsonb NOT NULL DEFAULT '[]'::jsonb,
            created_at timestamptz NOT NULL DEFAULT NOW(),
            UNIQUE (issuer, account_id)
        );
        DO $$
        DECLARE old_check record;
        BEGIN
            FOR old_check IN
                SELECT conname FROM pg_constraint
                WHERE conrelid='focusguard_device.accounts'::regclass AND contype='c'
                  AND pg_get_constraintdef(oid) ILIKE '%membership_revision = 1%'
            LOOP
                EXECUTE format('ALTER TABLE focusguard_device.accounts DROP CONSTRAINT %I', old_check.conname);
            END LOOP;
            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='focusguard_device.accounts'::regclass
                AND conname='accounts_membership_revision_positive') THEN
                ALTER TABLE focusguard_device.accounts ADD CONSTRAINT accounts_membership_revision_positive
                    CHECK (membership_revision >= 1);
            END IF;
        END $$;
        ALTER TABLE focusguard_device.accounts ADD COLUMN IF NOT EXISTS key_envelopes jsonb NOT NULL DEFAULT '[]'::jsonb;
        CREATE TABLE IF NOT EXISTS focusguard_device.membership_history (
            account_id uuid NOT NULL,
            generation_id uuid NOT NULL,
            membership_revision bigint NOT NULL CHECK (membership_revision >= 1),
            membership_digest bytea NOT NULL CHECK (octet_length(membership_digest)=32),
            membership jsonb NOT NULL,
            created_at timestamptz NOT NULL DEFAULT NOW(),
            PRIMARY KEY(account_id,membership_revision)
        );
        INSERT INTO focusguard_device.membership_history(account_id,generation_id,membership_revision,membership_digest,membership)
        SELECT account_id,generation_id,membership_revision,membership_digest,membership
        FROM focusguard_device.accounts ON CONFLICT(account_id,membership_revision) DO NOTHING;
        CREATE TABLE IF NOT EXISTS focusguard_device.challenges (
            id uuid PRIMARY KEY,
            subject_hash char(64) NOT NULL CHECK (length(subject_hash) = 64),
            issuer text NOT NULL CHECK (length(issuer) <= 2048),
            purpose text NOT NULL DEFAULT 'initial' CHECK (purpose IN ('initial','pairing')),
            request jsonb NOT NULL,
            nonce_hash bytea NOT NULL CHECK (octet_length(nonce_hash) = 32),
            expires_at timestamptz NOT NULL,
            attempts integer NOT NULL DEFAULT 0 CHECK (attempts BETWEEN 0 AND 5),
            consumed_at timestamptz NULL,
            created_at timestamptz NOT NULL DEFAULT NOW()
        );
        ALTER TABLE focusguard_device.challenges DROP COLUMN IF EXISTS subject;
        ALTER TABLE focusguard_device.challenges ADD COLUMN IF NOT EXISTS attempts integer NOT NULL DEFAULT 0;
        ALTER TABLE focusguard_device.challenges ADD COLUMN IF NOT EXISTS purpose text NOT NULL DEFAULT 'initial';
        CREATE INDEX IF NOT EXISTS bootstrap_challenges_subject_expiry
            ON focusguard_device.challenges(subject_hash, expires_at);
        CREATE TABLE IF NOT EXISTS focusguard_device.pairings (
            pairing_id uuid PRIMARY KEY,
            challenge_id uuid NOT NULL UNIQUE REFERENCES focusguard_device.challenges(id),
            subject_hash char(64) NOT NULL,
            account_id uuid NOT NULL REFERENCES focusguard_device.accounts(account_id),
            generation_id uuid NOT NULL,
            device_id uuid NOT NULL,
            signing_public_key text NOT NULL,
            encryption_public_key text NOT NULL,
            capabilities jsonb NOT NULL,
            state text NOT NULL CHECK (state IN ('Pending','Approved','Consumed','Cancelled','Expired')),
            expires_at timestamptz NOT NULL,
            membership jsonb NULL,
            key_envelopes jsonb NULL,
            created_at timestamptz NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS bootstrap_pairings_expiry ON focusguard_device.pairings(expires_at);
        CREATE TABLE IF NOT EXISTS focusguard_device.request_proofs (
            account_id uuid NOT NULL,
            device_id uuid NOT NULL,
            request_id uuid NOT NULL,
            expires_at timestamptz NOT NULL,
            PRIMARY KEY(account_id,device_id,request_id)
        );
        CREATE TABLE IF NOT EXISTS focusguard_device.device_sessions (
            session_hash bytea PRIMARY KEY CHECK (octet_length(session_hash)=32),
            account_id uuid NOT NULL,
            generation_id uuid NOT NULL,
            device_id uuid NOT NULL,
            membership_revision bigint NOT NULL,
            expires_at timestamptz NOT NULL,
            idle_until timestamptz NOT NULL,
            revoked_at timestamptz NULL,
            created_at timestamptz NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS device_sessions_account ON focusguard_device.device_sessions(account_id,device_id);
        """;

    public async Task<BootstrapChallengeResponse> CreateAsync(string issuer, string subject,
        BootstrapChallengeRequest input, CancellationToken cancellationToken)
    {
        ValidateCaller(issuer, subject);
        DeviceEncoding.RequireId(input.AccountId); DeviceEncoding.RequireId(input.GenerationId); DeviceEncoding.RequireId(input.DeviceId);
        _ = DeviceEncoding.Decode(input.SigningPublicKey, 32); _ = DeviceEncoding.Decode(input.EncryptionPublicKey, 32);
        var accountHash = SubjectHash(issuer, subject);
        var existingAccountId = Guid.Empty; var existingGenerationId = Guid.Empty;
        await using (var lookup = await database.OpenConnectionAsync(cancellationToken))
        await using (var existing = new NpgsqlCommand("SELECT account_id,generation_id FROM focusguard_device.accounts WHERE subject_hash=$1", lookup))
        {
            existing.Parameters.AddWithValue(accountHash);
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                existingAccountId = reader.GetGuid(0); existingGenerationId = reader.GetGuid(1);
            }
        }
        var purpose = existingAccountId == Guid.Empty ? "initial" : "pairing";
        if (purpose == "pairing" && (existingAccountId != Guid.ParseExact(input.AccountId, "D") ||
            existingGenerationId != Guid.ParseExact(input.GenerationId, "D"))) throw new BootstrapAlreadyExistsException();
        if (purpose == "initial" && (input.RecoverySigningPublicKey is null || input.RecoveryEncryptionPublicKey is null))
            throw new ArgumentException("Initial bootstrap requires recovery anchor keys.");
        if (input.RecoverySigningPublicKey is not null) _ = DeviceEncoding.Decode(input.RecoverySigningPublicKey, 32);
        if (input.RecoveryEncryptionPublicKey is not null) _ = DeviceEncoding.Decode(input.RecoveryEncryptionPublicKey, 32);
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using (var purge = new NpgsqlCommand("""
            DELETE FROM focusguard_device.challenges c
            WHERE c.expires_at <= NOW()
              AND NOT EXISTS (SELECT 1 FROM focusguard_device.pairings p WHERE p.challenge_id=c.id)
            """, connection))
            await purge.ExecuteNonQueryAsync(cancellationToken);
        var id = Guid.NewGuid(); var nonce = RandomNumberGenerator.GetBytes(32);
        var expires = DateTimeOffset.UtcNow.AddMinutes(5);
        var json = JsonSerializer.Serialize(input);
        await using var insert = new NpgsqlCommand("""
            INSERT INTO focusguard_device.challenges(id, subject_hash, issuer, request, nonce_hash, expires_at, purpose)
            VALUES ($1,$2,$3,$4::jsonb,$5,$6,$7)
            """, connection);
        insert.Parameters.AddWithValue(id); insert.Parameters.AddWithValue(accountHash); insert.Parameters.AddWithValue(issuer);
        insert.Parameters.AddWithValue(json); insert.Parameters.AddWithValue(SHA256.HashData(nonce)); insert.Parameters.AddWithValue(expires); insert.Parameters.AddWithValue(purpose);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        return new(id.ToString("D"), input.AccountId, input.GenerationId, input.DeviceId, DeviceEncoding.Encode(nonce), expires, purpose);
    }

    public async Task<BootstrapPairingResponse> RequestPairingAsync(string issuer, string subject,
        BootstrapPairingProof proof, string proofSignature, CancellationToken cancellationToken)
    {
        ValidateCaller(issuer, subject);
        var challenge = proof.Challenge;
        DeviceEncoding.RequireId(proof.PairingId); DeviceEncoding.RequireId(challenge.ChallengeId);
        if (proof.PairingId != challenge.ChallengeId) throw new InvalidBootstrapProofException();
        proof.Capabilities.Validate();
        var challengeId = proof.PairingId;
        var subjectHash = SubjectHash(issuer, subject); var id = Guid.ParseExact(challengeId, "D");
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using (var count = new NpgsqlCommand("""
            UPDATE focusguard_device.challenges SET attempts=attempts+1
            WHERE id=$1 AND subject_hash=$2 AND issuer=$3 AND purpose='pairing'
              AND consumed_at IS NULL AND expires_at>NOW() AND attempts<5
            """, connection))
        {
            count.Parameters.AddWithValue(id); count.Parameters.AddWithValue(subjectHash); count.Parameters.AddWithValue(issuer);
            if (await count.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidBootstrapProofException();
        }
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var readChallenge = new NpgsqlCommand("""
                SELECT request,nonce_hash,expires_at FROM focusguard_device.challenges
                WHERE id=$1 AND subject_hash=$2 AND issuer=$3 AND purpose='pairing'
                  AND consumed_at IS NULL AND expires_at>NOW() FOR UPDATE
                """, connection, transaction);
            readChallenge.Parameters.AddWithValue(id); readChallenge.Parameters.AddWithValue(subjectHash); readChallenge.Parameters.AddWithValue(issuer);
            await using var reader = await readChallenge.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidBootstrapProofException();
            var requestJson = reader.GetString(0); var nonceHash = (byte[])reader[1]; var expires = reader.GetFieldValue<DateTimeOffset>(2);
            await reader.DisposeAsync();
            var request = JsonSerializer.Deserialize<BootstrapChallengeRequest>(requestJson) ?? throw new InvalidBootstrapProofException();
            var account = await ReadAccountBinding(connection, transaction, subjectHash, cancellationToken);
            if (account is null || account.Value.AccountId != Guid.ParseExact(request.AccountId, "D") || account.Value.GenerationId != Guid.ParseExact(request.GenerationId, "D"))
                throw new InvalidBootstrapProofException();
            var nonce = DeviceEncoding.Decode(challenge.Nonce, 32);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(nonce), nonceHash) ||
                challenge.AccountId != request.AccountId || challenge.GenerationId != request.GenerationId || challenge.DeviceId != request.DeviceId)
                throw new InvalidBootstrapProofException();
            var proposedDevice = new DevicePublicIdentity(request.DeviceId, request.SigningPublicKey, request.EncryptionPublicKey);
            var canonicalProof = MembershipProtocol.CanonicalPairingProof(proof);
            try
            {
                if (!DeviceIdentity.Verify(proposedDevice, "FG-PAIRING-1", canonicalProof, proofSignature))
                    throw new InvalidBootstrapProofException();
            }
            finally { CryptographicOperations.ZeroMemory(canonicalProof); }
            await using var update = new NpgsqlCommand("UPDATE focusguard_device.challenges SET consumed_at=NOW() WHERE id=$1 AND consumed_at IS NULL", connection, transaction);
            update.Parameters.AddWithValue(id);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidBootstrapProofException();
            var capabilityJson = JsonSerializer.Serialize(proof.Capabilities);
            await using var insert = new NpgsqlCommand("""
                INSERT INTO focusguard_device.pairings(pairing_id,challenge_id,subject_hash,account_id,generation_id,
                    device_id,signing_public_key,encryption_public_key,capabilities,state,expires_at)
                VALUES ($1,$1,$2,$3,$4,$5,$6,$7,$8::jsonb,'Pending',$9)
                """, connection, transaction);
            insert.Parameters.AddWithValue(id); insert.Parameters.AddWithValue(subjectHash);
            insert.Parameters.AddWithValue(Guid.ParseExact(request.AccountId, "D"));
            insert.Parameters.AddWithValue(Guid.ParseExact(request.GenerationId, "D"));
            insert.Parameters.AddWithValue(Guid.ParseExact(request.DeviceId, "D"));
            insert.Parameters.AddWithValue(request.SigningPublicKey); insert.Parameters.AddWithValue(request.EncryptionPublicKey);
            insert.Parameters.AddWithValue(capabilityJson); insert.Parameters.AddWithValue(expires);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(challengeId, request.DeviceId, expires, "Pending");
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            await transaction.RollbackAsync(cancellationToken); throw new InvalidBootstrapProofException();
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<PendingPairingSummary>> ListPendingPairingsAsync(string issuer, string subject,
        CancellationToken cancellationToken)
    {
        ValidateCaller(issuer, subject);
        var subjectHash = SubjectHash(issuer, subject);
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT pairing_id,device_id,signing_public_key,encryption_public_key,capabilities,expires_at
            FROM focusguard_device.pairings
            WHERE subject_hash=$1 AND state='Pending' AND expires_at>NOW()
            ORDER BY created_at ASC LIMIT 10
            """, connection);
        command.Parameters.AddWithValue(subjectHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<PendingPairingSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var deviceId = reader.GetGuid(1).ToString("D");
            var signingKey = reader.GetString(2); var encryptionKey = reader.GetString(3);
            var capabilities = JsonSerializer.Deserialize<DeviceCapabilities>(reader.GetString(4)) ?? throw new InvalidDataException("Pairing capabilities missing.");
            var identity = new DevicePublicIdentity(deviceId, signingKey, encryptionKey);
            identity.Validate(); capabilities.Validate();
            result.Add(new(reader.GetGuid(0).ToString("D"), deviceId, signingKey, encryptionKey,
                capabilities, identity.Fingerprint, reader.GetFieldValue<DateTimeOffset>(5)));
        }
        return result;
    }

    public async Task<PairingApprovalResult> ApprovePairingAsync(string issuer, string subject, string accessToken,
        string requestTarget, string approverDeviceId, string requestId, long timestampUnixMilliseconds,
        string proofSignature, PairingApprovalRequest body, CancellationToken cancellationToken)
    {
        ValidateCaller(issuer, subject); DeviceEncoding.RequireId(body.PairingId);
        DeviceEncoding.RequireId(approverDeviceId); DeviceEncoding.RequireId(requestId);
        DeviceEncoding.RequireId(body.Membership.AccountId); DeviceEncoding.RequireId(body.Membership.GenerationId);
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (Math.Abs(nowMs - timestampUnixMilliseconds) > 60_000 || string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 16384)
            throw new InvalidDeviceProofException();
        var accountHash = SubjectHash(issuer, subject); var pairingId = Guid.ParseExact(body.PairingId, "D");
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var accountCommand = new NpgsqlCommand("""
                SELECT account_id,generation_id,membership_revision,membership_digest,membership
                FROM focusguard_device.accounts WHERE subject_hash=$1 FOR UPDATE
                """, connection, transaction);
            accountCommand.Parameters.AddWithValue(accountHash);
            await using var accountReader = await accountCommand.ExecuteReaderAsync(cancellationToken);
            if (!await accountReader.ReadAsync(cancellationToken)) throw new InvalidBootstrapProofException();
            var accountId = accountReader.GetGuid(0); var generationId = accountReader.GetGuid(1);
            var revision = accountReader.GetInt64(2); var previousDigest = (byte[])accountReader[3];
            var currentJson = accountReader.GetString(4);
            await accountReader.DisposeAsync();
            var current = JsonSerializer.Deserialize<MembershipDocument>(currentJson) ?? throw new InvalidBootstrapProofException();
            if (!MembershipProtocol.VerifyMembership(current) || current.AccountId != accountId.ToString("D") ||
                current.GenerationId != generationId.ToString("D") || current.ProposedRevision != revision ||
                MembershipProtocol.Digest(current) != DeviceEncoding.Encode(previousDigest))
                throw new InvalidBootstrapProofException();
            var approver = current.Members.SingleOrDefault(member => member.DeviceId == approverDeviceId);
            if (approver is null || approver.Status != "Active" || !approver.Roles.Contains("ManageDevices", StringComparer.Ordinal))
                throw new InvalidBootstrapProofException();

            await using var pairingCommand = new NpgsqlCommand("""
                SELECT device_id,generation_id,signing_public_key,encryption_public_key,capabilities,expires_at
                FROM focusguard_device.pairings
                WHERE pairing_id=$1 AND subject_hash=$2 AND account_id=$3 AND state='Pending' AND expires_at>NOW()
                FOR UPDATE
                """, connection, transaction);
            pairingCommand.Parameters.AddWithValue(pairingId); pairingCommand.Parameters.AddWithValue(accountHash); pairingCommand.Parameters.AddWithValue(accountId);
            await using var pairingReader = await pairingCommand.ExecuteReaderAsync(cancellationToken);
            if (!await pairingReader.ReadAsync(cancellationToken)) throw new InvalidBootstrapProofException();
            var requestedDevice = pairingReader.GetGuid(0).ToString("D");
            var requestedGeneration = pairingReader.GetGuid(1).ToString("D");
            var requestedSigningKey = pairingReader.GetString(2); var requestedEncryptionKey = pairingReader.GetString(3);
            await pairingReader.DisposeAsync();
            if (requestedGeneration != generationId.ToString("D")) throw new InvalidBootstrapProofException();

            var bearerDigest = DeviceEncoding.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)));
            var canonicalBody = MembershipProtocol.CanonicalPairingApprovalBody(body);
            var requestProof = new DeviceRequestProof("POST", requestTarget, accountId.ToString("D"), generationId.ToString("D"),
                approverDeviceId, requestId, timestampUnixMilliseconds, bearerDigest,
                DeviceEncoding.Encode(SHA256.HashData(canonicalBody)));
            var proofBytes = MembershipProtocol.CanonicalDeviceRequestProof(requestProof);
            try
            {
                if (!DeviceIdentity.Verify(new DevicePublicIdentity(approver.DeviceId, approver.SigningPublicKey, approver.EncryptionPublicKey),
                    "FG-REQUEST-1", proofBytes, proofSignature)) throw new InvalidDeviceProofException();
            }
            finally { CryptographicOperations.ZeroMemory(canonicalBody); CryptographicOperations.ZeroMemory(proofBytes); }
            await using (var replay = new NpgsqlCommand("""
                INSERT INTO focusguard_device.request_proofs(account_id,device_id,request_id,expires_at)
                VALUES ($1,$2,$3,NOW()+INTERVAL '5 minutes')
                """, connection, transaction))
            {
                replay.Parameters.AddWithValue(accountId); replay.Parameters.AddWithValue(Guid.ParseExact(approverDeviceId, "D"));
                replay.Parameters.AddWithValue(Guid.ParseExact(requestId, "D"));
                try { await replay.ExecuteNonQueryAsync(cancellationToken); }
                catch (PostgresException ex) when (ex.SqlState == "23505") { throw new InvalidDeviceProofException(); }
            }

            var next = body.Membership;
            if (!MembershipProtocol.VerifyMembership(next) || next.AccountId != accountId.ToString("D") ||
                next.GenerationId != generationId.ToString("D") || next.BaseRevision != revision || next.ProposedRevision != revision + 1 ||
                next.KeyEpoch != current.KeyEpoch || next.PreviousDigest != DeviceEncoding.Encode(previousDigest) ||
                next.SignerDeviceId != approverDeviceId || next.RecoverySigningPublicKey != current.RecoverySigningPublicKey ||
                next.RecoveryEncryptionPublicKey != current.RecoveryEncryptionPublicKey || next.Members.Length != current.Members.Length + 1)
                throw new InvalidBootstrapProofException();
            var added = next.Members.SingleOrDefault(member => member.DeviceId == requestedDevice);
            if (added is null || added.Status != "Active" || added.SigningPublicKey != requestedSigningKey ||
                added.EncryptionPublicKey != requestedEncryptionKey || added.Roles.Any(role => role == "StartStrict"))
                throw new InvalidBootstrapProofException();
            foreach (var existing in current.Members)
            {
                var retained = next.Members.SingleOrDefault(member => member.DeviceId == existing.DeviceId);
                if (retained is null || retained.DeviceId == added.DeviceId || retained.Status != existing.Status ||
                    retained.SigningPublicKey != existing.SigningPublicKey || retained.EncryptionPublicKey != existing.EncryptionPublicKey ||
                    !retained.Roles.Order(StringComparer.Ordinal).SequenceEqual(existing.Roles.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                    throw new InvalidBootstrapProofException();
            }

            if (body.KeyEnvelopes is null || body.KeyEnvelopes.Length != next.Members.Count(member => member.Status == "Active") + 1 ||
                body.KeyEnvelopes.Select(envelope => envelope.RecipientId).Distinct(StringComparer.Ordinal).Count() != body.KeyEnvelopes.Length)
                throw new InvalidBootstrapProofException();
            var nextDigest = MembershipProtocol.Digest(next); var signerKey = new DevicePublicIdentity(approver.DeviceId,
                approver.SigningPublicKey, approver.EncryptionPublicKey);
            var recipients = next.Members.Where(member => member.Status == "Active")
                .Select(member => (Id: member.DeviceId, Key: member.EncryptionPublicKey)).ToList();
            recipients.Add((accountId.ToString("D"), next.RecoveryEncryptionPublicKey));
            foreach (var recipient in recipients)
            {
                var envelope = body.KeyEnvelopes.FirstOrDefault(item => item.RecipientId == recipient.Id);
                if (envelope is null || !SignedKeyEnvelopeCodec.VerifySignedHeader(envelope,
                    new KeyEnvelopeContext(next.AccountId, next.GenerationId, next.KeyEpoch, recipient.Id, nextDigest, approverDeviceId),
                    signerKey, recipient.Id, recipient.Key)) throw new InvalidBootstrapProofException();
            }

            var membershipJson = JsonSerializer.Serialize(next);
            var envelopesJson = JsonSerializer.Serialize(body.KeyEnvelopes);
            await using var update = new NpgsqlCommand("""
                UPDATE focusguard_device.accounts
                SET membership_revision=$1,membership_digest=$2,membership=$3::jsonb,key_envelopes=$4::jsonb
                WHERE account_id=$5 AND membership_revision=$6 AND membership_digest=$7
                """, connection, transaction);
            update.Parameters.AddWithValue(next.ProposedRevision); update.Parameters.AddWithValue(DeviceEncoding.Decode(nextDigest, 32));
            update.Parameters.AddWithValue(membershipJson); update.Parameters.AddWithValue(envelopesJson);
            update.Parameters.AddWithValue(accountId); update.Parameters.AddWithValue(revision); update.Parameters.AddWithValue(previousDigest);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1) throw new MembershipConflictException();
            await InsertMembershipHistoryAsync(connection, transaction, accountId, generationId,
                next.ProposedRevision, nextDigest, membershipJson, cancellationToken);
            await using var approve = new NpgsqlCommand("""
                UPDATE focusguard_device.pairings SET state='Approved',membership=$1::jsonb,key_envelopes=$2::jsonb
                WHERE pairing_id=$3 AND state='Pending' AND expires_at>NOW()
                """, connection, transaction);
            approve.Parameters.AddWithValue(membershipJson); approve.Parameters.AddWithValue(envelopesJson); approve.Parameters.AddWithValue(pairingId);
            if (await approve.ExecuteNonQueryAsync(cancellationToken) != 1) throw new MembershipConflictException();
            var activeDeviceIds = next.Members.Where(member => member.Status == "Active")
                .Select(member => Guid.ParseExact(member.DeviceId, "D")).ToArray();
            await using var advanceSessions = new NpgsqlCommand("""
                UPDATE focusguard_device.device_sessions
                SET membership_revision=$1
                WHERE account_id=$2 AND device_id=ANY($3) AND revoked_at IS NULL
                """, connection, transaction);
            advanceSessions.Parameters.AddWithValue(next.ProposedRevision);
            advanceSessions.Parameters.AddWithValue(accountId);
            advanceSessions.Parameters.AddWithValue(activeDeviceIds);
            await advanceSessions.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(pairingId.ToString("D"), "Approved", next.ProposedRevision, nextDigest);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(cancellationToken);
            throw new InvalidDeviceProofException();
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<DeviceRevocationResult> RevokeDeviceAsync(string issuer, string subject, string accessToken,
        string requestTarget, string approverDeviceId, string requestId, long timestampUnixMilliseconds,
        string proofSignature, RevokeDeviceRequest body, CancellationToken cancellationToken)
    {
        ValidateCaller(issuer, subject);
        DeviceEncoding.RequireId(body.DeviceId);
        DeviceEncoding.RequireId(approverDeviceId);
        DeviceEncoding.RequireId(requestId);
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (Math.Abs(nowMs - timestampUnixMilliseconds) > 60_000 || string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 16384)
            throw new InvalidDeviceProofException();

        var accountHash = SubjectHash(issuer, subject);
        var revokedDeviceId = body.DeviceId;
        var revokedDeviceGuid = Guid.ParseExact(revokedDeviceId, "D");
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var accountCommand = new NpgsqlCommand("""
                SELECT account_id,generation_id,membership_revision,membership_digest,membership
                FROM focusguard_device.accounts WHERE subject_hash=$1 FOR UPDATE
                """, connection, transaction);
            accountCommand.Parameters.AddWithValue(accountHash);
            await using var accountReader = await accountCommand.ExecuteReaderAsync(cancellationToken);
            if (!await accountReader.ReadAsync(cancellationToken)) throw new InvalidBootstrapProofException();
            var accountId = accountReader.GetGuid(0);
            var generationId = accountReader.GetGuid(1);
            var revision = accountReader.GetInt64(2);
            var previousDigest = (byte[])accountReader[3];
            var currentJson = accountReader.GetString(4);
            await accountReader.DisposeAsync();

            var current = JsonSerializer.Deserialize<MembershipDocument>(currentJson)
                ?? throw new InvalidBootstrapProofException();
            var currentDigest = DeviceEncoding.Encode(previousDigest);
            if (!MembershipProtocol.VerifyMembership(current) || current.AccountId != accountId.ToString("D") ||
                current.GenerationId != generationId.ToString("D") || current.ProposedRevision != revision ||
                MembershipProtocol.Digest(current) != currentDigest)
                throw new InvalidBootstrapProofException();

            var approver = current.Members.SingleOrDefault(member => member.DeviceId == approverDeviceId);
            var target = current.Members.SingleOrDefault(member => member.DeviceId == revokedDeviceId);
            if (approver is null || approver.Status != "Active" || !approver.Roles.Contains("ManageDevices", StringComparer.Ordinal) ||
                target is null || target.Status != "Active" || target.DeviceId == approverDeviceId)
                throw new InvalidBootstrapProofException();

            var canonicalBody = MembershipProtocol.CanonicalRevokeDeviceBody(body);
            var requestProof = new DeviceRequestProof("POST", requestTarget, accountId.ToString("D"),
                generationId.ToString("D"), approverDeviceId, requestId, timestampUnixMilliseconds,
                TokenDigest(accessToken), DeviceEncoding.Encode(SHA256.HashData(canonicalBody)));
            var proofBytes = MembershipProtocol.CanonicalDeviceRequestProof(requestProof);
            try
            {
                if (!DeviceIdentity.Verify(new DevicePublicIdentity(approver.DeviceId, approver.SigningPublicKey,
                    approver.EncryptionPublicKey), "FG-REQUEST-1", proofBytes, proofSignature))
                    throw new InvalidDeviceProofException();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(canonicalBody);
                CryptographicOperations.ZeroMemory(proofBytes);
            }
            await RecordProofAsync(connection, transaction, accountId, Guid.ParseExact(approverDeviceId, "D"),
                Guid.ParseExact(requestId, "D"), cancellationToken);

            var next = body.Membership;
            if (!MembershipProtocol.VerifyMembership(next) || next.AccountId != accountId.ToString("D") ||
                next.GenerationId != generationId.ToString("D") || next.BaseRevision != revision ||
                next.ProposedRevision != revision + 1 || next.KeyEpoch != current.KeyEpoch + 1 ||
                next.PreviousDigest != currentDigest || next.SignerDeviceId != approverDeviceId ||
                next.RecoverySigningPublicKey != current.RecoverySigningPublicKey ||
                next.RecoveryEncryptionPublicKey != current.RecoveryEncryptionPublicKey ||
                next.Members.Length != current.Members.Length)
                throw new InvalidBootstrapProofException();

            foreach (var existing in current.Members)
            {
                var updated = next.Members.SingleOrDefault(member => member.DeviceId == existing.DeviceId);
                if (updated is null || updated.SigningPublicKey != existing.SigningPublicKey ||
                    updated.EncryptionPublicKey != existing.EncryptionPublicKey ||
                    !updated.Roles.Order(StringComparer.Ordinal).SequenceEqual(existing.Roles.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                    throw new InvalidBootstrapProofException();
                var expectedStatus = existing.DeviceId == revokedDeviceId ? "Revoked" : existing.Status;
                if (updated.Status != expectedStatus) throw new InvalidBootstrapProofException();
            }
            if (!next.Members.Any(member => member.Status == "Active" &&
                member.Roles.Contains("ManageDevices", StringComparer.Ordinal)))
                throw new InvalidBootstrapProofException();

            var nextDigest = MembershipProtocol.Digest(next);
            var signer = new DevicePublicIdentity(approver.DeviceId, approver.SigningPublicKey, approver.EncryptionPublicKey);
            var activeRecipients = next.Members.Where(member => member.Status == "Active")
                .Select(member => (Id: member.DeviceId, Key: member.EncryptionPublicKey)).ToList();
            activeRecipients.Add((accountId.ToString("D"), next.RecoveryEncryptionPublicKey));
            if (body.KeyEnvelopes is null || body.KeyEnvelopes.Length != activeRecipients.Count ||
                body.KeyEnvelopes.Select(envelope => envelope.RecipientId).Distinct(StringComparer.Ordinal).Count() != body.KeyEnvelopes.Length)
                throw new InvalidBootstrapProofException();
            foreach (var recipient in activeRecipients)
            {
                var envelope = body.KeyEnvelopes.SingleOrDefault(item => item.RecipientId == recipient.Id);
                if (envelope is null || !SignedKeyEnvelopeCodec.VerifySignedHeader(envelope,
                    new KeyEnvelopeContext(next.AccountId, next.GenerationId, next.KeyEpoch, recipient.Id, nextDigest, approverDeviceId),
                    signer, recipient.Id, recipient.Key))
                    throw new InvalidBootstrapProofException();
            }

            var membershipJson = JsonSerializer.Serialize(next);
            var envelopesJson = JsonSerializer.Serialize(body.KeyEnvelopes);
            await using var update = new NpgsqlCommand("""
                UPDATE focusguard_device.accounts
                SET membership_revision=$1,membership_digest=$2,membership=$3::jsonb,key_envelopes=$4::jsonb
                WHERE account_id=$5 AND membership_revision=$6 AND membership_digest=$7
                """, connection, transaction);
            update.Parameters.AddWithValue(next.ProposedRevision);
            update.Parameters.AddWithValue(DeviceEncoding.Decode(nextDigest, 32));
            update.Parameters.AddWithValue(membershipJson);
            update.Parameters.AddWithValue(envelopesJson);
            update.Parameters.AddWithValue(accountId);
            update.Parameters.AddWithValue(revision);
            update.Parameters.AddWithValue(previousDigest);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1) throw new MembershipConflictException();
            await InsertMembershipHistoryAsync(connection, transaction, accountId, generationId,
                next.ProposedRevision, nextDigest, membershipJson, cancellationToken);

            await using var revokeSessions = new NpgsqlCommand("""
                UPDATE focusguard_device.device_sessions SET revoked_at=NOW()
                WHERE account_id=$1 AND device_id=$2 AND revoked_at IS NULL
                """, connection, transaction);
            revokeSessions.Parameters.AddWithValue(accountId);
            revokeSessions.Parameters.AddWithValue(revokedDeviceGuid);
            await revokeSessions.ExecuteNonQueryAsync(cancellationToken);

            var remainingDeviceIds = next.Members.Where(member => member.Status == "Active")
                .Select(member => Guid.ParseExact(member.DeviceId, "D")).ToArray();
            await using var advanceRemainingSessions = new NpgsqlCommand("""
                UPDATE focusguard_device.device_sessions
                SET membership_revision=$1
                WHERE account_id=$2 AND device_id=ANY($3) AND revoked_at IS NULL
                """, connection, transaction);
            advanceRemainingSessions.Parameters.AddWithValue(next.ProposedRevision);
            advanceRemainingSessions.Parameters.AddWithValue(accountId);
            advanceRemainingSessions.Parameters.AddWithValue(remainingDeviceIds);
            await advanceRemainingSessions.ExecuteNonQueryAsync(cancellationToken);

            await using var cancelPairings = new NpgsqlCommand("""
                UPDATE focusguard_device.pairings SET state='Cancelled'
                WHERE account_id=$1 AND device_id=$2 AND state='Pending'
                """, connection, transaction);
            cancelPairings.Parameters.AddWithValue(accountId);
            cancelPairings.Parameters.AddWithValue(revokedDeviceGuid);
            await cancelPairings.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new("Revoked", revokedDeviceId, next.ProposedRevision, nextDigest);
        }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(cancellationToken);
            throw new InvalidDeviceProofException();
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<PairingStateResponse> ReadOwnPairingAsync(string issuer, string subject, string accessToken,
        string requestTarget, string requestId, long timestampUnixMilliseconds, string proofSignature,
        string pairingId, CancellationToken cancellationToken)
    {
        ValidateCaller(issuer, subject); DeviceEncoding.RequireId(pairingId); DeviceEncoding.RequireId(requestId);
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (Math.Abs(nowMs - timestampUnixMilliseconds) > 60_000 || string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 16384)
            throw new InvalidDeviceProofException();
        var subjectHash = SubjectHash(issuer, subject); var id = Guid.ParseExact(pairingId, "D");
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var query = new NpgsqlCommand("""
                SELECT p.account_id,p.generation_id,p.device_id,p.signing_public_key,p.encryption_public_key,
                    p.state,p.expires_at,p.membership,p.key_envelopes,a.membership_digest,
                    a.membership_revision,a.membership,a.key_envelopes
                FROM focusguard_device.pairings p JOIN focusguard_device.accounts a ON a.account_id=p.account_id
                WHERE p.pairing_id=$1 AND p.subject_hash=$2
                FOR UPDATE OF p
                """, connection, transaction);
            query.Parameters.AddWithValue(id); query.Parameters.AddWithValue(subjectHash);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidBootstrapProofException();
            var accountId = reader.GetGuid(0).ToString("D"); var generationId = reader.GetGuid(1).ToString("D");
            var deviceId = reader.GetGuid(2).ToString("D"); var signingKey = reader.GetString(3); var encryptionKey = reader.GetString(4);
            var state = reader.GetString(5); var expiresAt = reader.GetFieldValue<DateTimeOffset>(6);
            var membershipJson = reader.IsDBNull(7) ? null : reader.GetString(7);
            var envelopesJson = reader.IsDBNull(8) ? null : reader.GetString(8);
            var currentDigest = (byte[])reader[9];
            var currentRevision = reader.GetInt64(10);
            var currentMembershipJson = reader.GetString(11);
            var currentEnvelopesJson = reader.GetString(12);
            await reader.DisposeAsync();
            if (DateTimeOffset.UtcNow >= expiresAt && state == "Pending") state = "Expired";
            var digest = membershipJson is null ? "" : MembershipProtocol.Digest(
                JsonSerializer.Deserialize<MembershipDocument>(membershipJson) ?? throw new InvalidBootstrapProofException());
            if (state == "Approved" && digest != DeviceEncoding.Encode(currentDigest)) throw new InvalidBootstrapProofException();
            var currentMembership = JsonSerializer.Deserialize<MembershipDocument>(currentMembershipJson)
                ?? throw new InvalidBootstrapProofException();
            var currentMembershipDigest = DeviceEncoding.Encode(currentDigest);
            if (!MembershipProtocol.VerifyMembership(currentMembership) || currentMembership.AccountId != accountId ||
                currentMembership.GenerationId != generationId || currentMembership.ProposedRevision != currentRevision ||
                MembershipProtocol.Digest(currentMembership) != currentMembershipDigest)
                throw new InvalidBootstrapProofException();
            var currentEnvelopes = JsonSerializer.Deserialize<SignedKeyEnvelope[]>(currentEnvelopesJson)
                ?? throw new InvalidBootstrapProofException();
            var proof = new DeviceRequestProof("GET", requestTarget, accountId, generationId, deviceId, requestId,
                timestampUnixMilliseconds, TokenDigest(accessToken), DeviceEncoding.Encode(SHA256.HashData(ReadOnlySpan<byte>.Empty)));
            var signed = MembershipProtocol.CanonicalDeviceRequestProof(proof);
            try
            {
                var requester = new DevicePublicIdentity(deviceId, signingKey, encryptionKey);
                if (!DeviceIdentity.Verify(requester, "FG-PENDING-REQUEST-1", signed, proofSignature))
                    throw new InvalidDeviceProofException();
            }
            finally { CryptographicOperations.ZeroMemory(signed); }
            await RecordProofAsync(connection, transaction, Guid.ParseExact(accountId, "D"), Guid.ParseExact(deviceId, "D"),
                Guid.ParseExact(requestId, "D"), cancellationToken);
            var parsedMembership = (state is "Approved" or "Consumed") && membershipJson is not null
                ? JsonSerializer.Deserialize<MembershipDocument>(membershipJson) : null;
            var parsedEnvelopes = (state is "Approved" or "Consumed") && envelopesJson is not null
                ? JsonSerializer.Deserialize<SignedKeyEnvelope[]>(envelopesJson) : null;
            var membershipChain = parsedMembership is null ? null : await ReadMembershipChainAsync(connection,
                transaction, Guid.ParseExact(accountId, "D"), Guid.ParseExact(generationId, "D"),
                parsedMembership.ProposedRevision, digest, cancellationToken);
            var includeCurrentMembership = state is "Approved" or "Consumed";
            var currentMembershipChain = includeCurrentMembership ? await ReadMembershipChainAsync(connection,
                transaction, Guid.ParseExact(accountId, "D"), Guid.ParseExact(generationId, "D"),
                currentRevision, currentMembershipDigest, cancellationToken) : null;
            var fingerprint = new DevicePublicIdentity(deviceId, signingKey, encryptionKey).Fingerprint;
            await transaction.CommitAsync(cancellationToken);
            return new PairingStateResponse(pairingId, deviceId, state, expiresAt,
                fingerprint, parsedMembership is null ? "" : digest, parsedMembership, parsedEnvelopes)
            {
                MembershipChain = membershipChain ?? [],
                CurrentMembership = includeCurrentMembership ? currentMembership : null,
                CurrentMembershipDigest = includeCurrentMembership ? currentMembershipDigest : "",
                CurrentKeyEnvelopes = includeCurrentMembership ? currentEnvelopes : [],
                CurrentMembershipChain = currentMembershipChain ?? []
            };
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(cancellationToken);
            throw new InvalidDeviceProofException();
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ConfirmPairingResult> ConfirmPairingAsync(string issuer, string subject, string accessToken,
        string requestTarget, string requestId, long timestampUnixMilliseconds, string proofSignature,
        ConfirmPairingRequest body, CancellationToken cancellationToken)
    {
        ValidateCaller(issuer, subject); DeviceEncoding.RequireId(body.PairingId); DeviceEncoding.RequireId(requestId);
        var sessionBytes = DeviceEncoding.Decode(body.DeviceSession, 32);
        var sessionHash = SHA256.HashData(sessionBytes);
        CryptographicOperations.ZeroMemory(sessionBytes);
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (Math.Abs(nowMs - timestampUnixMilliseconds) > 60_000 || string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 16384)
            throw new InvalidDeviceProofException();
        var subjectHash = SubjectHash(issuer, subject); var pairingId = Guid.ParseExact(body.PairingId, "D");
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var query = new NpgsqlCommand("""
                SELECT p.account_id,p.generation_id,p.device_id,p.signing_public_key,p.encryption_public_key,
                    p.state,p.expires_at,p.membership,a.membership_revision,a.membership_digest
                FROM focusguard_device.pairings p JOIN focusguard_device.accounts a ON a.account_id=p.account_id
                WHERE p.pairing_id=$1 AND p.subject_hash=$2
                FOR UPDATE OF p,a
                """, connection, transaction);
            query.Parameters.AddWithValue(pairingId); query.Parameters.AddWithValue(subjectHash);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidBootstrapProofException();
            var accountId = reader.GetGuid(0).ToString("D"); var generationId = reader.GetGuid(1).ToString("D");
            var deviceId = reader.GetGuid(2).ToString("D"); var signingKey = reader.GetString(3); var encryptionKey = reader.GetString(4);
            var state = reader.GetString(5);
            var membershipJson = reader.IsDBNull(7) ? null : reader.GetString(7);
            var membershipRevision = reader.GetInt64(8); var membershipDigest = (byte[])reader[9];
            await reader.DisposeAsync();
            var digest = DeviceEncoding.Encode(membershipDigest);
            if (state is not ("Approved" or "Consumed") || membershipJson is null || body.MembershipDigest != digest)
                throw new InvalidBootstrapProofException();
            var membership = JsonSerializer.Deserialize<MembershipDocument>(membershipJson) ?? throw new InvalidBootstrapProofException();
            var activeMember = membership.Members.SingleOrDefault(member => member.DeviceId == deviceId);
            if (membershipRevision != membership.ProposedRevision || activeMember?.Status != "Active")
                throw new InvalidBootstrapProofException();
            var canonicalBody = MembershipProtocol.CanonicalConfirmPairingBody(body);
            var requestProof = new DeviceRequestProof("POST", requestTarget, accountId, generationId, deviceId, requestId,
                timestampUnixMilliseconds, TokenDigest(accessToken), DeviceEncoding.Encode(SHA256.HashData(canonicalBody)));
            var signed = MembershipProtocol.CanonicalDeviceRequestProof(requestProof);
            try
            {
                if (!DeviceIdentity.Verify(new DevicePublicIdentity(deviceId, signingKey, encryptionKey),
                    "FG-PAIRING-CONFIRM-1", signed, proofSignature)) throw new InvalidDeviceProofException();
            }
            finally { CryptographicOperations.ZeroMemory(canonicalBody); CryptographicOperations.ZeroMemory(signed); }
            await RecordProofAsync(connection, transaction, Guid.ParseExact(accountId, "D"), Guid.ParseExact(deviceId, "D"),
                Guid.ParseExact(requestId, "D"), cancellationToken);
            if (state == "Consumed")
            {
                await using var existingSession = new NpgsqlCommand("""
                    SELECT expires_at FROM focusguard_device.device_sessions
                    WHERE session_hash=$1 AND account_id=$2 AND generation_id=$3 AND device_id=$4
                      AND membership_revision=$5 AND revoked_at IS NULL AND expires_at>NOW() AND idle_until>NOW()
                    FOR UPDATE
                    """, connection, transaction);
                existingSession.Parameters.AddWithValue(sessionHash);
                existingSession.Parameters.AddWithValue(Guid.ParseExact(accountId, "D"));
                existingSession.Parameters.AddWithValue(Guid.ParseExact(generationId, "D"));
                existingSession.Parameters.AddWithValue(Guid.ParseExact(deviceId, "D"));
                existingSession.Parameters.AddWithValue(membershipRevision);
                var existingExpiry = await existingSession.ExecuteScalarAsync(cancellationToken);
                if (existingExpiry is not DateTimeOffset activeExpiry) throw new InvalidBootstrapProofException();
                await transaction.CommitAsync(cancellationToken);
                CryptographicOperations.ZeroMemory(sessionHash);
                return new(body.PairingId, "Consumed", activeExpiry);
            }
            // Pending approval was already time-checked by ApprovePairingAsync.
            // Receipt creates no new membership grant; permit recovery only for
            // the unchanged signed membership and the still-active requester.
            var sessionExpiry = DateTimeOffset.UtcNow.AddHours(8);
            await using var addSession = new NpgsqlCommand("""
                INSERT INTO focusguard_device.device_sessions(session_hash,account_id,generation_id,device_id,membership_revision,expires_at,idle_until)
                VALUES ($1,$2,$3,$4,$5,$6,NOW()+INTERVAL '30 minutes')
                """, connection, transaction);
            addSession.Parameters.AddWithValue(sessionHash); addSession.Parameters.AddWithValue(Guid.ParseExact(accountId, "D"));
            addSession.Parameters.AddWithValue(Guid.ParseExact(generationId, "D")); addSession.Parameters.AddWithValue(Guid.ParseExact(deviceId, "D"));
            addSession.Parameters.AddWithValue(membershipRevision); addSession.Parameters.AddWithValue(sessionExpiry);
            await addSession.ExecuteNonQueryAsync(cancellationToken);
            await using var consume = new NpgsqlCommand("UPDATE focusguard_device.pairings SET state='Consumed' WHERE pairing_id=$1 AND state='Approved'", connection, transaction);
            consume.Parameters.AddWithValue(pairingId);
            if (await consume.ExecuteNonQueryAsync(cancellationToken) != 1) throw new MembershipConflictException();
            await transaction.CommitAsync(cancellationToken);
            CryptographicOperations.ZeroMemory(sessionHash);
            return new(body.PairingId, "Consumed", sessionExpiry);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(cancellationToken);
            CryptographicOperations.ZeroMemory(sessionHash);
            throw new InvalidDeviceProofException();
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(cancellationToken);
            CryptographicOperations.ZeroMemory(sessionHash);
            throw;
        }
    }

    public async Task<string> RegisterFirstDeviceAsync(string issuer, string subject, string challengeId,
        BootstrapChallengeProof proof, string proofSignature, string deviceSession, MembershipDocument membership,
        SignedKeyEnvelope[] keyEnvelopes,
        CancellationToken cancellationToken)
    {
        ValidateCaller(issuer, subject); DeviceEncoding.RequireId(challengeId);
        if (proof is null || proof.ChallengeId != challengeId || !MembershipProtocol.VerifyMembership(membership))
            throw new InvalidBootstrapProofException();
        var sessionBytes = DeviceEncoding.Decode(deviceSession, 32);
        var sessionHash = SHA256.HashData(sessionBytes);
        CryptographicOperations.ZeroMemory(sessionBytes);
        var accountHash = SubjectHash(issuer, subject);
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        // Attempts commit independently so invalid proofs cannot roll back the
        // counter. A challenge permits at most five possession proofs.
        await using (var countAttempt = new NpgsqlCommand("""
            UPDATE focusguard_device.challenges SET attempts=attempts+1
            WHERE id=$1 AND subject_hash=$2 AND issuer=$3
              AND consumed_at IS NULL AND expires_at > NOW() AND attempts < 5
            """, connection))
        {
            countAttempt.Parameters.AddWithValue(Guid.ParseExact(challengeId, "D")); countAttempt.Parameters.AddWithValue(accountHash);
            countAttempt.Parameters.AddWithValue(issuer);
            if (await countAttempt.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidBootstrapProofException();
        }
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var select = new NpgsqlCommand("""
                SELECT request, nonce_hash FROM focusguard_device.challenges
                WHERE id=$1 AND subject_hash=$2 AND issuer=$3 AND purpose='initial'
                  AND consumed_at IS NULL AND expires_at > NOW() AND attempts BETWEEN 1 AND 5 FOR UPDATE
                """, connection, transaction);
            select.Parameters.AddWithValue(Guid.ParseExact(challengeId, "D")); select.Parameters.AddWithValue(accountHash);
            select.Parameters.AddWithValue(issuer);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidBootstrapProofException();
            var requestJson = reader.GetString(0);
            var nonceHash = (byte[])reader[1];
            await reader.DisposeAsync();
            var request = JsonSerializer.Deserialize<BootstrapChallengeRequest>(requestJson)
                ?? throw new InvalidBootstrapProofException();
            var nonce = DeviceEncoding.Decode(proof.Nonce, 32);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(nonce), nonceHash) ||
                proof.AccountId != request.AccountId || proof.GenerationId != request.GenerationId || proof.DeviceId != request.DeviceId ||
                membership.AccountId != request.AccountId || membership.GenerationId != request.GenerationId ||
                membership.SignerDeviceId != request.DeviceId || membership.RecoverySigningPublicKey != request.RecoverySigningPublicKey ||
                membership.RecoveryEncryptionPublicKey != request.RecoveryEncryptionPublicKey ||
                membership.Members[0].SigningPublicKey != request.SigningPublicKey ||
                membership.Members[0].EncryptionPublicKey != request.EncryptionPublicKey ||
                !MembershipProtocol.VerifyChallengeProof(proof, proofSignature,
                    new DevicePublicIdentity(request.DeviceId, request.SigningPublicKey, request.EncryptionPublicKey)))
                throw new InvalidBootstrapProofException();

            if (keyEnvelopes is null || keyEnvelopes.Length != 2)
                throw new InvalidBootstrapProofException();
            var membershipDigest = MembershipProtocol.Digest(membership);
            var signer = new DevicePublicIdentity(membership.SignerDeviceId,
                membership.Members[0].SigningPublicKey, membership.Members[0].EncryptionPublicKey);
            var deviceEnvelope = keyEnvelopes.SingleOrDefault(e => e.RecipientId == request.DeviceId);
            var recoveryEnvelope = keyEnvelopes.SingleOrDefault(e => e.RecipientId == membership.AccountId);
            if (deviceEnvelope is null || recoveryEnvelope is null ||
                !SignedKeyEnvelopeCodec.VerifySignedHeader(deviceEnvelope,
                    new KeyEnvelopeContext(membership.AccountId, membership.GenerationId, 1, request.DeviceId, membershipDigest, request.DeviceId),
                    signer, request.DeviceId, request.EncryptionPublicKey) ||
                !SignedKeyEnvelopeCodec.VerifySignedHeader(recoveryEnvelope,
                    new KeyEnvelopeContext(membership.AccountId, membership.GenerationId, 1, membership.AccountId, membershipDigest, request.DeviceId),
                    signer, membership.AccountId, membership.RecoveryEncryptionPublicKey))
                throw new InvalidBootstrapProofException();

            var membershipJson = JsonSerializer.Serialize(membership);
            var envelopesJson = JsonSerializer.Serialize(keyEnvelopes);
            var digest = DeviceEncoding.Decode(MembershipProtocol.Digest(membership), 32);
            await using var addAccount = new NpgsqlCommand("""
                INSERT INTO focusguard_device.accounts(account_id, issuer, subject_hash, generation_id,
                    membership_revision, membership_digest, membership, key_envelopes)
                VALUES ($1,$2,$3,$4,1,$5,$6::jsonb,$7::jsonb)
                """, connection, transaction);
            addAccount.Parameters.AddWithValue(Guid.ParseExact(membership.AccountId, "D"));
            addAccount.Parameters.AddWithValue(issuer); addAccount.Parameters.AddWithValue(accountHash);
            addAccount.Parameters.AddWithValue(Guid.ParseExact(membership.GenerationId, "D"));
            addAccount.Parameters.AddWithValue(digest); addAccount.Parameters.AddWithValue(membershipJson); addAccount.Parameters.AddWithValue(envelopesJson);
            await addAccount.ExecuteNonQueryAsync(cancellationToken);
            await InsertMembershipHistoryAsync(connection, transaction, Guid.ParseExact(membership.AccountId, "D"),
                Guid.ParseExact(membership.GenerationId, "D"), membership.ProposedRevision,
                MembershipProtocol.Digest(membership), membershipJson, cancellationToken);
            await using var addSession = new NpgsqlCommand("""
                INSERT INTO focusguard_device.device_sessions(session_hash,account_id,generation_id,device_id,
                    membership_revision,expires_at,idle_until)
                VALUES ($1,$2,$3,$4,1,NOW()+INTERVAL '8 hours',NOW()+INTERVAL '30 minutes')
                """, connection, transaction);
            addSession.Parameters.AddWithValue(sessionHash);
            addSession.Parameters.AddWithValue(Guid.ParseExact(membership.AccountId, "D"));
            addSession.Parameters.AddWithValue(Guid.ParseExact(membership.GenerationId, "D"));
            addSession.Parameters.AddWithValue(Guid.ParseExact(membership.Members[0].DeviceId, "D"));
            await addSession.ExecuteNonQueryAsync(cancellationToken);
            await using var consume = new NpgsqlCommand("UPDATE focusguard_device.challenges SET consumed_at=NOW() WHERE id=$1 AND consumed_at IS NULL", connection, transaction);
            consume.Parameters.AddWithValue(Guid.ParseExact(challengeId, "D"));
            if (await consume.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidBootstrapProofException();
            await transaction.CommitAsync(cancellationToken);
            CryptographicOperations.ZeroMemory(sessionHash);
            return membership.AccountId;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            await transaction.RollbackAsync(cancellationToken);
            CryptographicOperations.ZeroMemory(sessionHash);
            throw new BootstrapAlreadyExistsException();
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(cancellationToken);
            CryptographicOperations.ZeroMemory(sessionHash);
            throw;
        }
    }

    private static string SubjectHash(string issuer, string subject) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(issuer + "\0" + subject)));

    private static async Task<(Guid AccountId, Guid GenerationId)?> ReadAccountBinding(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string subjectHash, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT account_id,generation_id FROM focusguard_device.accounts
            WHERE subject_hash=$1 FOR UPDATE
            """, connection, transaction);
        command.Parameters.AddWithValue(subjectHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return (reader.GetGuid(0), reader.GetGuid(1));
    }

    private static string TokenDigest(string accessToken) =>
        DeviceEncoding.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)));

    private static async Task RecordProofAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid accountId, Guid deviceId, Guid requestId, CancellationToken cancellationToken)
    {
        await using var purge = new NpgsqlCommand("DELETE FROM focusguard_device.request_proofs WHERE expires_at<=NOW()", connection, transaction);
        await purge.ExecuteNonQueryAsync(cancellationToken);
        await using var insert = new NpgsqlCommand("""
            INSERT INTO focusguard_device.request_proofs(account_id,device_id,request_id,expires_at)
            VALUES ($1,$2,$3,NOW()+INTERVAL '5 minutes')
            """, connection, transaction);
        insert.Parameters.AddWithValue(accountId); insert.Parameters.AddWithValue(deviceId); insert.Parameters.AddWithValue(requestId);
        try { await insert.ExecuteNonQueryAsync(cancellationToken); }
        catch (PostgresException ex) when (ex.SqlState == "23505") { throw new InvalidDeviceProofException(); }
    }

    private static async Task InsertMembershipHistoryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid accountId, Guid generationId, long revision, string digest, string membershipJson,
        CancellationToken cancellationToken)
    {
        await using var insert = new NpgsqlCommand("""
            INSERT INTO focusguard_device.membership_history(account_id,generation_id,membership_revision,membership_digest,membership)
            VALUES ($1,$2,$3,$4,$5::jsonb)
            """, connection, transaction);
        insert.Parameters.AddWithValue(accountId);
        insert.Parameters.AddWithValue(generationId);
        insert.Parameters.AddWithValue(revision);
        insert.Parameters.AddWithValue(DeviceEncoding.Decode(digest, 32));
        insert.Parameters.AddWithValue(membershipJson);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<MembershipDocument[]?> ReadMembershipChainAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid accountId, Guid generationId, long expectedRevision,
        string expectedDigest, CancellationToken cancellationToken)
    {
        if (expectedRevision is < 1 or > MaximumMembershipHistoryLength) return null;
        var accountText = accountId.ToString("D");
        var generationText = generationId.ToString("D");
        var documents = new List<MembershipDocument>((int)expectedRevision);
        string? previousDigest = null;
        await using var query = new NpgsqlCommand("""
            SELECT generation_id,membership_revision,membership_digest,membership
            FROM focusguard_device.membership_history
            WHERE account_id=$1 AND membership_revision <= $2
            ORDER BY membership_revision ASC
            LIMIT $3
            """, connection, transaction);
        query.Parameters.AddWithValue(accountId);
        query.Parameters.AddWithValue(expectedRevision);
        query.Parameters.AddWithValue(expectedRevision + 1);
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var rowRevision = reader.GetInt64(1);
            if (documents.Count >= MaximumMembershipHistoryLength || rowRevision != documents.Count + 1L ||
                reader.GetGuid(0) != generationId)
                return null;
            var storedDigest = DeviceEncoding.Encode((byte[])reader[2]);
            MembershipDocument? document;
            try { document = JsonSerializer.Deserialize<MembershipDocument>(reader.GetString(3)); }
            catch (JsonException) { return null; }
            if (document is null || document.AccountId != accountText || document.GenerationId != generationText ||
                document.ProposedRevision != rowRevision || !MembershipProtocol.VerifyMembership(document))
                return null;
            string digest;
            try { digest = MembershipProtocol.Digest(document); }
            catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidDataException)
            { return null; }
            if (digest != storedDigest || (previousDigest is not null &&
                    !MembershipChainLinks(documents[^1], previousDigest, document)))
                return null;
            if (previousDigest is null && (document.BaseRevision != 0 || document.ProposedRevision != 1))
                return null;
            documents.Add(document);
            previousDigest = digest;
        }
        if (documents.Count != expectedRevision || previousDigest != expectedDigest) return null;
        return documents.ToArray();
    }

    private static bool MembershipChainLinks(MembershipDocument previous, string previousDigest,
        MembershipDocument current)
    {
        if (current.BaseRevision != previous.ProposedRevision || current.ProposedRevision != previous.ProposedRevision + 1 ||
            current.PreviousDigest != previousDigest || current.KeyEpoch < previous.KeyEpoch)
            return false;
        var previousSigner = previous.Members.SingleOrDefault(member => member.DeviceId == current.SignerDeviceId);
        var currentSigner = current.Members.SingleOrDefault(member => member.DeviceId == current.SignerDeviceId);
        if (previousSigner?.Status != "Active" || !previousSigner.Roles.Contains("ManageDevices", StringComparer.Ordinal) ||
            currentSigner?.Status != "Active" || !currentSigner.Roles.Contains("ManageDevices", StringComparer.Ordinal) ||
            previousSigner.SigningPublicKey != currentSigner.SigningPublicKey ||
            previousSigner.EncryptionPublicKey != currentSigner.EncryptionPublicKey)
            return false;
        foreach (var member in previous.Members)
        {
            var retained = current.Members.SingleOrDefault(candidate => candidate.DeviceId == member.DeviceId);
            if (retained is not null && (retained.SigningPublicKey != member.SigningPublicKey ||
                retained.EncryptionPublicKey != member.EncryptionPublicKey))
                return false;
        }
        return true;
    }

    private static void ValidateCaller(string issuer, string subject)
    {
        var localIdentityFixture = Uri.TryCreate(issuer, UriKind.Absolute, out var uri) &&
            uri.Scheme == "http" && uri.Host == "127.0.0.1" && uri.Port == 18443;
        if (string.IsNullOrWhiteSpace(issuer) || !Uri.TryCreate(issuer, UriKind.Absolute, out uri) ||
            uri.Scheme != "https" && !localIdentityFixture)
            throw new ArgumentException("HTTPS identity issuer required.");
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 255 || subject.Any(char.IsControl))
            throw new ArgumentException("Opaque identity subject required.");
    }
}

public sealed class BootstrapAlreadyExistsException : Exception;
public sealed class InvalidBootstrapProofException : Exception;
public sealed class InvalidDeviceProofException : Exception;
public sealed class MembershipConflictException : Exception;
