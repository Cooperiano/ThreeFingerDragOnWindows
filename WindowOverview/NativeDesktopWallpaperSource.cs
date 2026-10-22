using System.Runtime.InteropServices;

namespace MissionControlPrototype;

[Flags]
internal enum DesktopSlideshowState : uint
{
    Enabled = 0x01,
    Slideshow = 0x02,
    DisabledByRemoteSession = 0x04
}

internal interface IDesktopWallpaperReadApi : IDisposable
{
    int GetWallpaper(string? monitorId, out string imagePath);
    int GetMonitorDevicePathAt(uint monitorIndex, out string monitorId);
    int GetMonitorDevicePathCount(out uint count);
    int GetMonitorRectangle(string monitorId, out Rectangle bounds);
    int GetBackgroundColor(out uint colorRef);
    int GetPosition(out DesktopWallpaperPosition position);
    int GetStatus(out DesktopSlideshowState state);
}

internal static class NativeDesktopWallpaperSource
{
    private static readonly Color FallbackColor = Color.FromArgb(17, 20, 27);

    public static DesktopWallpaperConfiguration Read(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var api = new NativeDesktopWallpaperReadApi();
            return DesktopWallpaperConfigurationReader.Read(api, token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Wallpaper metadata is cosmetic. COM failures must never prevent
            // the overview from opening or expose user paths in diagnostics.
            return Unavailable();
        }
    }

    internal static DesktopWallpaperConfiguration Unavailable() => new(
        FallbackColor,
        DesktopWallpaperPosition.Fill,
        Array.Empty<DesktopWallpaperMonitor>(),
        Available: false,
        HadErrors: true);
}

internal static class DesktopWallpaperConfigurationReader
{
    internal const int S_OK = 0;
    internal const int S_FALSE = 1;
    private const uint MaximumMonitorCount = 64;

    public static DesktopWallpaperConfiguration Read(
        IDesktopWallpaperReadApi api,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        token.ThrowIfCancellationRequested();

        var hadErrors = false;
        var backgroundColor = Color.FromArgb(17, 20, 27);
        if (api.GetBackgroundColor(out var colorRef) == S_OK)
        {
            backgroundColor = FromColorRef(colorRef);
        }
        else
        {
            hadErrors = true;
        }

        var position = DesktopWallpaperPosition.Fill;
        if (api.GetPosition(out var reportedPosition) == S_OK && IsKnownPosition(reportedPosition))
        {
            position = reportedPosition;
        }
        else
        {
            hadErrors = true;
        }

        var wallpaperEnabled = true;
        var statusResult = api.GetStatus(out var status);
        if (statusResult == S_OK)
        {
            wallpaperEnabled = (status & DesktopSlideshowState.Enabled) != 0;
        }
        else
        {
            // Status is advisory. Older/transient implementations can still
            // return valid wallpaper paths, so continue with a partial result.
            hadErrors = true;
        }

        string? sharedImagePath = null;
        var usePerMonitorPaths = wallpaperEnabled;
        if (wallpaperEnabled)
        {
            var sharedResult = api.GetWallpaper(null, out var imagePath);
            if (sharedResult == S_OK)
            {
                sharedImagePath = imagePath ?? string.Empty;
                usePerMonitorPaths = false;
            }
            else if (sharedResult != S_FALSE)
            {
                // A failed common-path query does not prevent per-monitor
                // metadata from being useful.
                hadErrors = true;
            }
        }

        if (api.GetMonitorDevicePathCount(out var reportedCount) != S_OK || reportedCount == 0)
        {
            return NativeDesktopWallpaperSource.Unavailable();
        }

        var count = Math.Min(reportedCount, MaximumMonitorCount);
        if (reportedCount > MaximumMonitorCount)
        {
            hadErrors = true;
        }

        var monitors = new List<DesktopWallpaperMonitor>((int)count);
        for (uint index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            if (api.GetMonitorDevicePathAt(index, out var monitorId) != S_OK || string.IsNullOrEmpty(monitorId))
            {
                hadErrors = true;
                continue;
            }

            var rectangleResult = api.GetMonitorRectangle(monitorId, out var bounds);
            if (rectangleResult == S_FALSE)
            {
                // The API includes detached displays that still have an image
                // assignment. They are not part of the current virtual screen.
                continue;
            }
            if (rectangleResult != S_OK || bounds.Width <= 0 || bounds.Height <= 0)
            {
                hadErrors = true;
                continue;
            }

            var path = string.Empty;
            if (wallpaperEnabled)
            {
                if (!usePerMonitorPaths)
                {
                    path = sharedImagePath ?? string.Empty;
                }
                else if (api.GetWallpaper(monitorId, out var monitorPath) == S_OK)
                {
                    path = monitorPath ?? string.Empty;
                }
                else
                {
                    // Preserve the usable monitor geometry and let only this
                    // monitor fall back to the background color.
                    hadErrors = true;
                }
            }

            monitors.Add(new DesktopWallpaperMonitor(bounds, path));
        }

        return new DesktopWallpaperConfiguration(
            backgroundColor,
            position,
            monitors,
            Available: true,
            HadErrors: hadErrors);
    }

