using FocusGuard.Domain;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusGuard.Contracts;

public sealed record TodayRequest(DateTimeOffset StartUtc, DateTimeOffset EndUtc);
public sealed record ActivitySpan(Guid SessionId, string ProfileName, string Kind, DateTimeOffset StartUtc, DateTimeOffset EndUtc, bool Completed, Guid? ProfileId = null);
public sealed record TodayResponse(long FocusSeconds, int CompletedSessions, IReadOnlyList<ActivitySpan> Timeline, bool TimelineTruncated = false);

// Immutable normal-mode policy snapshot: editing a profile never changes an enabled schedule.
public sealed record WeeklySchedule(Guid Id, string Name, int Days, int StartMinute, string TimeZoneId, bool Enabled, ActivateSessionRequest Activation);
public sealed record ScheduleListRequest;
public sealed record ScheduleChangeRequest(string Action, Guid Id, WeeklySchedule? Schedule = null, bool Enabled = false);
public sealed record ScheduleView(Guid Id, string Name, int Days, int StartMinute, string TimeZoneId, bool Enabled, string ProfileName, int DurationMinutes, bool EnforcementEnabled, DateTimeOffset? NextStartUtc, DateTimeOffset? LastOccurrenceUtc, string LastResult, Guid? ProfileId = null, int ApplicationRuleCount = 0, int WebsiteRuleCount = 0, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? NextEndUtc = null, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TimeZoneIanaId = null);
public sealed record ScheduleResponse(bool Accepted, string Reason, IReadOnlyList<ScheduleView> Schedules);
public sealed record RevisionedScheduleListRequest([property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool IncludeNextEndUtc = false);
public sealed record RevisionedScheduleChangeRequest(string Action, Guid Id, string? ExpectedRevision, WeeklySchedule? Schedule = null, bool Enabled = false, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool IncludeNextEndUtc = false);
public sealed record RevisionedScheduleView(ScheduleView Schedule, string Revision, int BlockedApplicationRuleCount, int BlockedWebsiteRuleCount);
public sealed record RevisionedScheduleResponse(bool Accepted, string Reason, IReadOnlyList<RevisionedScheduleView> Schedules);

public static class WeeklyScheduleRevision
{
    // Conflict fingerprint only; not an authorization credential or monotonic version.
    public static string Compute(WeeklySchedule schedule)
    {
        var activation = schedule.Activation;
        var canonical = new
        {
            schedule.Id, Name = schedule.Name.Trim(), schedule.Days, schedule.StartMinute, TimeZoneId = schedule.TimeZoneId,
            schedule.Enabled, activation.ProfileId, ProfileName = activation.ProfileName?.Trim(), activation.DurationSeconds,
            activation.StrictMode, activation.RecoveryDelaySeconds, activation.EnforcementEnabled,
            Applications = (activation.ApplicationRules ?? []).OrderBy(rule => rule.Id).ToArray(),
            Websites = (activation.WebsiteRules ?? []).OrderBy(rule => rule.Id).ToArray()
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical)));
    }
}

public static partial class IpcProtocol
{
    public const int MaximumWeeklySchedules = 12;

    public static IpcCodecResult<IpcEnvelope> CreateRevisionedScheduleListRequest(Guid id, bool includeNextEndUtc = false) =>
        CreateEnvelope(IpcMessageType.RevisionedScheduleListRequest, id, new RevisionedScheduleListRequest(includeNextEndUtc));

    public static IpcCodecResult<RevisionedScheduleListRequest> ReadRevisionedScheduleListRequest(IpcEnvelope e) =>
        ReadPayload<RevisionedScheduleListRequest>(e, IpcMessageType.RevisionedScheduleListRequest, [], optionalProperties: ["includeNextEndUtc"]);

    public static IpcCodecResult<IpcEnvelope> CreateRevisionedScheduleChangeRequest(Guid id, RevisionedScheduleChangeRequest request) =>
        ValidateRevisionedScheduleChange(request).IsValid
            ? CreateEnvelope(IpcMessageType.RevisionedScheduleChangeRequest, id, request)
            : IpcCodecResult<IpcEnvelope>.Failure("Invalid revisioned schedule change.");

    public static IpcCodecResult<RevisionedScheduleChangeRequest> ReadRevisionedScheduleChangeRequest(IpcEnvelope e) =>
        ReadPayload<RevisionedScheduleChangeRequest>(e, IpcMessageType.RevisionedScheduleChangeRequest,
            ["action", "id", "expectedRevision", "schedule", "enabled"], ValidateRevisionedScheduleChange, ["includeNextEndUtc"]);

