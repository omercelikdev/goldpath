using System.Globalization;
using System.Text;

namespace Goldpath;

/// <summary>One raw record produced by a format reader: 1-based row number + named fields.</summary>
public sealed record GoldpathBulkRawRow(int RowNumber, IReadOnlyDictionary<string, string> Fields);

/// <summary>A structural problem the reader found in one line (reported, never thrown).</summary>
public sealed record GoldpathBulkRawError(int RowNumber, string Message);

/// <summary>
/// The format seam (bulk RFC D3): turns a byte stream into named-field records. v1 ships
/// CSV only; fixed-width/XLSX/JSON-lines wait for their written triggers.
/// </summary>
public interface IGoldpathBulkFormat
{
    /// <summary>
    /// Reads every record, calling <paramref name="onRow"/> per parseable record and
    /// <paramref name="onError"/> per structural finding. Returns the data-row count
    /// (header excluded, broken lines included — they have row numbers too).
    /// </summary>
    Task<int> ReadAsync(
        Stream stream,
        Func<GoldpathBulkRawRow, Task> onRow,
        Func<GoldpathBulkRawError, Task> onError,
        CancellationToken cancellationToken);
}

/// <summary>CSV reader options: deliberately boring, RFC-4180-shaped.</summary>
public sealed class GoldpathCsvOptions
{
    /// <summary>Field delimiter (default comma).</summary>
    public char Delimiter { get; set; } = ',';

    /// <summary>Culture used for typed conversion downstream (default invariant).</summary>
    public string Culture { get; set; } = "";

    /// <summary>Resolves the configured culture.</summary>
    public CultureInfo ResolveCulture()
        => Culture.Length == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(Culture);
}

/// <summary>
/// The v1 CSV format: header row REQUIRED (columns map by name, not position — a reordered
/// export must not silently shift money into the wrong field), quoted fields with escaped
/// quotes, configurable delimiter. Structural problems become row errors, never exceptions:
/// one broken line must not kill a 10k-row intake.
/// </summary>
public sealed class GoldpathCsvFormat : IGoldpathBulkFormat
{
    private readonly GoldpathCsvOptions _options;

    /// <summary>Creates the reader over the given options.</summary>
    public GoldpathCsvFormat(GoldpathCsvOptions options) => _options = options;

    /// <inheritdoc />
    public async Task<int> ReadAsync(
        Stream stream,
        Func<GoldpathBulkRawRow, Task> onRow,
        Func<GoldpathBulkRawError, Task> onError,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (headerLine is null || headerLine.Trim().Length == 0)
        {
            await onError(new GoldpathBulkRawError(0, "the file has no header row — v1 CSV maps columns by name"));
            return 0;
        }

        var header = SplitLine(headerLine);
        var rowNumber = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0)
            {
                continue;   // trailing/blank lines are noise, not data
            }

            rowNumber++;
            var fields = SplitLine(line);
            if (fields.Count != header.Count)
            {
                await onError(new GoldpathBulkRawError(rowNumber,
                    $"expected {header.Count} fields (per the header) but found {fields.Count}"));
                continue;
            }

            var map = new Dictionary<string, string>(header.Count, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Count; i++)
            {
                map[header[i].Trim()] = fields[i];
            }

            await onRow(new GoldpathBulkRawRow(rowNumber, map));
        }

        return rowNumber;
    }

    /// <summary>RFC-4180 field split: quotes wrap, doubled quotes escape, delimiter configurable.</summary>
    internal List<string> SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"' && current.Length == 0)
            {
                inQuotes = true;
            }
            else if (c == _options.Delimiter)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
