using FocusGuard.Contracts;

namespace FocusGuard.Ipc.Windows;

public enum IpcClientRole { Desktop, WebViewHost, ProcessAgent, NativeMessaging, Diagnostic }
public enum IpcEndpoint { Control, ProcessAgent, Browser }
public sealed record TrustedIpcBinary(IpcClientRole Role, string FileName, string Sha256,
    string? ManagedFileName = null, string? ManagedSha256 = null);
public sealed record OwnerLogonScope(string UserSid, string LogonSid, uint SessionId);

/// <summary>Evidence from a native observer, never fields supplied by an IPC payload.</summary>
public sealed record IpcPeerEvidence
{
    public uint ProcessId { get; internal init; }
    public string UserSid { get; internal init; }
    public string LogonSid { get; internal init; }
    public uint SessionId { get; internal init; }
    public string FinalImagePath { get; internal init; }
    public string ImageSha256 { get; internal init; }
    public bool ProcessAlive { get; internal init; }
    public bool ImageProtected { get; internal init; }
    public bool RootProtected { get; internal init; }
    public string? ManagedSha256 { get; internal init; }
    public bool ManagedProtected { get; internal init; }

    internal IpcPeerEvidence(uint processId, string userSid, string logonSid, uint sessionId,
        string finalImagePath, string imageSha256, bool processAlive, bool imageProtected, bool rootProtected,
        string? managedSha256 = null, bool managedProtected = false)
    {
        ProcessId = processId;
        UserSid = userSid;
        LogonSid = logonSid;
        SessionId = sessionId;
        FinalImagePath = finalImagePath;
        ImageSha256 = imageSha256;
        ProcessAlive = processAlive;
        ImageProtected = imageProtected;
        RootProtected = rootProtected;
        ManagedSha256 = managedSha256;
        ManagedProtected = managedProtected;
    }
}

public sealed record IpcAccessDecision(bool Allowed, IpcClientRole? Role, string Code)
{
    internal static IpcAccessDecision Deny(string code) => new(false, null, code);
}

/// <summary>Fail-closed operation policy. Native capture and deployment verification are separate required gates.</summary>
public sealed class IpcAuthorizationPolicy
{
    private readonly OwnerLogonScope _owner;
    private readonly string _root;
    private readonly TrustedIpcBinary[] _binaries;
    private static readonly HashSet<IpcMessageType> ControlRequests =
    [
        IpcMessageType.StatusRequest, IpcMessageType.ActivateSessionRequest, IpcMessageType.StopSessionRequest,
        IpcMessageType.PrepareSessionRequest, IpcMessageType.ActivatePreparedSessionRequest, IpcMessageType.SkipBreakRequest,
        IpcMessageType.ActivityRequest, IpcMessageType.CaptureRequest, IpcMessageType.SessionSummaryRequest,
        IpcMessageType.ResolveCapturesRequest, IpcMessageType.TodayRequest, IpcMessageType.ScheduleListRequest,
        IpcMessageType.ScheduleChangeRequest, IpcMessageType.ReviewedStopSessionRequest, IpcMessageType.ReviewedSkipBreakRequest,
        IpcMessageType.RevisionedScheduleListRequest, IpcMessageType.RevisionedScheduleChangeRequest
    ];
    private static readonly HashSet<IpcMessageType> DiagnosticRequests =
    [
        IpcMessageType.StatusRequest,
        IpcMessageType.ActivityRequest
    ];

    public IpcAuthorizationPolicy(OwnerLogonScope owner, string protectedInstallRoot, IEnumerable<TrustedIpcBinary> binaries)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(binaries);
        if (!IsOwnerSid(owner.UserSid) || !IsLogonSid(owner.LogonSid) || owner.SessionId == 0)
            throw new ArgumentException("Explicit interactive owner/logon scope required.", nameof(owner));
        if (!Path.IsPathFullyQualified(protectedInstallRoot)) throw new ArgumentException("Absolute root required.", nameof(protectedInstallRoot));
        _owner = owner;
        _root = IpcDirectoryPath.Normalize(protectedInstallRoot);
        _binaries = binaries.ToArray();
        if (_binaries.Length is 0 or > 16
            || _binaries.Select(binary => binary.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _binaries.Length
            || _binaries.Any(binary => binary is null || !Enum.IsDefined(binary.Role)
                || !IsLeafExecutable(binary.FileName) || !IsHash(binary.Sha256)
                || (binary.ManagedFileName is not null && (binary.ManagedFileName != Path.GetFileNameWithoutExtension(binary.FileName) + ".dll"
                    || !IsHash(binary.ManagedSha256)))
                || (binary.ManagedFileName is null && binary.ManagedSha256 is not null)))
            throw new ArgumentException("Invalid trusted binary manifest.", nameof(binaries));
    }

