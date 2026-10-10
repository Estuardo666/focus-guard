using FocusGuard.Contracts;

namespace FocusGuard.Contracts.Tests;

public sealed class ReviewedSessionContractTests
{
    [Fact]
    public void ReviewedRequestsRoundtripWithoutChangingClassicWireShapes()
    {
        var id = Guid.NewGuid();
        var stop = IpcProtocol.CreateReviewedStopRequest(Guid.NewGuid(), new(id));
        Assert.Equal(id, IpcProtocol.ReadReviewedStopRequest(stop.Value!).Value!.SessionId);
        var skip = IpcProtocol.CreateReviewedSkipRequest(Guid.NewGuid(), new(id, 1));
        Assert.Equal(new ReviewedSkipBreakRequest(id, 1), IpcProtocol.ReadReviewedSkipRequest(skip.Value!).Value);
        Assert.Equal("{}", IpcProtocol.CreateStopSessionRequest(Guid.NewGuid()).Value!.PayloadJson);
        Assert.Equal("{\"phaseIndex\":1}", CycleIpcProtocol.CreateSkipBreakRequest(Guid.NewGuid(), new(1)).Value!.PayloadJson);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"sessionId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"sessionId\":\"11111111-1111-4111-8111-111111111111\",\"strictMode\":false}")]
    [InlineData("{\"sessionId\":\"11111111-1111-4111-8111-111111111111\",\"sessionId\":\"11111111-1111-4111-8111-111111111111\"}")]
    public void StopRejectsMissingEmptyExtraAndDuplicateIdentity(string payload) =>
        Assert.False(IpcProtocol.ReadReviewedStopRequest(new(IpcProtocol.CurrentVersion, IpcMessageType.ReviewedStopSessionRequest, Guid.NewGuid(), payload)).IsValid);

    [Theory]
    [InlineData(-1)]
    [InlineData(49)]
    public void SkipRejectsPhaseOutsideFinitePlan(int index)
    {
        Assert.False(IpcProtocol.CreateReviewedSkipRequest(Guid.NewGuid(), new(Guid.NewGuid(), index)).IsValid);
        Assert.False(IpcProtocol.CreateReviewedSkipRequest(Guid.NewGuid(), new(Guid.Empty, 1)).IsValid);
    }
}
