using System.ComponentModel;
using System.Text.Json;

namespace FocusGuard.Ipc.Windows.Tests;

public sealed class IpcFailureDiagnosticTests
{
    [Theory]
    [InlineData("ownerScopeDenied", "PolicyOwnerScopeDenied")]
    [InlineData("processUnavailable", "PolicyProcessUnavailable")]
    [InlineData("untrustedDeployment", "PolicyUntrustedDeployment")]
    [InlineData("imageDenied", "PolicyImageDenied")]
    [InlineData("operationDenied", "PolicyOperationDenied")]
    [InlineData("private request payload", "Unclassified")]
    public void PolicyDenialSubcodesAreAllowListed(string policyCode, string expected)
    {
        var diagnostic = IpcFailureDiagnostic.FromException(new IpcPolicyDeniedException(policyCode, "private message"));
        Assert.Equal(expected, diagnostic.Code);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(diagnostic));
    }

    [Fact]
    public void PreservesNativeCauseAndFixedStageWithoutExceptionMessage()
    {
        var diagnostic = IpcFailureDiagnostic.FromException(new UnauthorizedAccessException(
            "Native caller identity unavailable.", new Win32Exception(5, "secret request and private path")));
        Assert.Equal("CallerIdentityUnavailable", diagnostic.Code);
        Assert.Equal(5, diagnostic.NativeError);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(diagnostic));
        Assert.DoesNotContain("private", JsonSerializer.Serialize(diagnostic));
    }

    [Fact]
    public void UnknownExceptionMessageIsNeverIncluded()
    {
        var diagnostic = IpcFailureDiagnostic.FromException(new IOException("https://private.test/?secret=token"));
        Assert.Equal("Unclassified", diagnostic.Code);
        Assert.Null(diagnostic.NativeError);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(diagnostic));
        Assert.DoesNotContain("token", JsonSerializer.Serialize(diagnostic));
    }

    [Fact]
    public void MoreSpecificInnerDeploymentFailureIsRetained()
    {
        var diagnostic = IpcFailureDiagnostic.FromException(new UnauthorizedAccessException(
            "Native service proof unavailable.", new UnauthorizedAccessException("Deployment has an untrusted write grant.")));
        Assert.Equal("DeploymentWriteGrantDenied", diagnostic.Code);
    }
}
