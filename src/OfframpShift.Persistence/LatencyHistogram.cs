namespace OfframpShift.Persistence;

// Fixed power-of-two microsecond buckets. Percentiles are upper bucket bounds.
// Owners serialize updates and snapshots. No raw samples are retained.
public sealed class LatencyHistogram
{
    private readonly long[] buckets = new long[32];
    private long count, totalUs, maximumUs;
    public void Record(long microseconds)
    {
        var value = Math.Max(0, microseconds);
        var bucket = 0;
        while (bucket < buckets.Length - 1 && value > (1L << bucket)) bucket++;
        buckets[bucket]++; count++;
        totalUs = value > long.MaxValue - totalUs ? long.MaxValue : totalUs + value;
        maximumUs = Math.Max(maximumUs, value);
    }
    public object Snapshot() => new
    { count, totalUs, maximumUs, p50Us = Percentile(50), p95Us = Percentile(95), p99Us = Percentile(99) };
    private long Percentile(int percent)
    {
        if (count == 0) return 0;
        var target = (long)Math.Ceiling(count * (percent / 100.0));
        long accumulated = 0;
        for (var i = 0; i < buckets.Length; i++)
        { accumulated += buckets[i]; if (accumulated >= target) return Math.Min(maximumUs, 1L << i); }
        return maximumUs;
    }
}
