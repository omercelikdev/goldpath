using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Goldpath;

/// <summary>A processed-row mark: the durable half of the (rail, file, line) idempotency key.</summary>
public sealed class GoldpathFileProcessedRow
{
    /// <summary>Rail name.</summary>
    public string Rail { get; set; } = "";

    /// <summary>File name.</summary>
    public string File { get; set; } = "";

    /// <summary>1-based line number.</summary>
    public int Line { get; set; }
}

/// <summary>A quarantined row with its reason.</summary>
public sealed class GoldpathFileQuarantineRow
{
    /// <summary>Rail name.</summary>
    public string Rail { get; set; } = "";

    /// <summary>File name.</summary>
    public string File { get; set; } = "";

    /// <summary>1-based line number.</summary>
    public int Line { get; set; }

    /// <summary>Why the row quarantined.</summary>
    public string Reason { get; set; } = "";
}

/// <summary>An archive mark for a completed file run.</summary>
public sealed class GoldpathFileArchiveRow
{
    /// <summary>Rail name.</summary>
    public string Rail { get; set; } = "";

    /// <summary>File name.</summary>
    public string File { get; set; } = "";
}

/// <summary>Model mapping for the database-backed file ledger.</summary>
public static class GoldpathFileExchangeModelExtensions
{
    /// <summary>Maps the ledger tables. Call from the app context's <c>OnModelCreating</c>.</summary>
    public static ModelBuilder AddGoldpathFileExchangeModel(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GoldpathFileProcessedRow>(e =>
        {
            e.ToTable("GoldpathFileProcessed");
            e.HasKey(x => new { x.Rail, x.File, x.Line });
            e.Property(x => x.Rail).HasMaxLength(128);
            e.Property(x => x.File).HasMaxLength(256);
        });
        modelBuilder.Entity<GoldpathFileQuarantineRow>(e =>
        {
            e.ToTable("GoldpathFileQuarantine");
            e.HasKey(x => new { x.Rail, x.File, x.Line });
            e.Property(x => x.Rail).HasMaxLength(128);
            e.Property(x => x.File).HasMaxLength(256);
            e.Property(x => x.Reason).HasMaxLength(1024);
        });
        modelBuilder.Entity<GoldpathFileArchiveRow>(e =>
        {
            e.ToTable("GoldpathFileArchive");
            e.HasKey(x => new { x.Rail, x.File });
            e.Property(x => x.Rail).HasMaxLength(128);
            e.Property(x => x.File).HasMaxLength(256);
        });
        return modelBuilder;
    }
}

/// <summary>
/// The database-backed ledger: rail progress lives in the app's own DbContext (mapped by
/// <see cref="GoldpathFileExchangeModelExtensions.AddGoldpathFileExchangeModel"/>), so the
/// zero-duplicate guarantee survives restarts and holds across nodes. Each call runs in its
/// own scope — the engine stays a singleton.
/// </summary>
public sealed class GoldpathEfFileLedger<TContext> : IGoldpathFileLedger
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopes;

    /// <summary>Registered by <c>AddGoldpathFileExchange&lt;TBuilder, TContext&gt;</c>.</summary>
    public GoldpathEfFileLedger(IServiceScopeFactory scopes) => _scopes = scopes;

    /// <inheritdoc />
    public async Task<bool> IsProcessedAsync(string rail, string file, int line, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        return await db.Set<GoldpathFileProcessedRow>().AsNoTracking()
            .AnyAsync(x => x.Rail == rail && x.File == file && x.Line == line, cancellationToken);
    }

    /// <inheritdoc />
    public async Task MarkProcessedAsync(string rail, string file, int line, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        db.Add(new GoldpathFileProcessedRow { Rail = rail, File = file, Line = line });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task QuarantineAsync(string rail, string file, int line, string reason, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var existing = await db.Set<GoldpathFileQuarantineRow>()
            .SingleOrDefaultAsync(x => x.Rail == rail && x.File == file && x.Line == line, cancellationToken);
        if (existing is null)
        {
            db.Add(new GoldpathFileQuarantineRow { Rail = rail, File = file, Line = line, Reason = reason });
        }
        else
        {
            existing.Reason = reason;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReleaseQuarantineAsync(string rail, string file, int line, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var existing = await db.Set<GoldpathFileQuarantineRow>()
            .SingleOrDefaultAsync(x => x.Rail == rail && x.File == file && x.Line == line, cancellationToken);
        if (existing is not null)
        {
            db.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GoldpathQuarantinedRow>> GetQuarantineAsync(string rail, string file, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var rows = await db.Set<GoldpathFileQuarantineRow>().AsNoTracking()
            .Where(x => x.Rail == rail && x.File == file)
            .OrderBy(x => x.Line)
            .ToListAsync(cancellationToken);
        return rows.Select(r => new GoldpathQuarantinedRow(r.Rail, r.File, r.Line, r.Reason)).ToList();
    }

    /// <inheritdoc />
    public async Task MarkArchivedAsync(string rail, string file, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var exists = await db.Set<GoldpathFileArchiveRow>().AsNoTracking()
            .AnyAsync(x => x.Rail == rail && x.File == file, cancellationToken);
        if (!exists)
        {
            db.Add(new GoldpathFileArchiveRow { Rail = rail, File = file });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
