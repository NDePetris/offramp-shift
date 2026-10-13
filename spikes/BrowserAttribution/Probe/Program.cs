using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using BrowserAttribution;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

internal static class Program
{
    internal static string PipeName => "offramp-pr0b-" + WindowsIdentity.GetCurrent().User!.Value;
    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return 2;
        try
        {
            if (args.Length == 1 && args[0] == "snapshot")
            { var o = new WindowsObserver(); Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(o.Foreground(), Wire.Json)); return 0; }
            if (args.Length == 1 && args[0] == "monitor") { await new Monitor().Run(); return 0; }
            if (!args.Any(a => a.StartsWith("chrome-extension://", StringComparison.Ordinal))) return 2;
            // Browser checks exact origins against the registered manifest. Recheck shape here.
            string origin = args.First(a => a.StartsWith("chrome-extension://", StringComparison.Ordinal));
            if (!System.Text.RegularExpressions.Regex.IsMatch(origin, "^chrome-extension://[a-p]{32}/$")) return 2;
            string? parentArg = args.FirstOrDefault(a => a.StartsWith("--parent-window=", StringComparison.Ordinal));
            long parent = 0;
            if (parentArg != null && !long.TryParse(parentArg[16..], out parent)) return 2;
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var connectTimeout = new CancellationTokenSource(2000);
            await pipe.ConnectAsync(connectTimeout.Token);
            // Native stdio is binary, length framed. No diagnostic stdout.
            var input = Console.OpenStandardInput(); var output = Console.OpenStandardOutput();
            using var stop = new CancellationTokenSource();
            async Task Forward(Stream source, Stream target, bool browserInput)
            {
                while (await Wire.Read(source, stop.Token) is { } m)
                {
                    if (browserInput)
                    {
                        if (m.Parent != 0 || m.Type is not ("hello" or "bind" or "sample" or "invalidate")) throw new InvalidDataException();
                        // Only launcher metadata may introduce an HWND, never extension JSON.
                        m = m with { Parent = m.Type == "bind" ? parent : 0 };
                    }
                    await Wire.Write(target, m, stop.Token);
                }
            }
            Task inbound = Forward(input, pipe, true), outbound = Forward(pipe, output, false);
            Task completed = await Task.WhenAny(inbound, outbound);
            try { await completed; }
            finally { stop.Cancel(); pipe.Dispose(); }
            return 0;
        }
        catch (Exception) { Console.Error.WriteLine("Browser probe disconnected or rejected input; no domain retained."); return 1; }
    }
}

internal sealed class Connection(NamedPipeServerStream pipe, int pid)
{
    public NamedPipeServerStream Pipe { get; } = pipe;
    public int Pid { get; } = pid;
    public string Session = "";
    public bool Stream;
    public Dictionary<int, Binding> Bindings = new();
    public Challenge? Pending;
    public Candidate? Candidate;
}

