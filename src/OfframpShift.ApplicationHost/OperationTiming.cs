using System.Diagnostics;
using OfframpShift.Persistence;

namespace OfframpShift.ApplicationHost;

// Owner-serialized fixed-size aggregates. Parent/child operations overlap.
internal sealed class OperationTiming
{
    private readonly LatencyHistogram latency = new();
    private long slowOperations, deadlineCrossings;
    public void Record(long started, long from, long to, long nextDeadline)
    {
        var us = Stopwatch.GetElapsedTime(started).Ticks / 10;
        latency.Record(us);
        if (us > 500_000) slowOperations++;
        // Count fixed periodic boundaries crossed during work, not lateness
        // already present at entry. This is overlap evidence, not causation.
        var first = Math.Max(nextDeadline, (from / 1_000 + 1) * 1_000);
        if (to >= first) deadlineCrossings += (to - first) / 1_000 + 1;
    }
    public object Snapshot() => new { latency = latency.Snapshot(), slowOperations, deadlineCrossings };
}
