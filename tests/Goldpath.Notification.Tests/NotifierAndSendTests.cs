using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Goldpath.Notification.Tests;

public class NotifierAndSendTests : IDisposable
{
    private readonly NotificationFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Request_renders_at_request_time_and_stamps_the_template_hash()
    {
        var notification = await _fixture.RequestAsync(NotificationFixture.Renewal("renewal:P-42:2026-08"));

        Assert.Equal(GoldpathNotificationState.Requested, notification.State);
        Assert.Equal("Poliçeniz P-42 yenilenmek üzere", notification.Subject);
        Assert.Contains("Sayın Ömer", notification.Body, StringComparison.Ordinal);
        Assert.Equal(_fixture.Options.Template("policy-renewal").Hash, notification.TemplateHash);
        Assert.Equal("tr", notification.Culture);
        Assert.Null(notification.Detail);   // a clean request carries no story
    }

    [Fact]
    public async Task The_same_dedup_key_lands_once()
    {
        var first = await _fixture.RequestAsync(NotificationFixture.Renewal("renewal:P-42:2026-08"));
        var second = await _fixture.RequestAsync(NotificationFixture.Renewal("renewal:P-42:2026-08", name: "Başkası"));

        Assert.Equal(first.Id, second.Id);   // the retry storm lands once
        Assert.Equal(1, _fixture.Query(db => db.Set<GoldpathNotification>().Count()));
    }

