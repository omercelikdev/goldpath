namespace GoldpathWorker.Host.Reports;

/// <summary>One summarized day — the walking skeleton's "chunk did real work" proof.</summary>
public class DailyReportRow
{
    /// <summary>Day offset the row summarizes (the job's range payloads walk these).</summary>
    public int DayOffset { get; set; }

    /// <summary>When the summary was (re)generated (UTC policy: DateTimeOffset).</summary>
    public DateTimeOffset GeneratedAt { get; set; }
}
