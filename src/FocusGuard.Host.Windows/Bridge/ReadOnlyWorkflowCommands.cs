using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Client;
using FocusGuard.Host.Windows.Hosting;
using System.Text.Json.Serialization;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed record BridgeActivitySpan(string SessionId, string ProfileName, string Kind, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc, bool Completed);
internal sealed record BridgeToday(long FocusSeconds, int CompletedSessions, IReadOnlyList<BridgeActivitySpan> Timeline, bool TimelineTruncated);
internal sealed record BridgeSchedule(string Id, string Name, int Days, int StartMinute, string TimeZoneId, bool Enabled,
    string ProfileName, int DurationMinutes, bool EnforcementEnabled, DateTimeOffset? NextStartUtc,
    DateTimeOffset? LastOccurrenceUtc, string LastResult, Guid? ProfileId, int ApplicationRuleCount, int WebsiteRuleCount,
    int BlockedApplicationRuleCount, int BlockedWebsiteRuleCount, string? Revision,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? NextEndUtc = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TimeZoneIanaId = null);
internal sealed record BridgeScheduleList(IReadOnlyList<BridgeSchedule> Items, bool EditingEnabled, string TimeZoneId);

internal sealed partial class NativeBridgeDispatcher
{
    private async ValueTask<(string? Error, object? Result)> HandleReadOnlyWorkflowAsync(BridgeRequest request, CancellationToken token)
    {
        if (request.Method == "today.get")
        {
            var range = request.Today!;
            var result = await client.GetTodayAsync(range, token).ConfigureAwait(false);
            if (result.FocusSeconds < 0 || result.CompletedSessions is < 0 or > 10000 || result.Timeline is null
                || result.Timeline.Count > 128 || result.TimelineTruncated && result.Timeline.Count != 128
                || result.Timeline.Any(span => span.SessionId == Guid.Empty || span.ProfileName is null || span.ProfileName.Length > 80
                    || span.Kind is not ("Focus" or "ShortBreak" or "LongBreak") || span.StartUtc.Offset != TimeSpan.Zero
                    || span.EndUtc.Offset != TimeSpan.Zero || span.EndUtc <= span.StartUtc || span.StartUtc < range.StartUtc
                    || span.EndUtc > range.EndUtc || span.ProfileId == Guid.Empty)) return ("InvalidServiceResponse", null);
            var timelineItems = result.Timeline.OrderBy(span => span.StartUtc).Select(span => new BridgeActivitySpan(
                span.SessionId.ToString("D"), span.ProfileName, span.Kind, span.StartUtc, span.EndUtc, span.Completed)).ToArray();
            return (null, new BridgeToday(result.FocusSeconds, result.CompletedSessions, timelineItems, result.TimelineTruncated));
        }

        var editingEnabled = authorization.Authorize(HostControlOperation.WriteProfiles).State == HostAuthorizationState.Authorized
            && client is IRevisionedScheduleControlClient;
        if (client is IRevisionedScheduleControlClient revisioned)
        {
            try
            {
                var revisionedSchedules = await revisioned.GetRevisionedSchedulesAsync(token).ConfigureAwait(false);
                if (revisionedSchedules.Accepted && revisionedSchedules.Schedules is { Count: <= IpcProtocol.MaximumWeeklySchedules })
                {
                    var revisionedItems = revisionedSchedules.Schedules.Select(item => item.Schedule).Select(schedule =>
                        new BridgeSchedule(schedule.Id.ToString("D"), schedule.Name, schedule.Days, schedule.StartMinute,
                            schedule.TimeZoneId, schedule.Enabled, schedule.ProfileName, schedule.DurationMinutes,
                            schedule.EnforcementEnabled, schedule.NextStartUtc, schedule.LastOccurrenceUtc, schedule.LastResult,
                            schedule.ProfileId, schedule.ApplicationRuleCount, schedule.WebsiteRuleCount,
                            revisionedSchedules.Schedules.Single(item => item.Schedule.Id == schedule.Id).BlockedApplicationRuleCount,
                            revisionedSchedules.Schedules.Single(item => item.Schedule.Id == schedule.Id).BlockedWebsiteRuleCount,
                            revisionedSchedules.Schedules.Single(item => item.Schedule.Id == schedule.Id).Revision,
                            schedule.NextEndUtc, schedule.TimeZoneIanaId)).ToArray();
                    if (ValidSchedules(revisionedItems))
                        return (null, new BridgeScheduleList(revisionedItems, editingEnabled, TimeZoneInfo.Local.Id));
                }
            }
            catch (Exception exception) when (exception is IOException or TimeoutException or InvalidDataException or OperationCanceledException)
            {
                if (token.IsCancellationRequested) throw;
                editingEnabled = false;
            }
        }
        var schedules = await client.GetSchedulesAsync(token).ConfigureAwait(false);
        if (!schedules.Accepted || schedules.Schedules is null || schedules.Schedules.Count > IpcProtocol.MaximumWeeklySchedules
            || schedules.Schedules.Any(schedule => schedule.Id == Guid.Empty || string.IsNullOrWhiteSpace(schedule.Name) || schedule.Name.Length > 80
                || schedule.Days is < 1 or > 127 || schedule.StartMinute is < 0 or > 1439 || string.IsNullOrWhiteSpace(schedule.TimeZoneId)
                || schedule.TimeZoneId.Length > 128 || string.IsNullOrWhiteSpace(schedule.ProfileName) || schedule.ProfileName.Length > 80
                || schedule.DurationMinutes is < 1 or > 720 || schedule.ApplicationRuleCount is < 0 or > 10000
                || schedule.WebsiteRuleCount is < 0 or > 10000 || schedule.LastResult is null || schedule.LastResult.Length > 32
                || schedule.NextStartUtc is { Offset: var nextOffset } && nextOffset != TimeSpan.Zero
                || schedule.LastOccurrenceUtc is { Offset: var lastOffset } && lastOffset != TimeSpan.Zero))
            return ("SchedulesUnavailable", null);
        var items = schedules.Schedules.Select(schedule => new BridgeSchedule(schedule.Id.ToString("D"), schedule.Name,
            schedule.Days, schedule.StartMinute, schedule.TimeZoneId, schedule.Enabled, schedule.ProfileName,
            schedule.DurationMinutes, schedule.EnforcementEnabled, schedule.NextStartUtc,
            schedule.LastOccurrenceUtc, schedule.LastResult, schedule.ProfileId, schedule.ApplicationRuleCount, schedule.WebsiteRuleCount, 0, 0, null)).ToArray();
        return (null, new BridgeScheduleList(items, false, TimeZoneInfo.Local.Id));
    }

