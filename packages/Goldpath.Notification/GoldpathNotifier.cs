using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Goldpath;

/// <summary>The request seam (notification RFC D2): the app requests, the module sends.</summary>
public interface IGoldpathNotifier
{
    /// <summary>
    /// Renders and persists one notification intent. Rendering happens HERE: a missing
    /// token throws into the app's transaction — a bad notice never persists. The same
    /// dedup key requested twice returns the EXISTING row (idempotent by construction).
    /// </summary>
    Task<GoldpathNotification> RequestAsync(GoldpathNotificationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The scoped notifier: writes the evidence row through the app's own DbContext, so a
/// request inside the app's transaction commits WITH the domain change (the outbox idea,
/// applied to human messages).
/// </summary>
public sealed class GoldpathNotifier<TContext> : IGoldpathNotifier
    where TContext : DbContext
{
    private readonly TContext _db;
    private readonly GoldpathNotificationOptions _options;
    private readonly IServiceProvider _services;
    private readonly TimeProvider _time;
    private readonly ILogger<GoldpathNotifier<TContext>> _logger;

    /// <summary>Registered scoped by <c>AddGoldpathNotification</c>.</summary>
    public GoldpathNotifier(
        TContext db, GoldpathNotificationOptions options, IServiceProvider services,
        TimeProvider time, ILogger<GoldpathNotifier<TContext>> logger)
    {
        _db = db;
        _options = options;
        _services = services;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GoldpathNotification> RequestAsync(GoldpathNotificationRequest request, CancellationToken cancellationToken)
    {
        var existing = await _db.Set<GoldpathNotification>()
            .FirstOrDefaultAsync(n => n.DedupKey == request.DedupKey, cancellationToken);
        if (existing is not null)
        {
            GoldpathNotificationMetrics.DedupHit(request.Template);
            return existing;   // the retry storm lands once
        }

        var template = _options.Template(request.Template);
        var (subject, body, culture) = template.ChannelTemplate(request.Channel).Render(request.Culture, request.Tokens);

        var maySend = await _options.MaySendHook(request, _services);
        var notification = new GoldpathNotification
        {
            Id = Guid.NewGuid(),
            DedupKey = request.DedupKey,
            Template = template.Key,
            TemplateHash = template.Hash,
            Channel = request.Channel,
            Recipient = request.Recipient,
            Culture = culture,
            Subject = subject,
            Body = body,
            State = maySend ? GoldpathNotificationState.Requested : GoldpathNotificationState.Suppressed,
            Detail = maySend ? null : "suppressed by the MaySend hook — suppression is evidence too",
            NotBefore = request.NotBefore,
            RequestedAt = _time.GetUtcNow(),
            Tenant = request.Tenant,
            CorrelationId = request.CorrelationId,
        };
        _db.Set<GoldpathNotification>().Add(notification);
        foreach (var attachment in request.Attachments)
        {
            _db.Set<GoldpathNotificationAttachment>().Add(new GoldpathNotificationAttachment
            {
                NotificationId = notification.Id,
                Name = attachment.Name,
                ContentType = attachment.ContentType,
                Content = attachment.Content,
            });
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent identical request may have won the unique index — same outcome.
            var winner = await LoseTheRaceAsync(request.DedupKey, cancellationToken);
            if (winner is null)
            {
                throw;   // not the dedup race — a real store failure
            }

            GoldpathNotificationMetrics.DedupHit(request.Template);
            return winner;
        }

        if (!maySend)
        {
            GoldpathNotificationMetrics.Suppressed(template.Key, request.Channel);
        }

        _logger.LogInformation(
            "Notification {NotificationId} requested: template {Template}, channel {Channel} ({State}).",
            notification.Id, template.Key, request.Channel, notification.State);
        return notification;
    }

    private async Task<GoldpathNotification?> LoseTheRaceAsync(string dedupKey, CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();
        return await _db.Set<GoldpathNotification>().AsNoTracking()
            .FirstOrDefaultAsync(n => n.DedupKey == dedupKey, cancellationToken);
    }
}
