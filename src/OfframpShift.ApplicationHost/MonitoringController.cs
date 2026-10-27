using System.Collections.Concurrent;
using System.Diagnostics;

namespace OfframpShift.ApplicationHost;

public sealed class MonitoringController
{
    private readonly BlockingCollection<(HostCommand Command, TaskCompletionSource<HostCommandResult> Completion)> commands = new(16);
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object submissionGate = new();
    private HostSnapshot? latest;
    private object? finalDiagnostics;
    // Published once after a graceful owner-loop stop; never polls a live owner.
    public object? FinalDiagnostics => Volatile.Read(ref finalDiagnostics);
    private BreakReminderMailbox? reminders;
    private volatile bool stopping;
    private bool commandInProgress;
    private int switching;
    private TaskCompletionSource? switchFinished;
    private DesktopFeasibility.SwitchAuthorization? activeSwitchAuthorization;
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
            return mailbox?.TryTakePublished(Environment.TickCount64, Volatile.Read(ref latest), out value) == true;
        }
    }

    internal MonitoringController(Func<MonitoringApplication> create, Action? prepareTurn = null)
    {
        var thread = new Thread(() => Run(create, prepareTurn)) { IsBackground = true, Name = "Offramp monitoring owner" };
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

    public async Task<DesktopFeasibility.SwitchCompletion> SwitchToWorkAsync(nint window,
        DesktopFeasibility.ISwitchExecutor? executor = null, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource finished;
        lock (submissionGate)
        {
            if (stopping || stopped.Task.IsCompleted)
                return DesktopFeasibility.SwitchCompletion.Reject("The application is stopping.");
            if (switching != 0) return DesktopFeasibility.SwitchCompletion.Reject("A switch request is already in progress.");
            switching = 1;
            switchFinished = finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        try
        {
            var result = await ExecuteAsync(new(HostCommandKind.AuthorizeSwitch) { NudgeWindow = window }).ConfigureAwait(false);
            if (!result.Succeeded || result.Switching is null)
                return DesktopFeasibility.SwitchCompletion.Reject(result.Message);
            lock (submissionGate)
            {
                if (stopping || cancellationToken.IsCancellationRequested)
                {
                    result.Switching.Revoke();
                    return DesktopFeasibility.SwitchCompletion.Reject("The application is stopping or the nudge closed.");
                }
                activeSwitchAuthorization = result.Switching;
            }
            // Revocation competes with consumption on the same atomic lease state.
            // Cancellation after consumption cannot cancel or undo a shell request.
            using var registration = cancellationToken.Register(result.Switching.Revoke);
            return await (executor ?? new DesktopFeasibility.IsolatedSwitchExecutor()).ExecuteAsync(result.Switching).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        { return DesktopFeasibility.SwitchCompletion.Unknown("Switch operation could not complete."); }
        finally
        {
            lock (submissionGate)
            {
                activeSwitchAuthorization?.Revoke(); activeSwitchAuthorization = null; switching = 0;
            }
            finished.TrySetResult();
        }
    }

    public Task StopAsync()
    {
        lock (submissionGate)
        {
            stopping = true; Volatile.Read(ref reminders)?.Invalidate();
            activeSwitchAuthorization?.Revoke();
            // The owner can stop immediately, but graceful UI shutdown must keep
            // the independent supervisor alive until its bounded cleanup finishes.
            return switchFinished is null ? stopped.Task : Task.WhenAll(stopped.Task, switchFinished.Task);
        }
    }

    private void Run(Func<MonitoringApplication> create, Action? prepareTurn)
    {
        Exception? failure = null;
        try
        {
            using var application = create();
            Volatile.Write(ref reminders, application.Reminders);
            var turns = new OperationTiming(); var commandTiming = new OperationTiming(); var publicationTiming = new OperationTiming();
            void Publish()
            {
                var started = Stopwatch.GetTimestamp(); var from = application.Elapsed; var deadline = application.NextSamplingDeadline;
                try { Volatile.Write(ref latest, application.Snapshot()); }
                finally { publicationTiming.Record(started, from, application.Elapsed, deadline); }
            }
            long nextPublication = 0;
            while (!stopping)
            {
                // Internal deterministic-test boundary. Production supplies no callback.
                prepareTurn?.Invoke();
                if (stopping) break;
                var turnStarted = Stopwatch.GetTimestamp(); var turnFrom = application.Elapsed; var turnDeadline = application.NextSamplingDeadline;
                try
                {
                    // One command per turn: a burst cannot starve observation indefinitely.
                    if (commands.TryTake(out var request, 10))
                    {
                        if (stopping) { request.Completion.TrySetResult(HostCommandResult.Failure("The monitoring owner is stopping.")); break; }
                        lock (submissionGate) commandInProgress = true;
                        var commandStarted = Stopwatch.GetTimestamp(); var commandFrom = application.Elapsed; var commandDeadline = application.NextSamplingDeadline;
                        try { var result = application.Execute(request.Command); Publish(); request.Completion.TrySetResult(result); }
                        catch { request.Completion.TrySetResult(HostCommandResult.Failure("Application action failed. Accounting stopped; restart the app.")); throw; }
                        finally { commandTiming.Record(commandStarted, commandFrom, application.Elapsed, commandDeadline); lock (submissionGate) commandInProgress = false; }
                    }
                    if (stopping) break;
                    application.Step();
                    if (Environment.TickCount64 >= nextPublication)
                    {
                        Publish();
                        nextPublication = Environment.TickCount64 + 250;
                    }
                }
                finally { turns.Record(turnStarted, turnFrom, application.Elapsed, turnDeadline); }
            }
            Volatile.Write(ref finalDiagnostics, new
            {
                owner = new { turns = turns.Snapshot(), commands = commandTiming.Snapshot(), publication = publicationTiming.Snapshot() },
                monitoring = application.ObservationDiagnostics
            });
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
