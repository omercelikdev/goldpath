using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace Goldpath;

/// <summary>An active delegation row (the record itself has no key; the row carries one).</summary>
public sealed class GoldpathApprovalDelegationRow
{
    /// <summary>Row id.</summary>
    public long Id { get; set; }

    /// <summary>Who delegated.</summary>
    public string From { get; set; } = "";

    /// <summary>Who received the delegation.</summary>
    public string To { get; set; } = "";

    /// <summary>Absolute UTC expiry.</summary>
    public DateTimeOffset Until { get; set; }
}

/// <summary>One collected quorum signature as a row (the record itself has no key; the row carries one).</summary>
public sealed class GoldpathApprovalSignatureRow
{
    /// <summary>Row id.</summary>
    public long Id { get; set; }

    /// <summary>The request the signature belongs to.</summary>
    public Guid RequestId { get; set; }

    /// <summary>Who signed.</summary>
    public string SignedBy { get; set; } = "";

    /// <summary>The rung the signature counts for.</summary>
    public string Role { get; set; } = "";

    /// <summary>When it was signed (UTC).</summary>
    public DateTimeOffset At { get; set; }
}

/// <summary>Model mapping for the database-backed approval store.</summary>
public static class GoldpathApprovalModelExtensions
{
    private static readonly JsonSerializerOptions TrailJson = JsonSerializerOptions.Default;

    /// <summary>Maps the approval tables. Call from the app context's <c>OnModelCreating</c>.</summary>
    public static ModelBuilder AddGoldpathApprovalModel(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GoldpathApprovalRequest>(e =>
        {
            e.ToTable("GoldpathApprovals");
            e.HasKey(x => x.Id);
            e.Property(x => x.Ladder).HasMaxLength(128);
            e.Property(x => x.Subject).HasMaxLength(256);
            e.Property(x => x.RequestedBy).HasMaxLength(128);
            e.Property(x => x.PendingRole).HasMaxLength(128);
            e.Property(x => x.DecidedBy).HasMaxLength(128);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(x => x.Status);
            // The trail is one audit document, read and written with its request — a JSON
            // column, not a join, so the store stays two tables on every provider.
            e.Property(x => x.Trail)
                .HasConversion(
                    trail => JsonSerializer.Serialize(trail, TrailJson),
                    json => JsonSerializer.Deserialize<List<GoldpathApprovalTrailEntry>>(json, TrailJson) ?? new List<GoldpathApprovalTrailEntry>(),
                    new ValueComparer<List<GoldpathApprovalTrailEntry>>(
                        (a, b) => JsonSerializer.Serialize(a, TrailJson) == JsonSerializer.Serialize(b, TrailJson),
                        v => JsonSerializer.Serialize(v, TrailJson).GetHashCode(),
                        v => JsonSerializer.Deserialize<List<GoldpathApprovalTrailEntry>>(JsonSerializer.Serialize(v, TrailJson), TrailJson)!))
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<GoldpathApprovalDelegationRow>(e =>
        {
            e.ToTable("GoldpathApprovalDelegations");
            e.HasKey(x => x.Id);
            e.Property(x => x.From).HasMaxLength(128);
            e.Property(x => x.To).HasMaxLength(128);
            e.HasIndex(x => x.Until);
        });

        modelBuilder.Entity<GoldpathApprovalSignatureRow>(e =>
        {
            e.ToTable("GoldpathApprovalSignatures");
            e.HasKey(x => x.Id);
            e.Property(x => x.SignedBy).HasMaxLength(128);
            e.Property(x => x.Role).HasMaxLength(128);
            e.HasIndex(x => x.RequestId);
        });

        return modelBuilder;
    }
}

/// <summary>
/// The database-backed store: approval state lives in the app's own DbContext (mapped by
/// <see cref="GoldpathApprovalModelExtensions.AddGoldpathApprovalModel"/>), so requests
/// survive restarts and every node sees the same worklist. Each call runs in its own scope,
/// so the store itself carries no scoped state.
/// </summary>
public sealed class GoldpathEfApprovalStore<TContext> : IGoldpathApprovalStore
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopes;

    /// <summary>Registered by <c>AddGoldpathApprovals&lt;TBuilder, TContext&gt;</c>.</summary>
    public GoldpathEfApprovalStore(IServiceScopeFactory scopes) => _scopes = scopes;

    /// <inheritdoc />
    public async Task AddAsync(GoldpathApprovalRequest request, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        db.Add(request);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GoldpathApprovalRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        return await db.Set<GoldpathApprovalRequest>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(GoldpathApprovalRequest request, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        db.Update(request);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GoldpathApprovalRequest>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        return await db.Set<GoldpathApprovalRequest>().AsNoTracking()
            .Where(x => x.Status == GoldpathApprovalStatus.Pending)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddDelegationAsync(GoldpathApprovalDelegation delegation, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        db.Add(new GoldpathApprovalDelegationRow { From = delegation.From, To = delegation.To, Until = delegation.Until });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GoldpathApprovalDelegation>> GetDelegationsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        // DateTimeOffset comparison does not translate on every provider (SQLite);
        // delegations are few by nature, so the expiry filter runs client-side.
        var rows = await db.Set<GoldpathApprovalDelegationRow>().AsNoTracking()
            .ToListAsync(cancellationToken);
        return rows.Where(r => r.Until > now)
            .Select(r => new GoldpathApprovalDelegation(r.From, r.To, r.Until)).ToList();
    }

    /// <inheritdoc />
    public async Task AddSignatureAsync(GoldpathApprovalSignature signature, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        db.Add(new GoldpathApprovalSignatureRow { RequestId = signature.RequestId, SignedBy = signature.SignedBy, Role = signature.Role, At = signature.At });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GoldpathApprovalSignature>> GetSignaturesAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        // Ordered client-side for the same provider reason as the delegations above —
        // DateTimeOffset ordering does not translate on SQLite, and one request's
        // signatures are few by nature.
        var rows = await db.Set<GoldpathApprovalSignatureRow>().AsNoTracking()
            .Where(x => x.RequestId == requestId)
            .ToListAsync(cancellationToken);
        return rows.OrderBy(r => r.At)
            .Select(r => new GoldpathApprovalSignature(r.RequestId, r.SignedBy, r.Role, r.At)).ToList();
    }
}