    public static IpcCodecResult<IpcEnvelope> CreateRevisionedScheduleResponse(Guid id, RevisionedScheduleResponse response) =>
        CreateEnvelope(IpcMessageType.RevisionedScheduleResponse, id, response);

    public static IpcCodecResult<RevisionedScheduleResponse> ReadRevisionedScheduleResponse(IpcEnvelope e) =>
        ReadPayload<RevisionedScheduleResponse>(e, IpcMessageType.RevisionedScheduleResponse,
            ["accepted", "reason", "schedules"], response => response is not null && response.Reason is { Length: <= 500 }
                && response.Schedules is { Count: <= MaximumWeeklySchedules } && response.Schedules.All(item => item is not null
                    && item.Revision is { Length: 64 } && item.Revision.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F')
                    && item.BlockedApplicationRuleCount is >= 0 and <= MaximumPolicyRuleCount
                    && item.BlockedWebsiteRuleCount is >= 0 and <= MaximumPolicyRuleCount
                    && item.Schedule is not null && ValidateScheduleView(item.Schedule).IsValid)
                ? IpcValidationResult.Success : new(["Invalid revisioned schedule response."]));

    public static IpcValidationResult ValidateRevisionedScheduleChange(RevisionedScheduleChangeRequest? request) =>
        request is not null && request.Id != Guid.Empty && (request.ExpectedRevision is null || IsScheduleRevision(request.ExpectedRevision))
        && (request.Action == "Save" && request.Schedule?.Id == request.Id && ValidateWeeklySchedule(request.Schedule).IsValid
            || request.Action is "Delete" or "Enable" && request.Schedule is null && IsScheduleRevision(request.ExpectedRevision))
            ? IpcValidationResult.Success : new(["Invalid revisioned schedule action or fields."]);

