using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OfframpShift.Core;

namespace WindowsObservation;

[SupportedOSPlatform("windows")]
internal sealed class Sampler(ObservationClock clock, DesktopWorker? worker)
{
    private readonly LastInputTracker input = new();
    public OfframpShift.Persistence.LatencyHistogram DesktopLatency { get; } = new();
    public OfframpShift.Persistence.LatencyHistogram ForegroundLatency { get; } = new();
    public OfframpShift.Persistence.LatencyHistogram SessionLatency { get; } = new();
    public OfframpShift.Persistence.LatencyHistogram InputLatency { get; } = new();
    public EvidencePoint Read()
    {
        var started = Stopwatch.GetTimestamp();
        var batchStarted = started;
        var initialForeground = Native.GetForegroundWindow();
        var initialSession = Native.Session();
        SessionLatency.Record(Stopwatch.GetElapsedTime(started).Ticks / 10);
        started = Stopwatch.GetTimestamp();
        var desktop = worker?.Read() ?? new(Capability.Unavailable, null, [], 0);
        DesktopLatency.Record(Stopwatch.GetElapsedTime(started).Ticks / 10);
        started = Stopwatch.GetTimestamp();
        var foreground = Native.Foreground();
        ForegroundLatency.Record(Stopwatch.GetElapsedTime(started).Ticks / 10);
        started = Stopwatch.GetTimestamp();
        var session = Native.Session();
        SessionLatency.Record(Stopwatch.GetElapsedTime(started).Ticks / 10);
        started = Stopwatch.GetTimestamp();
        var value = new Native.LastInput { Size = (uint)Marshal.SizeOf<Native.LastInput>() };
        var succeeded = Native.GetLastInputInfo(ref value);
        var uptime = Environment.TickCount64;
        var idle = succeeded ? input.Read(value.Time, uptime) : null;
        ulong? awake = Native.QueryUnbiasedInterruptTime(out var awakeTime) ? awakeTime : null;
        InputLatency.Record(Stopwatch.GetElapsedTime(started).Ticks / 10);
        if (initialForeground != foreground.Window || Native.GetForegroundWindow() != foreground.Window)
        {
            if (foreground.Application is not null) foreground.Failure = ForegroundFailure.ForegroundChangedDuringCollection;
            foreground.Application = null;
        }
        if (initialSession != session) session = SessionState.Unknown;
        return new(clock.Now, uptime, awake, foreground.Window, foreground.Process, foreground.Application,
            idle, session, desktop, foreground.Application is not null ? Capability.Available : Capability.Unavailable,
            idle is not null ? Capability.Available : Capability.Unavailable,
            session != SessionState.Unknown ? Capability.Available : Capability.Unavailable)
        {
            CollectionMs = Stopwatch.GetElapsedTime(batchStarted).Ticks / TimeSpan.TicksPerMillisecond,
            InputDiscontinuity = succeeded && input.LastReadDiscontinuous,
            ForegroundFailure = foreground.Failure,
            ForegroundIdentityQueryCode = foreground.IdentityQueryCode
        };
    }
}
