using OfframpShift.ApplicationHost;
using Forms = System.Windows.Forms;

namespace OfframpShift.App;

internal sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ToolStripMenuItem status = new("Starting") { Enabled = false };
    private readonly Forms.ToolStripMenuItem advisory = new("Allowance unavailable") { Enabled = false };
    private readonly Forms.ToolStripMenuItem pause;
    private readonly Forms.ToolStripMenuItem retry;
    private readonly Forms.ToolStripMenuItem save;
    private readonly Forms.ToolStripMenuItem personal;
    private readonly Forms.ToolStripMenuItem work;
    private readonly Forms.ToolStripMenuItem feedbackItem = new("Action needs &attention…") { Visible = false };
    private readonly TrayActionFeedback feedback = new();
    private HostSnapshot? snapshot;
    private bool disposed;
    private readonly System.Drawing.Icon lightIcon;
    private readonly System.Drawing.Icon darkIcon;
    private bool darkSelected;
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
        menu.Items.Add(advisory);
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
        try { icon = new() { Icon = lightIcon, Text = "Offramp Shift", ContextMenuStrip = menu, Visible = true }; }
        catch { lightIcon.Dispose(); darkIcon.Dispose(); menu.Dispose(); throw; }
        icon.DoubleClick += (_, _) => open();
        icon.BalloonTipClicked += (_, _) => ShowFeedback();
    }
    private void NotifyFailure() => icon.ShowBalloonTip(5_000, "Offramp Shift — action needs attention",
        "The action did not complete. Open the tray menu and choose Action needs attention for details.", Forms.ToolTipIcon.Warning);
    public void ApplyBrand(bool dark)
    {
        if (darkSelected == dark) return;
        darkSelected = dark; icon.Icon = dark ? darkIcon : lightIcon;
    }
    public void Apply(HostSnapshot value, DashboardModel model)
    {
        snapshot = value;
        status.Text = model.Status; icon.Text = ("Offramp Shift — " + model.Status)[..Math.Min(63, ("Offramp Shift — " + model.Status).Length)];
        advisory.Text = model.Advisory;
        pause.Text = value.State?.Paused == true ? "Resume" : "Pause";
        pause.Enabled = value.State?.Paused == true ? model.CanResume : model.CanPause;
        retry.Enabled = model.CanRetryMonitoring; retry.Text = model.RetryLabel;
        save.Enabled = model.CanRetrySave;
        personal.Enabled = work.Enabled = model.CanAssign;
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
        status.Text = "Unavailable"; icon.Text = "Offramp Shift — Unavailable";
        pause.Enabled = retry.Enabled = save.Enabled = personal.Enabled = work.Enabled = false;
    }
}
