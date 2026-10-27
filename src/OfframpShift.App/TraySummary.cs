namespace OfframpShift.App;

internal sealed record TraySummary(string Status, string Context, string Allowance, string Break, string Tooltip)
{
    public static TraySummary Starting { get; } = new("Starting", "Unknown", "Recorded usage unavailable",
        "Recorded break progress unavailable", "Offramp Shift — Starting");
    public TraySummary Unavailable() => Starting with { Status = "Monitoring owner stopped", Tooltip = "Monitoring stopped | Usage unavailable" };
    internal static TraySummary Create(DashboardModel model)
    {
        var qualifier = model.RecordedQualifier.Length > 0 ? "Last recorded: " : "";
        var allowance = model.Recorded is null ? "Recorded usage unavailable" : qualifier + model.HeroLabel + ": " + model.HeroValue;
        var progress = model.Recorded is null ? "Recorded break progress unavailable" : qualifier + "Qualifying break: " + model.BreakValue;
        var tooltip = model.Recorded is null ? "Usage unavailable" : qualifier + (model.ShowExhaustionExplanation ? "Allowance exhausted" : model.HeroValue + (model.HeroLabel == "Over allowance" ? " over" : " remaining"));
        // NotifyIcon's limit is 63 characters; menu rows retain the full facts.
        if (tooltip.Length > 63) tooltip = tooltip[..60] + "…";
        return new(model.PauseStatus.Length > 0 ? model.PauseStatus : model.Health, model.ContextLabel, allowance, progress, tooltip);
    }
    internal static string Duration(long milliseconds) => CommittedPresentation.Duration(milliseconds);
}
