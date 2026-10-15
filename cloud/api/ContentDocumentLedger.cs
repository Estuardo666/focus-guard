using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FocusGuard.Devices;
using Npgsql;

namespace FocusGuard.Cloud.Api;

public sealed record EncryptedDocumentResult(long Revision, EncryptedContentDocument Document)
{
    public long Cursor { get; init; }
}
public sealed record EncryptedDocumentSnapshot(long Cursor, long Revision, EncryptedContentDocument? Document);
public sealed record EncryptedDocumentWriteResult(string State, long Revision, string EntityId)
{
    public long Cursor { get; init; }
}

/// <summary>Opaque encrypted-document CAS; the API validates membership/signatures but never decrypts content.</summary>
public sealed class ContentDocumentLedger(NpgsqlDataSource database)
{
    public const string Schema = """
        CREATE TABLE IF NOT EXISTS focusguard_device.documents (
            account_id uuid NOT NULL REFERENCES focusguard_device.accounts(account_id),
            entity_id uuid NOT NULL,
            generation_id uuid NOT NULL,
            revision bigint NOT NULL CHECK (revision >= 1),
            key_epoch bigint NOT NULL CHECK (key_epoch >= 1),
            author_device_id uuid NOT NULL,
            author_membership_revision bigint NOT NULL CHECK (author_membership_revision >= 1),
            document jsonb NOT NULL,
            updated_at timestamptz NOT NULL DEFAULT NOW(),
            PRIMARY KEY(account_id,entity_id)
        );
        CREATE INDEX IF NOT EXISTS device_documents_generation
            ON focusguard_device.documents(account_id,generation_id,updated_at);
        ALTER TABLE focusguard_device.documents ADD COLUMN IF NOT EXISTS author_membership_revision bigint NOT NULL DEFAULT 1;
        CREATE TABLE IF NOT EXISTS focusguard_device.sync_cursors (
            account_id uuid PRIMARY KEY REFERENCES focusguard_device.accounts(account_id) ON DELETE CASCADE,
            cursor bigint NOT NULL CHECK (cursor BETWEEN 0 AND 9007199254740991),
            updated_at timestamptz NOT NULL DEFAULT NOW()
        );
        INSERT INTO focusguard_device.sync_cursors(account_id,cursor)
        SELECT accounts.account_id,COALESCE(MAX(documents.revision),0)
        FROM focusguard_device.accounts accounts
        LEFT JOIN focusguard_device.documents documents ON documents.account_id=accounts.account_id
        GROUP BY accounts.account_id ON CONFLICT(account_id) DO NOTHING;
        """;

    private sealed record AccountState(Guid AccountId, Guid GenerationId, long Revision,
        byte[] Digest, MembershipDocument Membership);

    public async Task<EncryptedDocumentResult?> ReadAsync(string issuer, string subject, string accessToken,
        string requestTarget, string deviceId, string deviceSession, string requestId, long timestampUnixMilliseconds,
        string proofSignature, string entityId, CancellationToken cancellationToken)
    {
        var snapshot = await ReadSnapshotAsync(issuer, subject, accessToken, requestTarget, deviceId,
            deviceSession, requestId, timestampUnixMilliseconds, proofSignature, entityId, cancellationToken);
        return snapshot.Document is null ? null : new(snapshot.Revision, snapshot.Document) { Cursor = snapshot.Cursor };
    }

