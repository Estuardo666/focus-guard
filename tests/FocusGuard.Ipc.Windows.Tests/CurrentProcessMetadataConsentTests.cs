using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace FocusGuard.Ipc.Windows.Tests;

public sealed class CurrentProcessMetadataConsentTests
{
    [Fact]
    [SupportedOSPlatform("windows")]
    public void OverlappingClientsKeepConsentUntilTheLastScopeCloses()
    {
        if (!OperatingSystem.IsWindows()) return;
        var original = CurrentProcessMetadataConsent.ReadCurrentDacl();
        using var first = CurrentProcessMetadataConsent.Enable(true);
        using (var second = CurrentProcessMetadataConsent.Enable(true))
        {
            var applied = CurrentProcessMetadataConsent.ReadCurrentDacl();
            first.Dispose();
            Assert.Equal(applied, CurrentProcessMetadataConsent.ReadCurrentDacl());
        }
        Assert.Equal(original, CurrentProcessMetadataConsent.ReadCurrentDacl());
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void ExactQueryOnlyPermissionPreservesAndRestoresCurrentProcessDacl()
    {
        if (!OperatingSystem.IsWindows()) return;
        var original = CurrentProcessMetadataConsent.ReadCurrentDacl();
        using (var consent = CurrentProcessMetadataConsent.Enable(true))
        {
            var acl = new RawAcl(CurrentProcessMetadataConsent.ReadCurrentDacl(), 0);
            var sid = new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null);
            var mask = 0;
            foreach (GenericAce ace in acl)
                if (ace is CommonAce entry && entry.AceQualifier == AceQualifier.AccessAllowed
                    && entry.SecurityIdentifier.Equals(sid)) mask |= entry.AccessMask;
            Assert.Equal(CurrentProcessMetadataConsent.QueryLimitedInformation, mask);
            var previous = new RawAcl(original, 0);
            for (var i = 0; i < previous.Count; i++)
            {
                var before = new byte[previous[i].BinaryLength]; previous[i].GetBinaryForm(before, 0);
                var after = new byte[acl[i].BinaryLength]; acl[i].GetBinaryForm(after, 0);
                Assert.Equal(before, after);
            }
        }
        Assert.Equal(original, CurrentProcessMetadataConsent.ReadCurrentDacl());
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void AbsentConsentCannotChangeProcessDacl()
    {
        if (!OperatingSystem.IsWindows()) return;
        var original = CurrentProcessMetadataConsent.ReadCurrentDacl();
        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            using var denied = CurrentProcessMetadataConsent.Enable(false);
        });
        Assert.Equal(original, CurrentProcessMetadataConsent.ReadCurrentDacl());
    }
}
