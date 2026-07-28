namespace KimiWebBox;

internal static class Program
{
    private const string MutexName = "KimiWebBox.SingleInstance";
    private const string ShowEventName = "KimiWebBox.ShowWindow";

    [STAThread]
    static void Main(string[] args)
    {
        bool startInTray = Array.Exists(args, a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance is running: ask it to show its window, then exit.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            return;
        }

        ApplicationConfiguration.Initialize();

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var form = new MainForm(startInTray);
        var listener = new Thread(() =>
        {
            while (true)
            {
                try { showEvent.WaitOne(); } catch { break; }
                try { form.BeginInvoke(new Action(form.ShowFromTray)); } catch { break; }
            }
        })
        { IsBackground = true };
        listener.Start();

        Application.Run(form);
    }
}
