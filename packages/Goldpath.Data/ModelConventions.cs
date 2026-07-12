using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Goldpath;

/// <summary>EF value converter for the <see cref="TenantId"/> value type.</summary>
public sealed class TenantIdConverter : ValueConverter<TenantId, string>
{
    /// <summary>Creates the converter.</summary>
    public TenantIdConverter()
        : base(static id => id.Value, static value => TenantId.Create(value))
    {
    }
}

/// <summary>
/// Goldpath model conventions (RFC goldpath-data §1): UTC temporal policy is enforced by the analyzer
/// (GP0301 — DateTimeOffset only); these helpers apply the wire-safe storage defaults.
/// The template generates both calls into the DbContext; brownfield consumers add them manually.
/// </summary>
public static class GoldpathModelConventions
{
    /// <summary>
    /// Applies a Goldpath package's model contribution with an OWNERSHIP decision (migrations
    /// RFC D3): when <paramref name="excludeFromMigrations"/> is true, every entity the
    /// contribution ADDS is mapped for querying but excluded from THIS context's
    /// migrations — a secondary head (e.g. a jobs worker running its own fleet) sees the
    /// shared tables without ever generating DDL for them. One table set, ONE owner.
    /// </summary>
    public static ModelBuilder AddGoldpathContribution(this ModelBuilder modelBuilder, bool excludeFromMigrations, Action<ModelBuilder> contribute)
    {
        if (!excludeFromMigrations)
        {
            contribute(modelBuilder);
            return modelBuilder;
        }

        var owned = modelBuilder.Model.GetEntityTypes().Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        contribute(modelBuilder);
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(e => !owned.Contains(e.Name)))
        {
            entityType.SetIsTableExcludedFromMigrations(true);
        }

        return modelBuilder;
    }

    /// <summary>
    /// Convention-level defaults — call from <c>DbContext.ConfigureConventions</c>:
    /// string max length 256 (explicit lengths still win), decimal precision 18,4
    /// (money-safe), <see cref="TenantId"/> conversion (max length 64).
    /// </summary>
    public static ModelConfigurationBuilder ApplyGoldpathConventions(this ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<string>().HaveMaxLength(256);
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
        configurationBuilder.Properties<TenantId>().HaveConversion<TenantIdConverter>().HaveMaxLength(TenantId.MaxLength);
        return configurationBuilder;
    }

    /// <summary>
    /// Model-level defaults — call from <c>DbContext.OnModelCreating</c>:
    /// enums stored as strings (readable, index-friendly, safe against reordering).
    /// </summary>
    public static ModelBuilder ApplyGoldpathModelDefaults(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

                // Respect explicit conversions in BOTH forms: a converter instance
                // (HasConversion(new ...)) and a provider-type conversion (HasConversion<int>()).
                if (clrType.IsEnum && property.GetValueConverter() is null && property.GetProviderClrType() is null)
                {
                    property.SetProviderClrType(typeof(string));
                    property.SetMaxLength(property.GetMaxLength() ?? 64);
                }
            }
        }

        return modelBuilder;
    }
}
