using FocusGuard.Contracts;
using FocusGuard.Domain;

namespace FocusGuard.Contracts.Tests;

public sealed class IpcProtocolTests
{
    [Fact]
    public void InactiveStatusRoundTripsWithNullSessionTimestamps()
    {
        var envelope = IpcProtocol.CreateStatusResponse(
            Guid.NewGuid(),
            new StatusResponse(
                true,
                false,
                "Inactive",
                0,
                null,
                null,
                null,
                null,
                StrictMode: false,
                EnforcementConnected: false));

        Assert.True(envelope.IsValid);
        var decoded = IpcProtocol.ReadStatusResponse(envelope.Value!);
        Assert.True(decoded.IsValid);
        Assert.Equal("Inactive", decoded.Value!.SessionState);
    }
    [Fact]
    public void StatusRequest_RoundTripsAsVersionedBoundedEnvelope()
    {
        var correlationId = Guid.NewGuid();
        var result = IpcProtocol.CreateStatusRequest(correlationId);

        Assert.True(result.IsValid);
        Assert.Equal(IpcProtocol.CurrentVersion, result.Value!.ProtocolVersion);
        Assert.Equal(IpcMessageType.StatusRequest, result.Value.MessageType);
        Assert.Equal(correlationId, result.Value.CorrelationId);
        Assert.True(IpcProtocol.ValidateEnvelope(result.Value).IsValid);
    }

    [Fact]
    public void ActivateRequest_RoundTripsTypedPayload()
    {
        var generated = RecoveryCodeService.Generate();
        var request = new ActivateSessionRequest(
            Guid.NewGuid(),
            3600,
            true,
            900,
            generated.Credential);
        var envelope = IpcProtocol.CreateActivateSessionRequest(Guid.NewGuid(), request);

        var decoded = IpcProtocol.ReadActivateSessionRequest(envelope.Value!);

        Assert.True(envelope.IsValid);
        Assert.True(decoded.IsValid);
        Assert.Equal(request, decoded.Value);
        Assert.DoesNotContain(generated.DisplayCode, envelope.Value!.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvelopeRejectsUnknownVersionMalformedJsonAndOversizedPayload()
    {
        var unknownVersion = new IpcEnvelope(99, IpcMessageType.StatusRequest, Guid.NewGuid(), "{}");
        var malformed = new IpcEnvelope(1, IpcMessageType.StatusRequest, Guid.NewGuid(), "{not-json");
        var oversized = new IpcEnvelope(1, IpcMessageType.StatusRequest, Guid.NewGuid(), $"{{\"value\":\"{new string('x', IpcProtocol.MaxPayloadBytes)}\"}}");

        Assert.False(IpcProtocol.ValidateEnvelope(unknownVersion).IsValid);
        Assert.False(IpcProtocol.ValidateEnvelope(malformed).IsValid);
        Assert.False(IpcProtocol.ValidateEnvelope(oversized).IsValid);
    }

    [Fact]
    public void ActivationValidationRejectsInvalidDurationsAndNormalRecoveryDelay()
    {
        var invalidDuration = new ActivateSessionRequest(Guid.NewGuid(), 59, false, 0);
        var invalidNormalDelay = new ActivateSessionRequest(Guid.NewGuid(), 60, false, 15);
        var emptyProfile = new ActivateSessionRequest(Guid.Empty, 60, false, 0);

        Assert.False(IpcProtocol.ValidateActivateSessionRequest(invalidDuration).IsValid);
        Assert.False(IpcProtocol.ValidateActivateSessionRequest(invalidNormalDelay).IsValid);
        Assert.False(IpcProtocol.ValidateActivateSessionRequest(emptyProfile).IsValid);
    }

    [Fact]
    public void ReadActivationRejectsWrongMessageTypeAndEmptyCorrelation()
    {
        var wrongType = new IpcEnvelope(1, IpcMessageType.StatusRequest, Guid.NewGuid(), "{}");
        var emptyCorrelation = new IpcEnvelope(1, IpcMessageType.ActivateSessionRequest, Guid.Empty, "{}");

        Assert.False(IpcProtocol.ReadActivateSessionRequest(wrongType).IsValid);
        Assert.False(IpcProtocol.ValidateEnvelope(emptyCorrelation).IsValid);
    }

    [Fact]
    public void ActivationRejectsDuplicateOrMalformedPolicyRules()
    {
        var duplicateId = Guid.NewGuid();
        var duplicate = new ActivateSessionRequest(
            Guid.NewGuid(),
            60,
            false,
            0,
            ApplicationRules: [new ApplicationPolicyRule(duplicateId, "Block", "FileName", "chat.exe")],
            WebsiteRules: [new WebsitePolicyRule(duplicateId, "Allow", "example.com")]);
        var queryPattern = duplicate with
        {
            WebsiteRules = [new WebsitePolicyRule(Guid.NewGuid(), "Block", "example.com?secret=1")]
        };

        Assert.False(IpcProtocol.ValidateActivateSessionRequest(duplicate).IsValid);
        Assert.False(IpcProtocol.ValidateActivateSessionRequest(queryPattern).IsValid);
    }

    [Fact]
    public void ControlEnvelopeRoundTripsStatusAndRejectsUnknownShape()
    {
        var correlationId = Guid.NewGuid();
        var response = new StatusResponse(
            true,
            false,
            "Active",
            60,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(1),
            StrictMode: false,
            EnforcementConnected: false,
            ScheduledIntent: new ScheduledSessionIntent("Deep Work", 1, EnforcementEnabled: false));
        var envelope = IpcProtocol.CreateStatusResponse(correlationId, response).Value!;

        var bytes = IpcProtocol.WriteEnvelope(envelope);
        var decodedEnvelope = IpcProtocol.ReadEnvelope(bytes);
        var decodedStatus = IpcProtocol.ReadStatusResponse(decodedEnvelope.Value!);
        var unknownShape = IpcProtocol.ReadEnvelope(
            System.Text.Encoding.UTF8.GetBytes(
                "{\"protocolVersion\":1,\"messageType\":0,\"correlationId\":\"" +
                Guid.NewGuid() +
                "\",\"payloadJson\":\"{}\",\"extra\":true}"));

        Assert.True(decodedEnvelope.IsValid);
        Assert.Equal(response, decodedStatus.Value);
        Assert.False(unknownShape.IsValid);
    }
}
