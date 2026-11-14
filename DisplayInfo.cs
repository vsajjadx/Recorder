using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Recorder;

public sealed record DisplayInfo(string Device, string Label, int X, int Y, int Width, int Height, uint RateNumerator, uint RateDenominator, bool Primary)
{
    public double RefreshRate => (double)RateNumerator / RateDenominator;
    public string Rate => $"{RateNumerator}/{RateDenominator}";
    public override string ToString() => Label;

    public static List<DisplayInfo> GetAll()
    {
        var result = new List<DisplayInfo>();
        foreach (var screen in Forms.Screen.AllScreens.OrderByDescending(s => s.Primary))
        {
            var mode = new DeviceMode { Size = 220 };
            var ok = EnumDisplaySettings(screen.DeviceName, -1, ref mode);
            uint numerator = ok && mode.Frequency > 1 ? mode.Frequency : 60, denominator = 1;
            // QueryDisplayConfig preserves fractional refresh rates such as 59.94 and 74.97 Hz.
            if (GetDisplayConfigBufferSizes(2, out var pathCount, out var modeCount) == 0)
            {
                var paths = new PathInfo[pathCount];
                var modes = new ModeInfo[modeCount];
                if (QueryDisplayConfig(2, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) == 0)
                {
                    foreach (var path in paths.Take((int)pathCount))
                    {
                        var name = new SourceName { Header = new DeviceHeader { Type = 1, Size = 84, Adapter = path.Source.Adapter, Id = path.Source.Id } };
                        if (DisplayConfigGetDeviceInfo(ref name) == 0 && name.Name == screen.DeviceName && path.Target.Rate.Numerator > 0 && path.Target.Rate.Denominator > 0)
                        { numerator = path.Target.Rate.Numerator; denominator = path.Target.Rate.Denominator; break; }
                    }
                }
            }
            var b = screen.Bounds;
            result.Add(new(screen.DeviceName, $"Display {result.Count + 1}{(screen.Primary ? " · Primary" : "")}  —  {b.Width} × {b.Height}", b.X, b.Y, b.Width, b.Height, numerator, denominator, screen.Primary));
        }
        return result;
    }

    [StructLayout(LayoutKind.Explicit, Size = 220)] private struct DeviceMode { [FieldOffset(68)] public ushort Size; [FieldOffset(184)] public uint Frequency; }
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] private struct PathSource { public Luid Adapter; public uint Id, Mode, Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct PathTarget { public Luid Adapter; public uint Id, Mode, Technology, Rotation, Scaling; public Rational Rate; public uint ScanLine, Available, Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct PathInfo { public PathSource Source; public PathTarget Target; public uint Flags; }
    [StructLayout(LayoutKind.Explicit, Size = 64)] private struct ModeInfo { [FieldOffset(0)] public uint Type; }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceHeader { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SourceName { public DeviceHeader Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string name, int mode, ref DeviceMode data);
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] PathInfo[] pathArray, ref uint modes, [Out] ModeInfo[] modeArray, IntPtr topology);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DisplayConfigGetDeviceInfo(ref SourceName name);
}
