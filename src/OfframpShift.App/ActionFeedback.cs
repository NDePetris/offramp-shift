using OfframpShift.ApplicationHost;

namespace OfframpShift.App;

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