    private static bool IsKnownPosition(DesktopWallpaperPosition position) => position is
        DesktopWallpaperPosition.Center or
        DesktopWallpaperPosition.Tile or
        DesktopWallpaperPosition.Stretch or
        DesktopWallpaperPosition.Fit or
        DesktopWallpaperPosition.Fill or
        DesktopWallpaperPosition.Span;

    private static Color FromColorRef(uint colorRef) => Color.FromArgb(
        (int)(colorRef & 0xff),
        (int)((colorRef >> 8) & 0xff),
        (int)((colorRef >> 16) & 0xff));
}

internal sealed class NativeDesktopWallpaperReadApi : IDesktopWallpaperReadApi
{
    private IDesktopWallpaperNative? _native = (IDesktopWallpaperNative)new DesktopWallpaperComObject();

    public int GetWallpaper(string? monitorId, out string imagePath)
    {
        var native = RequireNative();
        var result = native.GetWallpaper(monitorId, out var value);
        imagePath = TakeString(value);
        return result;
    }

    public int GetMonitorDevicePathAt(uint monitorIndex, out string monitorId)
    {
        var native = RequireNative();
        var result = native.GetMonitorDevicePathAt(monitorIndex, out var value);
        monitorId = TakeString(value);
        return result;
    }

    public int GetMonitorDevicePathCount(out uint count) =>
        RequireNative().GetMonitorDevicePathCount(out count);

    public int GetMonitorRectangle(string monitorId, out Rectangle bounds)
    {
        var result = RequireNative().GetMonitorRECT(monitorId, out var rectangle);
        bounds = rectangle.ToRectangle();
        return result;
    }

    public int GetBackgroundColor(out uint colorRef) => RequireNative().GetBackgroundColor(out colorRef);
    public int GetPosition(out DesktopWallpaperPosition position) => RequireNative().GetPosition(out position);
    public int GetStatus(out DesktopSlideshowState state) => RequireNative().GetStatus(out state);

    public void Dispose()
    {
        var native = Interlocked.Exchange(ref _native, null);
        if (native is not null && Marshal.IsComObject(native))
        {
            try { Marshal.FinalReleaseComObject(native); }
            catch { }
        }
    }

    private IDesktopWallpaperNative RequireNative() =>
        _native ?? throw new ObjectDisposedException(nameof(NativeDesktopWallpaperReadApi));

    private static string TakeString(IntPtr value)
    {
        if (value == IntPtr.Zero) return string.Empty;
        try { return Marshal.PtrToStringUni(value) ?? string.Empty; }
        finally { Marshal.FreeCoTaskMem(value); }
    }
}

[ComImport]
[Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")]
internal class DesktopWallpaperComObject
{
}

[ComImport]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaperNative
{
    [PreserveSig]
    int SetWallpaper(
        [MarshalAs(UnmanagedType.LPWStr)] string? monitorId,
        [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

    [PreserveSig]
    int GetWallpaper(
        [MarshalAs(UnmanagedType.LPWStr)] string? monitorId,
        out IntPtr wallpaper);

    [PreserveSig]
    int GetMonitorDevicePathAt(uint monitorIndex, out IntPtr monitorId);

    [PreserveSig]
    int GetMonitorDevicePathCount(out uint count);

    [PreserveSig]
    int GetMonitorRECT(
        [MarshalAs(UnmanagedType.LPWStr)] string monitorId,
        out NativeWallpaperRect displayRect);

    [PreserveSig]
    int SetBackgroundColor(uint colorRef);

    [PreserveSig]
    int GetBackgroundColor(out uint colorRef);

    [PreserveSig]
    int SetPosition(DesktopWallpaperPosition position);

    [PreserveSig]
    int GetPosition(out DesktopWallpaperPosition position);

    [PreserveSig]
    int SetSlideshow(IntPtr items);

    [PreserveSig]
    int GetSlideshow(out IntPtr items);

    [PreserveSig]
    int SetSlideshowOptions(uint options, uint slideshowTick);

    [PreserveSig]
    int GetSlideshowOptions(out uint options, out uint slideshowTick);

    [PreserveSig]
    int AdvanceSlideshow(
        [MarshalAs(UnmanagedType.LPWStr)] string? monitorId,
        uint direction);

    [PreserveSig]
    int GetStatus(out DesktopSlideshowState state);

    [PreserveSig]
    int Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeWallpaperRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
}
