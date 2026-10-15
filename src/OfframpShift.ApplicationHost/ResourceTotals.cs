using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OfframpShift.ApplicationHost;

[SupportedOSPlatform("windows")]
internal sealed class ResourceTotals
{
    private double retiredCpu;
    private long peakWorking, peakPrivate, initialPrivate, initialHandles, maximumHandles, maximumThreads;
    private double cpuSeconds;
    private ulong writeTransferBytes;
    public void Retire(Process? process)
    { if (process is not null && !process.HasExited) retiredCpu += process.TotalProcessorTime.TotalSeconds; }
    public void Sample(Process? helper)
    {
        using var host = Process.GetCurrentProcess();
        host.Refresh(); helper?.Refresh();
        var alive = helper is not null && !helper.HasExited;
        var working = host.WorkingSet64 + (alive ? helper!.WorkingSet64 : 0);
        var privateBytes = host.PrivateMemorySize64 + (alive ? helper!.PrivateMemorySize64 : 0);
        var handles = host.HandleCount + (alive ? helper!.HandleCount : 0);
        var threads = host.Threads.Count + (alive ? helper!.Threads.Count : 0);
        if (initialPrivate == 0) { initialPrivate = privateBytes; initialHandles = handles; }
        peakWorking = Math.Max(peakWorking, working); peakPrivate = Math.Max(peakPrivate, privateBytes);
        maximumHandles = Math.Max(maximumHandles, handles); maximumThreads = Math.Max(maximumThreads, threads);
        cpuSeconds = host.TotalProcessorTime.TotalSeconds + retiredCpu + (alive ? helper!.TotalProcessorTime.TotalSeconds : 0);
        if (GetProcessIoCounters(host.Handle, out var counters)) writeTransferBytes = counters.WriteTransfer;
    }
    public object Snapshot(double elapsed) => new
    {
        cpuPercentOfMachine = elapsed > 0 ? 100 * cpuSeconds / elapsed / Environment.ProcessorCount : 0,
        cpuSeconds,
        logicalProcessors = Environment.ProcessorCount,
        peakWorkingBytes = peakWorking,
        peakPrivateBytes = peakPrivate,
        initialPrivateBytes = initialPrivate,
        initialHandles,
        maximumHandles,
        maximumThreads,
        approximateHostWriteTransferBytes = writeTransferBytes,
        ioLimitation = "Process transfer bytes include non-disk IO; not physical SSD write volume."
    };
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadTransfer, WriteTransfer, OtherTransfer; }
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(nint process, out IoCounters counters);
}
