using CorPay.EodWorker.Reports;
using Xunit;

namespace CorPay.Api.Tests;

/// <summary>The EOD rule table: executed-but-never-fed is the ONLY mismatch that pages.</summary>
public class EodReconciliationTests
{
    [Fact]
    public void Balanced_books_produce_no_mismatch()
        => Assert.Empty(EodReconciliationJob.Reconcile(["A", "B"], ["A", "B"]));

    [Fact]
    public void An_executed_payment_the_ledger_never_heard_of_pages()
    {
        var mismatches = EodReconciliationJob.Reconcile(["A", "B", "C"], ["A", "C"]);
        var mismatch = Assert.Single(mismatches);
        Assert.Equal("B", mismatch.Reference);
        Assert.Contains("never reached the ledger feed", mismatch.Reason);
    }

    [Fact]
    public void Extra_feed_rows_do_not_page_the_run()
        => Assert.Empty(EodReconciliationJob.Reconcile(["A"], ["A", "GHOST"]));   // ghosts are the FEED side's audit, not EOD's

    [Fact]
    public void The_window_is_yesterday_utc_whole_day()
    {
        var (from, to) = EodReconciliationJob.Window(new DateTimeOffset(2026, 7, 13, 23, 45, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 7, 12, 0, 0, 0, TimeSpan.Zero), from);
        Assert.Equal(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero), to);
    }
}

/// <summary>NFR spot-check: the EOD rule at day-scale volume (the card: 100k in 45 min).</summary>
public class EodReconciliationVolumeTests
{
    [Fact]
    [Trait("Category", "Bench")]
    public void Reconciling_100k_references_is_instant_the_budget_belongs_to_io()
    {
        var executed = Enumerable.Range(0, 100_000).Select(i => $"PAY-{i}").ToList();
        var fed = executed.Where(r => !r.EndsWith("7", StringComparison.Ordinal)).ToList();   // ~10k missing

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var mismatches = EodReconciliationJob.Reconcile(executed, fed);
        watch.Stop();

        Assert.Equal(10_000, mismatches.Count);
        Assert.True(watch.ElapsedMilliseconds < 5_000, $"rule pass took {watch.ElapsedMilliseconds}ms");
        Console.WriteLine($"BENCH-CORPAY eod-rule 100k refs in {watch.ElapsedMilliseconds}ms ({mismatches.Count} mismatches)");
    }
}
