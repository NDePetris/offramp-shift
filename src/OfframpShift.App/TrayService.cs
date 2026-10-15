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
    private HostSnapshot? snapshot;
    private bool disposed;
    public TrayService(Action open, Action settings, Func<HostCommand, Task<string>> execute, Func<Task> exit)
    {
        Forms.ToolStripMenuItem Action(string label, HostCommandKind kind)
        {
            var item = new Forms.ToolStripMenuItem(label);
            item.Click += async (_, _) => await execute(new(kind)); return item;
        }
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(status);
        menu.Items.Add(advisory);
        menu.Items.Add("Open Offramp Shift", null, (_, _) => open());
        pause = new("Pause");
        pause.Click += async (_, _) => await execute(new(snapshot?.State?.Paused == true ? HostCommandKind.Resume : HostCommandKind.Pause));
        menu.Items.Add(pause);
        retry = Action("Retry monitoring", HostCommandKind.RetryMonitoring); menu.Items.Add(retry);
        save = Action("Retry save", HostCommandKind.RetrySave); menu.Items.Add(save);
        menu.Items.Add(new Forms.ToolStripSeparator());
        personal = Action("Use current desktop as Personal", HostCommandKind.AssignPersonal); menu.Items.Add(personal);
        work = Action("Use current desktop as Work", HostCommandKind.AssignWork); menu.Items.Add(work);
        menu.Items.Add("Settings", null, (_, _) => settings());
        menu.Items.Add("Exit", null, async (_, _) => await exit());
        // Replaced with canonical supplied icon when available; never synthesize logo geometry.
        icon = new() { Icon = System.Drawing.SystemIcons.Application, Text = "Offramp Shift", ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => open();
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
        personal.Enabled = work.Enabled = model.CanEdit && value.Settings.Consent;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; icon.Visible = false; icon.ContextMenuStrip?.Dispose(); icon.Dispose();
    }
    public void MarkUnavailable()
    {
        status.Text = "Unavailable"; icon.Text = "Offramp Shift — Unavailable";
        pause.Enabled = retry.Enabled = save.Enabled = personal.Enabled = work.Enabled = false;
    }
}