    [Fact]
    public async Task A_missing_token_never_persists()
    {
        var broken = new GoldpathNotificationRequest("policy-renewal", "recording", "a@b.c", "tr",
            new Dictionary<string, string> { ["Name"] = "Ömer" }, "renewal:broken");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.RequestAsync(broken));
        Assert.Equal(0, _fixture.Query(db => db.Set<GoldpathNotification>().Count()));   // the app transaction sees the throw
    }

    [Fact]
    public async Task Suppression_is_evidence()
    {
        using var fixture = new NotificationFixture(o => o.MaySend((r, _) => Task.FromResult(!r.Recipient.Contains("omer"))));
        var suppressed = await fixture.RequestAsync(NotificationFixture.Renewal("renewal:P-42:2026-08"));

        Assert.Equal(GoldpathNotificationState.Suppressed, suppressed.State);
        Assert.Contains("MaySend", suppressed.Detail, StringComparison.Ordinal);

        await fixture.RunSendPassAsync();
        Assert.Empty(fixture.Channel.Accepted);   // suppressed rows never reach a channel
    }

    [Fact]
    public async Task The_send_pass_claims_before_sending_and_stamps_the_evidence()
    {
        var notification = await _fixture.RequestAsync(NotificationFixture.Renewal("renewal:P-42:2026-08"));
        var failures = await _fixture.RunSendPassAsync();

        Assert.Empty(failures);
        var sent = _fixture.Query(db => db.Set<GoldpathNotification>().AsNoTracking().Single(n => n.Id == notification.Id));
        Assert.Equal(GoldpathNotificationState.Sent, sent.State);
        Assert.NotNull(sent.ClaimedAt);
        Assert.NotNull(sent.SentAt);
        Assert.True(sent.ClaimedAt <= sent.SentAt, "the claim is persisted BEFORE the channel call");
        Assert.Equal(1, sent.Attempts);
        Assert.Single(_fixture.Channel.Accepted);
        Assert.Equal(notification.Id, _fixture.Channel.Accepted[0].NotificationId);
    }

    [Fact]
    public async Task NotBefore_holds_the_send_until_its_time()
    {
        await _fixture.RequestAsync(NotificationFixture.Renewal("renewal:quiet"));
        var early = NotificationFixture.Renewal("renewal:later");
        await _fixture.RequestAsync(new GoldpathNotificationRequest(
            early.Template, early.Channel, early.Recipient, early.Culture, early.Tokens, early.DedupKey)
        {
            NotBefore = DateTimeOffset.UtcNow.AddHours(6),
        });

        await _fixture.RunSendPassAsync();

        Assert.Single(_fixture.Channel.Accepted);   // only the due one went
        Assert.Equal(1, _fixture.Query(db => db.Set<GoldpathNotification>().Count(n => n.State == GoldpathNotificationState.Requested)));
    }

    [Fact]
    public async Task Transient_failures_retry_inside_the_attempt_budget()
    {
        _fixture.Channel.Throws = 2;   // two refusals, then accept (MaxAttempts default 3)
        await _fixture.RequestAsync(NotificationFixture.Renewal("renewal:flaky"));

        var failures = await _fixture.RunSendPassAsync();

        Assert.Empty(failures);
        var sent = _fixture.Query(db => db.Set<GoldpathNotification>().AsNoTracking().Single());
        Assert.Equal(GoldpathNotificationState.Sent, sent.State);
        Assert.Equal(3, sent.Attempts);
    }

    [Fact]
    public async Task Exhausted_attempts_fail_into_the_repair_queue_and_replay_resends_once_confirmed()
    {
        await _fixture.RequestAsync(NotificationFixture.Renewal("renewal:poisoned", name: "FAIL"));

        var failures = await _fixture.RunSendPassAsync();

        var failure = Assert.Single(failures);
        Assert.Contains("gateway refused", failure.Reason, StringComparison.Ordinal);
        var failed = _fixture.Query(db => db.Set<GoldpathNotification>().AsNoTracking().Single());
        Assert.Equal(GoldpathNotificationState.Failed, failed.State);
        Assert.Equal(3, failed.Attempts);
        Assert.Empty(_fixture.Channel.Accepted);

        // Fix the world, then the human-confirmed replay (the jobs replay-items verb).
        _fixture.Mutate(db =>
        {
            var row = db.Set<GoldpathNotification>().Single();
            row.Body = row.Body!.Replace("FAIL", "Ömer", StringComparison.Ordinal);
        });
        using var scope = _fixture.Services.CreateScope();
        await _fixture.SendJob().ReplayItemAsync(failed.Id.ToString("N"),
            NotificationFixture.CreateContext(scope.ServiceProvider), CancellationToken.None);

        var healed = _fixture.Query(db => db.Set<GoldpathNotification>().AsNoTracking().Single());
        Assert.Equal(GoldpathNotificationState.Sent, healed.State);
        Assert.Single(_fixture.Channel.Accepted);
    }

    [Fact]
    public async Task The_send_plan_is_a_golden_shape_and_chunks_respect_their_size()
    {
        using var fixture = new NotificationFixture(o => o.ChunkSize = 2);
        for (var i = 1; i <= 3; i++)
        {
            await fixture.RequestAsync(NotificationFixture.Renewal($"renewal:{i}"));
        }

        using var scope = fixture.Services.CreateScope();
        var job = fixture.SendJob();
        var context = NotificationFixture.CreateContext(scope.ServiceProvider);
        // Noise the filters must EXCLUDE: a Sent row and a future-NotBefore row.
        fixture.Mutate(db =>
        {
            db.Set<GoldpathNotification>().Add(new GoldpathNotification { Id = Guid.NewGuid(), DedupKey = "noise:sent", State = GoldpathNotificationState.Sent, RequestedAt = DateTimeOffset.UtcNow });
            db.Set<GoldpathNotification>().Add(new GoldpathNotification { Id = Guid.NewGuid(), DedupKey = "noise:later", State = GoldpathNotificationState.Requested, NotBefore = DateTimeOffset.UtcNow.AddHours(6), RequestedAt = DateTimeOffset.UtcNow });
        });
        var plan = await job.PlanAsync(context, CancellationToken.None);
        Assert.Equal(["send:0", "send:1", "interrupted"], plan.ChunkPayloads);   // payloads are wire contracts
        Assert.Equal(3, plan.TotalItems);                                        // noise rows are NOT due

        // The first chunk claims EXACTLY ChunkSize rows; the third row stays unclaimed.
        await job.ExecuteChunkAsync(NotificationFixture.MakeChunk(0, "send:0"), context, CancellationToken.None);
        Assert.Equal(2, fixture.Query(db => db.Set<GoldpathNotification>().Count(n => n.ClaimedAt != null)));
        Assert.Equal(2, fixture.Channel.Accepted.Count);
    }

    [Fact]
    public async Task Sends_run_oldest_first()
    {
        var older = await _fixture.RequestAsync(NotificationFixture.Renewal("renewal:older"));
        var newer = await _fixture.RequestAsync(NotificationFixture.Renewal("renewal:newer"));
        _fixture.Mutate(db =>
        {
            db.Set<GoldpathNotification>().Single(n => n.Id == older.Id).RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        });

        await _fixture.RunSendPassAsync();

        Assert.Equal([older.Id, newer.Id], _fixture.Channel.Accepted.Select(m => m.NotificationId));
    }

    [Fact]
    public async Task Replay_resets_the_attempt_budget_and_a_missing_item_fails_loudly()
    {
        await _fixture.RequestAsync(NotificationFixture.Renewal("renewal:poisoned2", name: "FAIL"));
        await _fixture.RunSendPassAsync();
        var failed = _fixture.Query(db => db.Set<GoldpathNotification>().AsNoTracking().Single());
        Assert.Equal(3, failed.Attempts);

        _fixture.Mutate(db =>
        {
            var row = db.Set<GoldpathNotification>().Single();
            row.Body = row.Body!.Replace("FAIL", "ok", StringComparison.Ordinal);
        });
        using var scope = _fixture.Services.CreateScope();
        var context = NotificationFixture.CreateContext(scope.ServiceProvider);
        await _fixture.SendJob().ReplayItemAsync(failed.Id.ToString("N"), context, CancellationToken.None);
        Assert.Equal(1, _fixture.Query(db => db.Set<GoldpathNotification>().AsNoTracking().Single()).Attempts);   // fresh budget, one clean attempt

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.SendJob().ReplayItemAsync(Guid.NewGuid().ToString("N"), context, CancellationToken.None));
        Assert.Contains("No notification for repair item", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failure_details_are_exact_teaching_texts()
    {
        var interrupted = await _fixture.RequestAsync(NotificationFixture.Renewal("renewal:cut"));
        _fixture.Mutate(db => db.Set<GoldpathNotification>().Single().ClaimedAt = DateTimeOffset.UtcNow.AddMinutes(-10));
        await _fixture.RunSendPassAsync();

        Assert.Equal("interrupted mid-flight on a previous attempt — confirm with the provider, then replay",
            _fixture.Query(db => db.Set<GoldpathNotification>().AsNoTracking().Single(n => n.Id == interrupted.Id)).Detail);
    }

    [Fact]
    public async Task Retention_covers_failed_and_suppressed_rows_too()
    {
        using var fixture = new NotificationFixture(o => o.MaySend((r, _) => Task.FromResult(!r.DedupKey.Contains("supp"))));
        await fixture.RequestAsync(NotificationFixture.Renewal("renewal:supp"));
        await fixture.RequestAsync(NotificationFixture.Renewal("renewal:fail", name: "FAIL"));
        await fixture.RunSendPassAsync();
        fixture.Mutate(db =>
        {
            foreach (var row in db.Set<GoldpathNotification>())
            {
                row.RequestedAt = DateTimeOffset.UtcNow.AddDays(-120);
                if (row.FailedAt is not null)
                {
                    row.FailedAt = DateTimeOffset.UtcNow.AddDays(-120);
                }
            }
        });

        using var scope = fixture.Services.CreateScope();
        var job = fixture.RetentionJob();
        var context = NotificationFixture.CreateContext(scope.ServiceProvider);
        await job.ExecuteChunkAsync(NotificationFixture.MakeChunk(0, "policy-renewal"), context, CancellationToken.None);

        Assert.All(fixture.Query(db => db.Set<GoldpathNotification>().AsNoTracking().ToList()), n =>
        {
            Assert.Null(n.Body);
            Assert.NotNull(n.BodyDeletedAt);
        });
    }

    [Fact]
    public async Task A_fresh_claim_is_left_alone_by_the_sweep()
    {
        var notification = await _fixture.RequestAsync(NotificationFixture.Renewal("renewal:fresh"));
        _fixture.Mutate(db => db.Set<GoldpathNotification>().Single(n => n.Id == notification.Id).ClaimedAt
            = DateTimeOffset.UtcNow.AddMinutes(-1));   // inside StaleClaimAfter — a LIVE parallel chunk owns it

        using var scope = _fixture.Services.CreateScope();
        var chunk = NotificationFixture.MakeChunk(0, "interrupted");
        await _fixture.SendJob().ExecuteChunkAsync(chunk, NotificationFixture.CreateContext(scope.ServiceProvider), CancellationToken.None);

        Assert.Empty(NotificationFixture.Failures(chunk));
        Assert.Equal(GoldpathNotificationState.Requested,
            _fixture.Query(db => db.Set<GoldpathNotification>().AsNoTracking().Single()).State);   // untouched
    }

    [Fact]
    public async Task Retention_is_idempotent_and_skips_already_deleted_bodies()
    {
        await _fixture.RequestAsync(NotificationFixture.Renewal("renewal:once"));
        await _fixture.RunSendPassAsync();
        _fixture.Mutate(db => db.Set<GoldpathNotification>().Single().SentAt = DateTimeOffset.UtcNow.AddDays(-120));

        using var scope = _fixture.Services.CreateScope();
        var context = NotificationFixture.CreateContext(scope.ServiceProvider);
        await _fixture.RetentionJob().ExecuteChunkAsync(NotificationFixture.MakeChunk(0, "policy-renewal"), context, CancellationToken.None);
        var firstStamp = _fixture.Query(db => db.Set<GoldpathNotification>().AsNoTracking().Single()).BodyDeletedAt;
        Assert.NotNull(firstStamp);

        await _fixture.RetentionJob().ExecuteChunkAsync(NotificationFixture.MakeChunk(0, "policy-renewal"), context, CancellationToken.None);
        Assert.Equal(firstStamp, _fixture.Query(db => db.Set<GoldpathNotification>().AsNoTracking().Single()).BodyDeletedAt);   // no re-stamp
    }

    [Fact]
    public async Task Retention_leaves_rows_inside_the_window_untouched()
    {
        await _fixture.RequestAsync(NotificationFixture.Renewal("renewal:young"));
        await _fixture.RunSendPassAsync();   // SentAt = now, well inside the 90-day window

        using var scope = _fixture.Services.CreateScope();
        await _fixture.RetentionJob().ExecuteChunkAsync(NotificationFixture.MakeChunk(0, "policy-renewal"),
            NotificationFixture.CreateContext(scope.ServiceProvider), CancellationToken.None);

        var row = _fixture.Query(db => db.Set<GoldpathNotification>().AsNoTracking().Single());
        Assert.NotNull(row.Body);
        Assert.Null(row.BodyDeletedAt);
    }

    [Fact]
    public async Task Suppression_detail_is_the_exact_teaching_text()
    {
        using var fixture = new NotificationFixture(o => o.MaySend((_, _) => Task.FromResult(false)));
        var suppressed = await fixture.RequestAsync(NotificationFixture.Renewal("renewal:no"));
        Assert.Equal("suppressed by the MaySend hook — suppression is evidence too", suppressed.Detail);
    }

    [Fact]
    public async Task A_stale_claim_is_repaired_never_resent()
    {
        var notification = await _fixture.RequestAsync(NotificationFixture.Renewal("renewal:interrupted"));
        _fixture.Mutate(db => db.Set<GoldpathNotification>().Single(n => n.Id == notification.Id).ClaimedAt
            = DateTimeOffset.UtcNow.AddMinutes(-10));   // a previous attempt died mid-flight

        var failures = await _fixture.RunSendPassAsync();

        var failure = Assert.Single(failures);
        Assert.Contains("interrupted mid-flight", failure.Reason, StringComparison.Ordinal);
        Assert.Equal(notification.Id.ToString("N"), failure.ItemKey);   // the repair key format is a wire contract
        Assert.Empty(_fixture.Channel.Accepted);   // the provider may have accepted — NEVER silently re-send
        Assert.Equal(GoldpathNotificationState.Failed,
            _fixture.Query(db => db.Set<GoldpathNotification>().AsNoTracking().Single(n => n.Id == notification.Id)).State);
    }

    [Fact]
    public async Task The_sweep_never_touches_terminal_rows()
    {
        var sent = await _fixture.RequestAsync(NotificationFixture.Renewal("renewal:done-old"));
        await _fixture.RunSendPassAsync();
        _fixture.Mutate(db => db.Set<GoldpathNotification>().Single(n => n.Id == sent.Id).ClaimedAt
            = DateTimeOffset.UtcNow.AddMinutes(-30));   // ancient claim on a SENT row

        using var scope = _fixture.Services.CreateScope();
        var chunk = NotificationFixture.MakeChunk(0, "interrupted");
        await _fixture.SendJob().ExecuteChunkAsync(chunk, NotificationFixture.CreateContext(scope.ServiceProvider), CancellationToken.None);

        Assert.Empty(NotificationFixture.Failures(chunk));
        Assert.Equal(GoldpathNotificationState.Sent,
            _fixture.Query(db => db.Set<GoldpathNotification>().AsNoTracking().Single(n => n.Id == sent.Id)).State);
    }

    [Fact]
    public async Task Each_notification_carries_only_its_own_live_attachments()
    {
        var a = NotificationFixture.Renewal("renewal:att-a");
        var b = NotificationFixture.Renewal("renewal:att-b");
        await _fixture.RequestAsync(new GoldpathNotificationRequest(a.Template, a.Channel, a.Recipient, a.Culture, a.Tokens, a.DedupKey)
        {
            Attachments = [new GoldpathNotificationAttachmentContent("a.pdf", "application/pdf", [1])],
        });
        await _fixture.RequestAsync(new GoldpathNotificationRequest(b.Template, b.Channel, b.Recipient, b.Culture, b.Tokens, b.DedupKey)
        {
            Attachments = [new GoldpathNotificationAttachmentContent("b.pdf", "application/pdf", [2])],
        });
        _fixture.Mutate(db => db.Set<GoldpathNotificationAttachment>().Add(
            new GoldpathNotificationAttachment { NotificationId = Guid.NewGuid(), Name = "orphan-nulled.bin", ContentType = "x", Content = null }));

        await _fixture.RunSendPassAsync();

        Assert.Equal(2, _fixture.Channel.Accepted.Count);
        foreach (var message in _fixture.Channel.Accepted)
        {
            var expected = message.Recipient == "omer@example.test" ? new[] { "a.pdf", "b.pdf" } : null;
            Assert.Single(message.Attachments);   // never the neighbor's, never the content-nulled orphan
        }

        Assert.Equal(["a.pdf"], _fixture.Channel.Accepted[0].Attachments.Select(x => x.Name));
        Assert.Equal(["b.pdf"], _fixture.Channel.Accepted[1].Attachments.Select(x => x.Name));
    }

    [Fact]
    public async Task Replay_against_a_still_dead_channel_keeps_the_item_open_with_the_reason()
    {
        await _fixture.RequestAsync(NotificationFixture.Renewal("renewal:still-dead", name: "FAIL"));
        await _fixture.RunSendPassAsync();
        var failed = _fixture.Query(db => db.Set<GoldpathNotification>().AsNoTracking().Single());

        using var scope = _fixture.Services.CreateScope();
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.SendJob().ReplayItemAsync(failed.Id.ToString("N"),
                NotificationFixture.CreateContext(scope.ServiceProvider), CancellationToken.None));
        Assert.Contains("gateway refused", e.Message, StringComparison.Ordinal);
        Assert.Equal(GoldpathNotificationState.Failed,
            _fixture.Query(db => db.Set<GoldpathNotification>().AsNoTracking().Single()).State);
    }

    [Fact]
    public async Task An_unregistered_channel_fails_with_a_teaching_message()
    {
        using var fixture = new NotificationFixture(o => o.AddTemplate("hook", t => t.Channel("webhook", c => c.Body("", "x"))));
        await fixture.RequestAsync(new GoldpathNotificationRequest(
            "hook", "webhook", "https://x", "", new Dictionary<string, string>(), "hook:1"));

        var failures = await fixture.RunSendPassAsync();

        Assert.Contains("no channel named 'webhook'", Assert.Single(failures).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replaying_an_already_sent_notification_is_idempotent_evidence()
    {
        var notification = await _fixture.RequestAsync(NotificationFixture.Renewal("renewal:done"));
        await _fixture.RunSendPassAsync();
        Assert.Single(_fixture.Channel.Accepted);

        using var scope = _fixture.Services.CreateScope();
        await _fixture.SendJob().ReplayItemAsync(notification.Id.ToString("N"),
            NotificationFixture.CreateContext(scope.ServiceProvider), CancellationToken.None);

        Assert.Single(_fixture.Channel.Accepted);   // NOT re-sent
    }

    [Fact]
    public async Task Retention_nulls_bodies_and_attachment_content_but_the_evidence_survives()
    {
        var request = NotificationFixture.Renewal("renewal:retained");
        await _fixture.RequestAsync(new GoldpathNotificationRequest(
            request.Template, request.Channel, request.Recipient, request.Culture, request.Tokens, request.DedupKey)
        {
            Attachments = [new GoldpathNotificationAttachmentContent("policy.pdf", "application/pdf", [1, 2, 3])],
        });
        await _fixture.RunSendPassAsync();
        _fixture.Mutate(db => db.Set<GoldpathNotification>().Single().SentAt = DateTimeOffset.UtcNow.AddDays(-120));

        using (var scope = _fixture.Services.CreateScope())
        {
            var job = _fixture.RetentionJob();
            var context = NotificationFixture.CreateContext(scope.ServiceProvider);
            var plan = await job.PlanAsync(context, CancellationToken.None);
            Assert.Equal(["policy-renewal"], plan.ChunkPayloads);
            await job.ExecuteChunkAsync(NotificationFixture.MakeChunk(0, plan.ChunkPayloads[0]), context, CancellationToken.None);
        }

        var row = _fixture.Query(db => db.Set<GoldpathNotification>().AsNoTracking().Single());
        Assert.Null(row.Subject);
        Assert.Null(row.Body);
        Assert.NotNull(row.BodyDeletedAt);
        Assert.Equal(GoldpathNotificationState.Sent, row.State);                       // the evidence survives
        Assert.Equal(_fixture.Options.Template("policy-renewal").Hash, row.TemplateHash);   // the what-was-sent proof survives
        var attachment = _fixture.Query(db => db.Set<GoldpathNotificationAttachment>().AsNoTracking().Single());
        Assert.Null(attachment.Content);
        Assert.Equal("policy.pdf", attachment.Name);                              // the name is evidence too
    }
}
