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

    internal bool TryTake(long nowTick, out ReminderEvent? value) => Take(nowTick, null, false, out value);

    internal bool TryTakePublished(long nowTick, HostSnapshot? published, out ReminderEvent? value) => Take(nowTick, published, true, out value);

    private bool Take(long nowTick, HostSnapshot? published, bool requirePublication, out ReminderEvent? value)
    {
        lock (gate)
        {
            var available = sequence is not null && nowTick - createdTick is >= 0 and <= 2_000 &&
                nowTick - eligibleTick >= 0 && nowTick - eligibleTick <= validForMs;
            // Offer happens on the owner before its next snapshot publication.
            // Do not consume a fresh interactive event against pre-exhaustion UI state.
            // Waiting for publication never renews its original evidence/expiry.
            if (available && requirePublication && delivery is { Style: ReminderStyle.InteractiveWindow } item &&
                (published is null || published.ReminderEpoch != item.Epoch ||
                    published.ReminderGeneration < item.Generation || published.State is null || published.State.EffectSequence < item.Sequence))
            { value = null; return false; }
            value = available ? delivery : null;
            sequence = null; // Consume even when stale; never defer for a later context.
            return available;
        }
    }
}