    public IpcAccessDecision AuthorizeControl(IpcPeerEvidence peer, IpcMessageType requestType)
    {
        var decision = Authenticate(peer);
        if (!decision.Allowed) return decision;
        var allowed = decision.Role switch
        {
            IpcClientRole.Desktop or IpcClientRole.WebViewHost => ControlRequests.Contains(requestType),
            IpcClientRole.Diagnostic => DiagnosticRequests.Contains(requestType),
            _ => false
        };
        return allowed ? decision : IpcAccessDecision.Deny("operationDenied");
    }

    public IpcAccessDecision AuthorizeAgent(IpcPeerEvidence peer, string requestType) =>
        AuthorizeTyped(peer, IpcClientRole.ProcessAgent,
            requestType is ProcessAgentProtocol.HeartbeatType or ProcessAgentProtocol.AuditType);

    public IpcAccessDecision AuthorizeBrowser(IpcPeerEvidence peer, string requestType) =>
        AuthorizeTyped(peer, IpcClientRole.NativeMessaging,
            requestType is ExtensionHealthProtocol.StatusType or ExtensionHealthProtocol.HeartbeatType
                or ExtensionHealthProtocol.AuditType or ExtensionHealthProtocol.CaptureType);

    public IpcAccessDecision Authenticate(IpcPeerEvidence peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        if (peer.ProcessId == 0 || !peer.ProcessAlive) return IpcAccessDecision.Deny("processUnavailable");
        if (!string.Equals(peer.UserSid, _owner.UserSid, StringComparison.Ordinal)
            || !string.Equals(peer.LogonSid, _owner.LogonSid, StringComparison.Ordinal)
            || peer.SessionId != _owner.SessionId) return IpcAccessDecision.Deny("ownerScopeDenied");
        if (!peer.ImageProtected || !peer.RootProtected) return IpcAccessDecision.Deny("untrustedDeployment");
        if (!Path.IsPathFullyQualified(peer.FinalImagePath) || !IsHash(peer.ImageSha256))
            return IpcAccessDecision.Deny("imageDenied");
        // Native observer supplies a final canonical file path, not a caller-declared alias.
        if (!string.Equals(peer.FinalImagePath, Path.GetFullPath(peer.FinalImagePath), StringComparison.OrdinalIgnoreCase))
            return IpcAccessDecision.Deny("imageDenied");
        var binary = _binaries.FirstOrDefault(candidate => string.Equals(peer.FinalImagePath,
            Path.Combine(_root, candidate.FileName), StringComparison.OrdinalIgnoreCase));
        return binary is not null && string.Equals(peer.ImageSha256, binary.Sha256, StringComparison.OrdinalIgnoreCase)
            && (binary.ManagedFileName is null || (peer.ManagedProtected
                && string.Equals(peer.ManagedSha256, binary.ManagedSha256, StringComparison.OrdinalIgnoreCase)))
            ? new(true, binary.Role, "authorized") : IpcAccessDecision.Deny("imageDenied");
    }

    private IpcAccessDecision AuthorizeTyped(IpcPeerEvidence peer, IpcClientRole role, bool knownRequest)
    {
        var decision = Authenticate(peer);
        return !decision.Allowed ? decision : decision.Role == role && knownRequest
            ? decision : IpcAccessDecision.Deny("operationDenied");
    }

    private static bool IsLeafExecutable(string value) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= 100 && value.IndexOfAny(['/', '\\', ':']) < 0
        && value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && value is not ".exe";
    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool IsOwnerSid(string? value) => value is not null
        && (value.StartsWith("S-1-5-21-", StringComparison.Ordinal) || value.StartsWith("S-1-12-1-", StringComparison.Ordinal))
        && value.Split('-').Skip(4).All(part => uint.TryParse(part, out _)) && value.Split('-').Length == 8;
    private static bool IsLogonSid(string? value) => value is not null && value.StartsWith("S-1-5-5-", StringComparison.Ordinal)
        && value.Split('-').Length == 6 && value.Split('-').Skip(4).All(part => uint.TryParse(part, out _));
}
