namespace FocusGuard.Ipc.Windows.Tests;

public sealed class ProtectedIpcDeploymentTests
{
    [Fact]
    public void StandardProgramFilesAncestorsDoNotRejectSiblingCreationRights()
    {
        if (!OperatingSystem.IsWindows()) return;
        ProtectedIpcDeployment.EnsureProtectedAncestors(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
    }

    [Fact]
    public void UserWritableTemporaryRootCannotBecomeTrustedInstallation()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            ProtectedIpcDeployment.EnsureProtectedAncestors(Path.GetTempPath());
        });
    }
}
