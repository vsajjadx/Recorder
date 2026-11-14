using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Recorder;
public partial class MainWindow : Window
{
    private Process? _ffmpeg;
    private readonly int _width, _height, _fps;
    public MainWindow()
    {
        InitializeComponent();
        var screen = Forms.Screen.PrimaryScreen!;
        _width = screen.Bounds.Width; _height = screen.Bounds.Height; _fps = DisplayRefreshRate.Get();
        DisplayText.Text = $"{_width} × {_height}  ·  {_fps} FPS";
    }
    private void Record_Click(object sender, RoutedEventArgs e)
    {
        if (_ffmpeg is not null && !_ffmpeg.HasExited) { _ffmpeg.Kill(); _ffmpeg = null; RecordButton.Content = "START RECORDING"; StatusText.Text = "Saved"; return; }
        Directory.CreateDirectory(PathBox.Text);
        var file = Path.Combine(PathBox.Text, $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        var exe = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (!File.Exists(exe)) { System.Windows.MessageBox.Show("ffmpeg.exe is missing. Run tools\\prepare-ffmpeg.ps1 first.", "Recorder", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        var args = $"-y -f gdigrab -framerate {_fps} -video_size {_width}x{_height} -draw_mouse 1 -i desktop -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p -movflags +faststart \"{file}\"";
        _ffmpeg = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true });
        RecordButton.Content = "STOP & SAVE"; RecordButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(210,70,85)); StatusText.Text = "Recording…";
    }
}
internal static class DisplayRefreshRate { [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode); [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet=System.Runtime.InteropServices.CharSet.Ansi)] private struct DEVMODE { [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst=32)] public string dmDeviceName; public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput, dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate; [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst=32)] public string dmFormName; public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency, dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight; } public static int Get() { var d = new DEVMODE { dmDeviceName = new string('\0',32), dmFormName = new string('\0',32), dmSize=(short)System.Runtime.InteropServices.Marshal.SizeOf<DEVMODE>() }; return EnumDisplaySettings(null, -1, ref d) && d.dmDisplayFrequency > 0 ? d.dmDisplayFrequency : 60; } }
