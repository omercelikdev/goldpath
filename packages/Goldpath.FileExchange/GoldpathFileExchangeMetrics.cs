using System.Diagnostics.Metrics;

namespace Goldpath;

/// <summary>
/// The module's meter ("Goldpath.FileExchange" — the ServiceDefaults wildcard exports it):
/// one counter per lifecycle step, tagged by rail, so the ops dashboard reads the SAME story
/// the events tell — files in, files rejected whole, rows applied, rows quarantined, rows
/// skipped as duplicates (the replay proof, live).
/// </summary>
public static class GoldpathFileExchangeMetrics
{
    private static readonly Meter Meter = new("Goldpath.FileExchange");
    private static readonly Counter<long> FilesReceived = Meter.CreateCounter<long>("goldpath_fileexchange_files_received_total", description: "Files that passed the file-level contract and started ingesting.");
    private static readonly Counter<long> FilesRejected = Meter.CreateCounter<long>("goldpath_fileexchange_files_rejected_total", description: "Files that failed the file-level contract and ingested nothing.");
    private static readonly Counter<long> RowsProcessed = Meter.CreateCounter<long>("goldpath_fileexchange_rows_processed_total", description: "Rows applied by the rail's handler.");
    private static readonly Counter<long> RowsQuarantined = Meter.CreateCounter<long>("goldpath_fileexchange_rows_quarantined_total", description: "Rows quarantined with a reason (the batch continued).");
    private static readonly Counter<long> RowsDuplicate = Meter.CreateCounter<long>("goldpath_fileexchange_rows_duplicate_total", description: "Rows skipped because the (rail, file, line) key had already applied.");

    internal static void CountFileReceived(string rail) => FilesReceived.Add(1, Tag(rail));

    internal static void CountFileRejected(string rail) => FilesRejected.Add(1, Tag(rail));

    internal static void CountRows(string rail, int processed, int quarantined, int duplicate)
    {
        if (processed > 0)
        {
            RowsProcessed.Add(processed, Tag(rail));
        }

        if (quarantined > 0)
        {
            RowsQuarantined.Add(quarantined, Tag(rail));
        }

        if (duplicate > 0)
        {
            RowsDuplicate.Add(duplicate, Tag(rail));
        }
    }

    private static KeyValuePair<string, object?> Tag(string rail) => new("rail", rail);
}
