namespace OfframpShift.ApplicationHost;

public sealed record ReminderEvent(long Sequence, long Generation, nint ForegroundWindow,
    Guid PersonalDesktop, long ExpiresTick, ReminderStyle Style, Guid Epoch = default);

// One transient slot, shared with the Dispatcher. No history or retry queue.
internal sealed class BreakReminderMailbox
{
    private readonly object gate = new();
    private long? sequence;
    private long createdTick, eligibleTick, validForMs;
    private long lastSequence;
    private ReminderEvent? delivery;

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

    internal void Offer(long effectSequence, long nowTick, ReminderEvent? value = null)
    {
        lock (gate)
        {
            if (effectSequence <= lastSequence) return;
            lastSequence = effectSequence;
            sequence = effectSequence; createdTick = nowTick;
            delivery = value;
        }
    }

    internal bool TryTake(long nowTick) => TryTake(nowTick, out _);

    internal bool TryTake(long nowTick, out ReminderEvent? value)
    {
        lock (gate)
        {
            var available = sequence is not null && nowTick - createdTick is >= 0 and <= 2_000 &&
                nowTick - eligibleTick >= 0 && nowTick - eligibleTick <= validForMs;
            value = available ? delivery : null;
            sequence = null; // Consume even when stale; never defer for a later context.
            return available;
        }
    }
}
