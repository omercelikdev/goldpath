using Goldpath;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Goldpath.AuditTrail.Tests;

/// <summary>
/// The column-shape contract under a CONVENTIONS-SHAPED host (goldpath#198): the host
/// convention defaults every string to 256, and a document column that does not declare
/// its own shape silently becomes varchar(256) — refusing or truncating real content.
/// Package tests never saw it because their contexts skipped the convention; this one
/// applies it exactly as a generated app does.
/// </summary>
public sealed class ContentColumnShapeTests
{
    private sealed class ConventionsContext(DbContextOptions<ConventionsContext> options) : DbContext(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
            => configurationBuilder.Properties<string>().HaveMaxLength(256);   // the host convention, verbatim

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddGoldpathAuditLog();
    }

    [Fact]
    public void Document_columns_declare_their_own_shape()
    {
        using var context = new ConventionsContext(new DbContextOptionsBuilder<ConventionsContext>().UseSqlite("DataSource=:memory:").Options);
        Assert.Equal(-1, context.Model.FindEntityType(typeof(GoldpathAuditLogEntry))!.FindProperty("OldValue")!.GetMaxLength());
        Assert.Equal(-1, context.Model.FindEntityType(typeof(GoldpathAuditLogEntry))!.FindProperty("NewValue")!.GetMaxLength());
        Assert.Equal(512, context.Model.FindEntityType(typeof(GoldpathAuditLogEntry))!.FindProperty("EntityKey")!.GetMaxLength());
    }
}
