using Microsoft.CodeAnalysis;

namespace Goldpath.Analyzers;

/// <summary>All Goldpath diagnostic descriptors (rule ids are wire contracts — never recycled).</summary>
public static class Descriptors
{
    private const string Category = "Goldpath";
    private const string HelpBase = "https://github.com/omercelikdev/goldpath/blob/main/docs/rfc/";

    /// <summary>GP0102: new HttpClient() bypasses the resilience/discovery defaults.</summary>
    public static readonly DiagnosticDescriptor NewHttpClient = new(
        "GP0102",
        "Use IHttpClientFactory instead of new HttpClient()",
        "Instantiating HttpClient directly bypasses the Goldpath resilience and service-discovery defaults; inject it via IHttpClientFactory",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-servicedefaults.md");

    /// <summary>GP0202: Skip/Take offset pagination on a query.</summary>
    public static readonly DiagnosticDescriptor OffsetPagination = new(
        "GP0202",
        "Use keyset pagination instead of Skip/Take",
        "Skip/Take offset pagination degrades on large tables; use the Goldpath keyset primitive (ToPageAsync)",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-apidefaults.md");

    /// <summary>GP0301: DateTime property on a Goldpath-marked entity.</summary>
    public static readonly DiagnosticDescriptor DateTimeOnEntity = new(
        "GP0301",
        "Use DateTimeOffset on entities",
        "Property '{0}' is DateTime on a Goldpath-marked entity; the UTC policy requires DateTimeOffset",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-data.md");

    /// <summary>GP0302: runtime schema mutation without a Development guard.</summary>
    public static readonly DiagnosticDescriptor RuntimeMigrate = new(
        "GP0302",
        "Runtime Migrate/EnsureCreated must be Development-guarded",
        "'{0}' outside a Development guard: production applies the CI migration bundle, never runtime migration",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-data.md");

    /// <summary>GP0303: non-constant SQL in raw APIs.</summary>
    public static readonly DiagnosticDescriptor RawSqlInterpolation = new(
        "GP0303",
        "Do not compose raw SQL from interpolated or concatenated strings",
        "'{0}' receives a non-constant SQL string (injection risk); use the interpolation-safe FromSql/parameters",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-data.md");

    /// <summary>GP0501: IAuditLogged entities require AddGoldpathAuditLog() on the model.</summary>
    public static readonly DiagnosticDescriptor AuditLogNotWired = new(
        "GP0501",
        "IAuditLogged entities require AddGoldpathAuditLog() in the model",
        "'{0}' is IAuditLogged but no OnModelCreating in this assembly calls AddGoldpathAuditLog() — change rows would silently not be written",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-audittrail.md",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>GP0502: stamp fields are infrastructure-owned.</summary>
    public static readonly DiagnosticDescriptor ManualStampWrite = new(
        "GP0502",
        "Audit stamp fields are filled by the AuditTrail contributor",
        "Application code writes '{0}' on an IAuditedEntity — stamps are infrastructure-owned; remove the manual assignment",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-audittrail.md");

    /// <summary>GP0601: ISoftDeletable entities require ApplyGoldpathSoftDelete() on the model.</summary>
    public static readonly DiagnosticDescriptor SoftDeleteNotWired = new(
        "GP0601",
        "ISoftDeletable entities require ApplyGoldpathSoftDelete() in the model",
        "'{0}' is ISoftDeletable but no OnModelCreating in this assembly calls ApplyGoldpathSoftDelete() — deleted rows would stay visible",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-softdelete.md",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>GP1003: [Idempotent] on a query is a no-op.</summary>
    public static readonly DiagnosticDescriptor IdempotentOnQuery = new(
        "GP1003",
        "[Idempotent] on a query has no effect",
        "'{0}' is a query (already idempotent by contract) — the [Idempotent] attribute is a no-op here",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-idempotency.md");

    /// <summary>GP0401: publishing a type without the IIntegrationEvent marker.</summary>
    public static readonly DiagnosticDescriptor PublishUnmarked = new(
        "GP0401",
        "Only IIntegrationEvent types may cross the service boundary",
        "'{0}' is published to the bus but does not implement IIntegrationEvent; in-process events are Mediant notifications",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-messaging.md");

    /// <summary>GP0402: a type in both event worlds.</summary>
    public static readonly DiagnosticDescriptor NotificationCrossMarked = new(
        "GP0402",
        "A Mediant notification must not be an integration event",
        "'{0}' implements both Mediant INotification and IIntegrationEvent; one world only — split the type",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-messaging.md");

    /// <summary>GP0701: PII crossing the service boundary.</summary>
    public static readonly DiagnosticDescriptor ClassifiedOnIntegrationEvent = new(
        "GP0701",
        "Classified property on an integration event",
        "'{0}.{1}' is classified as personal/sensitive data and crosses the service boundary unmasked — carry an id or a masked projection instead",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-dataprotection.md");

    /// <summary>GP0702: classification without the DataProtection module.</summary>
    public static readonly DiagnosticDescriptor ClassifiedWithoutModule = new(
        "GP0702",
        "Classified property but the DataProtection module is absent",
        "'{0}.{1}' is classified but Goldpath.DataProtection is not referenced — nothing masks it in audit rows or logs",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-dataprotection.md",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>GP0801: a side-effecting request served from cache.</summary>
    public static readonly DiagnosticDescriptor CacheableOnCommand = new(
        "GP0801",
        "[Cacheable] on a command",
        "'{0}' is a command — serving its result from cache skips the side effect; cache queries, invalidate on commands",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-caching.md");

    /// <summary>GP0802: [InvalidatesCache] on a query is a no-op.</summary>
    public static readonly DiagnosticDescriptor InvalidatesCacheOnQuery = new(
        "GP0802",
        "[InvalidatesCache] on a query has no effect",
        "'{0}' is a query — it changes nothing, so it has nothing to invalidate",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-caching.md");

    /// <summary>GP0803: raw cache keys bypass tenant scoping.</summary>
    public static readonly DiagnosticDescriptor RawCacheKey = new(
        "GP0803",
        "Raw string cache key bypasses tenant scoping",
        "Build cache keys through GoldpathCacheKeys — a raw string key is shared across tenants (cross-tenant cache bleed)",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-caching.md");

    /// <summary>GP0901: IMultiTenant entities need the model wiring.</summary>
    public static readonly DiagnosticDescriptor MultiTenancyNotWired = new(
        "GP0901",
        "IMultiTenant entities require ApplyGoldpathMultiTenancy(this) in the model",
        "'{0}' is IMultiTenant but no model in this assembly calls ApplyGoldpathMultiTenancy — rows would be visible to every tenant",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-multitenancy.md",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>GP0902: tenant stamps are infrastructure-owned.</summary>
    public static readonly DiagnosticDescriptor ManualTenantWrite = new(
        "GP0902",
        "TenantId is stamped by the MultiTenancy contributor",
        "Application code writes '{0}.TenantId' — the contributor stamps it from the ambient tenant; cross-tenant writes need GoldpathTenant.Use(...)",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-multitenancy.md");

    /// <summary>GP0903: filter-dodging queries need an explicit Bypass scope.</summary>
    public static readonly DiagnosticDescriptor IgnoreFiltersWithoutBypass = new(
        "GP0903",
        "IgnoreQueryFilters on a tenant-filtered entity without a Bypass scope",
        "This query drops the tenant filter on '{0}' with no GoldpathTenant.Bypass() in sight — widen reads only inside the explicit, greppable scope",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-multitenancy.md");

    /// <summary>GP1101: raw lock names risk cross-tenant collisions.</summary>
    public static readonly DiagnosticDescriptor RawLockName = new(
        "GP1101",
        "Raw string lock name bypasses tenant scoping",
        "Build lock names through GoldpathLockNames — a raw string collides across tenants (use For(...) or the explicit Global(...))",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-locking.md");

    /// <summary>GP1102: an unheld handle is a lock leak.</summary>
    public static readonly DiagnosticDescriptor LockHandleNotDisposed = new(
        "GP1102",
        "Lock handle is not disposed",
        "The acquired handle is discarded or stored without a using — the lock leaks until the lease/session dies",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-locking.md");

    /// <summary>GP1201: the anonymous surface is inventory.</summary>
    public static readonly DiagnosticDescriptor AllowAnonymousInventory = new(
        "GP1201",
        "Anonymous endpoint",
        "'{0}' opts out of the secure-by-default policy — every [AllowAnonymous] is attack surface; keep the inventory deliberate",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-auth.md");

    /// <summary>GP1202: secrets never live in source.</summary>
    public static readonly DiagnosticDescriptor SecretInSource = new(
        "GP1202",
        "Auth secret as a string literal",
        "'{0}' is assigned a literal — keys and signing material belong in the secret store, never in source",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-auth.md");
    /// <summary>GP1301: chunk atomicity belongs to the runner.</summary>
    public static readonly DiagnosticDescriptor ChunkOwnTransaction = new(
        "GP1301",
        "ExecuteChunkAsync opens its own transaction",
        "The chunk opens a database transaction — checkpoint atomicity belongs to the jobs runner; a chunk-scoped transaction can commit work the checkpoint never saw (or vice versa)",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-jobs.md");

    /// <summary>GP1302: silence is how 07:00 gets missed.</summary>
    public static readonly DiagnosticDescriptor JobWithoutDeadline = new(
        "GP1302",
        "Job registered without a Deadline",
        "'{0}' has no Deadline — deadline prediction cannot alert on a job with no SLA; every scenario card's job has one",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-jobs.md");

    /// <summary>GP1303: plans discover, they never materialize.</summary>
    public static readonly DiagnosticDescriptor PlanMaterializesItems = new(
        "GP1303",
        "PlanAsync materializes the item list",
        "PlanAsync calls '{0}' — plans should page/count and emit range payloads; materializing 100k+ items into memory is the telco card's outage",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-jobs.md");
    /// <summary>GP1401: an archive that cannot honor erasure is a liability.</summary>
    public static readonly DiagnosticDescriptor ArchiveWithoutDataProtection = new(
        "GP1401",
        "Archived graph carries classified data without the DataProtection module",
        "The archive of '{0}' captures [GoldpathPersonalData] fields but the DataProtection module is not referenced — erasure requests against this archive would be impossible",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-archival.md");

    /// <summary>GP1402: age is rarely the only truth.</summary>
    public static readonly DiagnosticDescriptor RowRetentionWithoutGuard = new(
        "GP1402",
        "Row retention without a Where guard",
        "Row retention of '{0}' purges by age alone — make the safe-to-purge predicate explicit (for example: only rolled-up detail)",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-archival.md");

    /// <summary>GP1403: model the lifecycle, never guess it.</summary>
    public static readonly DiagnosticDescriptor ArchiveWithoutLifecycle = new(
        "GP1403",
        "Archive definition without a DueWhen lifecycle event",
        "The archive of '{0}' declares no DueWhen — archiving by insert-age alone usually means the lifecycle was never modeled",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-archival.md");

    /// <summary>GP1501: an unbounded intake is a decision nobody made.</summary>
    public static readonly DiagnosticDescriptor BulkBatchWithoutCeiling = new(
        "GP1501",
        "Bulk batch definition without a MaxRows ceiling",
        "The bulk definition of '{0}' declares no MaxRows — an unbounded intake is a denial-of-service invitation; the ceiling is a decision, not a default",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-bulk.md");

    /// <summary>GP1502: the engine batches row state per chunk; per-row saves fight it.</summary>
    public static readonly DiagnosticDescriptor BulkHandlerSavesPerRow = new(
        "GP1502",
        "Bulk row handler calls SaveChanges directly",
        "'{0}' calls SaveChanges inside a row handler — the engine writes row state BATCHED per chunk; per-row saves wreck the intake budget and fight the checkpoint semantics",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-bulk.md");

    /// <summary>GP1503: skipping the gate is legitimate — and must be visible.</summary>
    public static readonly DiagnosticDescriptor BulkAutoApprove = new(
        "GP1503",
        "Bulk batch definition skips the approval gate",
        "The bulk definition of '{0}' uses AutoApprove — legitimate for imports and reference data, but the gate's absence should be a visible review decision",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-bulk.md");

    /// <summary>GP1601: sending around the notifier is an evidence hole.</summary>
    public static readonly DiagnosticDescriptor NotificationBypass = new(
        "GP1601",
        "Direct SMTP client bypasses the notifier",
        "'{0}' is constructed directly while Goldpath.Notification is referenced — a message sent around the notifier leaves NO evidence row; request through IGoldpathNotifier instead",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-notification.md");

    /// <summary>GP1602: keeping rendered personal data forever must be a visible decision.</summary>
    public static readonly DiagnosticDescriptor NotificationTemplateWithoutRetention = new(
        "GP1602",
        "Notification template without a DeleteBodyAfter window",
        "The template '{0}' declares no DeleteBodyAfter — rendered bodies carry personal data and would be kept forever; make the retention window a visible decision",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-notification.md");

    /// <summary>GP1701: an unbounded L4 enumeration is an outage, not a campaign.</summary>
    public static readonly DiagnosticDescriptor CampaignWithoutCeiling = new(
        "GP1701",
        "Campaign type without a MaxTargets ceiling",
        "The campaign type over '{0}' declares no MaxTargets — an unbounded enumeration at L4 scale is an outage, not a campaign; the ceiling is a decision, not a default",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-campaign.md");

    /// <summary>GP1702: outcomes flow through the batching sink; per-item saves melt the database.</summary>
    public static readonly DiagnosticDescriptor CampaignHandlerSavesPerItem = new(
        "GP1702",
        "Campaign item handler calls SaveChanges directly",
        "'{0}' calls SaveChanges inside an item handler — outcomes flow through the batching SINK (constraint 4); per-item saves at 30M-item scale melt the database",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-campaign.md");

    /// <summary>GP1703: messaging humans around the notification seam skips the evidence discipline.</summary>
    public static readonly DiagnosticDescriptor CampaignHandlerBypassesNotification = new(
        "GP1703",
        "Campaign item handler messages humans without the notification seam",
        "'{0}' constructs an SMTP client inside a campaign item handler — evidence discipline for human-facing messages exists (Goldpath.Notification: dedup, template hash, per-recipient rows); bypassing it should be a visible decision",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-campaign.md");

    /// <summary>GP1801: two contexts generating DDL for the same shared tables.</summary>
    public static readonly DiagnosticDescriptor SharedTablesDoubleOwnership = new(
        "GP1801",
        "Shared Goldpath tables mapped by a second context without excludeFromMigrations",
        "'{0}' maps {1} while '{2}' in this assembly already owns it — one table set has ONE migration owner (migrations RFC D3); map the non-owner with excludeFromMigrations: true",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpBase + "goldpath-migrations.md",
        customTags: WellKnownDiagnosticTags.CompilationEnd);
}
