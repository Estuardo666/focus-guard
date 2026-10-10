using FocusGuard.Client;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Storage;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed partial class NativeBridgeDispatcher
{
    private async ValueTask<(string? Error, object? Result)> HandleScheduleCommandAsync(BridgeRequest request, CancellationToken token)
    {
        if (client is not IRevisionedScheduleControlClient revisioned || profileStore is null)
            return ("ScheduleEditorUnavailable", null);
        var input = request.Schedule!;
        var status = await client.GetStatusAsync(token).ConfigureAwait(false);
        if (!BridgeLimits.IsValidStatus(status)) return ("InvalidServiceResponse", null);
        if (!CanStart(status)) return ("SessionAlreadyActive", null);
        if (_profileOperationIds.Count >= 128) return ("OperationLimitReached", null);
        if (!_profileOperationIds.Add(input.OperationId!.Value)) return ("DuplicateOperation", null);
        token.ThrowIfCancellationRequested();
        if (!documentLifetime.TryGetCurrent(request.DocumentToken, out _)) return ("StaleDocument", null);

        RevisionedScheduleChangeRequest change;
        if (input.Action == "Save")
        {
            var profile = await profileStore.LoadProfileAsync(new(input.ProfileId!.Value), token).ConfigureAwait(false);
            if (profile is null) return ("ProfileMissing", null);
            if (!Profile.ValidateName(profile.Name).IsValid) return ("InvalidProfile", null);
            if (input.EnforcementEnabled && !profile.ApplicationRules.Any(rule => rule.Action == RuleAction.Block)
                && !profile.WebsiteRules.Any(rule => rule.Action == RuleAction.Block)) return ("NoBlockingRules", null);
            var activation = new ActivateSessionRequest(profile.Id.Value, checked(input.DurationMinutes * 60), false, 0,
                ApplicationRules: profile.ApplicationRules.Select(rule => new ApplicationPolicyRule(rule.Id, rule.Action.ToString(), rule.MatchKind.ToString(), rule.Value)).ToArray(),
                WebsiteRules: profile.WebsiteRules.Select(rule => new WebsitePolicyRule(rule.Id, rule.Action.ToString(), rule.Pattern)).ToArray(),
                EnforcementEnabled: input.EnforcementEnabled, ProfileName: profile.Name);
            var schedule = new WeeklySchedule(input.ScheduleId, input.Name!, input.Days, input.StartMinute,
                input.TimeZoneId!, input.Enabled, activation);
            change = new("Save", input.ScheduleId, input.ExpectedRevision, schedule, IncludeNextEndUtc: true);
        }
        else change = new(input.Action, input.ScheduleId, input.ExpectedRevision, Enabled: input.Enabled, IncludeNextEndUtc: true);

        var response = await revisioned.ChangeRevisionedScheduleAsync(change, token).ConfigureAwait(false);
        if (!response.Accepted)
            return (response.Reason switch
            {
                "Conflict" => "ScheduleConflict",
                "Overlap" => "ScheduleOverlap",
                "Limit" => "ScheduleLimit",
                "Missing" => "ScheduleMissing",
                "Invalid" => "InvalidInput",
                _ => "ScheduleRejected"
            }, null);
        return (null, Project(response));
    }

    private static BridgeScheduleList Project(RevisionedScheduleResponse response) => new(
        response.Schedules.Select(item => new BridgeSchedule(item.Schedule.Id.ToString("D"), item.Schedule.Name,
            item.Schedule.Days, item.Schedule.StartMinute, item.Schedule.TimeZoneId, item.Schedule.Enabled,
            item.Schedule.ProfileName, item.Schedule.DurationMinutes, item.Schedule.EnforcementEnabled,
            item.Schedule.NextStartUtc, item.Schedule.LastOccurrenceUtc, item.Schedule.LastResult,
            item.Schedule.ProfileId, item.Schedule.ApplicationRuleCount, item.Schedule.WebsiteRuleCount,
            item.BlockedApplicationRuleCount, item.BlockedWebsiteRuleCount, item.Revision,
            item.Schedule.NextEndUtc, item.Schedule.TimeZoneIanaId)).ToArray(),
        true, TimeZoneInfo.Local.Id);
}
