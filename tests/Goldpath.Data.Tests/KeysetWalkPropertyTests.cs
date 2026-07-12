using CsCheck;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Goldpath.Tests;

/// <summary>
/// The pagination invariant, generated instead of imagined: for ANY dataset (heavy duplicate
/// keys included) and ANY page size, a full cursor walk visits every row exactly once in order.
/// </summary>
public class KeysetWalkPropertyTests
{
    private sealed class Row
    {
        public long Id { get; set; }
        public long Bucket { get; set; }   // deliberately low-cardinality: duplicate-first-key pressure
    }

    private sealed class WalkDb(DbContextOptions<WalkDb> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    [Fact]
    public void Composite_walk_visits_every_row_exactly_once_for_any_dataset_and_page_size()
        => Gen.Select(Gen.Int[0, 60], Gen.Int[1, 20], Gen.Int[1, 5])
            .Sample(input =>
            {
                var (rowCount, pageSize, bucketCardinality) = input;

                using var connection = new SqliteConnection("DataSource=:memory:");
                connection.Open();
                using var db = new WalkDb(new DbContextOptionsBuilder<WalkDb>().UseSqlite(connection).Options);
                db.Database.EnsureCreated();

                for (var i = 1; i <= rowCount; i++)
                {
                    db.Rows.Add(new Row { Id = i, Bucket = i % bucketCardinality });
                }

                db.SaveChanges();

                var seen = new List<(long Bucket, long Id)>();
                string? cursor = null;
                do
                {
                    var page = db.Rows
                        .ToPageAsync(new PageRequest(cursor, pageSize), r => r.Bucket, r => r.Id)
                        .GetAwaiter().GetResult();
                    seen.AddRange(page.Items.Select(r => (r.Bucket, r.Id)));
                    cursor = page.NextCursor;
                }
                while (cursor is not null);

                var expected = Enumerable.Range(1, rowCount)
                    .Select(i => (Bucket: (long)(i % bucketCardinality), Id: (long)i))
                    .OrderBy(x => x.Bucket).ThenBy(x => x.Id)
                    .ToList();

                Assert.Equal(expected, seen);   // exact order, no skips, no duplicates
            }, iter: 40);
}
