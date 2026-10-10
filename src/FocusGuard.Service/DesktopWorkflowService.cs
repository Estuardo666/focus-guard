using System.Text.Json;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Storage;
using Microsoft.Extensions.Hosting;

namespace FocusGuard.Service;

public sealed record PersistedSchedule(WeeklySchedule Schedule, DateTimeOffset NotBeforeUtc, DateTimeOffset? LastOccurrenceUtc = null, string LastResult = "");

public sealed class DesktopWorkflowService(IFocusService focusService, ISessionHistoryStore historyStore, string? schedulePath = null, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = schedulePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FocusGuard", "weekly-schedules.json");
    private List<PersistedSchedule>? _schedules;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public async Task<TodayResponse> TodayAsync(TodayRequest request, CancellationToken token)
    {
        var entries = await historyStore.ListRangeAsync(request.StartUtc, request.EndUtc, token);
        var spans = new List<ActivitySpan>();
        long seconds = 0;
        foreach (var e in entries)
        {
            if (e.Phases is null)
            {
                // Old records do not have phase evidence. Retain their reported minutes, without inventing break spans.
                if (e.EndedAtUtc >= request.StartUtc && e.EndedAtUtc < request.EndUtc) seconds += e.FocusedMinutes * 60L;
                continue;
            }
            foreach (var phase in e.Phases)
            {
                var start = phase.StartUtc > request.StartUtc ? phase.StartUtc : request.StartUtc;
                var end = phase.EndUtc < request.EndUtc ? phase.EndUtc : request.EndUtc;
                if (end <= start) continue;
                if (phase.Kind == SessionPhaseKind.Focus) seconds += (long)(end - start).TotalSeconds;
                spans.Add(new(e.SessionId, e.ProfileName, phase.Kind.ToString(), start, end, e.Completed, e.ProfileId.Value));
            }
        }
        return new(seconds, entries.Count(e => e.Completed && e.EndedAtUtc >= request.StartUtc && e.EndedAtUtc < request.EndUtc),
            spans.OrderBy(s => s.StartUtc).TakeLast(128).ToArray(), spans.Count > 128);
    }

    public async Task<ScheduleResponse> ListAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try { await LoadAsync(token); return Response(true, ""); }
        finally { _gate.Release(); }
    }

    public async Task<ScheduleResponse> ChangeAsync(ScheduleChangeRequest change, CancellationToken token)
    {
        if (!IpcProtocol.ValidateScheduleChange(change).IsValid) return new(false, "Invalid", []);
        await _gate.WaitAsync(token);
        try
        {
            await LoadAsync(token);
            var updated = new List<PersistedSchedule>(_schedules!);
            var index = updated.FindIndex(s => s.Schedule.Id == change.Id);
            if (change.Action == "Save")
            {
                if (index < 0 && updated.Count >= IpcProtocol.MaximumWeeklySchedules) return Response(false, "Limit");
                var old = index < 0 ? null : updated[index];
                var value = new PersistedSchedule(change.Schedule!, _clock.GetUtcNow(), old?.LastOccurrenceUtc, old?.LastResult ?? "");
                if (index < 0) updated.Add(value); else updated[index] = value;
            }
            else if (index < 0) return Response(false, "Missing");
            else if (change.Action == "Delete") updated.RemoveAt(index);
            else updated[index] = updated[index] with { Schedule = updated[index].Schedule with { Enabled = change.Enabled }, NotBeforeUtc = _clock.GetUtcNow() };
            if (HasOverlap(updated)) return Response(false, "Overlap");
            await SaveAsync(updated, token);
            _schedules = updated;
            return Response(true, "Saved");
        }
        finally { _gate.Release(); }
    }

    public async Task<RevisionedScheduleResponse> ListRevisionedAsync(CancellationToken token, bool includeNextEndUtc = false)
    {
        await _gate.WaitAsync(token);
        try { await LoadAsync(token); return RevisionedResponse(true, "", includeNextEndUtc); }
        finally { _gate.Release(); }
    }

    public async Task<RevisionedScheduleResponse> ChangeRevisionedAsync(RevisionedScheduleChangeRequest change, CancellationToken token)
    {
        if (!IpcProtocol.ValidateRevisionedScheduleChange(change).IsValid) return new(false, "Invalid", []);
        await _gate.WaitAsync(token);
        try
        {
            await LoadAsync(token);
            var updated = new List<PersistedSchedule>(_schedules!);
            var index = updated.FindIndex(item => item.Schedule.Id == change.Id);
            var current = index < 0 ? null : updated[index];
            if (current is null)
            {
                if (change.Action != "Save" || change.ExpectedRevision is not null) return RevisionedResponse(false, "Conflict", change.IncludeNextEndUtc);
                if (updated.Count >= IpcProtocol.MaximumWeeklySchedules) return RevisionedResponse(false, "Limit", change.IncludeNextEndUtc);
            }
            else if (change.ExpectedRevision is null || WeeklyScheduleRevision.Compute(current.Schedule) != change.ExpectedRevision)
                return RevisionedResponse(false, "Conflict", change.IncludeNextEndUtc);

            if (change.Action == "Save")
            {
                var previous = current;
                var next = new PersistedSchedule(change.Schedule!, _clock.GetUtcNow(), previous?.LastOccurrenceUtc, previous?.LastResult ?? "");
                if (index < 0) updated.Add(next); else updated[index] = next;
            }
            else if (change.Action == "Delete") updated.RemoveAt(index);
            else updated[index] = current! with { Schedule = current.Schedule with { Enabled = change.Enabled }, NotBeforeUtc = _clock.GetUtcNow() };
            if (HasOverlap(updated)) return RevisionedResponse(false, "Overlap", change.IncludeNextEndUtc);
            await SaveAsync(updated, token);
            _schedules = updated;
            return RevisionedResponse(true, "Saved", change.IncludeNextEndUtc);
        }
        finally { _gate.Release(); }
    }

    public async Task TickAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            await LoadAsync(token);
            var now = _clock.GetUtcNow();
            foreach (var state in _schedules!.ToArray())
            {
                var s = state.Schedule;
                if (!s.Enabled) continue;
                var occurrence = Occurrences(s, now).LastOrDefault(o => o.StartUtc <= now && o.StartUtc >= state.NotBeforeUtc && (state.LastOccurrenceUtc is null || o.StartUtc > state.LastOccurrenceUtc));
                if (occurrence is null) continue;
                var index = _schedules.FindIndex(p => p.Schedule.Id == s.Id);
                // Persist the claim BEFORE activation. A crash can lose one normal session, but can never replay it.
                _schedules[index] = state with { LastOccurrenceUtc = occurrence.StartUtc, LastResult = "Claimed" };
                try { await SaveAsync(_schedules, token); }
                catch { _schedules[index] = state; throw; }
                var result = "Missed";
                if (occurrence.EndUtc - now >= Session.MinimumScheduledDuration)
                {
                    var status = await focusService.GetStatusAsync(token);
                    if (status.SessionState is "Active" or "Activating") result = "Busy";
                    else
                    {
                        var activated = await focusService.ActivateScheduledAsync(s.Activation, occurrence.EndUtc, token);
                        result = activated.IsValid && activated.Value!.Accepted ? "Started" : "Rejected";
                    }
                }
                _schedules[index] = _schedules[index] with { LastResult = result };
                await SaveAsync(_schedules, token);
            }
        }
        finally { _gate.Release(); }
    }

    private static IReadOnlyList<WeeklyOccurrence> Occurrences(WeeklySchedule s, DateTimeOffset now) =>
        WeeklyOccurrenceCalculator.Around(now, s.Days, s.StartMinute, s.Activation.DurationSeconds, s.TimeZoneId);

    private bool HasOverlap(List<PersistedSchedule> list)
    {
        var spans = list.Where(s => s.Schedule.Enabled).SelectMany(s => Occurrences(s.Schedule, _clock.GetUtcNow()).Select(o => (s.Schedule.Id, o))).OrderBy(p => p.o.StartUtc).ToArray();
        for (var i = 0; i < spans.Length; i++)
            for (var j = i + 1; j < spans.Length && spans[j].o.StartUtc < spans[i].o.EndUtc; j++)
                if (spans[i].Id != spans[j].Id) return true;
        return false;
    }

    private ScheduleResponse Response(bool accepted, string reason) => new(accepted, reason, ScheduleViews());

    private RevisionedScheduleResponse RevisionedResponse(bool accepted, string reason, bool includeNextEndUtc = false) => new(accepted, reason,
        ScheduleViews(includeNextEndUtc).Select(view =>
        {
            var schedule = _schedules!.Single(item => item.Schedule.Id == view.Id).Schedule;
            return new RevisionedScheduleView(view, WeeklyScheduleRevision.Compute(schedule),
                schedule.Activation.ApplicationRules?.Count(rule => rule.Action == "Block") ?? 0,
                schedule.Activation.WebsiteRules?.Count(rule => rule.Action == "Block") ?? 0);
        }).ToArray());

    private IReadOnlyList<ScheduleView> ScheduleViews(bool includeNextEndUtc = false)
    {
        var now = _clock.GetUtcNow();
        return _schedules!.Select(p =>
        {
            var s = p.Schedule;
            var next = s.Enabled ? Occurrences(s, now).FirstOrDefault(o => o.StartUtc >= now && o.StartUtc >= p.NotBeforeUtc
                && (p.LastOccurrenceUtc is null || o.StartUtc > p.LastOccurrenceUtc)) : null;
            var timeZoneIanaId = includeNextEndUtc && TimeZoneInfo.TryConvertWindowsIdToIanaId(s.TimeZoneId, out var ianaId) ? ianaId : null;
            return new ScheduleView(s.Id, s.Name, s.Days, s.StartMinute, s.TimeZoneId, s.Enabled, s.Activation.ProfileName ?? "Focus", s.Activation.DurationSeconds / 60,
                s.Activation.EnforcementEnabled, next?.StartUtc, p.LastOccurrenceUtc, p.LastResult, s.Activation.ProfileId,
                s.Activation.ApplicationRules?.Count ?? 0, s.Activation.WebsiteRules?.Count ?? 0,
                includeNextEndUtc ? next?.EndUtc : null, timeZoneIanaId);
        }).ToArray();
    }

    private async Task LoadAsync(CancellationToken token)
    {
        if (_schedules is not null) return;
        if (!File.Exists(_path)) { _schedules = []; return; }
        if (new FileInfo(_path).Length > 1024 * 1024) throw new InvalidDataException("Schedule store exceeds its bound.");
        var loaded = JsonSerializer.Deserialize<List<PersistedSchedule>>(await File.ReadAllTextAsync(_path, token), JsonOptions);
        if (loaded is null || loaded.Count > IpcProtocol.MaximumWeeklySchedules || loaded.Any(s => s is null || !IpcProtocol.ValidateWeeklySchedule(s.Schedule).IsValid
            || s.NotBeforeUtc.Offset != TimeSpan.Zero || s.LastOccurrenceUtc?.Offset != TimeSpan.Zero && s.LastOccurrenceUtc is not null)
            || loaded.Select(s => s.Schedule.Id).Distinct().Count() != loaded.Count)
            throw new InvalidDataException("Invalid schedule store; no schedule was activated.");
        _schedules = loaded;
    }

    private async Task SaveAsync(List<PersistedSchedule> schedules, CancellationToken token)
    {
        var content = JsonSerializer.Serialize(schedules, JsonOptions);
        if (System.Text.Encoding.UTF8.GetByteCount(content) > 1024 * 1024) throw new InvalidDataException("Schedule store exceeds its bound.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temporary = _path + ".tmp";
        try { await File.WriteAllTextAsync(temporary, content, token); File.Move(temporary, _path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class WeeklyScheduleWorker(DesktopWorkflowService workflows, TimeProvider? timeProvider = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var clock = timeProvider ?? TimeProvider.System;
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(5);
            try { await workflows.TickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { await ServiceFailureLog.WriteAsync(nameof(WeeklyScheduleWorker), exception); delay = TimeSpan.FromSeconds(30); }
            try { await Task.Delay(delay, clock, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
