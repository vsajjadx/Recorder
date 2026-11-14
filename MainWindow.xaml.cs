using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace Recorder;
public partial class MainWindow : Window
{
    private readonly Settings _settings = Settings.Load();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private RecordingSession? _session;
    private HwndSource? _source;
    private bool _busy, _closing;
    private bool _initialized;
    public MainWindow()
    {
        InitializeComponent();
        PathBox.Text = _settings.OutputFolder;
        AudioToggle.IsChecked = _settings.SystemAudio;
        CursorToggle.IsChecked = _settings.Cursor;
        RefreshDisplays();
        _timer.Tick += (_, _) =>
        {
            if (_session is null) return;
            TimerText.Text = _session.Elapsed.ToString(@"hh\:mm\:ss");
            var fps = _session.ActualFps;
            DetailText.Text = _session.Profile.Label + (fps > 0 ? $" · {fps:0.#} FPS" : "");
            if (_session.Elapsed.TotalSeconds > 5 && fps > 0 && fps < _session.RequestedFps * 0.9)
                DetailText.Text += " · Encoder is below display rate";
        };
        _initialized = true;
    }
    private void RefreshDisplays()
    {
        var preferred = (DisplayBox.SelectedItem as DisplayInfo)?.Device ?? _settings.Display;
        var screens = DisplayInfo.GetAll();
        DisplayBox.ItemsSource = screens;
        DisplayBox.SelectedItem = screens.FirstOrDefault(s => s.Device == preferred) ?? screens.FirstOrDefault();
    }
    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        int dark = 1;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowMessage);
        if (!RegisterHotKey(handle, 1, 0x0002 | 0x0004 | 0x4000, 0x52)) ShortcutText.Text = "Use the button below to start or stop recording";
    }
    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == 1) { handled = true; _ = ToggleRecordingAsync(); }
        if (msg == 0x007E && _session is not null && !_busy) _ = StopForDisplayChangeAsync();
        return IntPtr.Zero;
    }
    private async Task StopForDisplayChangeAsync()
    {
        await ToggleRecordingAsync();
        DetailText.Text = "Display settings changed. Recording saved; start a new one to use the new mode.";
        RefreshDisplays();
    }
    private async void Record_Click(object sender, RoutedEventArgs e) => await ToggleRecordingAsync();
    private async Task ToggleRecordingAsync()
    {
        if (_busy) return;
        _busy = true;
        SetControls(false);
        try
        {
            if (_session is not null)
            {
                StatusText.Text = "Saving recording…";
                var session = _session;
                _timer.Stop();
                var file = await session.StopAsync();
                _session = null;
                StatusText.Text = "Recording saved";
                DetailText.Text = Path.GetFileName(file);
                DetailText.ToolTip = file;
            }
            else
            {
                RefreshDisplays();
                if (DisplayBox.SelectedItem is not DisplayInfo display) throw new IOException("No active display was found.");
                var folder = Path.GetFullPath(Environment.ExpandEnvironmentVariables(PathBox.Text.Trim()));
                Directory.CreateDirectory(folder);
                var check = Path.Combine(folder, ".recorder-write-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(check, ""); File.Delete(check);
                PathBox.Text = folder;
                SaveSettings();
                StatusText.Text = "Preparing recording…";
                DetailText.Text = "Checking display, audio and hardware encoder.";
                var options = new RecordingOptions(display, folder, AudioToggle.IsChecked == true, CursorToggle.IsChecked == true);
                var profile = await RecordingSession.DetectProfileAsync(display);
                try { _session = await RecordingSession.StartAsync(options, profile); }
                catch when (profile.DesktopDuplication || profile.Encoder != "libx264")
                {
                    DetailText.Text = "Using compatibility capture…";
                    _session = await RecordingSession.StartAsync(options, new("libx264", false));
                }
                TimerText.Text = "00:00:00";
                StatusText.Text = "Recording";
                _timer.Start();
                _ = ObserveSessionAsync(_session);
            }
        }
        catch (Exception e)
        {
            _timer.Stop();
            if (_session is not null) { await _session.DisposeAsync(); _session = null; }
            StatusText.Text = "Recording needs attention";
            DetailText.Text = e.Message;
            DetailText.ToolTip = e.ToString();
        }
        finally { _busy = false; SetControls(true); }
    }
    private async Task ObserveSessionAsync(RecordingSession session)
    {
        try { await session.Completion; } catch { }
        if (_session == session && !_busy)
        {
            await ToggleRecordingAsync();
            if (StatusText.Text == "Recording saved") DetailText.Text = "Capture ended. The available recording was saved.";
        }
    }
    private void SetControls(bool enabled)
    {
        var idle = _session is null;
        DisplayBox.IsEnabled = AudioToggle.IsEnabled = CursorToggle.IsEnabled = PathBox.IsEnabled = BrowseButton.IsEnabled = enabled && idle;
        RecordButton.IsEnabled = enabled;
        RecordButton.Content = idle ? "●   Start recording" : "■   Stop & save";
        AutomationProperties.SetName(RecordButton, idle ? "Start recording" : "Stop and save");
        RecordButton.Background = idle ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromRgb(209, 63, 87));
    }
    private void Display_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DisplayBox.SelectedItem is not DisplayInfo d) return;
        ResolutionText.Text = $"{d.Width} × {d.Height}";
        FpsText.Text = $"{d.RefreshRate:0.###} FPS";
        if (_initialized) SaveSettings();
    }
    private void Options_Changed(object sender, RoutedEventArgs e) { if (_initialized) SaveSettings(); }
    private void Path_LostFocus(object sender, RoutedEventArgs e) => SaveSettings();
    private void SaveSettings()
    {
        _settings.OutputFolder = PathBox.Text;
        _settings.SystemAudio = AudioToggle.IsChecked == true;
        _settings.Cursor = CursorToggle.IsChecked == true;
        _settings.Display = (DisplayBox.SelectedItem as DisplayInfo)?.Device;
        try { _settings.Save(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose recording folder" };
        if (Directory.Exists(PathBox.Text)) dialog.InitialDirectory = PathBox.Text;
        if (dialog.ShowDialog(this) == true) { PathBox.Text = dialog.FolderName; SaveSettings(); }
    }
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try { var path = Path.GetFullPath(PathBox.Text); Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = true }); }
        catch (Exception ex) { DetailText.Text = ex.Message; }
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closing) return;
        if (_busy) { e.Cancel = true; return; }
        if (_session is not null)
        {
            e.Cancel = true;
            await ToggleRecordingAsync();
            UnregisterHotKey(new WindowInteropHelper(this).Handle, 1);
            _source?.RemoveHook(WindowMessage);
            _closing = true;
            Close();
            return;
        }
        SaveSettings();
        UnregisterHotKey(new WindowInteropHelper(this).Handle, 1);
        _source?.RemoveHook(WindowMessage);
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
