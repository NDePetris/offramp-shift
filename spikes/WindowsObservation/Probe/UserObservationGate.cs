namespace WindowsObservation;

// Portable acceptance bookkeeping. Operator labels are expectations, not OS evidence.
// No history or native identifiers escape in the resulting summary.
public sealed class UserObservationGate(bool desktop)
{
    public const int RequiredConfirmations = 21; // Initial endpoint + twenty transitions.
    private Guid? personal, work;
    public int Confirmations { get; private set; }
    public string? Failure { get; private set; }
    public bool Complete => Failure is null && Confirmations == RequiredConfirmations;
    public bool ExpectFirst => Confirmations % 2 == 0;

    public bool Confirm(bool firstWindow, bool foregroundMatches, bool identityResolved,
        bool hookObserved, bool healthy, Guid? currentDesktop, Guid[] topology)
    {
        if (Failure is not null || Complete) return false;
        if (!healthy) return Fail("ProviderOrDeliveryFailure");
        if (!foregroundMatches || !identityResolved) return Fail("ForegroundOrIdentityMismatch");
        if (!desktop)
        {
            if (firstWindow != ExpectFirst) return Fail("UnexpectedTestWindow");
            if (!hookObserved) return Fail("ForegroundHookMissing");
        }
        else
        {
            if (currentDesktop is not { } id || id == Guid.Empty || topology.Length < 2 ||
                topology.Any(x => x == Guid.Empty) || topology.Distinct().Count() != topology.Length || !topology.Contains(id))
                return Fail("DesktopUnavailable");
            if (Confirmations == 0) personal = id;
            else if (Confirmations == 1)
            {
                if (id == personal) return Fail("DesktopDidNotChange");
                work = id;
            }
            else if (id != (ExpectFirst ? personal : work)) return Fail("UnexpectedDesktop");
            if (work is not null && (!topology.Contains(personal!.Value) || !topology.Contains(work.Value)))
                return Fail("BindingMissing");
        }
        Confirmations++;
        return true;
    }

    public void Stop(string reason) { Failure ??= reason; }
    private bool Fail(string reason) { Failure = reason; return false; }
}
