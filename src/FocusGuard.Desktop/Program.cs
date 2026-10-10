using System.Globalization;
using Avalonia;
using FocusGuard.Client;

namespace FocusGuard.Desktop;

internal static class Program
{
    /// <summary>
    /// The interface is written and reviewed in Spanish, so Spanish is the default
    /// regardless of the machine's locale. The English resources stay in the assembly
    /// and still win when the OS asks for them explicitly, but a UI that renders half
    /// its labels in one language and half in another is worse than either alone.
    /// </summary>
    private static readonly CultureInfo DefaultUiCulture = new("es");

    [STAThread]
    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentUICulture = DefaultUiCulture;
        CultureInfo.CurrentUICulture = DefaultUiCulture;

        if (args.Contains(ServiceHealthCheck.Argument, StringComparer.Ordinal))
        {
#if FOCUSGUARD_PRODUCTION
            FocusGuard.Ipc.Windows.VerifiedPipeConnectionFactory? probePipes = null;
#endif
            try
            {
                var result = ServiceHealthProbe.RunAsync(() =>
                {
#if FOCUSGUARD_PRODUCTION
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                    probePipes = new(AppContext.BaseDirectory, FocusGuard.Ipc.Windows.IpcClientRole.Desktop);
                    return new NamedPipeFocusControlClient(connectionFactory: probePipes);
#else
                    return new NamedPipeFocusControlClient();
#endif
                }, CancellationToken.None).GetAwaiter().GetResult();
                ServiceHealthProbe.WriteDiagnostic(result);
                return result.ExitCode;
            }
            finally
            {
                // Keep every outcome observable to the installer's exact-process probe.
                if (args.Contains("--installer-owner-probe", StringComparer.Ordinal)) Thread.Sleep(2000);
#if FOCUSGUARD_PRODUCTION
                if (OperatingSystem.IsWindows()) probePipes?.Dispose();
#endif
            }
        }

#if FOCUSGUARD_PRODUCTION
        if (!OperatingSystem.IsWindows()) return 2;
        using var authenticatedPipes = new FocusGuard.Ipc.Windows.VerifiedPipeConnectionFactory(AppContext.BaseDirectory,
            FocusGuard.Ipc.Windows.IpcClientRole.Desktop);
        FocusGuard.Contracts.PipeConnectionFactories.Current = authenticatedPipes;
#endif

        App.StartMinimized = DesktopStartupOptions.ShouldStartMinimized(args);

        if (!OperatingSystem.IsWindows())
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        if (!DesktopSingleInstance.TryAcquire(out var instance))
        {
            return 0;
        }

        using (instance)
        {
            ProcessAgentStartup.EnsureRunning(AppContext.BaseDirectory);
            App.SingleInstance = instance;
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect();
#if DEBUG
        builder.LogToTrace();
#endif
        return builder;
    }
}
