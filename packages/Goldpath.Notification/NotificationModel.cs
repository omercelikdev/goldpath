using Microsoft.EntityFrameworkCore;

namespace Goldpath;

/// <summary>Lifecycle of one notification (notification RFC D2/D6). Terminal states keep their evidence forever.</summary>
public enum GoldpathNotificationState
{
    /// <summary>Rendered, persisted, waiting for the send run (or its NotBefore).</summary>
    Requested = 0,

    /// <summary>The MaySend hook said no — suppression is evidence too. Terminal.</summary>
    Suppressed = 1,

    /// <summary>The channel ACCEPTED the message (accepted ≠ delivered/read — named honestly). Terminal.</summary>
    Sent = 2,

    /// <summary>Attempts exhausted or interrupted mid-flight; sits in the repair queue for a HUMAN-confirmed replay.</summary>
    Failed = 3,
}

/// <summary>
/// One notification: the EVIDENCE row (who/when/what/through-which-channel answered by
/// query). Rendered at request time; the template hash proves WHAT was sent even after
/// the body retention window nulls the content.
/// </summary>
public class GoldpathNotification
{
    /// <summary>Surrogate id (the repair-queue item key).</summary>
    public Guid Id { get; set; }

    /// <summary>The business identity — UNIQUE: the same event requested twice lands once.</summary>
    public string DedupKey { get; set; } = "";

    /// <summary>The registered template key.</summary>
    public string Template { get; set; } = "";

    /// <summary>SHA-256 over the template's registered content — the what-was-sent proof.</summary>
    public string TemplateHash { get; set; } = "";

    /// <summary>The channel name (email, webhook, ...).</summary>
    public string Channel { get; set; } = "";

    /// <summary>The destination address (masked on report surfaces when DataProtection is present).</summary>
    public string Recipient { get; set; } = "";

    /// <summary>The culture the template rendered with (after fallback).</summary>
    public string Culture { get; set; } = "";

    /// <summary>Rendered subject (nulled by body retention alongside the body).</summary>
    public string? Subject { get; set; }

    /// <summary>Rendered body (nulled after the template's DeleteBodyAfter window).</summary>
    public string? Body { get; set; }

    /// <summary>Set when body retention removed the rendered content.</summary>
    public DateTimeOffset? BodyDeletedAt { get; set; }

    /// <summary>Lifecycle state — the optimistic-concurrency token.</summary>
    public GoldpathNotificationState State { get; set; }

    /// <summary>Earliest send time (quiet hours are a field, not a policy engine).</summary>
    public DateTimeOffset? NotBefore { get; set; }

    /// <summary>Request timestamp.</summary>
    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>Stamped BEFORE the channel call (claim-before-send, MDM constraint 2).</summary>
    public DateTimeOffset? ClaimedAt { get; set; }

    /// <summary>The channel-accepted timestamp.</summary>
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>The gave-up (or interrupted) timestamp.</summary>
    public DateTimeOffset? FailedAt { get; set; }

    /// <summary>Send attempts consumed (bounded per definition).</summary>
    public int Attempts { get; set; }

    /// <summary>The last channel error / the suppression or interruption reason (teaching text).</summary>
    public string? Detail { get; set; }

    /// <summary>Owning tenant (fail-closed scoping), when tenant-bound.</summary>
    public string? Tenant { get; set; }

    /// <summary>The caller's correlation id (per-instruction tracing, finance NFR).</summary>
    public string? CorrelationId { get; set; }
}

/// <summary>One attachment of a notification (nulled alongside the body by retention).</summary>
public class GoldpathNotificationAttachment
{
    /// <summary>Surrogate id.</summary>
    public long Id { get; set; }

    /// <summary>Owning notification.</summary>
    public Guid NotificationId { get; set; }

    /// <summary>File name shown to the recipient.</summary>
    public string Name { get; set; } = "";

    /// <summary>MIME type.</summary>
    public string ContentType { get; set; } = "";

    /// <summary>The bytes (null after body retention — the NAME survives as evidence).</summary>
    public byte[]? Content { get; set; }
}

/// <summary>Maps the notification tables onto the app's own DbContext (same database).</summary>
public static class GoldpathNotificationModel
{
    /// <summary>Adds the evidence and attachment tables to the model.</summary>
    public static ModelBuilder AddGoldpathNotification(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GoldpathNotification>(notification =>
        {
            notification.ToTable("GoldpathNotifications");
            notification.HasKey(n => n.Id);
            notification.Property(n => n.Id).ValueGeneratedNever();
            notification.Property(n => n.DedupKey).HasMaxLength(256);
            notification.Property(n => n.Template).HasMaxLength(128);
            notification.Property(n => n.TemplateHash).HasMaxLength(64);
            notification.Property(n => n.Channel).HasMaxLength(64);
            notification.Property(n => n.Recipient).HasMaxLength(512);
            notification.Property(n => n.Culture).HasMaxLength(16);
            notification.Property(n => n.Subject).HasMaxLength(1024);
            notification.Property(n => n.Detail).HasMaxLength(1024);
            notification.Property(n => n.Tenant).HasMaxLength(128);
            notification.Property(n => n.CorrelationId).HasMaxLength(128);
            notification.Property(n => n.State).IsConcurrencyToken();
            notification.HasIndex(n => n.DedupKey).IsUnique();
            notification.HasIndex(n => new { n.State, n.RequestedAt });
            notification.HasIndex(n => new { n.Template, n.RequestedAt });
        });

        modelBuilder.Entity<GoldpathNotificationAttachment>(attachment =>
        {
            attachment.ToTable("GoldpathNotificationAttachments");
            attachment.HasKey(a => a.Id);
            attachment.Property(a => a.Name).HasMaxLength(260);
            attachment.Property(a => a.ContentType).HasMaxLength(128);
            attachment.HasIndex(a => a.NotificationId);
        });

        return modelBuilder;
    }
}
