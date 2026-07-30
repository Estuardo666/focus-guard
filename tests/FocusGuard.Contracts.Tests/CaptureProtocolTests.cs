using FocusGuard.Contracts;

namespace FocusGuard.Contracts.Tests;

public sealed class CaptureProtocolTests
{
    [Theory]
    [InlineData("https://amazon.com/s?k=monitor")]
    [InlineData("https://x.com/home#inbox")]
    [InlineData("ftp://example.com/file")]
    [InlineData("not a url")]
    public void CaptureRequest_RejectsAWebsiteSourceThatIsNotASanitizedHttpAddress(string source)
    {
        var request = new CaptureRequest("Note", CaptureProtocol.WebsiteKind, source);

        Assert.False(IpcProtocol.ValidateCaptureRequest(request).IsValid);
    }

    [Fact]
    public void CaptureRequest_AllowsAnApplicationPathAndAnEmptyNote()
    {
        var request = new CaptureRequest(
            null,
            CaptureProtocol.ApplicationKind,
            "C:\\Program Files\\ChatGPT\\ChatGPT.exe");

        Assert.True(IpcProtocol.ValidateCaptureRequest(request).IsValid);
    }

    [Fact]
    public void CaptureRequest_RejectsTextBeyondTheBoundedLength()
    {
        var request = new CaptureRequest(
            new string('a', IpcProtocol.MaximumCaptureTextLength + 1),
            CaptureProtocol.WebsiteKind,
            "https://x.com/");

        Assert.False(IpcProtocol.ValidateCaptureRequest(request).IsValid);
    }

    [Fact]
    public void CaptureRoundTrip_PreservesTheRequestAndTheAcceptedResponse()
    {
        var correlationId = Guid.NewGuid();
        var request = new CaptureRequest("Revisar X", CaptureProtocol.WebsiteKind, "https://x.com/");
        var envelope = IpcProtocol.CreateCaptureRequest(correlationId, request);

        var decoded = IpcProtocol.ReadCaptureRequest(
            IpcProtocol.ReadEnvelope(IpcProtocol.WriteEnvelope(envelope.Value!)).Value!);

        Assert.True(envelope.IsValid);
        Assert.Equal(request, decoded.Value);
    }

    [Fact]
    public void CaptureResponse_MustCarryAnIdWhenItClaimsToHaveAccepted()
    {
        var correlationId = Guid.NewGuid();
        var accepted = IpcProtocol.CreateCaptureResponse(
            correlationId,
            new CaptureResponse(true, "Saved", null));

        Assert.False(IpcProtocol.ReadCaptureResponse(accepted.Value!).IsValid);
    }

    [Fact]
    public void SessionSummaryRoundTrip_PreservesMetricsAndCaptures()
    {
        var correlationId = Guid.NewGuid();
        var summary = new SessionSummaryResponse(
            Available: true,
            SessionId: Guid.NewGuid(),
            Outcome: CaptureProtocol.CompletedOutcome,
            ProfileName: "Deep Work",
            PlannedMinutes: 50,
            FocusedMinutes: 50,
            BlockedAttempts: 14,
            StreakDays: 6,
            EndedAtUtc: DateTimeOffset.UtcNow,
            Captures:
            [
                new CapturedItemView(
                    Guid.NewGuid(),
                    "Revisar respuesta de Carlos",
                    CaptureProtocol.WebsiteKind,
                    "https://x.com/",
                    "x.com",
                    DateTimeOffset.UtcNow,
                    CaptureProtocol.PendingResolution)
            ]);
        var envelope = IpcProtocol.CreateSessionSummaryResponse(correlationId, summary);

        var decoded = IpcProtocol.ReadSessionSummaryResponse(
            IpcProtocol.ReadEnvelope(IpcProtocol.WriteEnvelope(envelope.Value!)).Value!);

        Assert.True(decoded.IsValid);
        // A record's generated equality compares the capture list by reference, so the
        // list is checked separately from the scalar metrics.
        Assert.Equal(summary with { Captures = [] }, decoded.Value! with { Captures = [] });
        Assert.Equal(summary.Captures, decoded.Value!.Captures);
    }

    [Fact]
    public void SessionSummary_RejectsAnAvailableResultWithoutASession()
    {
        var envelope = IpcProtocol.CreateSessionSummaryResponse(
            Guid.NewGuid(),
            new SessionSummaryResponse(
                Available: true,
                SessionId: null,
                Outcome: CaptureProtocol.CompletedOutcome,
                ProfileName: "Deep Work",
                PlannedMinutes: 50,
                FocusedMinutes: 50,
                BlockedAttempts: 0,
                StreakDays: 0,
                EndedAtUtc: DateTimeOffset.UtcNow,
                Captures: []));

        Assert.False(IpcProtocol.ReadSessionSummaryResponse(envelope.Value!).IsValid);
    }

    [Fact]
    public void ResolveCaptures_RejectsDuplicateOrUnknownResolutions()
    {
        var captureId = Guid.NewGuid();

        Assert.False(IpcProtocol.CreateResolveCapturesRequest(
            Guid.NewGuid(),
            new ResolveCapturesRequest([captureId, captureId], CaptureProtocol.DismissedResolution)).IsValid);
        Assert.False(IpcProtocol.CreateResolveCapturesRequest(
            Guid.NewGuid(),
            new ResolveCapturesRequest([captureId], CaptureProtocol.PendingResolution)).IsValid);
        Assert.True(IpcProtocol.CreateResolveCapturesRequest(
            Guid.NewGuid(),
            new ResolveCapturesRequest([captureId], CaptureProtocol.OpenedResolution)).IsValid);
    }

    [Fact]
    public void ExtensionHealth_AcceptsACaptureCarryingASanitizedUrlAndBoundedText()
    {
        var now = DateTimeOffset.UtcNow;
        var request = new ExtensionHealthRequest(
            ExtensionHealthProtocol.CurrentVersion,
            ExtensionHealthProtocol.CaptureType,
            Guid.NewGuid(),
            now,
            BlockedUrl: "https://instagram.com/",
            CaptureText: "Ver mensajes");

        var decoded = ExtensionHealthProtocol.ReadRequest(
            ExtensionHealthProtocol.WriteRequest(request),
            now);

        Assert.True(decoded.IsValid);
        Assert.Equal("Ver mensajes", decoded.Value!.CaptureText);
    }

    [Fact]
    public void ExtensionHealth_RejectsCaptureTextOnAHeartbeat()
    {
        var now = DateTimeOffset.UtcNow;
        var request = new ExtensionHealthRequest(
            ExtensionHealthProtocol.CurrentVersion,
            ExtensionHealthProtocol.HeartbeatType,
            Guid.NewGuid(),
            now,
            CaptureText: "Ver mensajes");

        Assert.False(ExtensionHealthProtocol
            .ReadRequest(ExtensionHealthProtocol.WriteRequest(request), now)
            .IsValid);
    }
}
