using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed record BridgeCyclePresetMutation(string? Id, string? Name, bool? IsBuiltIn,
    BridgeCycleConfiguration? Configuration, string? Revision, string Action);

internal sealed partial class NativeBridgeDispatcher
{
    private async ValueTask<(string? Error, object? Result)> HandleCyclePresetCommandAsync(BridgeRequest request, CancellationToken token)
    {
        if (profileStore is not IRevisionedCyclePresetStore store) return ("PresetEditorUnavailable", null);
        var status = await client.GetStatusAsync(token).ConfigureAwait(false);
        if (!BridgeLimits.IsValidStatus(status)) return ("InvalidServiceResponse", null);
        if (!CanStart(status)) return ("SessionAlreadyActive", null);
        if (_profileOperationIds.Count >= 128) return ("OperationLimitReached", null);
        var input = request.CyclePreset!;
        if (!_profileOperationIds.Add(input.OperationId!.Value)) return ("DuplicateOperation", null);
        _normalReview = null;
        _cycleReview = null;
        token.ThrowIfCancellationRequested();
        if (!documentLifetime.TryGetCurrent(request.DocumentToken, out _)) return ("StaleDocument", null);

        CyclePresetMutationResult result;
        if (request.Method == "cyclePreset.save")
            result = await store.TrySaveCyclePresetAsync(new(input.PresetId!.Value, input.Name!, input.Configuration!, false), input.Revision, token).ConfigureAwait(false);
        else if (request.Method == "cyclePreset.delete")
            result = await store.TryDeleteCyclePresetAsync(input.PresetId!.Value, input.Revision!, token).ConfigureAwait(false);
        else
            result = await store.TryDuplicateCyclePresetAsync(input.SourcePresetId!.Value, input.Revision!, input.Name!, token).ConfigureAwait(false);

        if (result.Status != CyclePresetMutationStatus.Saved)
            return (result.Status switch
            {
                CyclePresetMutationStatus.Conflict or CyclePresetMutationStatus.Missing => "CyclePresetConflict",
                CyclePresetMutationStatus.NameConflict => "CyclePresetNameConflict",
                CyclePresetMutationStatus.Referenced => "CyclePresetReferenced",
                CyclePresetMutationStatus.BuiltIn => "CyclePresetBuiltIn",
                CyclePresetMutationStatus.InvalidInput => "InvalidInput",
                _ => "PresetEditorUnavailable"
            }, null);
        var preset = result.Preset;
        return (null, new BridgeCyclePresetMutation(preset?.Id.ToString("D"), preset?.Name, preset?.IsBuiltIn,
            preset is null ? null : CycleConfiguration(preset.Configuration), preset is null ? null : CyclePresetRevision.Compute(preset),
            request.Method == "cyclePreset.delete" ? "Deleted" : request.Method == "cyclePreset.duplicate" ? "Duplicated" : "Saved"));
    }
}
