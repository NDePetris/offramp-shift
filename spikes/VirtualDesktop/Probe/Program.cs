using System.Diagnostics;
using System.Reflection;
using DesktopFeasibility;
using DesktopProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--worker") return Worker();
            return Run(args);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] == "help")
        {
            Console.WriteLine("Desktop feasibility probe (experimental; automatic switching is unavailable).\n" +
                "snapshot\nwatch <seconds>\nbind <Personal|Work> <file>\nverify <file>\n" +
                "switch <Personal|Work> <file> --confirm-experimental\nisolation-test\nsafety-test");
            return 0;
        }
        if (args[0] == "safety-test" && args.Length == 1)
        {
            var baseline = Observe();
            var target = baseline.Desktops.FirstOrDefault(d => d.Id != baseline.Current)?.Id
                ?? throw new InvalidOperationException("Safety checks require two existing desktops; none are created.");
            var cases = new (string Name, Func<SwitchRequest, SwitchRequest> Change, string Reason)[]
            {
                ("unconfirmed", r => r with { Confirmed = false }, "confirmation"),
                ("missing target", r => r with { Target = Guid.NewGuid() }, "Destination missing"),
                ("already current", r => r with { Target = r.Expected.Current }, "already current"),
                ("stale", r => r with { Expected = r.Expected with { ObservedUptimeMs = r.Expected.ObservedUptimeMs - 10000 } }, "monotonic"),
                ("changed shell", r => r with { Expected = r.Expected with { ShellProcessId = r.Expected.ShellProcessId + 1 } }, "Explorer restarted"),
                ("changed source", r => r with { Expected = r.Expected with { Current = target } }, "changed desktops"),
                ("noninteractive", r => r with { Expected = r.Expected with { Interactive = false } }, "interactive")
            };
            foreach (var item in cases)
            {
                var fresh = Observe();
                if (fresh.Current != baseline.Current) throw new InvalidOperationException("User changed desktops during safety tests.");
                var normal = new SwitchRequest(Guid.NewGuid(), fresh, target, true);
                var reply = Call(new("switch", item.Change(normal)));
                if (reply.Switch is not { Attempted: false } || !reply.Switch.Status.StartsWith("Rejected:", StringComparison.Ordinal))
                    throw new InvalidOperationException("Safety rejection failed: " + item.Name + " " + Wire.Encode(reply));
                if (!reply.Switch.Status.Contains(item.Reason, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Wrong rejection reason: " + item.Name + " " + Wire.Encode(reply));
                Console.WriteLine("PASS live rejection: " + item.Name + "; no switch attempted");
            }
            if (Observe().Current != baseline.Current) throw new InvalidOperationException("Desktop changed during safety tests; investigate user race.");
            return 0;
        }
        if (args[0] == "isolation-test" && args.Length == 1)
        {
            try { Call(new("hang", DelayMs: 10000), 200); throw new InvalidOperationException("Timeout did not fire."); }
            catch (TimeoutException) { Console.WriteLine("PASS: stalled child terminated; no retry or switch request."); }
            var crash = Call(new("crash"));
            if (crash.Ok) throw new InvalidOperationException("Child crash was not reported.");
            Console.WriteLine("PASS: abnormal child exit reported; parent survived; no retry.");
            var malformed = CallRaw("{broken}");
            if (malformed.Ok) throw new InvalidOperationException("Malformed input accepted.");
            Console.WriteLine("PASS: malformed worker input rejected.");
            return 0;
        }
        if (args[0] == "watch" && args.Length == 2 && int.TryParse(args[1], out var seconds) && seconds is >= 1 and <= 600)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                Console.WriteLine(Wire.Encode(Call(new("snapshot"))));
                Thread.Sleep(500);
            }
            return 0;
        }
        if (args[0] == "snapshot" && args.Length == 1)
        {
            var reply = Call(new("snapshot"));
            Console.WriteLine(Wire.Encode(reply));
            return reply.Ok ? 0 : 1;
        }
        if (args[0] == "bind" && args.Length == 3 && args[1] is "Personal" or "Work")
        {
            var snapshot = Observe();
            if (!snapshot.Interactive) throw new InvalidOperationException("Binding requires an interactive session.");
            var path = Path.GetFullPath(args[2]);
            var existing = File.Exists(path) ? Wire.Decode<Bindings>(File.ReadAllText(path))
                : new(1, snapshot.Provider, snapshot.Configuration.Build, null, null);
            // Reject stale/deleted existing bindings; user must intentionally start a new file to rebind.
            Safety.ValidateBindings(existing, snapshot, false);
            var binding = args[1] == "Personal" ? existing with { Personal = snapshot.Current } : existing with { Work = snapshot.Current };
            Safety.ValidateBindings(binding, snapshot, false);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, Wire.Encode(binding));
            File.Move(temp, path, true);
            Console.WriteLine(Wire.Encode(binding));
            return 0;
        }
        if (args[0] == "verify" && args.Length == 2)
        {
            var binding = Wire.Decode<Bindings>(File.ReadAllText(args[1]));
            var snapshot = Observe();
            Safety.ValidateBindings(binding, snapshot, true);
            Console.WriteLine(Wire.Encode(new { bindings = binding, snapshot, result = "Identities present; automatic-switch capability NOT certified" }));
            return 0;
        }
        if (args[0] == "switch" && args.Length == 4 && args[1] is "Personal" or "Work" && args[3] == "--confirm-experimental")
        {
            var binding = Wire.Decode<Bindings>(File.ReadAllText(args[2]));
            var expected = Observe();
            var target = Safety.Target(binding, expected, args[1]);
            var request = new SwitchRequest(Guid.NewGuid(), expected, target, true);
            var reply = Call(new("switch", request));
            Console.WriteLine(Wire.Encode(reply));
            return reply.Ok && reply.Switch?.Status == "Verified" ? 0 : 1;
        }
        throw new ArgumentException("Invalid command. Run 'help'. Switching requires explicit experimental confirmation.");
    }

    private static Snapshot Observe()
    {
        var reply = Call(new("snapshot"));
        if (!reply.Ok || reply.Snapshot == null) throw new InvalidOperationException(reply.Error ?? "No snapshot.");
        return reply.Snapshot;
    }

    private static int Worker()
    {
        try
        {
            var input = Console.In.ReadToEnd();
            if (input.Length > 65536) throw new InvalidDataException("Oversized worker request.");
            var request = Wire.Decode<WorkerRequest>(input);
            // Deterministic fault injection never constructs a native session.
            if (request.Command == "hang") { Thread.Sleep(Math.Clamp(request.DelayMs, 0, 10000)); return 0; }
            if (request.Command == "crash") return 23;
            if (request.Command is not ("snapshot" or "switch")) throw new InvalidDataException("Unknown worker command.");
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows required.");
            using var session = new NativeSession();
            WorkerReply reply;
            if (request.Command == "snapshot") reply = new(true, Snapshot: session.Observe());
            else
            {
                if (request.Switch == null) throw new InvalidDataException("Missing switch request.");
                var clock = Stopwatch.StartNew();
                var result = SwitchExperiment.Run(request.Switch, session, () => DateTimeOffset.UtcNow,
                    () => Environment.TickCount64, () => clock.ElapsedMilliseconds, Thread.Sleep);
                reply = new(result.Status == "Verified", Switch: result);
            }
            Console.WriteLine(Wire.Encode(reply));
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.WriteLine(Wire.Encode(new WorkerReply(false, Error: ex.GetType().Name + ": " + ex.Message)));
            return 1;
        }
    }

    private static WorkerReply Call(WorkerRequest request, int timeoutMs = 3000) => CallRaw(Wire.Encode(request), timeoutMs);
    private static WorkerReply CallRaw(string input, int timeoutMs = 3000)
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate process host.");
        var start = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--worker");
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Worker did not start.");
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        child.StandardInput.Write(input);
        child.StandardInput.Close();
        if (!child.WaitForExit(timeoutMs))
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit(1000);
            // A timed-out switch is indeterminate: killing the child cannot undo an accepted shell request.
            throw new TimeoutException("Worker deadline exceeded. Result indeterminate; do not retry automatically.");
        }
        var output = stdout.GetAwaiter().GetResult();
        _ = stderr.GetAwaiter().GetResult();
        if (output.Length == 0) return new(false, Error: "Worker exited without response; exit=" + child.ExitCode);
        if (output.Length > 65536) return new(false, Error: "Oversized worker response.");
        var reply = Wire.Decode<WorkerReply>(output);
        if (child.ExitCode != 0 && reply.Ok) return new(false, Error: "Inconsistent worker exit status.");
        return reply;
    }
}
