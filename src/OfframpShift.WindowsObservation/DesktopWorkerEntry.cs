using System.Runtime.Versioning;

namespace WindowsObservation;

public static class DesktopWorkerEntry
{
    // Must run before creating the UI, singleton or checkpoint session.
    [SupportedOSPlatform("windows")]
    public static int Run() => DesktopWorker.Run();
}
