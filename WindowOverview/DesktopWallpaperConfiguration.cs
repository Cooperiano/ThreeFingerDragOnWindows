namespace MissionControlPrototype;

internal sealed record DesktopWallpaperMonitor(Rectangle Bounds, string ImagePath);

internal sealed record DesktopWallpaperConfiguration(
    Color BackgroundColor,
    DesktopWallpaperPosition Position,
    IReadOnlyList<DesktopWallpaperMonitor> Monitors,
    bool Available,
    bool HadErrors);
