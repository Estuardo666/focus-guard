using System.Text;
using FocusGuard.Contracts;
using FocusGuard.Domain;

namespace FocusGuard.Contracts.Tests;

public sealed class CycleIpcProtocolTests
{
    [Fact]
    public void PrepareRequest_RoundTripsWithVersionTwoAndExactShape()
    {
        var request = new PrepareSessionRequest(
            Guid.NewGuid(),
            "Focus",
            SessionMode.FocusCycle,
            0,
            new FocusCycleConfigurationContract(
                60,
                60,
                120,
                2,
                CycleTerminationMode.CycleCount,
                2,
                null),
            false,
            0,
            ApplicationRules: [],
            WebsiteRules: []);

        var envelope = CycleIpcProtocol.CreatePrepareRequest(Guid.NewGuid(), request);
        var decoded = CycleIpcProtocol.ReadPrepareRequest(envelope.Value!);

        Assert.True(envelope.IsValid);
        Assert.Equal(2, envelope.Value!.ProtocolVersion);
        Assert.True(decoded.IsValid);
        Assert.Equal(request.ProfileId, decoded.Value!.ProfileId);
        Assert.Equal(request.CycleConfiguration, decoded.Value.CycleConfiguration);
        Assert.Equal(request.Mode, decoded.Value.Mode);
        Assert.Empty(decoded.Value.ApplicationRules!);
        Assert.Empty(decoded.Value.WebsiteRules!);
    }

    [Fact]
    public void PrepareRequest_RejectsUnknownNestedFields()
    {
        var request = new PrepareSessionRequest(
            Guid.NewGuid(),
            "Focus",
            SessionMode.FocusCycle,
            0,
            new FocusCycleConfigurationContract(60, 60, 120, 2, CycleTerminationMode.CycleCount, 2, null),
            false,
            0,
            ApplicationRules: [],
            WebsiteRules: []);
        var envelope = CycleIpcProtocol.CreatePrepareRequest(Guid.NewGuid(), request).Value!;
        var payload = envelope.PayloadJson.Replace(
            "\"cycleConfiguration\":{",
            "\"cycleConfiguration\":{\"unknown\":1,",
            StringComparison.Ordinal);
        var decoded = CycleIpcProtocol.ReadPrepareRequest(envelope with { PayloadJson = payload });

        Assert.False(decoded.IsValid);
    }

    [Fact]
    public void SkipRequest_RejectsNegativePhaseIndex()
    {
        var envelope = CycleIpcProtocol.CreateSkipBreakRequest(
            Guid.NewGuid(),
            new SkipBreakRequest(-1));

        Assert.False(envelope.IsValid);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PresentRuleCollectionIsValidatedEvenWhenOtherCollectionIsNull(bool breakRules, bool website)
    {
        var invalidApps = new ApplicationPolicyRule[] { new(Guid.NewGuid(), breakRules ? "Allow" : "Unknown", "FileName", "audit.exe") };
        var invalidSites = new WebsitePolicyRule[] { new(Guid.NewGuid(), breakRules ? "Allow" : "Unknown", "audit.example") };
        var request = new PrepareSessionRequest(Guid.NewGuid(), "Nullable audit", SessionMode.FocusCycle, 0,
            new(60, 60, 60, 2, CycleTerminationMode.CycleCount, 2, null), false, 0,
            ApplicationRules: breakRules || website ? null : invalidApps,
            WebsiteRules: breakRules || !website ? null : invalidSites,
            BreakApplicationRules: breakRules && !website ? invalidApps : null,
            BreakWebsiteRules: breakRules && website ? invalidSites : null);
        Assert.False(CycleIpcProtocol.CreatePrepareRequest(Guid.NewGuid(), request).IsValid);
        var raw = new IpcEnvelope(IpcProtocol.CurrentVersion, IpcMessageType.PrepareSessionRequest, Guid.NewGuid(), System.Text.Json.JsonSerializer.Serialize(request, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        Assert.False(CycleIpcProtocol.ReadPrepareRequest(raw).IsValid);
        var valid = request with { ApplicationRules = null, WebsiteRules = null, BreakApplicationRules = null, BreakWebsiteRules = null };
        Assert.True(CycleIpcProtocol.CreatePrepareRequest(Guid.NewGuid(), valid).IsValid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrepareRequestPreservesBlockOnlyBreakExceptions(bool website)
    {
        var request = new PrepareSessionRequest(Guid.NewGuid(), "Break exceptions", SessionMode.FocusCycle, 0,
            new(60, 60, 60, 2, CycleTerminationMode.CycleCount, 2, null), false, 0,
            ApplicationRules: [], WebsiteRules: [],
            BreakApplicationRules: website ? [] : [new(Guid.NewGuid(), "Block", "FileName", "game.exe")],
            BreakWebsiteRules: website ? [new(Guid.NewGuid(), "Block", "games.example")] : []);

        var encoded = CycleIpcProtocol.CreatePrepareRequest(Guid.NewGuid(), request);
        Assert.True(encoded.IsValid);
        var decoded = CycleIpcProtocol.ReadPrepareRequest(encoded.Value!);
        Assert.True(decoded.IsValid);
        Assert.Equal(website ? 0 : 1, decoded.Value!.BreakApplicationRules!.Count);
        Assert.Equal(website ? 1 : 0, decoded.Value.BreakWebsiteRules!.Count);
        Assert.Equal("Block", website
            ? Assert.Single(decoded.Value.BreakWebsiteRules!).Action
            : Assert.Single(decoded.Value.BreakApplicationRules!).Action);
    }
}
