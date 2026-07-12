using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Goldpath.Campaign.Tests;

public class PacerTests
{
    private static Action<GoldpathCampaignOptions> FastTicks(int sliceMs = 400, int tickMs = 20)
        => o =>
        {
            o.LeadershipSlice = TimeSpan.FromMilliseconds(sliceMs);
            o.LeaderTick = TimeSpan.FromMilliseconds(tickMs);
            o.EnumerationBatchSize = 4;
        };

    [Fact]
    public async Task PlanIsOneLeadershipChunk()
    {
        using var fixture = new CampaignFixture(FastTicks());
        using var scope = fixture.Services.CreateScope();
        var plan = await fixture.Pacer().PlanAsync(
            CampaignFixture.CreateContext(scope.ServiceProvider), CancellationToken.None);
        Assert.Equal(["lead"], plan.ChunkPayloads);
    }

    [Fact]
    public async Task OneSliceEnumeratesAndReleasesUnderAGenerousPolicy()
    {
        using var fixture = new CampaignFixture(FastTicks(), sourceSize: 10);
        var campaign = await fixture.CreateAsync(new GoldpathCampaignPolicy(10_000, null, 100, null, null, "UTC"));
        await fixture.RunPacerSliceAsync();

        var row = fixture.Reload(campaign.Id);
        Assert.Equal(GoldpathCampaignState.Running, row.State);
        Assert.True(row.EnumerationComplete);
        Assert.Equal(10, row.EnumeratedThrough);
        Assert.Equal(10, row.ReleasedThrough);
        Assert.Equal(10, fixture.Publisher.Published.OfType<GoldpathCampaignItemMessage>().Count());
    }

    [Fact]
    public async Task SecondSliceCompletesAfterTheFleetReports()
    {
        using var fixture = new CampaignFixture(FastTicks(), sourceSize: 6);
        var campaign = await fixture.CreateAsync(new GoldpathCampaignPolicy(10_000, null, 100, null, null, "UTC"));
        await fixture.RunPacerSliceAsync();
        await fixture.ConsumeAllPublishedAsync();   // the consumer fleet, condensed
        await fixture.RunPacerSliceAsync();

        var row = fixture.Reload(campaign.Id);
        Assert.Equal(GoldpathCampaignState.Completed, row.State);
        Assert.Equal(6, row.SucceededCount);
        Assert.Equal(6, fixture.Executed.Count);
    }

