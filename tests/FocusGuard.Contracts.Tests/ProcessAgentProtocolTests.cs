using System.Text;
using FocusGuard.Contracts;

namespace FocusGuard.Contracts.Tests;

public sealed class ProcessAgentProtocolTests
{
    private static readonly DateTimeOffset Now =
        new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Heartbeat_RoundTripsExactAcknowledgement()
    {
        var request = new ProcessAgentRequest(
            1, "heartbeat", Guid.NewGuid(), Guid.NewGuid(), Now,
            Guid.NewGuid(), new string('A', 64));

        var decoded = ProcessAgentProtocol.ReadRequest(
            ProcessAgentProtocol.WriteRequest(request), Now);

        Assert.True(decoded.IsValid);
        Assert.Equal(request, decoded.Value);
    }

    [Theory]
    [InlineData("C:\\private\\app.exe", "Terminated")]
    [InlineData("app.exe", "Unknown")]
    [InlineData("app.exe?secret=1", "Failed")]
    public void Audit_RejectsUnsanitizedOrUnknownFields(string executable, string outcome)
    {
        var request = new ProcessAgentRequest(
            1, "audit", Guid.NewGuid(), Guid.NewGuid(), Now,
            BlockedExecutable: executable, Outcome: outcome);

        Assert.False(ProcessAgentProtocol.ReadRequest(
            ProcessAgentProtocol.WriteRequest(request), Now).IsValid);
    }

    [Fact]
    public void Audit_RoundTripsOnlySanitizedExecutableAndOutcome()
    {
        var request = new ProcessAgentRequest(
            1, "audit", Guid.NewGuid(), Guid.NewGuid(), Now,
            Guid.NewGuid(), new string('B', 64),
            "blocked-app.exe", "Terminated");

        var decoded = ProcessAgentProtocol.ReadRequest(
            ProcessAgentProtocol.WriteRequest(request), Now);

        Assert.True(decoded.IsValid);
        Assert.Equal("blocked-app.exe", decoded.Value!.BlockedExecutable);
        Assert.Equal("Terminated", decoded.Value.Outcome);
    }

    [Fact]
    public void Request_RejectsUnknownJsonProperty()
    {
        var payload = Encoding.UTF8.GetBytes(
            $$"""{"protocolVersion":1,"type":"heartbeat","correlationId":"{{Guid.NewGuid()}}","agentInstanceId":"{{Guid.NewGuid()}}","sentAtUtc":"2030-01-01T12:00:00Z","acknowledgedPolicyId":null,"acknowledgedPolicyHash":null,"blockedExecutable":null,"outcome":null,"extra":true}""");

        Assert.False(ProcessAgentProtocol.ReadRequest(payload, Now).IsValid);
    }
}
