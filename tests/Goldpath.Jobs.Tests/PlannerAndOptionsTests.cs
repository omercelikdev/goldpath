using CsCheck;
using Quartz;
using Xunit;

namespace Goldpath.Jobs.Tests;

public class PlannerAndOptionsTests
{
    [Fact]
    public void ByRange_partitions_the_whole_interval_without_gaps_or_overlaps()
    {
        // Property: chunks reassemble EXACTLY [0, total) in order — losing or duplicating
        // a range payload is losing or double-processing real items.
        var gen = Gen.Select(Gen.Long[0, 100_000], Gen.Int[1, 5_000]);
        gen.Sample(pair =>
        {
            var (total, chunkSize) = pair;
            var plan = GoldpathJobPlanner.ByRange(total, chunkSize);
            long cursor = 0;
            foreach (var payload in plan.ChunkPayloads)
            {
                var (start, end) = GoldpathJobPlanner.ParseRange(payload);
                if (start != cursor || end <= start || end - start > chunkSize)
                {
                    return false;
                }

                cursor = end;
            }

            return cursor == total && plan.TotalItems == total;
        });
    }

    [Fact]
    public void ByRange_rejects_nonsense()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GoldpathJobPlanner.ByRange(-1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => GoldpathJobPlanner.ByRange(10, 0));
        Assert.Empty(GoldpathJobPlanner.ByRange(0, 10).ChunkPayloads);
    }

    [Fact]
    public void ParseRange_rejects_malformed_payloads()
    {
        Assert.Throws<FormatException>(() => GoldpathJobPlanner.ParseRange("nope"));
        Assert.Throws<FormatException>(() => GoldpathJobPlanner.ParseRange("5:1"));
        Assert.Throws<FormatException>(() => GoldpathJobPlanner.ParseRange(":9"));
    }

    [Fact]
    public void AddJob_records_the_full_definition()
    {
        var options = new GoldpathJobsOptions();
        options.AddJob<ScriptedJob>(j =>
        {
            j.Cron = "0 30 1 * * ?";
            j.Calendar = "banking-tr";
            j.TimeZoneId = "Europe/Istanbul";
            j.Deadline = TimeSpan.FromHours(5.5);
            j.MaxParallelChunks = 4;
            j.MaxChunkAttempts = 5;
            j.InterChunkDelay = TimeSpan.FromMilliseconds(50);
            j.StartAfter<ScriptedJob>();
            j.PinInput(_ => "v1");
        });

        var definition = Assert.Single(options.Jobs);
        Assert.Equal(typeof(ScriptedJob), definition.JobType);
        Assert.Equal(nameof(ScriptedJob), definition.Name);
        Assert.Equal("0 30 1 * * ?", definition.Cron);
        Assert.Equal("banking-tr", definition.CalendarName);
        Assert.Equal("Europe/Istanbul", definition.TimeZoneId);
        Assert.Equal(TimeSpan.FromHours(5.5), definition.Deadline);
        Assert.Equal(4, definition.MaxParallelChunks);
        Assert.Equal(5, definition.MaxChunkAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(50), definition.InterChunkDelay);
        Assert.Equal([nameof(ScriptedJob)], definition.StartAfterJobs);
        Assert.NotNull(definition.InputVersionFactory);
    }

    [Fact]
    public void Defaults_are_sane_without_configuration()
    {
        var options = new GoldpathJobsOptions();
        options.AddJob<ScriptedJob>();

        var definition = Assert.Single(options.Jobs);
        Assert.Null(definition.Cron);          // ad-hoc only
        Assert.Null(definition.Deadline);      // GP0502 will warn — silence is how 07:00 gets missed
        Assert.Equal(1, definition.MaxParallelChunks);
        Assert.Equal(3, definition.MaxChunkAttempts);
        Assert.True(options.MaxConcurrency > 0);
        Assert.True(options.CheckinInterval < options.CheckinMisfireThreshold);
    }

    [Fact]
    public void Business_days_calendar_excludes_weekends_and_given_holidays()
    {
        var plain = GoldpathCalendars.BusinessDays();
        var withHolidays = GoldpathCalendars.BusinessDays([new DateTime(2026, 7, 15)]);

        var saturday = new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);
        var sunday = new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);
        var wednesday = new DateTimeOffset(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
        var holiday = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

        Assert.False(plain.IsTimeIncluded(saturday));
        Assert.False(plain.IsTimeIncluded(sunday));
        Assert.True(plain.IsTimeIncluded(wednesday));
        Assert.Equal("Excludes weekends", plain.Description);
        Assert.False(withHolidays.IsTimeIncluded(saturday));   // base calendar still applies
        Assert.False(withHolidays.IsTimeIncluded(sunday));
        Assert.False(withHolidays.IsTimeIncluded(holiday));
        Assert.True(withHolidays.IsTimeIncluded(wednesday));
        Assert.Equal("Business days minus holidays", withHolidays.Description);
    }

    [Fact]
    public void AddCalendar_registers_by_name_and_chains()
    {
        var options = new GoldpathJobsOptions();
        var calendar = GoldpathCalendars.BusinessDays();

        Assert.Same(options, options.AddCalendar("banking-tr", calendar));
        Assert.Same(calendar, options.Calendars["banking-tr"]);
        Assert.Same(options, options.AddJob<ScriptedJob>());   // AddJob chains too
        Assert.NotEmpty(options.SchedulerName);                // entry-assembly default, never blank
    }
}
