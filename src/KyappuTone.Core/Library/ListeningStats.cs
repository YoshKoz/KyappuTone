namespace KyappuTone.Core.Library;

/// <summary>One listening event, as persisted in the PlayHistory table.</summary>
public sealed record PlayHistoryEntry(string Path, DateTimeOffset StartedAt, long MsPlayed, bool Completed);

/// <summary>Listening time and completed plays for one local date.</summary>
public sealed record DailyListening(DateOnly Date, TimeSpan Listened, int Plays);

/// <summary>Listening time for one local day-of-week/hour bucket.</summary>
public sealed record HourlyListening(DayOfWeek Day, int Hour, TimeSpan Listened);

/// <summary>
/// Aggregates raw listening events into totals, a daily series and a weekly
/// heatmap. Bucketing always uses the supplied time zone, never raw UTC.
/// </summary>
public sealed class ListeningStats
{
    /// <summary>Listening time and completed plays inside the requested window.</summary>
    public TimeSpan TotalListening { get; private init; }

    /// <summary>Number of completed plays inside the requested window.</summary>
    public int TotalPlays { get; private init; }

    /// <summary>One entry per day, ascending, ending today (includes empty days).</summary>
    public IReadOnlyList<DailyListening> Daily { get; private init; } = Array.Empty<DailyListening>();

    /// <summary>All 7×24 day/hour buckets, Sunday first, including empty buckets.</summary>
    public IReadOnlyList<HourlyListening> Hourly { get; private init; } = Array.Empty<HourlyListening>();

    /// <summary>Computes statistics for the last <paramref name="days"/> local days.</summary>
    public static ListeningStats Compute(
        IEnumerable<PlayHistoryEntry> entries,
        DateTimeOffset now,
        TimeZoneInfo zone,
        int days = 30)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);

        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var firstDate = today.AddDays(-(days - 1));

        var dailyMs = new long[days];
        var dailyPlays = new int[days];
        var hourlyMs = new long[7, 24];
        var totalMs = 0L;
        var totalPlays = 0;

        foreach (var entry in entries)
        {
            var local = TimeZoneInfo.ConvertTime(entry.StartedAt, zone);
            var date = DateOnly.FromDateTime(local.DateTime);
            if (date < firstDate || date > today) continue;

            var ms = Math.Max(0, entry.MsPlayed);
            var dayIndex = date.DayNumber - firstDate.DayNumber;

            dailyMs[dayIndex] += ms;
            totalMs += ms;

            if (entry.Completed)
            {
                dailyPlays[dayIndex]++;
                totalPlays++;
            }

            hourlyMs[(int)local.DayOfWeek, local.Hour] += ms;
        }

        var daily = new DailyListening[days];
        for (var index = 0; index < days; index++)
        {
            daily[index] = new DailyListening(
                firstDate.AddDays(index),
                TimeSpan.FromMilliseconds(dailyMs[index]),
                dailyPlays[index]);
        }

        var hourly = new HourlyListening[7 * 24];
        for (var day = 0; day < 7; day++)
        {
            for (var hour = 0; hour < 24; hour++)
            {
                hourly[day * 24 + hour] = new HourlyListening(
                    (DayOfWeek)day,
                    hour,
                    TimeSpan.FromMilliseconds(hourlyMs[day, hour]));
            }
        }

        return new ListeningStats
        {
            TotalListening = TimeSpan.FromMilliseconds(totalMs),
            TotalPlays = totalPlays,
            Daily = daily,
            Hourly = hourly,
        };
    }
}
