using System.Windows;
using WindowsObservation;

namespace OfframpShift.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // These modes never create WPF, settings, the singleton or accounting storage.
        if (args is ["--desktop-worker"]) return DesktopWorkerEntry.Run();
        if (args is ["--switch-worker"]) return DesktopFeasibility.SwitchWorkerEntry.Run();
        if (args is ["--switch-qualification-worker"]) return DesktopFeasibility.SwitchWorkerEntry.Run(qualification: true);
        if (args is ["--qualify-switch-26200.9711", "--confirm-experimental", var personal, var work])
            return DesktopFeasibility.SwitchWorkerEntry.Qualify(personal, work);
        if (args is ["--worker-malformed"])
        { Console.ReadLine(); Console.WriteLine("{broken}"); return 0; }
        if (args is ["--worker-hang"])
        { Console.ReadLine(); Thread.Sleep(10_000); return 0; }
        if (args.Length != 0) return 2;
        using var instance = new SingleInstance();
        if (!instance.IsOwner)
        {
            if (!instance.Activate()) MessageBox.Show("The existing Offramp Shift app could not be opened. It may still be starting or stopping.", "Offramp Shift");
            return 0;
        }
        var app = new ShiftApplication(instance);
        return app.Run();
    }
}
