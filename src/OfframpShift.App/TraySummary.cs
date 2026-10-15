using System.Globalization;
using OfframpShift.ApplicationHost;
using OfframpShift.Persistence;

namespace OfframpShift.App;

internal sealed record TraySummary(string Status, string Context, string Allowance, string Break, string Tooltip)
{
    public static TraySummary Starting { get; } = new("Starting", "Current context: Unknown",
        "Saved allowance unavailable", "Saved qualifying break unavailable", "Offramp Shift — Starting");

    public TraySummary Unavailable() => this with
    {
        Status = Allowance == Starting.Allowance ? "Unavailable" : "Unavailable — saved values retained",
        Context = "Current context: Unknown",
        Tooltip = "Offramp Shift — Unavailable"
    };

    public static TraySummary Create(HostSnapshot snapshot, string status, bool stale, bool needsSetup)
    {
        var saved = snapshot.Persistence == PersistenceStatus.Ready && snapshot.State is not null && snapshot.Configuration is not null;
        var active = !stale && snapshot.Available && snapshot.State?.Paused == false && !needsSetup;
        var label = stale ? "Unavailable — stale updates" : status;
        var context = stale || !snapshot.Available ? "Unknown" : snapshot.Context.ToString();
        var allowance = "Saved allowance unavailable";
        var remaining = "";
        var progress = "Saved qualifying break unavailable";
        if (saved && snapshot.State is { } state && snapshot.Configuration is { } configuration)
        {
            remaining = state.Exhausted || state.UsageMs >= configuration.AllowanceMs ? "Allowance exhausted" :
                Duration(configuration.AllowanceMs - state.UsageMs) + " left";
            allowance = "Saved allowance: " + remaining;
            progress = state.BreakCompleted ? "Saved qualifying break: completed; allowance reset" :
                $"Saved qualifying break: {Duration(state.BreakProgressMs)} of {Duration(configuration.RequiredBreakMs)}";
        }
        var heading = label + (needsSetup && !stale ? " — setup incomplete; open Settings" : "");
        if (saved && !active) heading += " — saved values retained";
        var tooltip = "Offramp Shift — " + label + (needsSetup && !stale ? " | Setup incomplete" : "");
        if (active)
        {
            // Prefer status and saved allowance over context if the native tooltip limit is reached.
            var detail = saved ? (label == remaining ? " (saved)" : " | " + remaining + " (saved)") : "";
            var full = tooltip + detail + " | " + context;
            tooltip = full.Length <= 63 ? full : label + detail;
        }
        return new(heading, "Current context: " + context, allowance, progress, tooltip);
    }

    // Whole minutes only; sub-minute credit/remaining time stays explicit without ticking seconds.
    internal static string Duration(long milliseconds) => milliseconds == 0 ? "0m" : milliseconds < 60_000 ? "<1m" :
        (milliseconds / 60_000).ToString(CultureInfo.CurrentCulture) + "m";
}
