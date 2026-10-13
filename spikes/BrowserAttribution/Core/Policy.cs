namespace BrowserAttribution;

public sealed record Window(long Hwnd, int Pid, long ProcessBirth, long Generation, string Application, bool Eligible);
public sealed record Binding(string Session, int WindowId, Window Window);
public sealed record Challenge(string Nonce, long Issued, long FocusEpoch, Window Window);
public sealed record Candidate(Binding Binding, Challenge Challenge, Message Sample, long Received);
public sealed record Decision(string Application, string? Domain, string Reason);

public static class Policy
{
    public const long ReplyDeadline = 400;
    public const long Lease = 500;
    public static bool Accept(Binding b, Challenge q, Message m, Window current, long epoch, long now) =>
        current.Eligible && b.Window == current && q.Window == current && q.FocusEpoch == epoch &&
        now >= q.Issued && now - q.Issued <= ReplyDeadline && m.Type == "sample" &&
        m.Session == b.Session && m.WindowId == b.WindowId && m.Nonce == q.Nonce &&
        m.Focused && m.Stable && !m.Private && Wire.Domain(m.Domain);

    public static Decision Classify(Window current, long epoch, long now, IEnumerable<Candidate> candidates)
    {
        var matching = candidates.Where(c => c.Binding.Window == current &&
            c.Challenge.FocusEpoch == epoch && now >= c.Received && now - c.Received <= Lease &&
            Accept(c.Binding, c.Challenge, c.Sample, current, epoch, c.Received)).ToArray();
        if (!current.Eligible) return new(current.Application, null, "unsupported-foreground");
        if (matching.Length != 1) return new(current.Application, null, matching.Length == 0 ? "no-fresh-unique-binding" : "ambiguous");
        return new(current.Application, matching[0].Sample.Domain, "bound-fresh-foreground");
    }
}
