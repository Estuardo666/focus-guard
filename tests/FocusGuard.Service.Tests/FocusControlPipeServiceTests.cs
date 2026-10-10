using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;
using FocusGuard.Contracts;
using FocusGuard.Ipc.Windows;

namespace FocusGuard.Service.Tests;

public sealed class FocusControlPipeServiceTests
{
    [Fact]
    public async Task ShutdownCancelsAnIncompleteReadWithoutStoppingTheAuthoritativeSession()
    {
        var name = "FocusGuard.Tests.ControlReadStop." + Guid.NewGuid();
        var focus = new InMemoryFocusService();
        var activated = await focus.ActivateAsync(new(Guid.NewGuid(), 300, false, 0), default);
        using var server = new FocusControlPipeService(focus, name);
        await server.StartAsync(default);
        try
        {
            await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(limit.Token);
            await client.WriteAsync(new byte[] { 1 }, limit.Token);
            await server.StopAsync(limit.Token);
            Assert.True(server.ExecuteTask!.IsCompletedSuccessfully);
            var status = await focus.GetStatusAsync(default);
            Assert.Equal(activated.Value!.SessionId, status.SessionId);
            Assert.Equal(activated.Value.EndUtc, status.EndUtc);
            Assert.Equal("Active", status.SessionState);
        }
        finally { await server.StopAsync(default); }
    }

    [Fact]
    public async Task StoppingControlListenerReleasesThePendingPipeNamespace()
    {
        var name = "FocusGuard.Tests.ControlStop." + Guid.NewGuid();
        using var server = new FocusControlPipeService(new InMemoryFocusService(), name);
        await server.StartAsync(default);
        try
        {
            await SendAsync(name, IpcProtocol.CreateStatusRequest(Guid.NewGuid()).Value!);
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await server.StopAsync(stop.Token);
            await using var probe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await Assert.ThrowsAsync<TimeoutException>(() => probe.ConnectAsync(150));
            using var replacement = new FocusControlPipeService(new InMemoryFocusService(), name);
            await replacement.StartAsync(default);
            try
            {
                var response = await SendAsync(name, IpcProtocol.CreateStatusRequest(Guid.NewGuid()).Value!);
                Assert.True(IpcProtocol.ReadStatusResponse(response).Value!.ServiceReady);
            }
            finally { await replacement.StopAsync(default); }
        }
        finally { await server.StopAsync(default); }
    }

