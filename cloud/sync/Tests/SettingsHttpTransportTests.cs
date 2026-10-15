using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FocusGuard.Devices;
using FocusGuard.Sync;
using Xunit;

public sealed class SettingsHttpTransportTests
{
    [Fact]
    public async Task PostUsesCanonicalCiphertextFreshPossessionProofAndActualTokenDigest()
    {
        using var fixture = new Fixture(); var ids = new List<string>(); var bodies = new List<byte[]>();
        fixture.Sender.Handler = async request =>
        {
            Assert.Equal("https://api.example.invalid/v1/documents/" + fixture.Entity, request.RequestUri!.AbsoluteUri);
            var body = await request.Content!.ReadAsByteArrayAsync(); bodies.Add(body);
            var id = request.Headers.GetValues("X-FG-Request-Id").Single(); ids.Add(id);
            var time = DateTimeOffset.Parse(request.Headers.GetValues("X-FG-Timestamp").Single(), System.Globalization.CultureInfo.InvariantCulture);
            var proof = new DeviceRequestProof("POST", request.RequestUri.AbsolutePath, fixture.Account.AccountId,
                fixture.Account.GenerationId, fixture.Author.Public.DeviceId, id, time.ToUnixTimeMilliseconds(),
                Sender.Digest, DeviceEncoding.Encode(SHA256.HashData(body)));
            Assert.True(DeviceIdentity.Verify(fixture.Author.Public, "FG-REQUEST-1", MembershipProtocol.CanonicalDeviceRequestProof(proof),
                request.Headers.GetValues("X-FG-Proof").Single()));
            return Reply(HttpStatusCode.Created, new { state = "Stored", revision = 1, entityId = fixture.Entity }, "1");
        };
        var operation = Guid.NewGuid(); var wire = fixture.Envelope();
        Assert.Equal(SettingsPublishResult.Stored, await fixture.Transport.PublishAsync(operation, wire, CancellationToken.None));
        Assert.Equal(SettingsPublishResult.Stored, await fixture.Transport.PublishAsync(operation, wire, CancellationToken.None));
        Assert.NotEqual(ids[0], ids[1]); Assert.Equal(bodies[0], bodies[1]);
        Assert.DoesNotContain("\"theme\"", Encoding.UTF8.GetString(bodies[0]));
    }

