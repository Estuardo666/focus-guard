using System.Runtime.InteropServices;
using System.Text;

namespace FocusGuard.Setup;

/// <summary>One process the OS says is holding a handle open on an installer file.</summary>
public sealed record BlockingProcess(int ProcessId, string ApplicationName);

/// <summary>
/// Wraps the Windows Restart Manager API (the same mechanism the MSI installer and Windows
/// Update use to show "these programs need to close before you can continue"). Without it,
/// a locked-file failure just says which DLL and offers no next step -- someone without a
/// developer to ask has no way to act on that. With it, the installer can name the actual
/// running program and offer to close it.
/// </summary>
public static class RestartManagerPreflight
{
    private const int RmRebootReasonNone = 0;
    private const int ErrorMoreData = 234;

    /// <summary>
    /// Returns every running process (other than this one) holding a handle open on any
    /// file under <paramref name="root"/>. Empty when nothing is blocking, or when the
    /// directory does not exist yet (a fresh install has nothing to preflight).
    /// </summary>
    public static IReadOnlyList<BlockingProcess> FindBlockingProcesses(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray();
        if (files.Length == 0)
        {
            return [];
        }

        var sessionKey = new StringBuilder(CCH_RM_SESSION_KEY + 1);
        var startResult = RmStartSession(out var session, 0, sessionKey);
        if (startResult != 0)
        {
            // The preflight is a courtesy, not a gate: if the API itself is unavailable,
            // fall through and let the installer's own retry/rollback handle the failure.
            return [];
        }

        try
        {
            var registerResult = RmRegisterResources(
                session,
                (uint)files.Length,
                files,
                0,
                null,
                0,
                null);
            if (registerResult != 0)
            {
                return [];
            }

            uint procInfoNeeded = 0;
            uint procInfo = 0;
            uint rebootReasons = RmRebootReasonNone;
            var listResult = RmGetList(session, out procInfoNeeded, ref procInfo, null, ref rebootReasons);
            if (listResult == ErrorMoreData)
            {
                procInfo = procInfoNeeded;
                var processInfo = new RM_PROCESS_INFO[procInfo];
                listResult = RmGetList(session, out procInfoNeeded, ref procInfo, processInfo, ref rebootReasons);
                if (listResult != 0)
                {
                    return [];
                }

                var currentProcessId = Environment.ProcessId;
                return processInfo
                    .Take((int)procInfo)
                    .Where(process => process.Process.dwProcessId != currentProcessId)
                    .Select(process => new BlockingProcess(
                        process.Process.dwProcessId,
                        string.IsNullOrWhiteSpace(process.strAppName) ? "(unknown)" : process.strAppName))
                    // A process can register more than one open file; only the closable
                    // list needs to be shown once per process.
                    .GroupBy(process => process.ProcessId)
                    .Select(group => group.First())
                    .ToArray();
            }

            return [];
        }
        finally
        {
            RmEndSession(session);
        }
    }

    private const int CCH_RM_SESSION_KEY = 32;
    private const int CCH_RM_MAX_APP_NAME = 255;
    private const int CCH_RM_MAX_SVC_NAME = 63;

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_APP_NAME + 1)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCH_RM_MAX_SVC_NAME + 1)]
        public string strServiceShortName;

        public int ApplicationType;
        public uint AppStatus;
        public int TSSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, StringBuilder strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint pSessionHandle,
        uint nFiles,
        string[] rgsFilenames,
        uint nApplications,
        [In] RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        ref uint lpdwRebootReasons);
}
