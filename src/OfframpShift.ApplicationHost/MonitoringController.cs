using System.Collections.Concurrent;

namespace OfframpShift.ApplicationHost;

public sealed class MonitoringController
{
    private readonly BlockingCollection<(HostCommand Command, TaskCompletionSource<HostCommandResult> Completion)> commands = new(16);
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object submissionGate = new();
    private HostSnapshot? latest;
    private BreakReminderMailbox? reminders;
    private volatile bool stopping;
    private bool commandInProgress;
    public HostSnapshot? Latest => Volatile.Read(ref latest);
    public Task Completion => stopped.Task;
    public bool TryTakeReminder() => TryTakeReminder(out _);
    public bool TryTakeReminder(out ReminderEvent? value)
    {
        lock (submissionGate)
        {
            value = null;
            var mailbox = Volatile.Read(ref reminders);
            if (stopping || stopped.Task.IsCompleted || commandInProgress || commands.Count != 0)
            { mailbox?.Invalidate(); return false; }
            return mailbox?.TryTake(Environment.TickCount64, out value) == true;
        }
    }

    internal MonitoringController(Func<MonitoringApplication> create)
    {
        var thread = new Thread(() => Run(create)) { IsBackground = true, Name = "Offramp monitoring owner" };
        thread.Start();
    }

    public Task<HostCommandResult> ExecuteAsync(HostCommand command)
    {
        var completion = new TaskCompletionSource<HostCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (submissionGate)
        {
            Volatile.Read(ref reminders)?.Invalidate();
            if (stopping || stopped.Task.IsCompleted || !commands.TryAdd((command, completion)))
                completion.SetResult(HostCommandResult.Failure("The application is stopping or busy. Try again after the current action completes."));
        }
        return completion.Task;
    }

    public Task StopAsync() { lock (submissionGate) { stopping = true; Volatile.Read(ref reminders)?.Invalidate(); } return stopped.Task; }

    private void Run(Func<MonitoringApplication> create)
    {
        Exception? failure = null;
        try
        {
            using var application = create();
            Volatile.Write(ref reminders, application.Reminders);
            long nextPublication = 0;
            while (!stopping)
            {
                // One command per turn: a burst cannot starve observation indefinitely.
                if (commands.TryTake(out var request, 10))
                {
                    if (stopping) { request.Completion.TrySetResult(HostCommandResult.Failure("The monitoring owner is stopping.")); break; }
                    lock (submissionGate) commandInProgress = true;
                    try { var result = application.Execute(request.Command); Volatile.Write(ref latest, application.Snapshot()); request.Completion.TrySetResult(result); }
                    catch { request.Completion.TrySetResult(HostCommandResult.Failure("Application action failed. Accounting stopped; restart the app.")); throw; }
                    finally { lock (submissionGate) commandInProgress = false; }
                }
                if (stopping) break;
                application.Step();
                if (Environment.TickCount64 >= nextPublication)
                {
                    Volatile.Write(ref latest, application.Snapshot());
                    nextPublication = Environment.TickCount64 + 250;
                }
            }
        }
        catch (Exception e) { failure = e; }
        finally
        {
            lock (submissionGate)
            {
                stopping = true;
                Volatile.Read(ref reminders)?.Invalidate();
                while (commands.TryTake(out var pending)) pending.Completion.TrySetResult(HostCommandResult.Failure("The monitoring owner stopped."));
            }
            if (failure is null) stopped.TrySetResult();
            else stopped.TrySetException(new InvalidOperationException("Monitoring owner or cleanup failed. No recovery was performed."));
        }
    }
}
