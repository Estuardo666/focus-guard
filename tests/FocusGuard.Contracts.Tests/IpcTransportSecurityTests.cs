namespace FocusGuard.Contracts.Tests;

public sealed class IpcTransportSecurityTests
{
    [Fact]
    public async Task UncomposedTransportCannotAccessAnInstalledEndpoint()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PipeConnectionFactories.Current.ConnectAsync("FocusGuard.Control.v2", default).AsTask());
    }
}
