using System.Diagnostics;
using OfframpShift.Core;

namespace WindowsObservation;

public sealed class ObservationClock : IMonitoringClock
{
    private readonly object gate = new();
    private Guid epoch = Guid.NewGuid();
    private long origin = Stopwatch.GetTimestamp();
    public MonotonicTime Now
    {
        get { lock (gate) return new(epoch, Stopwatch.GetElapsedTime(origin).Ticks / TimeSpan.TicksPerMillisecond); }
    }
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public void NewEpoch() { lock (gate) { origin = Stopwatch.GetTimestamp(); epoch = Guid.NewGuid(); } }
}
