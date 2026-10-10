using FocusGuard.Contracts;

namespace FocusGuard.Contracts.Tests;

public sealed class DesktopWorkflowProtocolTests
{
    [Fact]
    public void ScheduleRoundTripValidatesNestedFieldsAndRejectsStrictMode()
    {
        var s = new WeeklySchedule(Guid.NewGuid(), "Work", 62, 600, "UTC", true, new(Guid.NewGuid(), 3000, false, 0));
        var envelope = IpcProtocol.CreateScheduleChangeRequest(Guid.NewGuid(), new("Save", s.Id, s));
        Assert.True(envelope.IsValid);
        Assert.True(IpcProtocol.ReadScheduleChangeRequest(envelope.Value!).IsValid);
        Assert.False(IpcProtocol.ValidateWeeklySchedule(s with { Activation = s.Activation with { StrictMode = true } }).IsValid);
        Assert.False(IpcProtocol.ReadScheduleChangeRequest(envelope.Value! with { PayloadJson = envelope.Value.PayloadJson.Replace("\"enabled\":true", "\"unknown\":true") }).IsValid);
    }
    [Theory]
    [InlineData(0, 600, "UTC")]
    [InlineData(128, 600, "UTC")]
    [InlineData(62, 1440, "UTC")]
    [InlineData(62, 600, "Missing/Zone")]
    public void InvalidWeeklyFieldsCannotCrossThePipe(int days, int minute, string zone)
    {
        var s = new WeeklySchedule(Guid.NewGuid(), "Work", days, minute, zone, true, new(Guid.NewGuid(), 3000, false, 0));
        Assert.False(IpcProtocol.CreateScheduleChangeRequest(Guid.NewGuid(), new("Save", s.Id, s)).IsValid);
    }
    [Fact]
    public void TodayBoundsRejectExcessiveRangesAndUnknownFields()
    {
        var start = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        Assert.False(IpcProtocol.CreateTodayRequest(Guid.NewGuid(), new(start, start.AddDays(3))).IsValid);
        var e = IpcProtocol.CreateTodayRequest(Guid.NewGuid(), new(start, start.AddDays(1))).Value!;
        Assert.True(IpcProtocol.ReadTodayRequest(e).IsValid);
        Assert.False(IpcProtocol.ReadTodayRequest(e with { PayloadJson = e.PayloadJson.TrimEnd('}') + ",\"unknown\":1}" }).IsValid);
    }

    [Fact]
    public void RevisionedScheduleEndIsOptInForLegacyAndNewClientsAndValidatedAsAnInterval()
    {
        var id = Guid.NewGuid();
        var legacyRequest = IpcProtocol.CreateRevisionedScheduleListRequest(id);
        Assert.Equal("{}", legacyRequest.Value!.PayloadJson);
        Assert.False(IpcProtocol.ReadRevisionedScheduleListRequest(legacyRequest.Value).Value!.IncludeNextEndUtc);

        var endRequest = IpcProtocol.CreateRevisionedScheduleListRequest(id, includeNextEndUtc: true);
        Assert.True(IpcProtocol.ReadRevisionedScheduleListRequest(endRequest.Value!).Value!.IncludeNextEndUtc);

        var schedule = new WeeklySchedule(Guid.NewGuid(), "Work", 62, 600, "UTC", true, new(Guid.NewGuid(), 60, false, 0));
        var legacyChange = IpcProtocol.CreateRevisionedScheduleChangeRequest(id, new("Save", schedule.Id, null, schedule));
        Assert.DoesNotContain("includeNextEndUtc", legacyChange.Value!.PayloadJson);
        Assert.False(IpcProtocol.ReadRevisionedScheduleChangeRequest(legacyChange.Value).Value!.IncludeNextEndUtc);
        var extendedChange = IpcProtocol.CreateRevisionedScheduleChangeRequest(id, new("Save", schedule.Id, null, schedule, IncludeNextEndUtc: true));
        Assert.True(IpcProtocol.ReadRevisionedScheduleChangeRequest(extendedChange.Value!).Value!.IncludeNextEndUtc);

        var start = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
        var view = new ScheduleView(Guid.NewGuid(), "Morning", 1 << (int)DayOfWeek.Friday, 600, "UTC", true,
            "Work", 1, false, start, null, "", Guid.NewGuid(), 0, 0, start.AddMinutes(1), "Etc/UTC");
        var response = new RevisionedScheduleResponse(true, "", [new(view, new string('A', 64), 0, 0)]);
        var envelope = IpcProtocol.CreateRevisionedScheduleResponse(id, response);
        Assert.True(IpcProtocol.ReadRevisionedScheduleResponse(envelope.Value!).IsValid);
        var legacyView = view with { NextEndUtc = null, TimeZoneIanaId = null };
        var legacyEnvelope = IpcProtocol.CreateRevisionedScheduleResponse(id,
            response with { Schedules = [new(legacyView, new string('A', 64), 0, 0)] });
        Assert.DoesNotContain("nextEndUtc", legacyEnvelope.Value!.PayloadJson);
        Assert.DoesNotContain("timeZoneIanaId", legacyEnvelope.Value.PayloadJson);

        var invalid = view with { NextEndUtc = start };
        var invalidEnvelope = IpcProtocol.CreateRevisionedScheduleResponse(id,
            response with { Schedules = [new(invalid, new string('A', 64), 0, 0)] });
        Assert.False(IpcProtocol.ReadRevisionedScheduleResponse(invalidEnvelope.Value!).IsValid);
    }
}
