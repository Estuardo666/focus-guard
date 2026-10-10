using System.Diagnostics;

namespace FocusGuard.Desktop.Tests;

public sealed class ReactPilotLauncherTests
{
    [Fact]
    public void StartsTheSiblingReactHostWithOnlyTheExplicitPilotArgument()
    {
        var platform = new RecordingProcessOperations { HostExists = true };
        var launcher = new ReactPilotLauncher(@"C:\Program Files\FocusGuard", platform);

        var result = launcher.Open();

        Assert.Equal(ReactPilotLaunchResult.Started, result);
        Assert.Equal(Path.GetFullPath(@"C:\Program Files\FocusGuard\FocusGuard.UI.exe"), platform.StartInfo!.FileName);
        Assert.Equal(Path.GetFullPath(@"C:\Program Files\FocusGuard"), platform.StartInfo.WorkingDirectory);
        Assert.False(platform.StartInfo.UseShellExecute);
        Assert.Equal(new[] { "--pilot" }, platform.StartInfo.ArgumentList);
    }

    [Fact]
    public void ActivatesAnExistingHostInsteadOfLaunchingADuplicate()
    {
        var platform = new RecordingProcessOperations { HostExists = true, ExistingHost = true };
        var launcher = new ReactPilotLauncher(@"C:\Program Files\FocusGuard", platform);

        var result = launcher.Open();

        Assert.Equal(ReactPilotLaunchResult.AlreadyOpen, result);
        Assert.Equal(1, platform.ActivationChecks);
        Assert.Null(platform.StartInfo);
    }

    [Fact]
    public void ReportsMissingHostAndFailedStartWithoutRetryingOrElevating()
    {
        var absent = new RecordingProcessOperations { HostExists = false };
        Assert.Equal(ReactPilotLaunchResult.NotInstalled,
            new ReactPilotLauncher(@"C:\Program Files\FocusGuard", absent).Open());
        Assert.Null(absent.StartInfo);

        var failed = new RecordingProcessOperations { HostExists = true, StartSucceeded = false };
        Assert.Equal(ReactPilotLaunchResult.Failed,
            new ReactPilotLauncher(@"C:\Program Files\FocusGuard", failed).Open());
        Assert.Equal(new[] { "--pilot" }, failed.StartInfo!.ArgumentList);
    }

    private sealed class RecordingProcessOperations : IReactPilotProcessOperations
    {
        public bool HostExists { get; init; }
        public bool ExistingHost { get; init; }
        public bool StartSucceeded { get; init; } = true;
        public int ActivationChecks { get; private set; }
        public ProcessStartInfo? StartInfo { get; private set; }

        public bool FileExists(string path) => HostExists
            && string.Equals(Path.GetFileName(path), "FocusGuard.UI.exe", StringComparison.OrdinalIgnoreCase);

        public bool ActivateExisting(string executablePath)
        {
            ActivationChecks++;
            return ExistingHost;
        }

        public bool Start(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
            return StartSucceeded;
        }
    }
}
