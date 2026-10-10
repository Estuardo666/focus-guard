using System.Reflection;

namespace FocusGuard.Ipc.Windows.Tests;

public sealed class EvidenceBoundaryTests
{
    [Fact]
    public void ServiceCallersCannotConstructOrMutateNativeProofFromPayloads()
    {
        Assert.Empty(typeof(IpcPeerEvidence).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        foreach (var property in typeof(IpcPeerEvidence).GetProperties())
            Assert.False(property.SetMethod?.IsPublic ?? false);
        Assert.Empty(typeof(ObservedPipeClient).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }
}
