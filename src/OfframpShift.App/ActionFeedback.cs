using OfframpShift.ApplicationHost;

namespace OfframpShift.App;

// Native callbacks have no per-balloon identity. Never replace a reminder with
// an error balloon (or vice versa); ambiguous clicks must not dismiss an error.
internal sealed class TrayBalloonState
{
    private enum Kind { None, Reminder, Error }
    private Kind current;
    internal bool BeginReminder(bool hasError)
    {
        if (hasError || current != Kind.None) return false;
        current = Kind.Reminder; return true;
    }
    internal bool BeginError()
    {
        if (current != Kind.None) return false;
        current = Kind.Error; return true;
    }
    internal bool ClickOpensError => current == Kind.Error;
    internal void Closed() => current = Kind.None;
}

// Only explicit command outcomes enter this state; polling never produces alerts.
internal sealed class TrayActionFeedback
{
    public string Message { get; private set; } = "";
    private HostCommandKind? failedCommand;
    internal async Task<bool> ExecuteAsync(HostCommand command, Func<HostCommand, Task<HostCommandResult>> execute)
    {
        var result = await execute(command);
        if (result.Succeeded)
        {
            if (failedCommand == command.Kind) Dismiss();
            return false;
        }
        var changed = failedCommand != command.Kind || Message != result.Message;
        failedCommand = command.Kind;
        Message = result.Message;
        return changed;
    }
    public void Dismiss() { Message = ""; failedCommand = null; }
}

internal static class ClosePreference
{
    // Null means stay accessible, either by cancellation or because saving failed.
    internal static async Task<bool?> ResolveAsync(bool? saved, bool? choice,
        Func<HostCommand, Task<HostCommandResult>> execute, Action<string> reportFailure)
    {
        if (saved is not null) return saved;
        if (choice is null) return null;
        var result = await execute(new(HostCommandKind.ChangeCloseBehavior) { CloseToTray = choice });
        if (result.Succeeded) return choice;
        reportFailure("Your close preference was not saved. The window will stay open. " + result.Message +
            " Close again to retry or choose differently. Use Exit Offramp Shift to exit without saving a close preference.");
        return null;
    }
}
