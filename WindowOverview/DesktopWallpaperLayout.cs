using System;
using System.Drawing;

namespace MissionControlPrototype;

internal enum DesktopWallpaperPosition
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5
}

internal static class DesktopWallpaperLayout
{
    public static RectangleF GetImageBounds(Size image, Rectangle target, DesktopWallpaperPosition position)
    {
        if (image.Width <= 0 || image.Height <= 0 || target.Width <= 0 || target.Height <= 0)
            return RectangleF.Empty;

        return position switch
        {
            DesktopWallpaperPosition.Center => Center(image.Width, image.Height, target),
            // The renderer repeats this original-size tile using a TextureBrush.
            DesktopWallpaperPosition.Tile => new RectangleF(target.X, target.Y, image.Width, image.Height),
            DesktopWallpaperPosition.Stretch => new RectangleF(target.X, target.Y, target.Width, target.Height),
            DesktopWallpaperPosition.Fit => Scale(image, target, fill: false),
            // For Span the renderer supplies the entire virtual desktop target.
            DesktopWallpaperPosition.Fill or DesktopWallpaperPosition.Span => Scale(image, target, fill: true),
            _ => RectangleF.Empty
        };
    }

    public static Rectangle ToClientBounds(Rectangle screenBounds, Point virtualOrigin) =>
        new(screenBounds.X - virtualOrigin.X, screenBounds.Y - virtualOrigin.Y,
            screenBounds.Width, screenBounds.Height);

    private static RectangleF Scale(Size image, Rectangle target, bool fill)
    {
        var horizontalScale = (double)target.Width / image.Width;
        var verticalScale = (double)target.Height / image.Height;
        var scale = fill ? Math.Max(horizontalScale, verticalScale) : Math.Min(horizontalScale, verticalScale);
        return Center(image.Width * scale, image.Height * scale, target);
    }

    private static RectangleF Center(double width, double height, Rectangle target) =>
        new((float)(target.X + ((target.Width - width) / 2)),
            (float)(target.Y + ((target.Height - height) / 2)),
            (float)width, (float)height);
}
