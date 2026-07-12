using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Goldpath;

BenchmarkRunner.Run<CursorBenchmarks>();

/// <summary>
/// Baseline for the pagination primitive's hot path (library constitution: "optimized" claims
/// are measured, not asserted). Run: dotnet run -c Release --project benchmarks/Goldpath.Benchmarks
/// CI regression tracking arrives with the CI templates (Phase 1 item 7).
/// </summary>
[MemoryDiagnoser]
public class CursorBenchmarks
{
    private static readonly DateTimeOffset s_timestamp = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid s_id = Guid.Parse("4b4bd6b7-1111-2222-3333-000000000042");
    private readonly string _singleCursor = GoldpathCursor.Encode(123456789L);
    private readonly string _compositeCursor = GoldpathCursor.Encode(s_timestamp, s_id);

    [Benchmark]
    public string EncodeSingle() => GoldpathCursor.Encode(123456789L);

    [Benchmark]
    public string EncodeComposite() => GoldpathCursor.Encode(s_timestamp, s_id);

    [Benchmark]
    public bool DecodeSingle() => GoldpathCursor.TryDecode<long>(_singleCursor, out _);

    [Benchmark]
    public bool DecodeComposite() => GoldpathCursor.TryDecode<DateTimeOffset, Guid>(_compositeCursor, out _, out _);
}