    public async Task<long> ReadAccountCursorAsync(string issuer, string subject, string accessToken,
        string requestTarget, string deviceId, string deviceSession, string requestId, long timestampUnixMilliseconds,
        string proofSignature, CancellationToken cancellationToken)
    {
        ValidateRequest(issuer, subject, accessToken, deviceId, requestId, timestampUnixMilliseconds);
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var account = await ReadAccountAsync(connection, transaction, SubjectHash(issuer, subject),
                lockForUpdate: false, cancellationToken);
            var member = RequireMember(account.Membership, deviceId, "Read");
            await RequireSessionAsync(connection, transaction, account, deviceId, deviceSession, cancellationToken);
            await VerifyAndRecordRequestAsync(connection, transaction, account, member, accessToken,
                "GET", requestTarget, requestId, timestampUnixMilliseconds, proofSignature,
                SHA256.HashData(ReadOnlySpan<byte>.Empty), cancellationToken);
            var cursor = await ReadCursorAsync(connection, transaction, account.AccountId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return cursor;
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

    public async Task<EncryptedDocumentSnapshot> ReadSnapshotAsync(string issuer, string subject, string accessToken,
        string requestTarget, string deviceId, string deviceSession, string requestId, long timestampUnixMilliseconds,
        string proofSignature, string entityId, CancellationToken cancellationToken)
    {
        ValidateRequest(issuer, subject, accessToken, deviceId, requestId, timestampUnixMilliseconds);
        DeviceEncoding.RequireId(entityId);
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var account = await ReadAccountAsync(connection, transaction, SubjectHash(issuer, subject),
                lockForUpdate: false, cancellationToken);
            var member = RequireMember(account.Membership, deviceId, "Read");
            await RequireSessionAsync(connection, transaction, account, deviceId, deviceSession, cancellationToken);
            await VerifyAndRecordRequestAsync(connection, transaction, account, member, accessToken,
                "GET", requestTarget, requestId, timestampUnixMilliseconds, proofSignature,
                SHA256.HashData(ReadOnlySpan<byte>.Empty), cancellationToken);
            var cursor = await ReadCursorAsync(connection, transaction, account.AccountId, cancellationToken);

            await using var query = new NpgsqlCommand("""
                SELECT generation_id,revision,key_epoch,author_device_id,author_membership_revision,document
                FROM focusguard_device.documents WHERE account_id=$1 AND entity_id=$2
                FOR SHARE
                """, connection, transaction);
            query.Parameters.AddWithValue(account.AccountId);
            query.Parameters.AddWithValue(Guid.ParseExact(entityId, "D"));
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.DisposeAsync();
                await transaction.CommitAsync(cancellationToken);
                return new(cursor, 0, null);
            }
            var generationId = reader.GetGuid(0).ToString("D");
            var revision = reader.GetInt64(1);
            var keyEpoch = reader.GetInt64(2);
            var authorDeviceId = reader.GetGuid(3).ToString("D");
            var authorMembershipRevision = reader.GetInt64(4);
            var documentJson = reader.GetString(5);
            await reader.DisposeAsync();
            if (generationId != account.GenerationId.ToString("D") || keyEpoch > account.Membership.KeyEpoch ||
                authorMembershipRevision > account.Revision)
                throw new DocumentKeyEpochConflictException();
            var document = JsonSerializer.Deserialize<EncryptedContentDocument>(documentJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("Stored encrypted document is invalid.");
            if (document.ProposedRevision != revision || document.AuthorDeviceId != authorDeviceId ||
                document.MembershipRevision != authorMembershipRevision)
                throw new InvalidDataException("Stored encrypted document metadata mismatch.");
            var authorMembershipHistory = await ReadMembershipAtRevisionAsync(connection, transaction,
                account.AccountId, account.GenerationId, authorMembershipRevision, cancellationToken);
            var authorMembership = JsonSerializer.Deserialize<MembershipDocument>(authorMembershipHistory.Json)
                ?? throw new InvalidDataException("Author membership is invalid.");
            if (!MembershipProtocol.VerifyMembership(authorMembership) ||
                MembershipProtocol.Digest(authorMembership) != DeviceEncoding.Encode(authorMembershipHistory.Digest))
                throw new InvalidDataException("Author membership signature is invalid.");
            var author = authorMembership.Members.SingleOrDefault(member => member.DeviceId == authorDeviceId);
            if (author is null || author.Status != "Active" || !author.Roles.Contains("Read", StringComparer.Ordinal))
                throw new InvalidContentDocumentException();
            var context = Context(document);
            if (!ContentDocumentCodec.VerifySignedHeader(document, context,
                new DevicePublicIdentity(author.DeviceId, author.SigningPublicKey, author.EncryptionPublicKey)))
                throw new InvalidDataException("Stored encrypted document signature is invalid.");

            await transaction.CommitAsync(cancellationToken);
            return new(cursor, revision, document);
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

    public async Task<EncryptedDocumentWriteResult> WriteAsync(string issuer, string subject, string accessToken,
        string requestTarget, string deviceId, string deviceSession, string requestId, long timestampUnixMilliseconds,
        string proofSignature, string entityId, EncryptedContentDocument document,
        CancellationToken cancellationToken)
    {
        ValidateRequest(issuer, subject, accessToken, deviceId, requestId, timestampUnixMilliseconds);
        DeviceEncoding.RequireId(entityId);
        ArgumentNullException.ThrowIfNull(document);
        var canonicalBody = ContentDocumentCodec.CanonicalDocumentBody(document);
        if (canonicalBody.Length > 400_000) throw new ArgumentException("Encrypted document body is too large.");
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var account = await ReadAccountAsync(connection, transaction, SubjectHash(issuer, subject),
                lockForUpdate: true, cancellationToken);
            var member = RequireMember(account.Membership, deviceId, "WriteSettings");
            await RequireSessionAsync(connection, transaction, account, deviceId, deviceSession, cancellationToken);
            await VerifyAndRecordRequestAsync(connection, transaction, account, member, accessToken,
                "POST", requestTarget, requestId, timestampUnixMilliseconds, proofSignature,
                SHA256.HashData(canonicalBody), cancellationToken);

            if (document.AuthorDeviceId != deviceId || document.AccountId != account.AccountId.ToString("D") ||
                document.GenerationId != account.GenerationId.ToString("D") || document.EntityId != entityId ||
                document.KeyEpoch != account.Membership.KeyEpoch || document.MembershipRevision != account.Revision)
                throw new InvalidContentDocumentException();
            var author = new DevicePublicIdentity(member.DeviceId, member.SigningPublicKey, member.EncryptionPublicKey);
            if (!ContentDocumentCodec.VerifySignedHeader(document, Context(document), author))
                throw new InvalidContentDocumentException();

            long currentRevision;
            await using (var read = new NpgsqlCommand("""
                SELECT generation_id,revision,key_epoch FROM focusguard_device.documents
                WHERE account_id=$1 AND entity_id=$2 FOR UPDATE
                """, connection, transaction))
            {
                read.Parameters.AddWithValue(account.AccountId);
                read.Parameters.AddWithValue(Guid.ParseExact(entityId, "D"));
                await using var reader = await read.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)) currentRevision = 0;
                else
                {
                    if (reader.GetGuid(0) != account.GenerationId || reader.GetInt64(2) != account.Membership.KeyEpoch)
                        throw new DocumentKeyEpochConflictException();
                    currentRevision = reader.GetInt64(1);
                }
            }
            if (document.BaseRevision != currentRevision || document.ProposedRevision != currentRevision + 1)
                throw new MembershipConflictException();

            var serialized = JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (currentRevision == 0)
            {
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO focusguard_device.documents(account_id,entity_id,generation_id,revision,key_epoch,
                        author_device_id,author_membership_revision,document)
                    VALUES ($1,$2,$3,$4,$5,$6,$7,$8::jsonb)
                    """, connection, transaction);
                insert.Parameters.AddWithValue(account.AccountId);
                insert.Parameters.AddWithValue(Guid.ParseExact(entityId, "D"));
                insert.Parameters.AddWithValue(account.GenerationId);
                insert.Parameters.AddWithValue(document.ProposedRevision);
                insert.Parameters.AddWithValue(document.KeyEpoch);
                insert.Parameters.AddWithValue(Guid.ParseExact(deviceId, "D"));
                insert.Parameters.AddWithValue(document.MembershipRevision);
                insert.Parameters.AddWithValue(serialized);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                await using var update = new NpgsqlCommand("""
                    UPDATE focusguard_device.documents
                    SET revision=$1,key_epoch=$2,author_device_id=$3,author_membership_revision=$4,
                        document=$5::jsonb,updated_at=NOW()
                    WHERE account_id=$6 AND entity_id=$7 AND generation_id=$8 AND revision=$9
                    """, connection, transaction);
                update.Parameters.AddWithValue(document.ProposedRevision);
                update.Parameters.AddWithValue(document.KeyEpoch);
                update.Parameters.AddWithValue(Guid.ParseExact(deviceId, "D"));
                update.Parameters.AddWithValue(document.MembershipRevision);
                update.Parameters.AddWithValue(serialized);
                update.Parameters.AddWithValue(account.AccountId);
                update.Parameters.AddWithValue(Guid.ParseExact(entityId, "D"));
                update.Parameters.AddWithValue(account.GenerationId);
                update.Parameters.AddWithValue(currentRevision);
                if (await update.ExecuteNonQueryAsync(cancellationToken) != 1) throw new MembershipConflictException();
            }

            var cursor = await AdvanceCursorAsync(connection, transaction, account.AccountId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new EncryptedDocumentWriteResult("Stored", document.ProposedRevision, entityId) { Cursor = cursor };
        }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(cancellationToken);
            throw new MembershipConflictException();
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(canonicalBody); }
    }

    private static async Task<long> ReadCursorAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid accountId, CancellationToken cancellationToken)
    {
        await using (var ensure = new NpgsqlCommand("""
            INSERT INTO focusguard_device.sync_cursors(account_id,cursor) VALUES ($1,0)
            ON CONFLICT(account_id) DO NOTHING
            """, connection, transaction))
        {
            ensure.Parameters.AddWithValue(accountId);
            await ensure.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var read = new NpgsqlCommand(
            "SELECT cursor FROM focusguard_device.sync_cursors WHERE account_id=$1 FOR SHARE", connection, transaction);
        read.Parameters.AddWithValue(accountId);
        return (long)(await read.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidDataException("Account sync cursor is missing."));
    }

    private static async Task<long> AdvanceCursorAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid accountId, CancellationToken cancellationToken)
    {
        await using var advance = new NpgsqlCommand("""
            INSERT INTO focusguard_device.sync_cursors(account_id,cursor) VALUES ($1,1)
            ON CONFLICT(account_id) DO UPDATE
            SET cursor=focusguard_device.sync_cursors.cursor+1,updated_at=NOW()
            RETURNING cursor
            """, connection, transaction);
        advance.Parameters.AddWithValue(accountId);
        return (long)(await advance.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidDataException("Account sync cursor did not advance."));
    }

    private async Task<AccountState> ReadAccountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string subjectHash, bool lockForUpdate, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT account_id,generation_id,membership_revision,membership_digest,membership
            FROM focusguard_device.accounts WHERE subject_hash=$1 {(lockForUpdate ? "FOR UPDATE" : "FOR SHARE")}
            """, connection, transaction);
        command.Parameters.AddWithValue(subjectHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidBootstrapProofException();
        var accountId = reader.GetGuid(0);
        var generationId = reader.GetGuid(1);
        var revision = reader.GetInt64(2);
        var digest = (byte[])reader[3];
        var json = reader.GetString(4);
        await reader.DisposeAsync();
        var membership = JsonSerializer.Deserialize<MembershipDocument>(json)
            ?? throw new InvalidBootstrapProofException();
        if (!MembershipProtocol.VerifyMembership(membership) || membership.AccountId != accountId.ToString("D") ||
            membership.GenerationId != generationId.ToString("D") || membership.ProposedRevision != revision ||
            MembershipProtocol.Digest(membership) != DeviceEncoding.Encode(digest))
            throw new InvalidBootstrapProofException();
        return new(accountId, generationId, revision, digest, membership);
    }

    private static DeviceMember RequireMember(MembershipDocument membership, string deviceId, string role)
    {
        DeviceEncoding.RequireId(deviceId);
        var member = membership.Members.SingleOrDefault(candidate => candidate.DeviceId == deviceId);
        if (member is null || member.Status != "Active" || !member.Roles.Contains(role, StringComparer.Ordinal))
            throw new InvalidDeviceProofException();
        return member;
    }

    private static async Task<(string Json, byte[] Digest)> ReadMembershipAtRevisionAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid accountId, Guid generationId, long revision,
        CancellationToken cancellationToken)
    {
        await using var query = new NpgsqlCommand("""
            SELECT membership,membership_digest FROM focusguard_device.membership_history
            WHERE account_id=$1 AND generation_id=$2 AND membership_revision=$3
            FOR SHARE
            """, connection, transaction);
        query.Parameters.AddWithValue(accountId);
        query.Parameters.AddWithValue(generationId);
        query.Parameters.AddWithValue(revision);
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidDataException("Author membership revision is missing.");
        return (reader.GetString(0), (byte[])reader[1]);
    }

    private static async Task RequireSessionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        AccountState account, string deviceId, string deviceSession, CancellationToken cancellationToken)
    {
        byte[] sessionBytes;
        try { sessionBytes = DeviceEncoding.Decode(deviceSession, 32); }
        catch (FormatException) { throw new InvalidDeviceProofException(); }
        var sessionHash = SHA256.HashData(sessionBytes);
        CryptographicOperations.ZeroMemory(sessionBytes);
        try
        {
            await using var query = new NpgsqlCommand("""
                SELECT membership_revision FROM focusguard_device.device_sessions
                WHERE session_hash=$1 AND account_id=$2 AND generation_id=$3 AND device_id=$4
                  AND revoked_at IS NULL AND expires_at>NOW() AND idle_until>NOW()
                FOR UPDATE
                """, connection, transaction);
            query.Parameters.AddWithValue(sessionHash);
            query.Parameters.AddWithValue(account.AccountId);
            query.Parameters.AddWithValue(account.GenerationId);
            query.Parameters.AddWithValue(Guid.ParseExact(deviceId, "D"));
            var revision = await query.ExecuteScalarAsync(cancellationToken);
            if (revision is not long membershipRevision) throw new InvalidDeviceProofException();
            if (membershipRevision != account.Revision) throw new StaleDeviceSessionException();
            await using var touch = new NpgsqlCommand("""
                UPDATE focusguard_device.device_sessions
                SET idle_until=LEAST(expires_at,NOW()+INTERVAL '30 minutes')
                WHERE session_hash=$1 AND revoked_at IS NULL
                """, connection, transaction);
            touch.Parameters.AddWithValue(sessionHash);
            if (await touch.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidDeviceProofException();
        }
        finally { CryptographicOperations.ZeroMemory(sessionHash); }
    }

    private static async Task VerifyAndRecordRequestAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        AccountState account, DeviceMember member, string accessToken, string method, string requestTarget,
        string requestId, long timestampUnixMilliseconds, string proofSignature, ReadOnlyMemory<byte> bodyDigest,
        CancellationToken cancellationToken)
    {
        var proof = new DeviceRequestProof(method, requestTarget, account.AccountId.ToString("D"),
            account.GenerationId.ToString("D"), member.DeviceId, requestId, timestampUnixMilliseconds,
            TokenDigest(accessToken), DeviceEncoding.Encode(bodyDigest.Span));
        var canonical = MembershipProtocol.CanonicalDeviceRequestProof(proof);
        try
        {
            if (!DeviceIdentity.Verify(new DevicePublicIdentity(member.DeviceId, member.SigningPublicKey,
                member.EncryptionPublicKey), "FG-REQUEST-1", canonical, proofSignature))
                throw new InvalidDeviceProofException();
        }
        finally { CryptographicOperations.ZeroMemory(canonical); }

        await using var purge = new NpgsqlCommand("DELETE FROM focusguard_device.request_proofs WHERE expires_at<=NOW()", connection, transaction);
        await purge.ExecuteNonQueryAsync(cancellationToken);
        await using var insert = new NpgsqlCommand("""
            INSERT INTO focusguard_device.request_proofs(account_id,device_id,request_id,expires_at)
            VALUES ($1,$2,$3,NOW()+INTERVAL '5 minutes')
            """, connection, transaction);
        insert.Parameters.AddWithValue(account.AccountId);
        insert.Parameters.AddWithValue(Guid.ParseExact(member.DeviceId, "D"));
        insert.Parameters.AddWithValue(Guid.ParseExact(requestId, "D"));
        try { await insert.ExecuteNonQueryAsync(cancellationToken); }
        catch (PostgresException exception) when (exception.SqlState == "23505") { throw new InvalidDeviceProofException(); }
    }

    private static ContentDocumentContext Context(EncryptedContentDocument document) => new(document.AccountId,
        document.GenerationId, document.EntityId, document.Kind, document.BaseRevision,
        document.ProposedRevision, document.KeyEpoch, document.MembershipRevision);

    private static string SubjectHash(string issuer, string subject) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(issuer + "\0" + subject)));

    private static string TokenDigest(string accessToken) =>
        DeviceEncoding.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)));

    private static void ValidateRequest(string issuer, string subject, string accessToken, string deviceId,
        string requestId, long timestampUnixMilliseconds)
    {
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject) || subject.Length > 255 ||
            string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 16384)
            throw new ArgumentException("Invalid identity request.");
        DeviceEncoding.RequireId(deviceId);
        DeviceEncoding.RequireId(requestId);
        if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestampUnixMilliseconds) > 60_000)
            throw new InvalidDeviceProofException();
    }
}

public sealed class DocumentKeyEpochConflictException : Exception;
public sealed class InvalidContentDocumentException : Exception;
public sealed class StaleDeviceSessionException : Exception;
