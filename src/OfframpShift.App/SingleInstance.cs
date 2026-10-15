using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace OfframpShift.App;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string name;
    private readonly CancellationTokenSource stop = new();
    private Task? server;
    public bool IsOwner { get; }
    public SingleInstance(string suffix = "")
    {
        var user = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("User identity unavailable.");
        name = "OfframpShift-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user + suffix)))[..24];
        mutex = new(false, @"Local\" + name);
        try { IsOwner = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsOwner = true; }
    }
    public void Listen(Action activate)
    {
        if (!IsOwner || server is not null) throw new InvalidOperationException();
        server = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(stop.Token);
                    var value = new byte[1];
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    deadline.CancelAfter(1_000);
                    try { if (await pipe.ReadAsync(value, deadline.Token) == 1 && value[0] == 1) activate(); }
                    catch (OperationCanceledException) when (!stop.IsCancellationRequested) { }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { } // Storage lease still prevents a second accounting owner.
        });
    }
    public bool Activate()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(2_000); pipe.WriteByte(1); pipe.Flush(); return true;
        }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException) { return false; }
    }
    public void Dispose()
    {
        stop.Cancel();
        // The listener owns/disposes its pipe; the mutex is released on its creating UI thread.
        if (IsOwner) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
