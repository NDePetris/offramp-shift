using OfframpShift.ApplicationHost;
using Forms = System.Windows.Forms;

namespace OfframpShift.App;

internal sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ToolStripMenuItem status = new("Starting") { Enabled = false };
    private readonly Forms.ToolStripMenuItem context = new("Current context: Unknown") { Enabled = false };
    private readonly Forms.ToolStripMenuItem allowance = new("Saved allowance unavailable") { Enabled = false };
    private readonly Forms.ToolStripMenuItem qualifyingBreak = new("Saved qualifying break unavailable") { Enabled = false };
    private readonly Forms.ToolStripMenuItem pause;
    private readonly Forms.ToolStripMenuItem retry;
    private readonly Forms.ToolStripMenuItem save;
    private readonly Forms.ToolStripMenuItem personal;
    private readonly Forms.ToolStripMenuItem work;
    private readonly Forms.ToolStripMenuItem feedbackItem = new("Action needs &attention…") { Visible = false };
    private readonly TrayActionFeedback feedback = new();
    private readonly TrayBalloonState balloons = new();
    private HostSnapshot? snapshot;
    private bool disposed;
    private readonly System.Drawing.Icon lightIcon;
    private readonly System.Drawing.Icon darkIcon;
    private bool darkSelected;
    private TraySummary summary = TraySummary.Starting;
    public TrayService(Action open, Action settings, Func<HostCommand, Task<HostCommandResult>> execute, Func<Task> exit)
    {
        async Task Run(HostCommand command)
        {
            var notify = await feedback.ExecuteAsync(command, execute);
            if (disposed) return;
            feedbackItem.Visible = feedback.Message.Length > 0;
            if (notify) NotifyFailure();
        }
        Forms.ToolStripMenuItem Action(string label, HostCommandKind kind)
        {
            var item = new Forms.ToolStripMenuItem(label);
            item.Click += async (_, _) => await Run(new(kind)); return item;
        }
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(status);
        menu.Items.Add(context);
        menu.Items.Add(allowance);
        menu.Items.Add(qualifyingBreak);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(feedbackItem);
        feedbackItem.Click += (_, _) => ShowFeedback();
        menu.Items.Add("Open Offramp Shift", null, (_, _) => open());
        pause = new("Pause");
        pause.Click += async (_, _) => await Run(new(snapshot?.State?.Paused == true ? HostCommandKind.Resume : HostCommandKind.Pause));
        menu.Items.Add(pause);
        retry = Action("Retry monitoring", HostCommandKind.RetryMonitoring); menu.Items.Add(retry);
        save = Action("Retry save", HostCommandKind.RetrySave); menu.Items.Add(save);
        menu.Items.Add(new Forms.ToolStripSeparator());
        personal = Action("Use current desktop as Personal", HostCommandKind.AssignPersonal); menu.Items.Add(personal);
        work = Action("Use current desktop as Work", HostCommandKind.AssignWork); menu.Items.Add(work);
        menu.Items.Add("Settings", null, (_, _) => settings());
        menu.Items.Add("Exit", null, async (_, _) => await exit());
        lightIcon = BrandAssets.LoadTrayIcon(false);
        try { darkIcon = BrandAssets.LoadTrayIcon(true); }
        catch { lightIcon.Dispose(); menu.Dispose(); throw; }
        try { icon = new() { Icon = lightIcon, Text = summary.Tooltip, ContextMenuStrip = menu, Visible = true }; }
        catch { lightIcon.Dispose(); darkIcon.Dispose(); menu.Dispose(); throw; }
        icon.DoubleClick += (_, _) => open();
        icon.BalloonTipClicked += (_, _) =>
        {
            var opensError = balloons.ClickOpensError;
            balloons.Closed();
            if (opensError) ShowFeedback();
        };
        icon.BalloonTipClosed += (_, _) => balloons.Closed();
    }
    private void NotifyFailure()
    {
        if (balloons.BeginError())
            RequestBalloon("Offramp Shift — action needs attention",
                "The action did not complete. Open the tray menu and choose Action needs attention for details.", Forms.ToolTipIcon.Warning);
    }
    // True means a request returned without exception, never proof of display.
    internal bool RequestReminder()
    {
        if (disposed || !balloons.BeginReminder(feedback.Message.Length > 0)) return false;
        return RequestBalloon("Time for a break", "You've used your Personal allowance. A break will help you reset.", Forms.ToolTipIcon.Info);
    }
    private bool RequestBalloon(string title, string text, Forms.ToolTipIcon kind)
    {
        try { icon.ShowBalloonTip(5_000, title, text, kind); return true; }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.ExternalException)
        { balloons.Closed(); return false; }
    }
    public void ApplyBrand(bool dark)
    {
        if (darkSelected == dark) return;
        darkSelected = dark; icon.Icon = dark ? darkIcon : lightIcon;
    }
    public void Apply(HostSnapshot value, DashboardModel model)
    {
        snapshot = value;
        ApplySummary(model.Tray);
        pause.Text = value.State?.Paused == true ? "Resume" : "Pause";
        pause.Enabled = value.State?.Paused == true ? model.CanResume : model.CanPause;
        retry.Enabled = model.CanRetryMonitoring; retry.Text = model.RetryLabel;
        save.Enabled = model.CanRetrySave;
        personal.Enabled = work.Enabled = model.CanAssign;
    }
    private void ApplySummary(TraySummary value)
    {
        if (summary == value) return;
        if (summary.Status != value.Status) status.Text = value.Status;
        if (summary.Context != value.Context) context.Text = value.Context;
        if (summary.Allowance != value.Allowance) allowance.Text = value.Allowance;
        if (summary.Break != value.Break) qualifyingBreak.Text = value.Break;
        if (summary.Tooltip != value.Tooltip) icon.Text = value.Tooltip;
        summary = value;
    }
    private void ShowFeedback()
    {
        if (disposed || feedback.Message.Length == 0) return;
        Forms.MessageBox.Show("Last unsuccessful action:\n\n" + feedback.Message + "\n\nSelect OK to dismiss this message.",
            "Offramp Shift — action needs attention", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
        feedback.Dismiss(); feedbackItem.Visible = false;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; icon.Visible = false; icon.ContextMenuStrip?.Dispose(); icon.Dispose();
        lightIcon.Dispose(); darkIcon.Dispose();
    }
    public void MarkUnavailable()
    {
        ApplySummary(summary.Unavailable());
        pause.Enabled = retry.Enabled = save.Enabled = personal.Enabled = work.Enabled = false;
    }
}