internal sealed class Monitor
{
    private readonly WindowsObserver observer = new();
    private readonly System.Threading.Channels.Channel<(Connection C, Message? M)> inbox = System.Threading.Channels.Channel.CreateBounded<(Connection, Message?)>(256);
    private readonly List<Connection> connections = new();
    private Window? previous;
    private long previousEpoch = -1;
    private long lastChallenge;
    public async Task Run()
    {
        using var singleton = new Mutex(false, Program.PipeName + "-monitor", out bool created);
        if (!created) throw new InvalidOperationException("Monitor already running");
        // The first instance owns the pipe; no TCP/HTTP listener or discovery port.
        _ = AcceptLoop();
        while (true)
        {
            long now = Environment.TickCount64;
            var (current, epoch) = observer.Observation();
            if (current != previous || epoch != previousEpoch)
            {
                foreach (var c in connections) { c.Candidate = null; c.Pending = null; }
                previous = current; previousEpoch = epoch;
            }
            while (inbox.Reader.TryRead(out var item))
                await Handle(item.C, item.M, current, epoch, now);
            // Recheck after inbox processing, so changing foreground never preserves an old candidate.
            (current, epoch) = observer.Observation();
            if (now - lastChallenge >= 200)
            {
                lastChallenge = now;
                foreach (var c in connections.Where(c => c.Stream).ToArray())
                {
                    var q = new Challenge(Guid.NewGuid().ToString("N"), now, epoch, current);
                    c.Pending = q;
                    await Send(c, new() { Type = "challenge", Session = c.Session, Nonce = q.Nonce });
                }
            }
            // Sequential writes may take time. Never publish the pre-write foreground or clock.
            (current, epoch) = observer.Observation(); now = Environment.TickCount64;
            var decision = Policy.Classify(current, epoch, now, connections.Where(c => c.Candidate != null).Select(c => c.Candidate!));
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, monotonicMs = now, focusEpoch = epoch, hwnd = current.Hwnd, pid = current.Pid, decision.Application, decision.Domain, decision.Reason }, Wire.Json));
            await Task.Delay(100);
        }
    }
    private async Task AcceptLoop()
    {
        try
        {
            while (true)
            {
                var pipe = new NamedPipeServerStream(Program.PipeName, PipeDirection.InOut, 16, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
                if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint pid)) { pipe.Dispose(); continue; }
                var c = new Connection(pipe, (int)pid);
                _ = Receive(c);
            }
        }
        catch (Exception) { Console.Error.WriteLine("Pipe accept failed; stop and restart monitor."); }
    }
    private async Task Receive(Connection c)
    {
        try
        {
            while (true)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var m = await Wire.Read(c.Pipe, timeout.Token);
                if (m == null) break;
                await inbox.Writer.WriteAsync((c, m));
            }
        }
        catch (Exception) { }
        finally { c.Pipe.Dispose(); await inbox.Writer.WriteAsync((c, null)); }
    }
    private async Task Send(Connection c, Message m)
    {
        try { using var timeout = new CancellationTokenSource(100); await Wire.Write(c.Pipe, m, timeout.Token); }
        catch (Exception) { c.Candidate = null; c.Pending = null; c.Pipe.Dispose(); }
    }
    private async Task Handle(Connection c, Message? m, Window current, long epoch, long now)
    {
        if (m == null) { connections.Remove(c); c.Candidate = null; return; }
        if (!connections.Contains(c)) connections.Add(c);
        if (c.Session != "" && c.Session != m.Session) { c.Pipe.Dispose(); c.Candidate = null; return; }
        if (m.Type == "hello" && c.Session == "")
        { c.Session = m.Session; c.Stream = true; return; }
        if (m.Type == "bind" && c.Session == "")
        {
            c.Session = m.Session;
            var w = observer.Inspect(m.Parent);
            // Explicit popup pairing must match a single active worker session and browser ancestry.
            var workers = connections.Where(x => x.Stream && x.Session == m.Session).ToArray();
            bool ownBinary = false;
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById(c.Pid);
                // Monitor may run via dotnet DLL; hosts are always the registered apphost.
                ownBinary = string.Equals(p.MainModule?.FileName, Path.Combine(AppContext.BaseDirectory, "BrowserProbe.exe"), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { }
            // Popup focus may belong to a child UI window; root it only for focus corroboration.
            bool foreground = w.Hwnd == current.Hwnd || GetAncestor((nint)current.Hwnd, 2).ToInt64() == w.Hwnd;
            if (workers.Length == 1 && w.Eligible && foreground && m.WindowId >= 0 && !m.Private && ownBinary &&
                WindowsObserver.BrowserAncestor(c.Pid, w.Pid) && WindowsObserver.BrowserAncestor(workers[0].Pid, w.Pid))
            {
                // Conflicting bindings cannot temporarily win by replying first.
                var conflicts = connections.Where(x => x.Stream).SelectMany(x => x.Bindings.Values.Select(b => (C: x, B: b)))
                    .Where(x => x.B.Window == w && (x.C != workers[0] || x.B.WindowId != m.WindowId)).ToArray();
                if (conflicts.Length != 0)
                {
                    foreach (var conflict in conflicts) { conflict.C.Bindings.Remove(conflict.B.WindowId); conflict.C.Candidate = null; }
                    workers[0].Bindings.Remove(m.WindowId); workers[0].Candidate = null;
                    await Send(c, new() { Type = "bound", Session = m.Session, WindowId = -1 });
                    return;
                }
                workers[0].Bindings[m.WindowId] = new(m.Session, m.WindowId, w);
                workers[0].Candidate = null;
                await Send(c, new() { Type = "bound", Session = m.Session, WindowId = m.WindowId });
            }
            else { await Send(c, new() { Type = "bound", Session = m.Session, WindowId = -1 }); }
            return;
        }
        if (!c.Stream || m.Parent != 0) { c.Pipe.Dispose(); return; }
        if (m.Type == "invalidate")
        { c.Candidate = null; c.Pending = null; if (m.WindowId >= 0) c.Bindings.Remove(m.WindowId); return; }
        if (m.Type != "sample" || c.Pending is not { } q) { c.Candidate = null; return; }
        c.Pending = null; c.Candidate = null;
        if (c.Bindings.TryGetValue(m.WindowId, out var b) && Policy.Accept(b, q, m, current, epoch, now))
            c.Candidate = new(b, q, m, now);
    }
    [DllImport("kernel32.dll")] private static extern bool GetNamedPipeClientProcessId(nint pipe, out uint pid);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
}
