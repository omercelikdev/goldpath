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
