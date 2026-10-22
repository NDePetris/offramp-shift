using System.Globalization;
using OfframpShift.ApplicationHost;
using OfframpShift.Core;
using OfframpShift.Persistence;

namespace OfframpShift.App;

internal sealed record RecordedAccounting(Guid Epoch, long Sequence, EngineState State, MonitoringConfiguration Configuration);

// One Dispatcher-owned store, shared through DashboardModel. No ledger or clock.
internal sealed class CommittedPresentation
{
    private long sequence = -1;
    private Guid epoch;
    private Guid? personal, work;
    private bool terminated;
    internal RecordedAccounting? Recorded { get; private set; }
    internal DesktopScope LastContext { get; private set; }

    internal bool Accept(HostSnapshot snapshot, long nowTick)
    {
        // A controller publishes a globally increasing sequence, including across
        // epochs. Reject old epochs before they can clear/repopulate the cache.
        if (terminated || snapshot.Sequence < sequence || snapshot.Sequence == sequence && epoch != snapshot.AccountingPresentationEpoch) return false;
        if (epoch != snapshot.AccountingPresentationEpoch)
        {
            epoch = snapshot.AccountingPresentationEpoch;
            Recorded = null; LastContext = DesktopScope.Unknown;
        }
        if (personal != snapshot.Settings.Personal || work != snapshot.Settings.Work)
            LastContext = DesktopScope.Unknown;
        personal = snapshot.Settings.Personal; work = snapshot.Settings.Work;
        sequence = snapshot.Sequence;
        if (snapshot.Persistence is PersistenceStatus.RecoveryRequired or PersistenceStatus.Incompatible or PersistenceStatus.Disposed)
            Recorded = null;
        else if (snapshot.Persistence == PersistenceStatus.Ready && snapshot.State is { } state && snapshot.Configuration is { } configuration)
            Recorded = new(epoch, sequence, state, configuration);
        if (IsFresh(snapshot, nowTick) && snapshot.Context is DesktopScope.Personal or DesktopScope.Work)
            LastContext = snapshot.Context;
        return true;
    }

    internal void Terminate()
    {
        terminated = true; Recorded = null; LastContext = DesktopScope.Unknown;
    }

    internal static bool IsFresh(HostSnapshot snapshot, long nowTick) => nowTick >= snapshot.PublishedTick &&
        (ulong)nowTick - (ulong)snapshot.PublishedTick <= 2_000;

    internal static string Duration(long milliseconds) => milliseconds <= 0 ? "0 min" : milliseconds < 60_000 ? "<1 min" :
        (milliseconds / 60_000).ToString(CultureInfo.CurrentCulture) + " min";
}
