using System.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace Recorder;
public partial class App : System.Windows.Application
{
    private Mutex? _instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new Mutex(true, @"Local\Vsajjadx.Recorder", out var created);
        if (!created)
        {
            foreach (var process in Process.GetProcessesByName("Recorder"))
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId || process.MainWindowHandle == IntPtr.Zero) continue;
                    ShowWindow(process.MainWindowHandle, 9);
                    SetForegroundWindow(process.MainWindowHandle);
                    break;
                }
            }
            _instance.Dispose(); _instance = null;
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
}