    private static bool ValidSchedules(IReadOnlyList<BridgeSchedule> schedules) =>
        schedules.Count <= IpcProtocol.MaximumWeeklySchedules && schedules.All(schedule =>
            schedule.Id.Length == 36 && Guid.TryParseExact(schedule.Id, "D", out var id) && id != Guid.Empty
            && !string.IsNullOrWhiteSpace(schedule.Name) && schedule.Name.Length <= 80
            && schedule.Days is >= 1 and <= 127 && schedule.StartMinute is >= 0 and < 1440
            && schedule.DurationMinutes is >= 1 and <= 720 && !string.IsNullOrWhiteSpace(schedule.TimeZoneId)
            && schedule.TimeZoneId.Length <= 100 && !string.IsNullOrWhiteSpace(schedule.ProfileName)
            && schedule.ProfileName.Length <= 80 && schedule.LastResult is not null && schedule.LastResult.Length <= 32
            && schedule.ApplicationRuleCount is >= 0 and <= 10000 && schedule.WebsiteRuleCount is >= 0 and <= 10000
            && schedule.BlockedApplicationRuleCount >= 0 && schedule.BlockedApplicationRuleCount <= schedule.ApplicationRuleCount
            && schedule.BlockedWebsiteRuleCount >= 0 && schedule.BlockedWebsiteRuleCount <= schedule.WebsiteRuleCount
            && (schedule.Revision is null || schedule.Revision.Length == 64
                && schedule.Revision.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F'))
            && (schedule.NextStartUtc is null || schedule.NextStartUtc.Value.Offset == TimeSpan.Zero)
            && (schedule.NextEndUtc is null || schedule.NextStartUtc is not null && schedule.NextEndUtc.Value.Offset == TimeSpan.Zero
                && schedule.NextEndUtc > schedule.NextStartUtc)
            && (schedule.TimeZoneIanaId is null || TimeZoneInfo.TryConvertIanaIdToWindowsId(schedule.TimeZoneIanaId, out _))
            && (schedule.LastOccurrenceUtc is null || schedule.LastOccurrenceUtc.Value.Offset == TimeSpan.Zero));
}
