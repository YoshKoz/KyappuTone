using KyappuTone.Core.Library;
using Shouldly;
using Xunit;

namespace KyappuTone.Core.Tests.Library;

public class ListeningStatsTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);

    private static PlayHistoryEntry Entry(int daysAgo, int hour, int minutes, bool completed = true)
    {
        var started = new DateTimeOffset(
            Now.UtcDateTime.Date.AddDays(-daysAgo).AddHours(hour).AddMinutes(minutes),
            TimeSpan.Zero);
        return new PlayHistoryEntry("/music/a.flac", started, (long)TimeSpan.FromMinutes(3).TotalMilliseconds, completed);
    }

    [Fact]
    public void Compute_Sums_Listening_And_Counts_Completed_Plays()
    {
        var entries = new[]
        {
            new PlayHistoryEntry("/a", Now.AddHours(-1), 30_000, true),
            new PlayHistoryEntry("/b", Now.AddHours(-2), 20_000, false),
            new PlayHistoryEntry("/c", Now.AddHours(-3), 10_000, true),
        };

        var stats = ListeningStats.Compute(entries, Now, TimeZoneInfo.Utc, days: 7);

        stats.TotalListening.ShouldBe(TimeSpan.FromSeconds(60));
        stats.TotalPlays.ShouldBe(2);
    }

    [Fact]
    public void Compute_Excludes_Entries_Outside_The_Window()
    {
        var entries = new[]
        {
            Entry(daysAgo: 10, hour: 12, minutes: 0),
            Entry(daysAgo: 3, hour: 12, minutes: 0),
        };

        var stats = ListeningStats.Compute(entries, Now, TimeZoneInfo.Utc, days: 7);

        stats.Daily.Sum(day => day.Plays).ShouldBe(1);
        stats.TotalPlays.ShouldBe(1);
    }

    [Fact]
    public void Compute_Buckets_By_Local_Date_And_Hour()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");
        var utc = new DateTimeOffset(2026, 1, 1, 22, 30, 0, TimeSpan.Zero);
        var entry = new PlayHistoryEntry("/a", utc, 60_000, true);

        var stats = ListeningStats.Compute(new[] { entry }, utc.AddHours(2), zone, days: 7);

        var local = TimeZoneInfo.ConvertTime(utc, zone);
        stats.Daily.Single(day => day.Listened > TimeSpan.Zero).Date.ShouldBe(DateOnly.FromDateTime(local.DateTime));
        stats.Hourly.Single(hour => hour.Listened > TimeSpan.Zero).Hour.ShouldBe(0);
        stats.Hourly.Single(hour => hour.Listened > TimeSpan.Zero).Day.ShouldBe(local.DayOfWeek);
    }

    [Fact]
    public void Compute_Returns_Zero_Filled_Buckets_For_Empty_History()
    {
        var stats = ListeningStats.Compute(Array.Empty<PlayHistoryEntry>(), Now, TimeZoneInfo.Utc, days: 30);

        stats.TotalListening.ShouldBe(TimeSpan.Zero);
        stats.TotalPlays.ShouldBe(0);
        stats.Daily.Count.ShouldBe(30);
        stats.Daily.ShouldAllBe(day => day.Listened == TimeSpan.Zero && day.Plays == 0);
        stats.Hourly.Count.ShouldBe(7 * 24);
        stats.Hourly.ShouldAllBe(hour => hour.Listened == TimeSpan.Zero);
    }

    [Fact]
    public void Daily_Buckets_Are_Ascending_And_End_Today()
    {
        var stats = ListeningStats.Compute(Array.Empty<PlayHistoryEntry>(), Now, TimeZoneInfo.Utc, days: 14);

        stats.Daily.First().Date.ShouldBe(DateOnly.FromDateTime(Now.UtcDateTime.Date.AddDays(-13)));
        stats.Daily.Last().Date.ShouldBe(DateOnly.FromDateTime(Now.UtcDateTime.Date));
        stats.Daily.Select(day => day.Date).ShouldBe(stats.Daily.Select(day => day.Date).OrderBy(date => date));
    }

    [Fact]
    public void Negative_Durations_Are_Clamped_To_Zero()
    {
        var entry = new PlayHistoryEntry("/a", Now, -5_000, true);

        var stats = ListeningStats.Compute(new[] { entry }, Now, TimeZoneInfo.Utc, days: 7);

        stats.TotalListening.ShouldBe(TimeSpan.Zero);
        stats.TotalPlays.ShouldBe(1);
    }
}