    [Fact]
    public async Task WeeklyScheduleAndHistoryCrossTheRealControlPipe()
    {
        var directory = Path.Combine(Path.GetTempPath(), "focusguard-workflow-pipe-" + Guid.NewGuid());
        var name = "FocusGuard.Tests.Workflows." + Guid.NewGuid();
        var history = new FocusGuard.Storage.SqliteSessionHistoryStore(Path.Combine(directory, "history.db"));
        var service = new InMemoryFocusService(historyStore: history);
        var workflows = new DesktopWorkflowService(service, history, Path.Combine(directory, "schedules.json"));
        using var server = new FocusControlPipeService(service, name, workflows);
        await server.StartAsync(default);
        try
        {
            var schedule = new WeeklySchedule(Guid.NewGuid(), "Work", 62, 600, "UTC", false, new(Guid.NewGuid(), 3000, false, 0, ProfileName: "Work"));
            var saved = IpcProtocol.ReadScheduleResponse(await SendAsync(name, IpcProtocol.CreateScheduleChangeRequest(Guid.NewGuid(), new("Save", schedule.Id, schedule)).Value!));
            Assert.True(saved.Value!.Accepted);
            var listed = IpcProtocol.ReadScheduleResponse(await SendAsync(name, IpcProtocol.CreateScheduleListRequest(Guid.NewGuid()).Value!));
            Assert.Equal(schedule.Id, Assert.Single(listed.Value!.Schedules).Id);
            var start = DateTimeOffset.UtcNow.Date;
            var today = IpcProtocol.ReadTodayResponse(await SendAsync(name, IpcProtocol.CreateTodayRequest(Guid.NewGuid(), new(new(start, TimeSpan.Zero), new(start.AddDays(1), TimeSpan.Zero))).Value!));
            Assert.True(today.IsValid);
            Assert.Empty(today.Value!.Timeline);
        }
        finally { await server.StopAsync(default); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task LargeRecentActivityResponseFitsTheBoundedControlEnvelope()
    {
        var directory = Path.Combine(Path.GetTempPath(), "focusguard-activity-pipe-" + Guid.NewGuid());
        var name = "FocusGuard.Tests.Activity." + Guid.NewGuid();
        Directory.CreateDirectory(directory);
        using var audit = new JsonLinesFocusAuditStore(Path.Combine(directory, "activity.jsonl"));
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 200; index++)
        {
            var target = $"https://example.com/{new string('x', 450)}{index:D3}";
            await audit.WriteAsync(
                new FocusAuditEvent(
                    Guid.NewGuid(),
                    now.AddSeconds(-index),
                    FocusAuditEventKind.WebsiteBlocked,
                    target,
                    new string('o', 80)),
                CancellationToken.None);
        }

        var focus = new InMemoryFocusService(auditStore: audit);
        using var server = new FocusControlPipeService(focus, name);
        await server.StartAsync(default);
        try
        {
            var smallEnvelope = await SendAsync(
                name,
                IpcProtocol.CreateActivityRequest(Guid.NewGuid(), new ActivityRequest(2)).Value!);
            var smallActivity = IpcProtocol.ReadActivityResponse(smallEnvelope);
            Assert.True(smallActivity.IsValid);
            Assert.Equal(2, smallActivity.Value!.Events.Count);

            await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(timeout.Token);
            var request = IpcProtocol.CreateActivityRequest(Guid.NewGuid(), new ActivityRequest(200)).Value!;
            await BoundedPipeFraming.WriteAsync(client, IpcProtocol.WriteEnvelope(request), timeout.Token);

            var responseBytes = await BoundedPipeFraming.ReadAsync(client, timeout.Token);
            Assert.NotNull(responseBytes);
            var envelope = IpcProtocol.ReadEnvelope(responseBytes);
            Assert.True(envelope.IsValid);
            var activity = IpcProtocol.ReadActivityResponse(envelope.Value!);
            Assert.True(activity.IsValid);
            Assert.InRange(activity.Value!.Events.Count, 1, 199);
            Assert.EndsWith("000", activity.Value.Events[0].Target, StringComparison.Ordinal);
            Assert.InRange(IpcProtocol.WriteEnvelope(envelope.Value!).Length, 1, IpcProtocol.MaxPayloadBytes);

            var status = IpcProtocol.ReadStatusResponse(await SendAsync(name, IpcProtocol.CreateStatusRequest(Guid.NewGuid()).Value!));
            Assert.True(status.IsValid);
            Assert.Equal("Inactive", status.Value!.SessionState);
        }
        finally
        {
            await server.StopAsync(default);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ControlPipeActivatesReportsAndStopsNormalSession()
    {
        var pipeName = $"FocusGuard.Tests.Control.{Guid.NewGuid():N}";
        using var server = new FocusControlPipeService(
            new InMemoryFocusService(),
            pipeName);
        await server.StartAsync(CancellationToken.None);
        try
        {
            var activationRequest = new ActivateSessionRequest(
                Guid.NewGuid(),
                300,
                false,
                0);
            var activationEnvelope = IpcProtocol.CreateActivateSessionRequest(
                Guid.NewGuid(),
                activationRequest).Value!;
            var activationResponse = IpcProtocol.ReadActivateSessionResponse(
                await SendAsync(pipeName, activationEnvelope));

            var statusEnvelope = IpcProtocol.CreateStatusRequest(Guid.NewGuid()).Value!;
            var statusResponse = IpcProtocol.ReadStatusResponse(
                await SendAsync(pipeName, statusEnvelope));

            var stopEnvelope = IpcProtocol.CreateStopSessionRequest(Guid.NewGuid()).Value!;
            var stopResponse = IpcProtocol.ReadStopSessionResponse(
                await SendAsync(pipeName, stopEnvelope));

            Assert.True(activationResponse.Value!.Accepted);
            Assert.Equal("Active", statusResponse.Value!.SessionState);
            Assert.False(statusResponse.Value.EnforcementConnected);
            Assert.True(stopResponse.Value!.Accepted);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DiagnosticActivationDenialPrecedesDispatchAndKeepsListenerAvailable()
    {
        var name = $"FocusGuard.Tests.DiagnosticReadOnly.{Guid.NewGuid():N}";
        var focus = new InMemoryFocusService();
        using var server = new FocusControlPipeService(focus, name, security: new DiagnosticReadOnlyPipeSecurity());
        await server.StartAsync(default);
        try
        {
            await using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await client.ConnectAsync(timeout.Token);
                var activation = IpcProtocol.CreateActivateSessionRequest(
                    Guid.NewGuid(),
                    new ActivateSessionRequest(Guid.NewGuid(), 60, false, 0)).Value!;
                await BoundedPipeFraming.WriteAsync(client, IpcProtocol.WriteEnvelope(activation), timeout.Token);
                Assert.Null(await BoundedPipeFraming.ReadAsync(client, timeout.Token));
            }

            var status = IpcProtocol.ReadStatusResponse(
                await SendAsync(name, IpcProtocol.CreateStatusRequest(Guid.NewGuid()).Value!));
            Assert.True(status.IsValid);
            Assert.Equal("Inactive", status.Value!.SessionState);
            Assert.Equal(0, status.Value.RemainingSeconds);
            Assert.False(server.ExecuteTask!.IsCompleted);
        }
        finally
        {
            await server.StopAsync(default);
        }
    }

    [Fact]
    public async Task RejectedRemoteControlConnectionLeavesAuthenticatedListenerAvailable()
    {
        if (!OperatingSystem.IsWindows()) return;
        var name = "FocusGuard.Tests.RemoteControl." + Guid.NewGuid().ToString("N");
        using var identity = WindowsIdentity.GetCurrent();
        using var server = new FocusControlPipeService(
            new InMemoryFocusService(), name, security: new NativePipeSecurity(identity.User!.Value));
        await server.StartAsync(default);
        try
        {
            await using var remote = new NamedPipeClientStream(
                "localhost", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var connectDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var rejection = await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => remote.ConnectAsync(connectDeadline.Token));
            Assert.Equal(unchecked((int)0x80070005), rejection.HResult);
            Assert.False(server.ExecuteTask!.IsCompleted);

            var response = IpcProtocol.ReadStatusResponse(await SendAsync(
                name, IpcProtocol.CreateStatusRequest(Guid.NewGuid()).Value!));
            Assert.True(response.Value!.ServiceReady);
        }
        finally
        {
            await server.StopAsync(default);
        }
        Assert.True(server.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ControlPipeRejectsWrongCorrelationAtClientBoundary()
    {
        var request = IpcProtocol.CreateStatusRequest(Guid.NewGuid()).Value!;
        var response = IpcProtocol.CreateStatusResponse(
            Guid.NewGuid(),
            new StatusResponse(
                true,
                false,
                "Inactive",
                0,
                null,
                null,
                null,
                null,
                StrictMode: false,
                EnforcementConnected: false)).Value!;

        Assert.NotEqual(request.CorrelationId, response.CorrelationId);
    }

    private static async Task<IpcEnvelope> SendAsync(
        string pipeName,
        IpcEnvelope request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token);
        await BoundedPipeFraming.WriteAsync(
            pipe,
            IpcProtocol.WriteEnvelope(request),
            timeout.Token);
        var responseBytes = await BoundedPipeFraming.ReadAsync(pipe, timeout.Token);
        var response = IpcProtocol.ReadEnvelope(responseBytes!);
        Assert.True(response.IsValid);
        Assert.Equal(request.CorrelationId, response.Value!.CorrelationId);
        return response.Value;
    }

    [SupportedOSPlatform("windows")]
    private sealed class NativePipeSecurity(string ownerSid) : IIpcServerSecurity
    {
        public NamedPipeServerStream CreatePipe(string name, bool firstInstance, int maximumInstances) =>
            WindowsAuthenticatedPipeStreams.CreateServer(
                name,
                WindowsAuthenticatedPipeStreams.CreateOwnerSecurity(ownerSid, ownerSid),
                firstInstance,
                maximumInstances);

        public void AuthorizeControl(NamedPipeServerStream pipe, IpcMessageType requestType) { }
        public void AuthorizeAgent(NamedPipeServerStream pipe, string requestType) { }
        public void AuthorizeBrowser(NamedPipeServerStream pipe, string requestType) { }
    }

    private sealed class DiagnosticReadOnlyPipeSecurity : IIpcServerSecurity
    {
        public NamedPipeServerStream CreatePipe(string name, bool firstInstance, int maximumInstances)
        {
            var options = PipeOptions.Asynchronous | (firstInstance ? PipeOptions.FirstPipeInstance : PipeOptions.None);
            return new NamedPipeServerStream(name, PipeDirection.InOut, maximumInstances, PipeTransmissionMode.Byte, options);
        }

        public void AuthorizeControl(NamedPipeServerStream pipe, IpcMessageType requestType)
        {
            if (requestType is not (IpcMessageType.StatusRequest or IpcMessageType.ActivityRequest))
            {
                throw new InvalidDataException("Diagnostic mutation denied before dispatch.");
            }
        }

        public void AuthorizeAgent(NamedPipeServerStream pipe, string requestType) =>
            throw new InvalidDataException("Diagnostic role cannot use the Agent endpoint.");

        public void AuthorizeBrowser(NamedPipeServerStream pipe, string requestType) =>
            throw new InvalidDataException("Diagnostic role cannot use the Browser endpoint.");
    }
}
