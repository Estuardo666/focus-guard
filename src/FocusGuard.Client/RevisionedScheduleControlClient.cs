using FocusGuard.Contracts;
using System.IO.Pipes;

namespace FocusGuard.Client;

public interface IRevisionedScheduleControlClient
{
    ValueTask<RevisionedScheduleResponse> GetRevisionedSchedulesAsync(CancellationToken token);
    ValueTask<RevisionedScheduleResponse> ChangeRevisionedScheduleAsync(RevisionedScheduleChangeRequest request, CancellationToken token);
}

public sealed partial class NamedPipeFocusControlClient : IRevisionedScheduleControlClient
{
    public async ValueTask<RevisionedScheduleResponse> GetRevisionedSchedulesAsync(CancellationToken token)
    {
        var id = Guid.NewGuid(); var request = IpcProtocol.CreateRevisionedScheduleListRequest(id, includeNextEndUtc: true);
        if (!request.IsValid) throw new InvalidDataException("Invalid revisioned schedule query.");
        return DecodeRevisioned(await SendAsync(request.Value!, id, token).ConfigureAwait(false));
    }

    public async ValueTask<RevisionedScheduleResponse> ChangeRevisionedScheduleAsync(RevisionedScheduleChangeRequest change, CancellationToken token)
    {
        var id = Guid.NewGuid(); var request = IpcProtocol.CreateRevisionedScheduleChangeRequest(id, change);
        if (!request.IsValid) throw new InvalidDataException("Invalid revisioned schedule change.");
        return DecodeRevisioned(await SendAsync(request.Value!, id, token).ConfigureAwait(false));
    }

    private static RevisionedScheduleResponse DecodeRevisioned(IpcEnvelope envelope)
    {
        var decoded = IpcProtocol.ReadRevisionedScheduleResponse(envelope);
        return decoded.IsValid ? decoded.Value! : throw new InvalidDataException("Invalid revisioned schedule response.");
    }
}
