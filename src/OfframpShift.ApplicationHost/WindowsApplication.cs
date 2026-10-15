using System.Diagnostics;
using System.Runtime.Versioning;
using OfframpShift.Persistence;
using WindowsObservation;

namespace OfframpShift.ApplicationHost;

public static class WindowsApplication
{
    [SupportedOSPlatform("windows")]
    public static MonitoringController Start(string? directory = null) => new(() =>
    {
        var path = directory ?? SqliteCheckpointStore.DefaultDirectory;
        var clock = new ObservationClock(); var timer = Stopwatch.StartNew();
        return new(clock, clock.NewEpoch, () => timer.ElapsedMilliseconds, () => new WindowsObservationSource(clock),
            () => DurableMonitoringSession.Open(clock, new(), path), new(path), WindowsObservationSource.Supported,
            WindowsObservationSource.Build);
    });
}
