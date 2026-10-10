using System.Diagnostics;
using FocusGuard.Client.Preferences;

namespace FocusGuard.Desktop.Tests;

public sealed class DesktopLifecycleTests
{
    [Fact]
    public async Task SupervisorRestartsAnExitedChildAndCancellationLeavesItsReplacementRunning()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var directory = new TemporaryDirectory();
        var expected = Path.Combine(directory.Path, "FocusGuard.ProcessAgent.exe");
        File.WriteAllText(expected, string.Empty);
        var configuration = typeof(DesktopLifecycleTests).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
            .Cast<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration;
        var target = Path.Combine(FindRepositoryRoot(), "src", "FocusGuard.TestTarget", "bin", configuration,
            "net10.0", "FocusGuard.TestTarget.dll");
        Assert.True(File.Exists(target));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var replacementStarted = new TaskCompletionSource<Process>(TaskCreationOptions.RunContinuationsAsynchronously);
        var children = new List<Process>();
        Process? running = null;
        var supervisor = new ProcessAgentSupervisor(directory.Path, checkInterval: TimeSpan.FromMilliseconds(25),
            ensureRunning: root => ProcessAgentStartup.EnsureRunning(root,
                () => running is { HasExited: false } ? new[] { expected } : [],
                path =>
                {
                    Assert.Equal(expected, path);
                    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                    start.ArgumentList.Add(target);
                    start.ArgumentList.Add("--lifetime");
                    start.ArgumentList.Add("2");
                    running = Process.Start(start)!;
                    children.Add(running);
                    if (children.Count == 2) replacementStarted.TrySetResult(running);
                    // Return a separate handle: EnsureRunning disposes only the handle it owns.
                    return Process.GetProcessById(running.Id);
                }));
        var supervision = supervisor.RunAsync(cancellation.Token);
        try
        {
            var replacement = await replacementStarted.Task.WaitAsync(TimeSpan.FromSeconds(25));
            Assert.True(children[0].HasExited);
            cancellation.Cancel();
            await supervision;
            Assert.False(replacement.HasExited);
            await replacement.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, replacement.ExitCode);
        }
        finally
        {
            cancellation.Cancel();
            await supervision;
            foreach (var child in children) { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); child.Dispose(); }
        }
    }

    [Theory]
    [InlineData(AppTheme.Light, AppTheme.Dark, AppTheme.Dark, AppTheme.Light)]
    [InlineData(null, AppTheme.Light, AppTheme.Dark, AppTheme.Light)]
    [InlineData(null, null, AppTheme.Light, AppTheme.Light)]
    public void ThemeResolutionUsesProfileThenGlobalThenDefault(
        AppTheme? profile,
        AppTheme? global,
        AppTheme fallback,
        AppTheme expected)
    {
        Assert.Equal(expected, ThemePreferenceResolver.Resolve(profile, global, fallback));
    }

    [Theory]
    [InlineData("--minimized", true)]
    [InlineData("--MINIMIZED", true)]
    [InlineData("--other", false)]
    public void StartupArgumentControlsInitialNotificationAreaPresentation(
        string argument,
        bool expected)
    {
        Assert.Equal(expected, DesktopStartupOptions.ShouldStartMinimized([argument]));
    }

    [Fact]
    public void StartupCommandQuotesTheExactExecutableAndRequestsMinimizedLaunch()
    {
        var executable = Path.Combine("C:\\Program Files", "FocusGuard", "FocusGuard.Desktop.exe");

        var command = WindowsStartupRegistration.BuildCommand(executable);

        Assert.Equal($"\"{Path.GetFullPath(executable)}\" --minimized", command);
    }

    [Fact]
    public void ProcessAgentUsesTheWindowsSubsystemWithoutAConsole()
    {
        var project = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "FocusGuard.ProcessAgent",
            "FocusGuard.ProcessAgent.csproj"));

        Assert.Contains("<OutputType>WinExe</OutputType>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void TrayFlyoutPlacementStaysInsideTheWorkingArea()
    {
        var workArea = new Avalonia.PixelRect(100, 50, 1600, 900);

        var position = TrayFlyoutPlacement.BottomRight(
            workArea,
            flyoutWidth: 420,
            flyoutHeight: 560,
            inset: 12);

        Assert.Equal(new Avalonia.PixelPoint(1268, 378), position);
    }

    [Fact]
    public void MissingProcessAgentDoesNotAttemptLaunch()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var starts = 0;

        var running = ProcessAgentStartup.EnsureRunning(
            directory,
            () => [],
            _ =>
            {
                starts++;
                return null;
            });

        Assert.False(running);
        Assert.Equal(0, starts);
    }

    [Fact]
    public void ExistingSiblingProcessAgentIsNotStartedTwice()
    {
        using var directory = new TemporaryDirectory();
        var agentPath = Path.Combine(directory.Path, "FocusGuard.ProcessAgent.exe");
        File.WriteAllBytes(agentPath, []);
        var starts = 0;

        var running = ProcessAgentStartup.EnsureRunning(
            directory.Path,
            () => [agentPath],
            _ =>
            {
                starts++;
                return null;
            });

        Assert.True(running);
        Assert.Equal(0, starts);
    }

    [Fact]
    public void MissingRunningSiblingProcessAgentIsStarted()
    {
        using var directory = new TemporaryDirectory();
        var agentPath = Path.Combine(directory.Path, "FocusGuard.ProcessAgent.exe");
        File.WriteAllBytes(agentPath, []);
        string? startedPath = null;

        var running = ProcessAgentStartup.EnsureRunning(
            directory.Path,
            () => [],
            path =>
            {
                startedPath = path;
                return Process.GetCurrentProcess();
            });

        Assert.True(running);
        Assert.Equal(agentPath, startedPath);
    }

    [Fact]
    public async Task ProcessAgentSupervisorRetriesAfterAFailedCheck()
    {
        using var cancellation = new CancellationTokenSource();
        var checks = 0;
        var delays = 0;
        var supervisor = new ProcessAgentSupervisor(
            AppContext.BaseDirectory,
            checkInterval: TimeSpan.FromSeconds(5),
            ensureRunning: _ => ++checks > 1,
            delay: (_, _, _) =>
            {
                if (++delays == 2)
                {
                    cancellation.Cancel();
                }
                return Task.CompletedTask;
            });

        await supervisor.RunAsync(cancellation.Token);

        Assert.Equal(2, checks);
        Assert.Equal(2, delays);
    }

    [Fact]
    public void ProcessAgentSupervisorContainsDiscoveryFailures()
    {
        var supervisor = new ProcessAgentSupervisor(
            AppContext.BaseDirectory,
            ensureRunning: _ => throw new UnauthorizedAccessException());

        Assert.False(supervisor.TryEnsureRunning());
    }

    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(false, false, 5)]
    public void RefreshIntervalPreservesVisibleAndActiveResponsiveness(
        bool isVisible,
        bool isActive,
        int expectedSeconds)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            DesktopRefreshPolicy.GetInterval(isVisible, isActive));
    }

    [Theory]
    [InlineData(5, 60)]
    [InlineData(6, 54)]
    [InlineData(7, 48)]
    [InlineData(9, 42)]
    public void TimerDigitsShrinkToFitTheSessionPanel(int characterCount, double expectedFontSize)
    {
        Assert.Equal(expectedFontSize, TimerDigitLayout.FontSizeFor(characterCount));
    }

    [Fact]
    public void SecondDesktopInstanceSignalsTheExistingInstance()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        VerifySecondInstanceSignal();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void VerifySecondInstanceSignal()
    {
        var instanceKey = Guid.NewGuid().ToString("N");
        Assert.True(DesktopSingleInstance.TryAcquire(instanceKey, out var primary));
        Assert.NotNull(primary);
        using (primary)
        using (var activated = new ManualResetEventSlim())
        {
            primary.StartListening(activated.Set);
            var acquiredSecondary = true;
            var secondary = new Thread(() =>
            {
                acquiredSecondary = DesktopSingleInstance.TryAcquire(instanceKey, out var unexpected);
                unexpected?.Dispose();
            });

            secondary.Start();
            Assert.True(secondary.Join(TimeSpan.FromSeconds(2)));
            Assert.False(acquiredSecondary);
            Assert.True(activated.Wait(TimeSpan.FromSeconds(2)));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusGuard.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the FocusGuard repository root.");
    }
}
