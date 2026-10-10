namespace FocusGuard.Domain;

public sealed record WeeklyOccurrence(DateTimeOffset StartUtc, DateTimeOffset EndUtc);

/// <summary>Local weekly boundaries converted once to UTC. DST gaps are skipped; ambiguous times run once at the standard offset.</summary>
public static class WeeklyOccurrenceCalculator
{
    public static IReadOnlyList<WeeklyOccurrence> Around(DateTimeOffset nowUtc, int days, int startMinute, int durationSeconds, string timeZoneId)
    {
        if (days is < 1 or > 127 || startMinute is < 0 or >= 1440 || durationSeconds is < 60 or > 43200)
            throw new ArgumentOutOfRangeException(nameof(days));
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var localToday = TimeZoneInfo.ConvertTime(nowUtc, zone).Date;
        var result = new List<WeeklyOccurrence>();
        for (var d = -1; d <= 8; d++)
        {
            var date = localToday.AddDays(d);
            if ((days & (1 << (int)date.DayOfWeek)) == 0) continue;
            var localStart = DateTime.SpecifyKind(date.AddMinutes(startMinute), DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(localStart)) continue;
            var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, zone), TimeSpan.Zero);
            result.Add(new(start, start.AddSeconds(durationSeconds)));
        }
        return result.OrderBy(o => o.StartUtc).ToArray();
    }
}
