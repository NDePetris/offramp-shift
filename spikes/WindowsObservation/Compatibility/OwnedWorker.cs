using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using WindowsObservation;

namespace DesktopCompatibility;

internal sealed record Reply(DesktopReading? Reading, string? Failure);

internal sealed class OwnedWorker : IDisposable
{
    private readonly Process process;
    private bool terminal;
    internal bool CleanupConfirmed { get; private set; }

    internal OwnedWorker(string mode)
    {
        var executable = Environment.ProcessPath ?? throw new DiagnosticFailure("ExecutableUnavailable");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(Policy.OptIn);
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        process = Process.Start(start) ?? throw new DiagnosticFailure("WorkerStartFailed");
    }

    internal DesktopReading Read(int deadlineMs = 2_000)
    {
        if (terminal) throw new DiagnosticFailure("WorkerTerminal");
        try
        {
            return ReadAsync().WaitAsync(TimeSpan.FromMilliseconds(deadlineMs)).GetAwaiter().GetResult();
        }
        catch (DiagnosticFailure) { terminal = true; throw; }
        catch (TimeoutException) { terminal = true; throw new DiagnosticFailure("WorkerTimeout"); }
        catch { terminal = true; throw new DiagnosticFailure("WorkerProtocolOrExit"); }
    }

    private async Task<DesktopReading> ReadAsync()
    {
        if (process.HasExited) throw new DiagnosticFailure("WorkerExit");
        await process.StandardInput.WriteLineAsync("read");
        await process.StandardInput.FlushAsync();
        // Bound output before allocating/deserializing. Never forward child stdout/stderr.
        var line = new StringBuilder();
        var buffer = new char[1];
        while (await process.StandardOutput.ReadAsync(buffer.AsMemory()) != 0)
        {
            if (buffer[0] == '\n')
            {
                var reply = JsonSerializer.Deserialize<Reply>(line.ToString());
                if (reply?.Failure is { } failure)
                    throw new DiagnosticFailure(failure == "ComFailure" ? "ComFailure" : "ReaderInvalidated");
                if (!Policy.Valid(reply?.Reading)) throw new DiagnosticFailure("InvalidReading");
                return reply!.Reading!;
            }
            if (line.Length == 4_096) throw new DiagnosticFailure("WorkerOutputLimit");
            line.Append(buffer[0]);
        }
        throw new DiagnosticFailure("WorkerExit");
    }

    public void Dispose()
    {
        if (CleanupConfirmed) return;
        terminal = true;
        try
        {
            WorkerShutdown.Stop(() => process.HasExited, () => process.Kill(entireProcessTree: true), () => process.WaitForExit(1_000));
            CleanupConfirmed = process.HasExited;
        }
        catch { throw new DiagnosticFailure("WorkerCleanupFailed"); }
        finally { process.Dispose(); }
    }
}
