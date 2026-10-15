namespace OfframpShift.ApplicationHost;

// One transient slot, shared with the Dispatcher. No history or retry queue.
internal sealed class BreakReminderMailbox
{
    private readonly object gate = new();
    private long? sequence;
    private long createdTick, eligibleTick, validForMs;
    private long lastSequence;

    internal void Invalidate() { lock (gate) sequence = null; }
    internal void ResetSession() { lock (gate) { sequence = null; lastSequence = 0; } }

    internal void Update(bool eligible, long nowTick, long evidenceRemainingMs)
    {
        lock (gate)
        {
            if (!eligible || nowTick - createdTick is < 0 or > 2_000) sequence = null;
            eligibleTick = nowTick;
            validForMs = Math.Clamp(evidenceRemainingMs, 0, 2_000);
        }
    }

    internal void Offer(long effectSequence, long nowTick)
    {
        lock (gate)
        {
            if (effectSequence <= lastSequence) return;
            lastSequence = effectSequence;
            sequence = effectSequence; createdTick = nowTick;
        }
    }

    internal bool TryTake(long nowTick)
    {
        lock (gate)
        {
            var available = sequence is not null && nowTick - createdTick is >= 0 and <= 2_000 &&
                nowTick - eligibleTick >= 0 && nowTick - eligibleTick <= validForMs;
            sequence = null; // Consume even when stale; never defer for a later context.
            return available;
        }
    }
}