    private static bool IsScheduleRevision(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static IpcValidationResult ValidateScheduleView(ScheduleView schedule) =>
        schedule.Id != Guid.Empty && schedule.Name is { Length: > 0 and <= 80 } && schedule.ProfileName is { Length: > 0 and <= 80 }
        && schedule.Days is >= 1 and <= 127 && schedule.StartMinute is >= 0 and < 1440 && schedule.DurationMinutes is >= 1 and <= 720
        && schedule.LastResult is { Length: <= 500 } && schedule.ApplicationRuleCount is >= 0 and <= MaximumPolicyRuleCount
        && schedule.WebsiteRuleCount is >= 0 and <= MaximumPolicyRuleCount && schedule.TimeZoneId is { Length: > 0 and <= 100 }
        && (schedule.NextStartUtc is null || schedule.NextStartUtc.Value.Offset == TimeSpan.Zero)
        && (schedule.NextEndUtc is null || schedule.NextStartUtc is not null && schedule.NextEndUtc.Value.Offset == TimeSpan.Zero && schedule.NextEndUtc > schedule.NextStartUtc)
        && (schedule.TimeZoneIanaId is null || TimeZoneInfo.TryConvertIanaIdToWindowsId(schedule.TimeZoneIanaId, out _))
        && (schedule.LastOccurrenceUtc is null || schedule.LastOccurrenceUtc.Value.Offset == TimeSpan.Zero)
        ? IpcValidationResult.Success : new(["Invalid schedule view."]);

    public static IpcCodecResult<IpcEnvelope> CreateTodayRequest(Guid id, TodayRequest request) =>
        ValidateToday(request).IsValid ? CreateEnvelope(IpcMessageType.TodayRequest, id, request) : IpcCodecResult<IpcEnvelope>.Failure("Invalid day range.");
    public static IpcCodecResult<TodayRequest> ReadTodayRequest(IpcEnvelope envelope) =>
        ReadPayload<TodayRequest>(envelope, IpcMessageType.TodayRequest, ["startUtc", "endUtc"], ValidateToday);
    private static IpcValidationResult ValidateToday(TodayRequest? request) =>
        request is not null && request.StartUtc.Offset == TimeSpan.Zero && request.EndUtc.Offset == TimeSpan.Zero
        && request.EndUtc > request.StartUtc && request.EndUtc - request.StartUtc <= TimeSpan.FromDays(2)
            ? IpcValidationResult.Success : new(["Day range must be UTC and at most two days."]);
    public static IpcCodecResult<IpcEnvelope> CreateTodayResponse(Guid id, TodayResponse response) => CreateEnvelope(IpcMessageType.TodayResponse, id, response);
    public static IpcCodecResult<TodayResponse> ReadTodayResponse(IpcEnvelope envelope) =>
        ReadPayload<TodayResponse>(envelope, IpcMessageType.TodayResponse, ["focusSeconds", "completedSessions", "timeline", "timelineTruncated"], r =>
            r is not null && r.FocusSeconds >= 0 && r.CompletedSessions >= 0 && r.Timeline is { Count: <= 128 }
            && r.Timeline.All(s => s is not null && s.SessionId != Guid.Empty && s.ProfileName is { Length: <= 80 }
                && s.Kind is "Focus" or "ShortBreak" or "LongBreak" && s.StartUtc.Offset == TimeSpan.Zero
                && s.EndUtc.Offset == TimeSpan.Zero && s.EndUtc > s.StartUtc)
                ? IpcValidationResult.Success : new(["Invalid Today response."]));
    public static IpcCodecResult<IpcEnvelope> CreateScheduleListRequest(Guid id) => CreateEnvelope(IpcMessageType.ScheduleListRequest, id, new ScheduleListRequest());
    public static IpcCodecResult<ScheduleListRequest> ReadScheduleListRequest(IpcEnvelope e) => ReadPayload<ScheduleListRequest>(e, IpcMessageType.ScheduleListRequest, []);
    public static IpcCodecResult<IpcEnvelope> CreateScheduleChangeRequest(Guid id, ScheduleChangeRequest r) =>
        ValidateScheduleChange(r).IsValid ? CreateEnvelope(IpcMessageType.ScheduleChangeRequest, id, r) : IpcCodecResult<IpcEnvelope>.Failure("Invalid schedule change.");
    public static IpcCodecResult<ScheduleChangeRequest> ReadScheduleChangeRequest(IpcEnvelope e) =>
        ReadPayload<ScheduleChangeRequest>(e, IpcMessageType.ScheduleChangeRequest, ["action", "id", "schedule", "enabled"], ValidateScheduleChange);
    public static IpcValidationResult ValidateScheduleChange(ScheduleChangeRequest? r) =>
        r is not null && r.Id != Guid.Empty && (r.Action == "Save" && r.Schedule?.Id == r.Id && ValidateWeeklySchedule(r.Schedule).IsValid
            || r.Action is "Delete" or "Enable" && r.Schedule is null)
                ? IpcValidationResult.Success : new(["Invalid schedule action or fields."]);
    public static IpcValidationResult ValidateWeeklySchedule(WeeklySchedule? s)
    {
        if (s is null || s.Id == Guid.Empty || string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 80
            || s.Days is < 1 or > 127 || s.StartMinute is < 0 or >= 1440 || string.IsNullOrWhiteSpace(s.TimeZoneId)
            || s.TimeZoneId.Length > 100 || s.Activation is null || s.Activation.StrictMode
            || s.Activation.DurationSeconds is < 60 or > 43200 || !ValidateActivateSessionRequest(s.Activation).IsValid)
            return new(["Schedules require days, local time, a normal profile and a finite duration (1–720 minutes)."]);
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(s.TimeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return new(["Unknown time zone."]); }
        return IpcValidationResult.Success;
    }
    public static IpcCodecResult<IpcEnvelope> CreateScheduleResponse(Guid id, ScheduleResponse response) => CreateEnvelope(IpcMessageType.ScheduleResponse, id, response);
    public static IpcCodecResult<ScheduleResponse> ReadScheduleResponse(IpcEnvelope e) =>
        ReadPayload<ScheduleResponse>(e, IpcMessageType.ScheduleResponse, ["accepted", "reason", "schedules"], r =>
            r is not null && r.Reason is { Length: <= 500 } && r.Schedules is { Count: <= MaximumWeeklySchedules }
            && r.Schedules.All(s => s is not null && s.Id != Guid.Empty && s.Name is { Length: <= 80 }
                && s.ProfileName is { Length: <= 80 } && s.Days is >= 1 and <= 127 && s.StartMinute is >= 0 and < 1440
                && s.DurationMinutes is >= 1 and <= 720 && s.LastResult is { Length: <= 500 }
                && s.ApplicationRuleCount is >= 0 and <= MaximumPolicyRuleCount && s.WebsiteRuleCount is >= 0 and <= MaximumPolicyRuleCount
                && s.TimeZoneId is { Length: > 0 and <= 100 })
                ? IpcValidationResult.Success : new(["Invalid schedule response."]));
}
