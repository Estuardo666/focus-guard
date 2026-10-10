using FocusGuard.Contracts;

namespace FocusGuard.Client;

public sealed partial class NamedPipeFocusControlClient
{
    public async ValueTask<TodayResponse> GetTodayAsync(TodayRequest request, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var envelope = IpcProtocol.CreateTodayRequest(id, request);
        if (!envelope.IsValid) throw new InvalidDataException(string.Join(" ", envelope.Errors));
        var result = IpcProtocol.ReadTodayResponse(await SendAsync(envelope.Value!, id, token));
        return result.IsValid ? result.Value! : throw new InvalidDataException(string.Join(" ", result.Errors));
    }
    public async ValueTask<ScheduleResponse> GetSchedulesAsync(CancellationToken token)
    {
        var id = Guid.NewGuid();
        return Decode(await SendAsync(IpcProtocol.CreateScheduleListRequest(id).Value!, id, token));
    }
    public async ValueTask<ScheduleResponse> ChangeScheduleAsync(ScheduleChangeRequest request, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var envelope = IpcProtocol.CreateScheduleChangeRequest(id, request);
        if (!envelope.IsValid) throw new InvalidDataException(string.Join(" ", envelope.Errors));
        return Decode(await SendAsync(envelope.Value!, id, token));
    }
    private static ScheduleResponse Decode(IpcEnvelope envelope)
    {
        var result = IpcProtocol.ReadScheduleResponse(envelope);
        return result.IsValid ? result.Value! : throw new InvalidDataException(string.Join(" ", result.Errors));
    }
}
