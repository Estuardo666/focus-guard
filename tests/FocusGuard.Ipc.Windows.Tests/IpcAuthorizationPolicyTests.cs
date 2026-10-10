using FocusGuard.Contracts;

namespace FocusGuard.Ipc.Windows.Tests;

public sealed class IpcAuthorizationPolicyTests
{
    private const string OwnerSid = "S-1-5-21-1-2-3-1001";
    private const string LogonSid = "S-1-5-5-10-20";
    private static readonly string Root = Path.GetFullPath("C:/Program Files/FocusGuard");
    private static readonly string Hash = new('A', 64);
    private static IpcAuthorizationPolicy Policy() => new(new(OwnerSid, LogonSid, 1), Root,
        Enum.GetValues<IpcClientRole>().Select(role => new TrustedIpcBinary(role, role + ".exe", Hash)));
    private static IpcPeerEvidence Peer(IpcClientRole role) =>
        new(42, OwnerSid, LogonSid, 1, Path.Combine(Root, role + ".exe"), Hash, true, true, true);

    [Theory]
    [InlineData(IpcClientRole.Desktop)]
    [InlineData(IpcClientRole.WebViewHost)]
    public void InteractiveUiRolesCanReadAndMutate(IpcClientRole role)
    {
        Assert.True(Policy().AuthorizeControl(Peer(role), IpcMessageType.StatusRequest).Allowed);
        Assert.True(Policy().AuthorizeControl(Peer(role), IpcMessageType.StopSessionRequest).Allowed);
        Assert.True(Policy().AuthorizeControl(Peer(role), IpcMessageType.ReviewedStopSessionRequest).Allowed);
        Assert.True(Policy().AuthorizeControl(Peer(role), IpcMessageType.ReviewedSkipBreakRequest).Allowed);
        Assert.True(Policy().AuthorizeControl(Peer(role), IpcMessageType.ActivatePreparedSessionRequest).Allowed);
        Assert.True(Policy().AuthorizeControl(Peer(role), IpcMessageType.ScheduleChangeRequest).Allowed);
    }

    [Fact]
    public void DiagnosticRoleCanOnlyReadStatusAndActivity()
    {
        var peer = Peer(IpcClientRole.Diagnostic);
        foreach (var requestType in Enum.GetValues<IpcMessageType>())
        {
            var expected = requestType is IpcMessageType.StatusRequest or IpcMessageType.ActivityRequest;
            Assert.Equal(expected, Policy().AuthorizeControl(peer, requestType).Allowed);
        }
    }

    [Theory]
    [InlineData(IpcClientRole.Desktop)]
    [InlineData(IpcClientRole.WebViewHost)]
    [InlineData(IpcClientRole.Diagnostic)]
    [InlineData(IpcClientRole.ProcessAgent)]
    [InlineData(IpcClientRole.NativeMessaging)]
    public void ChannelRolesDoNotCrossAuthorize(IpcClientRole role)
    {
        Assert.Equal(role == IpcClientRole.ProcessAgent, Policy().AuthorizeAgent(Peer(role), ProcessAgentProtocol.HeartbeatType).Allowed);
        Assert.Equal(role == IpcClientRole.ProcessAgent, Policy().AuthorizeAgent(Peer(role), ProcessAgentProtocol.AuditType).Allowed);
        Assert.Equal(role == IpcClientRole.NativeMessaging, Policy().AuthorizeBrowser(Peer(role), ExtensionHealthProtocol.HeartbeatType).Allowed);
        Assert.Equal(role == IpcClientRole.NativeMessaging, Policy().AuthorizeBrowser(Peer(role), ExtensionHealthProtocol.CaptureType).Allowed);
        Assert.Equal(role == IpcClientRole.NativeMessaging, Policy().AuthorizeBrowser(Peer(role), ExtensionHealthProtocol.StatusType).Allowed);
    }

    [Theory]
    [InlineData("pid")]
    [InlineData("dead")]
    [InlineData("owner")]
    [InlineData("logon")]
    [InlineData("session")]
    [InlineData("hash")]
    [InlineData("temp-copy")]
    [InlineData("writable-image")]
    [InlineData("writable-root")]
    [InlineData("alias")]
    public void UntrustedEvidenceIsRejected(string kind)
    {
        var peer = Peer(IpcClientRole.Desktop);
        peer = kind switch
        {
            "pid" => peer with { ProcessId = 0 },
            "dead" => peer with { ProcessAlive = false },
            "owner" => peer with { UserSid = "S-1-5-21-1-2-3-1002" },
            "logon" => peer with { LogonSid = "S-1-5-5-10-21" },
            "session" => peer with { SessionId = 2 },
            "hash" => peer with { ImageSha256 = new string('B', 64) },
            "temp-copy" => peer with { FinalImagePath = Path.Combine(Path.GetTempPath(), "Desktop.exe") },
            "writable-image" => peer with { ImageProtected = false },
            "writable-root" => peer with { RootProtected = false },
            _ => peer with { FinalImagePath = Path.Combine(Root, "nested", "..", "Desktop.exe") }
        };
        Assert.False(Policy().AuthorizeControl(peer, IpcMessageType.StatusRequest).Allowed);
    }

    [Fact]
    public void UnknownMessagesAndResponseTypesCannotAuthorizeAnOperation()
    {
        Assert.False(Policy().AuthorizeControl(Peer(IpcClientRole.Desktop), (IpcMessageType)999).Allowed);
        Assert.False(Policy().AuthorizeControl(Peer(IpcClientRole.Desktop), IpcMessageType.StatusResponse).Allowed);
        Assert.False(Policy().AuthorizeAgent(Peer(IpcClientRole.ProcessAgent), "stop").Allowed);
        Assert.False(Policy().AuthorizeBrowser(Peer(IpcClientRole.NativeMessaging), "recover").Allowed);
    }

    [Theory]
    [InlineData("S-1-5-18", "S-1-5-5-10-20", 1)]
    [InlineData("S-1-5-19", "S-1-5-5-10-20", 1)]
    [InlineData(OwnerSid, "S-1-5-32-544", 1)]
    [InlineData(OwnerSid, LogonSid, 0)]
    public void ServiceOrNoninteractiveIdentityCannotBecomeOwner(string sid, string logon, int session)
    {
        Assert.Throws<ArgumentException>(() => new IpcAuthorizationPolicy(new(sid, logon, (uint)session), Root,
            [new(IpcClientRole.Desktop, "Desktop.exe", Hash)]));
    }

    [Theory]
    [InlineData("../Desktop.exe")]
    [InlineData("Desktop.exe:stream")]
    [InlineData(".exe")]
    [InlineData("C:/Temp/Desktop.exe")]
    public void ManifestCannotAuthorizeAnEscapingPath(string fileName) =>
        Assert.Throws<ArgumentException>(() => new IpcAuthorizationPolicy(new(OwnerSid, LogonSid, 1), Root,
            [new(IpcClientRole.Desktop, fileName, Hash)]));
}
