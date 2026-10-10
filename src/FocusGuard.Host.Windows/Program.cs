using System.Runtime.InteropServices;
using System.Security.Principal;
using FocusGuard.Platform.Windows;
using FocusGuard.Host.Windows.Hosting;

namespace FocusGuard.Host.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var pilotEnabled = args.Length == 1 && args[0] == "--pilot";
        if (!TryGetSmokeScenario(pilotEnabled ? [] : args, out var smokeScenario))
        {
            return 2;
        }

        var smokeMode = smokeScenario != HostSmokeScenario.None;

        if (!IsRunningAtMediumIntegrity())
        {
            if (!smokeMode)
            {
                MessageBox.Show(
                    HostWindowStrings.Get("ElevatedDescription"),
                    HostWindowStrings.Get("WindowTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return 2;
        }

        // A React-only install still needs the signed-in user's enforcement agent.
        // Smoke runs intentionally avoid starting a machine-installed companion.
        using var processAgentSupervisor = smokeMode
            ? null
            : new ProcessAgentSupervisorLifetime(
                new ProcessAgentSupervisor(AppContext.BaseDirectory));
        using var form = HostComposition.CreateDefault(smokeScenario, pilotEnabled);
        if (smokeScenario == HostSmokeScenario.WindowDpiContract)
        {
            return SatisfiesWindowDpiContract(form) ? 0 : 4;
        }

        Application.Run(form);
        return smokeMode ? (form.SmokeSucceeded ? 0 : 3) : 0;
    }

    private static bool SatisfiesWindowDpiContract(MainForm form)
    {
        try
        {
            var handle = form.Handle;
            var context = GetWindowDpiAwarenessContext(handle);
            var dpi = GetDpiForWindow(handle);
            if (context == IntPtr.Zero || dpi == 0
                || !AreDpiAwarenessContextsEqual(context, PerMonitorV2DpiContext)
                || form.AutoScaleMode != AutoScaleMode.Dpi
                || form.DeviceDpi != dpi)
            {
                return false;
            }

            var minimumWidthDip = (int)Math.Round(form.MinimumSize.Width * 96d / dpi);
            var minimumHeightDip = (int)Math.Round(form.MinimumSize.Height * 96d / dpi);
            return minimumWidthDip == 720 && minimumHeightDip == 480;
        }
        catch (Exception exception) when (exception is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static bool TryGetSmokeScenario(string[] args, out HostSmokeScenario scenario)
    {
        scenario = args.Length switch
        {
            0 => HostSmokeScenario.None,
            1 when string.Equals(args[0], "--smoke", StringComparison.Ordinal) => HostSmokeScenario.LocalLoad,
            1 when string.Equals(args[0], "--smoke-runtime-missing", StringComparison.Ordinal) => HostSmokeScenario.RuntimeMissing,
            1 when string.Equals(args[0], "--smoke-renderer-failure", StringComparison.Ordinal) => HostSmokeScenario.RendererRecovery,
            1 when string.Equals(args[0], "--smoke-renderer-crash", StringComparison.Ordinal) => HostSmokeScenario.RendererCrash,
            1 when string.Equals(args[0], "--smoke-window-dpi", StringComparison.Ordinal) => HostSmokeScenario.WindowDpiContract,
            _ => (HostSmokeScenario)(-1)
        };

        return Enum.IsDefined(scenario);
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool IsRunningAtMediumIntegrity()
    {
        if (IsElevated())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var token = identity.AccessToken.DangerousGetHandle();
        _ = GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var requiredLength);
        if (requiredLength <= 0)
        {
            return false;
        }

        var buffer = Marshal.AllocHGlobal(requiredLength);
        try
        {
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, requiredLength, out _))
            {
                return false;
            }

            var label = Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
            var subAuthorityCountPointer = GetSidSubAuthorityCount(label.Label.Sid);
            if (subAuthorityCountPointer == IntPtr.Zero)
            {
                return false;
            }

            var subAuthorityCount = Marshal.ReadByte(subAuthorityCountPointer);
            if (subAuthorityCount == 0)
            {
                return false;
            }

            var lastSubAuthority = GetSidSubAuthority(label.Label.Sid, (uint)(subAuthorityCount - 1));
            if (lastSubAuthority == IntPtr.Zero)
            {
                return false;
            }

            var integrityRid = unchecked((uint)Marshal.ReadInt32(lastSubAuthority));
            return integrityRid >= MediumIntegrityRid && integrityRid < HighIntegrityRid;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private const int TokenIntegrityLevel = 25;
    private const uint MediumIntegrityRid = 0x2000;
    private const uint HighIntegrityRid = 0x3000;
    private static readonly IntPtr PerMonitorV2DpiContext = new(-4);

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenMandatoryLabel
    {
        public SidAndAttributes Label;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr token,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthorityIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
}
