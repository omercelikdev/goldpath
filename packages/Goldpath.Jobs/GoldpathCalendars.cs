using Quartz;
using Quartz.Impl.Calendar;

namespace Goldpath;

/// <summary>
/// Calendar factories for the common enterprise shapes. Register the result with
/// <c>AddCalendar(name, ...)</c> and reference it from a job's <c>Calendar</c> — the
/// schedule then skips excluded days (the finance card's banking-day rule).
/// </summary>
public static class GoldpathCalendars
{
    /// <summary>Weekdays only — Saturday and Sunday excluded.</summary>
    public static ICalendar BusinessDays()
    {
        var calendar = new WeeklyCalendar { Description = "Excludes weekends" };
        calendar.SetDayExcluded(DayOfWeek.Saturday, true);
        calendar.SetDayExcluded(DayOfWeek.Sunday, true);
        return calendar;
    }

    /// <summary>
    /// Business days minus the given holidays — the holiday table stays APP DATA
    /// (configuration or database), never hardcoded in the platform.
    /// </summary>
    public static ICalendar BusinessDays(IEnumerable<DateTime> holidays)
    {
        var calendar = new HolidayCalendar { CalendarBase = BusinessDays(), Description = "Business days minus holidays" };
        foreach (var day in holidays)
        {
            calendar.AddExcludedDate(day.Date);
        }

        return calendar;
    }
}