    [Fact]
    public async Task ReadChecksRoutingRevisionAndStrongEtag()
    {
        using var fixture = new Fixture(); var document = fixture.Document();
        fixture.Sender.Handler = _ => Task.FromResult(Reply(HttpStatusCode.OK, new { revision = 1, document }, "1"));
        var bytes = await fixture.Transport.ReadAsync(CancellationToken.None);
        Assert.NotNull(bytes);
        Assert.Equal(GlobalSettings.Default, SettingsEnvelopeCodec.Open(bytes, fixture.Context, fixture.Key, fixture.Author.Public).Values);
        fixture.Sender.Handler = _ => Task.FromResult(Reply(HttpStatusCode.OK, new { revision = 1, document }, "2"));
        await Assert.ThrowsAsync<SettingsTransportException>(() => fixture.Transport.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CursorHeaderAndBodyMustAgreeAndCursorCannotRollBack()
    {
        using var fixture = new Fixture();
        var document = fixture.Document();
        fixture.Sender.Handler = _ =>
        {
            var response = Reply(HttpStatusCode.OK, new { revision = 1, document, cursor = 5 }, "1");
            response.Headers.Add("X-FG-Cursor", "5");
            return Task.FromResult(response);
        };
        Assert.NotNull(await fixture.Transport.ReadAsync(CancellationToken.None));
        Assert.Equal(5, fixture.Transport.LastSeenCursor);

        fixture.Sender.Handler = _ =>
        {
            var response = Reply(HttpStatusCode.OK, new { revision = 1, document, cursor = 4 }, "1");
            response.Headers.Add("X-FG-Cursor", "4");
            return Task.FromResult(response);
        };
        var rollback = await Assert.ThrowsAsync<SettingsTransportException>(() => fixture.Transport.ReadAsync(CancellationToken.None));
        Assert.Equal(SettingsTransportFailure.Protocol, rollback.Failure);
        Assert.Equal(5, fixture.Transport.LastSeenCursor);

        fixture.Sender.Handler = _ =>
        {
            var response = Reply(HttpStatusCode.OK, new { revision = 1, document, cursor = 6 }, "1");
            response.Headers.Add("X-FG-Cursor", "7");
            return Task.FromResult(response);
        };
        var mismatch = await Assert.ThrowsAsync<SettingsTransportException>(() => fixture.Transport.ReadAsync(CancellationToken.None));
        Assert.Equal(SettingsTransportFailure.Protocol, mismatch.Failure);
        Assert.Equal(5, fixture.Transport.LastSeenCursor);
    }

    [Fact]
    public async Task CursorPollUsesItsSignedEndpointAndFallsBackToOlderApis()
    {
        using var fixture = new Fixture();
        fixture.Sender.Handler = request =>
        {
            Assert.Equal("https://api.example.invalid/v1/sync/cursor", request.RequestUri!.AbsoluteUri);
            var requestId = request.Headers.GetValues("X-FG-Request-Id").Single();
            var time = DateTimeOffset.Parse(request.Headers.GetValues("X-FG-Timestamp").Single(),
                System.Globalization.CultureInfo.InvariantCulture);
            var proof = new DeviceRequestProof("GET", "/v1/sync/cursor", fixture.Account.AccountId,
                fixture.Account.GenerationId, fixture.Author.Public.DeviceId, requestId, time.ToUnixTimeMilliseconds(),
                Sender.Digest, DeviceEncoding.Encode(SHA256.HashData(ReadOnlySpan<byte>.Empty)));
            Assert.True(DeviceIdentity.Verify(fixture.Author.Public, "FG-REQUEST-1",
                MembershipProtocol.CanonicalDeviceRequestProof(proof), request.Headers.GetValues("X-FG-Proof").Single()));
            var response = Reply(HttpStatusCode.OK, new { cursor = 12 });
            response.Headers.Add("X-FG-Cursor", "12");
            return Task.FromResult(response);
        };
        Assert.Equal(12, await fixture.Transport.ReadCursorAsync(CancellationToken.None));
        Assert.Equal(12, fixture.Transport.LastSeenCursor);

        fixture.Sender.Handler = _ => Task.FromResult(Reply(HttpStatusCode.NotFound, new { error = "NotFound" }));
        Assert.Null(await fixture.Transport.ReadCursorAsync(CancellationToken.None));
        Assert.Null(fixture.Transport.LastSeenCursor);
    }

    [Theory]
    [InlineData("DocumentRekeyRequired", SettingsTransportFailure.RekeyRequired)]
    [InlineData("DeviceMembershipUpdateRequired", SettingsTransportFailure.MembershipChanged)]
    [InlineData("Unexpected", SettingsTransportFailure.Protocol)]
    public async Task NonRevision409CannotBecomeACasConflict(string code, SettingsTransportFailure expected)
    {
        using var fixture = new Fixture();
        fixture.Sender.Handler = _ => Task.FromResult(Reply(HttpStatusCode.Conflict, new { error = code }));
        var error = await Assert.ThrowsAsync<SettingsTransportException>(() => fixture.Transport.PublishAsync(Guid.NewGuid(), fixture.Envelope(), CancellationToken.None));
        Assert.Equal(expected, error.Failure);
    }

    [Fact]
    public async Task OnlyExplicitRevisionConflictProducesCasOutcome()
    {
        using var fixture = new Fixture();
        fixture.Sender.Handler = _ => Task.FromResult(Reply(HttpStatusCode.Conflict, new { error = "DocumentRevisionConflict" }));
        Assert.Equal(SettingsPublishResult.RevisionConflict, await fixture.Transport.PublishAsync(Guid.NewGuid(), fixture.Envelope(), CancellationToken.None));
    }

    [Theory]
    [InlineData(401, SettingsTransportFailure.Authentication)]
    [InlineData(403, SettingsTransportFailure.Permission)]
    [InlineData(404, SettingsTransportFailure.AmbiguousMissing)]
    [InlineData(302, SettingsTransportFailure.Protocol)]
    [InlineData(503, SettingsTransportFailure.Unavailable)]
    public async Task FailureStatusCannotLookLikeMissingOrCommittedDocument(int status, SettingsTransportFailure expected)
    {
        using var fixture = new Fixture();
        fixture.Sender.Handler = _ => Task.FromResult(Reply((HttpStatusCode)status, new { error = "DocumentUnavailable" }));
        var error = await Assert.ThrowsAsync<SettingsTransportException>(() => fixture.Transport.ReadAsync(CancellationToken.None));
        Assert.Equal(expected, error.Failure); Assert.Equal(1, fixture.Sender.Requests);
    }

    [Fact]
    public async Task AccountSwitchDuringIdentityRefreshDoesNotSend()
    {
        using var fixture = new Fixture();
        fixture.Sender.BeforeFactory = () => fixture.Sender.AccountBinding = DeviceEncoding.Encode(SHA256.HashData("other-subject"u8));
        var error = await Assert.ThrowsAsync<SettingsTransportException>(() => fixture.Transport.ReadAsync(CancellationToken.None));
        Assert.Equal(SettingsTransportFailure.Authentication, error.Failure); Assert.Equal(0, fixture.Sender.Requests);
    }

    [Fact]
    public async Task ThrottleCarriesRetryAfterAndOversizedResponseIsRejected()
    {
        using var fixture = new Fixture();
        fixture.Sender.Handler = _ =>
        {
            var response = Reply((HttpStatusCode)429, new { error = "RateLimited" });
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30)); return Task.FromResult(response);
        };
        var throttled = await Assert.ThrowsAsync<SettingsTransportException>(() => fixture.Transport.ReadAsync(CancellationToken.None));
        Assert.Equal(SettingsTransportFailure.Throttled, throttled.Failure); Assert.Equal(TimeSpan.FromSeconds(30), throttled.RetryAfter);
        fixture.Sender.Handler = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[131_073]) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json"); return Task.FromResult(response);
        };
        await Assert.ThrowsAsync<SettingsTransportException>(() => fixture.Transport.ReadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("http://api.example.invalid/")]
    [InlineData("https://api.example.invalid/path")]
    [InlineData("https://api.example.invalid/?secret=value")]
    public void UnsafeOriginIsRejected(string origin)
    {
        using var fixture = new Fixture();
        Assert.Throws<ArgumentException>(() => new SettingsDocumentHttpTransport(new Uri(origin), fixture.Sender, fixture.Account,
            fixture.Author, fixture.Session, fixture.Entity));
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, object body, string? etag = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (etag is not null) response.Headers.ETag = new EntityTagHeaderValue("\"" + etag + "\"");
        return response;
    }
    private sealed class Sender : ISettingsRequestSender
    {
        public static string Digest => DeviceEncoding.Encode(SHA256.HashData("synthetic-access-token"u8));
        public string? AccountBinding { get; set; }
        public Action? BeforeFactory { get; set; }
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Handler { get; set; } = _ => throw new InvalidOperationException("Unconfigured test response");
        public int Requests { get; private set; }
        public async Task<HttpResponseMessage> SendAsync(Func<string, HttpRequestMessage> factory, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); BeforeFactory?.Invoke();
            using var request = factory(Digest); Requests++;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "synthetic-access-token");
            return await Handler(request);
        }
    }
    private sealed class Fixture : IDisposable
    {
        public DeviceIdentity Author { get; } = DeviceIdentity.Create();
        public byte[] Key { get; } = RandomNumberGenerator.GetBytes(32);
        public DeviceAccountContext Account { get; } = new("https://identity.example.invalid/realms/test", Sender.Digest,
            Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"));
        public string Entity { get; } = Guid.NewGuid().ToString("D");
        public string Session { get; } = DeviceEncoding.Encode(RandomNumberGenerator.GetBytes(32));
        public Sender Sender { get; }
        public SettingsDocumentHttpTransport Transport { get; }
        public ContentDocumentContext Context => new(Account.AccountId, Account.GenerationId, Entity, GlobalSettings.DocumentKind, 0, 1, 1, 1);
        public Fixture()
        {
            Sender = new() { AccountBinding = Account.AccountBinding };
            Transport = new(new Uri("https://api.example.invalid/"), Sender, Account, Author, Session, Entity);
        }
        public byte[] Envelope() => SettingsEnvelopeCodec.Prepare(new(Guid.NewGuid(), 0, GlobalSettings.Default), Context, Key, Author);
        public EncryptedContentDocument Document() => JsonSerializer.Deserialize<EncryptedContentDocument>(Envelope(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        public void Dispose() { Author.Dispose(); CryptographicOperations.ZeroMemory(Key); }
    }
}