    [Fact]
    public async Task CompletionWithFailuresFilesTheRepairQueueOnce()
    {
        using var fixture = new CampaignFixture(FastTicks(), sourceSize: 3);
        fixture.FailIds.Add(2);
        var campaign = await fixture.CreateAsync(new GoldpathCampaignPolicy(10_000, null, 100, null, null, "UTC"));
        await fixture.RunPacerSliceAsync();
        await fixture.ConsumeAllPublishedAsync();

        var failures = await fixture.RunPacerSliceAsync();   // the completing slice files the failed set
        var failure = Assert.Single(failures);
        Assert.Equal(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{campaign.Id:N}#2"), failure.ItemKey);
        Assert.Contains("the provider refused target 2", failure.Reason, StringComparison.Ordinal);
        Assert.Equal(GoldpathCampaignState.CompletedWithFailures, fixture.Reload(campaign.Id).State);

        Assert.Empty(await fixture.RunPacerSliceAsync());    // exactly once — the flip cannot re-fire
    }

    [Fact]
    public async Task ClosedWindowReleasesNothing()
    {
        using var fixture = new CampaignFixture(FastTicks(), sourceSize: 5);
        // A one-hour window starting six hours from now is closed whatever the clock says.
        var start = TimeOnly.FromDateTime(DateTime.UtcNow.AddHours(6));
        var end = TimeOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var campaign = await fixture.CreateAsync(new GoldpathCampaignPolicy(10_000, null, 100, start, end, "UTC"));
        await fixture.RunPacerSliceAsync();

        var row = fixture.Reload(campaign.Id);
        Assert.True(row.EnumerationComplete);        // enumeration is not paced — only release is
        Assert.Equal(0, row.ReleasedThrough);
        Assert.Empty(fixture.Publisher.Published);
    }

    [Fact]
    public async Task DailyQuotaCapsTheDay()
    {
        using var fixture = new CampaignFixture(FastTicks(), sourceSize: 10);
        var campaign = await fixture.CreateAsync(new GoldpathCampaignPolicy(10_000, 3, 100, null, null, "UTC"));
        await fixture.RunPacerSliceAsync();

        var row = fixture.Reload(campaign.Id);
        Assert.Equal(3, row.ReleasedThrough);
        Assert.Equal(3, row.ReleasedToday);
        Assert.Equal(3, fixture.Publisher.Published.OfType<GoldpathCampaignItemMessage>().Count());
    }

    [Fact]
    public async Task MaxInFlightHoldsReleasesUntilOutcomesDrain()
    {
        using var fixture = new CampaignFixture(FastTicks(), sourceSize: 10);
        var campaign = await fixture.CreateAsync(new GoldpathCampaignPolicy(10_000, null, 2, null, null, "UTC"));
        await fixture.RunPacerSliceAsync();
        Assert.Equal(2, fixture.Reload(campaign.Id).ReleasedThrough);   // pinned at the ceiling

        await fixture.ConsumeAllPublishedAsync();                       // both drain to terminal
        await fixture.RunPacerSliceAsync();
        Assert.Equal(4, fixture.Reload(campaign.Id).ReleasedThrough);   // two more slots opened
    }

    [Fact]
    public async Task TpsBudgetPacesInsteadOfDumping()
    {
        // 2 TPS on 500ms ticks banks one token per tick; a 1.2s slice affords at most ~3.
        using var fixture = new CampaignFixture(o =>
        {
            o.LeadershipSlice = TimeSpan.FromMilliseconds(1_200);
            o.LeaderTick = TimeSpan.FromMilliseconds(500);
            o.EnumerationBatchSize = 100;
        }, sourceSize: 10);
        var campaign = await fixture.CreateAsync(new GoldpathCampaignPolicy(2, null, 100, null, null, "UTC"));
        await fixture.RunPacerSliceAsync();

        var released = fixture.Reload(campaign.Id).ReleasedThrough;
        Assert.InRange(released, 1, 4);   // paced — never the full 10 in one slice
    }

    [Fact]
    public async Task StaleClaimsAreSweptToTheRepairQueue()
    {
        using var fixture = new CampaignFixture(o =>
        {
            FastTicks()(o);
            o.StaleClaimAfter = TimeSpan.FromMinutes(10);
        }, sourceSize: 3);
        var campaign = await fixture.CreateAsync(new GoldpathCampaignPolicy(10_000, null, 100, null, null, "UTC"));
        await fixture.RunPacerSliceAsync();

        // A consumer claimed seq 1 and died an hour ago.
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CampaignTestContext>();
            await fixture.Engine.ClaimAsync(db, campaign.Id, 1, CancellationToken.None);
        }

        fixture.Mutate(db => db.Set<GoldpathCampaignItem>().Single(i => i.Seq == 1).ClaimedAt = DateTimeOffset.UtcNow.AddHours(-1));

        var failures = await fixture.RunPacerSliceAsync();
        var failure = Assert.Single(failures);
        Assert.Equal(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{campaign.Id:N}#1"), failure.ItemKey);
        Assert.Contains("interrupted mid-flight", failure.Reason, StringComparison.Ordinal);

        var item = fixture.Query(db => db.Set<GoldpathCampaignItem>().Single(i => i.Seq == 1));
        Assert.Equal(GoldpathCampaignItemState.Failed, item.State);
        Assert.Contains("confirm the provider, then replay", item.Error, StringComparison.Ordinal);
        Assert.Equal(1, fixture.Reload(campaign.Id).FailedCount);
    }

    [Fact]
    public async Task FreshClaimsAreNotSwept()
    {
        using var fixture = new CampaignFixture(FastTicks(), sourceSize: 2);
        var campaign = await fixture.CreateAsync(new GoldpathCampaignPolicy(10_000, null, 100, null, null, "UTC"));
        await fixture.RunPacerSliceAsync();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CampaignTestContext>();
            await fixture.Engine.ClaimAsync(db, campaign.Id, 1, CancellationToken.None);
        }

        var failures = await fixture.RunPacerSliceAsync();
        Assert.Empty(failures);
        Assert.Equal(GoldpathCampaignItemState.Processing,
            fixture.Query(db => db.Set<GoldpathCampaignItem>().Single(i => i.Seq == 1).State));
    }

    [Fact]
    public async Task PausedCampaignsAreLeftAlone()
    {
        using var fixture = new CampaignFixture(FastTicks(), sourceSize: 3);
        var campaign = await fixture.CreateAsync(new GoldpathCampaignPolicy(10_000, null, 100, null, null, "UTC"));
        fixture.Mutate(db => db.Set<GoldpathCampaign>().Single(c => c.Id == campaign.Id).State = GoldpathCampaignState.Paused);

        await fixture.RunPacerSliceAsync();
        var row = fixture.Reload(campaign.Id);
        Assert.Equal(GoldpathCampaignState.Paused, row.State);
        Assert.Equal(0, row.EnumeratedThrough);   // not workable — the leader never adopted it
        Assert.Empty(fixture.Publisher.Published);
    }

    [Fact]
    public async Task PacerReplaysThroughTheEngine()
    {
        using var fixture = new CampaignFixture(FastTicks(), sourceSize: 2);
        fixture.FailIds.Add(2);
        var campaign = await fixture.CreateAsync(new GoldpathCampaignPolicy(10_000, null, 100, null, null, "UTC"));
        await fixture.RunPacerSliceAsync();
        await fixture.ConsumeAllPublishedAsync();
        fixture.FailIds.Remove(2);

        using var scope = fixture.Services.CreateScope();
        await fixture.Pacer().ReplayItemAsync(
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{campaign.Id:N}#2"),
            CampaignFixture.CreateContext(scope.ServiceProvider), CancellationToken.None);

        Assert.Equal(0, fixture.Reload(campaign.Id).FailedCount);
    }
}
