using CorPay.Api.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CorPay.Api.Tests;

/// <summary>
/// SQLite cannot translate DateTimeOffset comparisons (provider limitation; Npgsql can) —
/// the OFFICIAL workaround for tests: convert to binary. Production never sees this type.
/// </summary>
public class SqliteOrdersDbContext(DbContextOptions<OrdersDbContext> options) : OrdersDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
        {
            property.SetValueConverter(property.ClrType == typeof(DateTimeOffset)
                ? new DateTimeOffsetToBinaryConverter()
                : new ValueConverter<DateTimeOffset?, long?>(
                    v => v.HasValue ? v.Value.ToUnixTimeMilliseconds() : null,
                    v => v.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(v.Value) : null));
        }
    }
}
