using System.Text;
using FocusGuard.Contracts;

namespace FocusGuard.Contracts.Tests;

public sealed class ExtensionHealthProtocolTests
{
    private static readonly DateTimeOffset NowUtc =
        new(2026, 7, 28, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReadRequest_AcceptsMinimalHeartbeat()
    {
        var correlationId = Guid.NewGuid();
        var payload = ExtensionHealthProtocol.WriteRequest(new ExtensionHealthRequest(
            1,
            "heartbeat",
            correlationId,
            NowUtc));

        var result = ExtensionHealthProtocol.ReadRequest(payload, NowUtc);

        Assert.True(result.IsValid);
        Assert.Equal(correlationId, result.Value!.CorrelationId);
    }

    [Theory]
    [InlineData("""{"protocolVersion":1,"type":"activate","correlationId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","sentAtUtc":"2026-07-28T15:00:00+00:00"}""")]
    [InlineData("""{"protocolVersion":1,"type":"release","correlationId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","sentAtUtc":"2026-07-28T15:00:00+00:00"}""")]
    [InlineData("""{"protocolVersion":1,"type":"status","correlationId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","sentAtUtc":"2026-07-28T15:00:00+00:00","profileId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"}""")]
    [InlineData("""{"protocolVersion":1,"type":"status","correlationId":"00000000-0000-0000-0000-000000000000","sentAtUtc":"2026-07-28T15:00:00+00:00"}""")]
    [InlineData("""{"protocolVersion":1,"type":"status","correlationId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","sentAtUtc":"2026-07-28T15:00:00-05:00"}""")]
    public void ReadRequest_RejectsInvalidOrAuthorityChangingMessages(string json)
    {
        var result = ExtensionHealthProtocol.ReadRequest(Encoding.UTF8.GetBytes(json), NowUtc);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ReadRequest_RejectsOversizedAndOverNestedMessages()
    {
        var oversized = new byte[ExtensionHealthProtocol.MaxMessageBytes + 1];
        var nested = Encoding.UTF8.GetBytes(
            """{"protocolVersion":1,"type":"status","correlationId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","sentAtUtc":"2026-07-28T15:00:00+00:00","x":{"a":{"b":{"c":{"d":{"e":{"f":{"g":{"h":1}}}}}}}}}""");

        Assert.False(ExtensionHealthProtocol.ReadRequest(oversized, NowUtc).IsValid);
        Assert.False(ExtensionHealthProtocol.ReadRequest(nested, NowUtc).IsValid);
    }

    [Theory]
    [InlineData("https://example.com/path?secret=value")]
    [InlineData("https://example.com/path#secret")]
    [InlineData("file:///private.txt")]
    public void ReadRequest_RejectsUnsanitizedBrowserAudit(string url)
    {
        var request = new ExtensionHealthRequest(
            1,
            ExtensionHealthProtocol.AuditType,
            Guid.NewGuid(),
            NowUtc,
            Guid.NewGuid(),
            new string('A', 64),
            url);

        Assert.False(ExtensionHealthProtocol.ReadRequest(
            ExtensionHealthProtocol.WriteRequest(request),
            NowUtc).IsValid);
    }
}
