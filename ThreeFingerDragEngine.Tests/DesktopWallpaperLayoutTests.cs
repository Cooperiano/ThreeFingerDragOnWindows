using System.Drawing;
using MissionControlPrototype;
using Xunit;

namespace ThreeFingerDragEngine.Tests;

public class DesktopWallpaperLayoutTests
{
    [Fact]
    public void Positions_MatchDesktopWallpaperNumericValues()
    {
        Assert.Equal(0, (int)DesktopWallpaperPosition.Center);
        Assert.Equal(1, (int)DesktopWallpaperPosition.Tile);
        Assert.Equal(2, (int)DesktopWallpaperPosition.Stretch);
        Assert.Equal(3, (int)DesktopWallpaperPosition.Fit);
        Assert.Equal(4, (int)DesktopWallpaperPosition.Fill);
        Assert.Equal(5, (int)DesktopWallpaperPosition.Span);
    }

    [Fact]
    public void Fill_LandscapeImageCoversSquareTargetWithoutStretching()
    {
        var bounds = DesktopWallpaperLayout.GetImageBounds(new Size(1920, 1080),
            new Rectangle(10, 20, 1000, 1000), DesktopWallpaperPosition.Fill);

        AssertBounds(bounds, -378.888888889, 20, 1777.777777778, 1000);
        AssertAspectRatio(new Size(1920, 1080), bounds);
    }

    [Fact]
    public void Fit_LandscapeImageIsEntirelyVisibleAndCentered()
    {
        var bounds = DesktopWallpaperLayout.GetImageBounds(new Size(1920, 1080),
            new Rectangle(10, 20, 1000, 1000), DesktopWallpaperPosition.Fit);

        AssertBounds(bounds, 10, 238.75, 1000, 562.5);
        AssertAspectRatio(new Size(1920, 1080), bounds);
    }

    [Theory]
    [InlineData(4, 0, -1166.666666667, 1920, 3413.333333333)]
    [InlineData(3, 656.25, 0, 607.5, 1080)]
    public void PortraitImage_RemainsProportionalOnLandscapeMonitor(int position,
        double x, double y, double width, double height)
    {
        var image = new Size(1080, 1920);
        var bounds = DesktopWallpaperLayout.GetImageBounds(image,
            new Rectangle(0, 0, 1920, 1080), (DesktopWallpaperPosition)position);

        AssertBounds(bounds, x, y, width, height);
        AssertAspectRatio(image, bounds);
    }

    [Theory]
    [InlineData(200, 100, -300, 200, 800, 600, 0, 450)]
    [InlineData(1600, 1200, 0, 0, 800, 600, -400, -300)]
    [InlineData(5, 3, 0, 0, 10, 8, 2.5, 2.5)]
    public void Center_KeepsOriginalPixelsAndAllowsFractionalCentering(int imageWidth, int imageHeight,
        int x, int y, int width, int height, double expectedX, double expectedY)
    {
        var bounds = DesktopWallpaperLayout.GetImageBounds(new Size(imageWidth, imageHeight),
            new Rectangle(x, y, width, height), DesktopWallpaperPosition.Center);

        AssertBounds(bounds, expectedX, expectedY, imageWidth, imageHeight);
    }

    [Fact]
    public void Stretch_UsesExactTargetRatherThanPreservingImageRatio()
    {
        var bounds = DesktopWallpaperLayout.GetImageBounds(new Size(2, 3),
            new Rectangle(-100, -50, 320, 200), DesktopWallpaperPosition.Stretch);

        Assert.Equal(new RectangleF(-100, -50, 320, 200), bounds);
    }

    [Fact]
    public void Tile_ReturnsOneOriginalSizeTileAtTargetOrigin()
    {
        var bounds = DesktopWallpaperLayout.GetImageBounds(new Size(20, 10),
            new Rectangle(-400, 100, 1000, 700), DesktopWallpaperPosition.Tile);

        Assert.Equal(new RectangleF(-400, 100, 20, 10), bounds);
    }

    [Theory]
    [InlineData(-1920, 0, 1920, 1080, -1920, -1080, 0, 1080)]
    [InlineData(0, -1080, 2560, 1440, -1920, -1080, 1920, 0)]
    [InlineData(-3840, -2160, 3840, 2160, -3840, -2160, 0, 0)]
    [InlineData(0, 0, 1920, 1080, 0, 0, 0, 0)]
    [InlineData(40960, -21600, 7680, 4320, -40960, -21600, 81920, 0)]
    public void ToClientBounds_OffsetsNegativeVirtualOriginAndPreservesMonitorSize(
        int x, int y, int width, int height, int originX, int originY, int expectedX, int expectedY)
    {
        var bounds = DesktopWallpaperLayout.ToClientBounds(new Rectangle(x, y, width, height),
            new Point(originX, originY));

        Assert.Equal(new Rectangle(expectedX, expectedY, width, height), bounds);
    }

    [Fact]
    public void Span_UsesEntireVirtualDesktopTargetAndFillScaling()
    {
        var image = new Size(1920, 1080);
        var virtualDesktop = new Rectangle(-1920, -1080, 5760, 2160);

        var bounds = DesktopWallpaperLayout.GetImageBounds(image, virtualDesktop, DesktopWallpaperPosition.Span);

        AssertBounds(bounds, -1920, -1620, 5760, 3240);
        AssertAspectRatio(image, bounds);
        Assert.Equal(DesktopWallpaperLayout.GetImageBounds(image, virtualDesktop, DesktopWallpaperPosition.Fill), bounds);
        var clientTarget = DesktopWallpaperLayout.ToClientBounds(virtualDesktop, virtualDesktop.Location);
        AssertBounds(DesktopWallpaperLayout.GetImageBounds(image, clientTarget, DesktopWallpaperPosition.Span),
            0, -540, 5760, 3240);
    }

    [Fact]
    public void Fit_KeepsFractionalGeometryInsteadOfTruncatingToPixels()
    {
        var bounds = DesktopWallpaperLayout.GetImageBounds(new Size(3, 2),
            new Rectangle(0, 0, 10, 10), DesktopWallpaperPosition.Fit);

        AssertBounds(bounds, 0, 1.666666667, 10, 6.666666667);
    }

    [Theory]
    [InlineData(1920, 1080, 1000, 1000)]
    [InlineData(1080, 1920, 1920, 1080)]
    [InlineData(3840, 1080, 1600, 900)]
    [InlineData(1000, 1000, 1920, 1080)]
    [InlineData(1, 1024, 2560, 1440)]
    [InlineData(1024, 1, 1440, 2560)]
    [InlineData(1, 1, 98304, 4320)]
    [InlineData(int.MaxValue, 1, 1, int.MaxValue)]
    [InlineData(1, int.MaxValue, int.MaxValue, 1)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue)]
    public void FitFillAndSpan_RemainFiniteProportionalCenteredAndRespectCoverage(
        int imageWidth, int imageHeight, int width, int height)
    {
        var image = new Size(imageWidth, imageHeight);
        var target = new Rectangle(-64000, -32000, width, height);
        foreach (var position in new[] { DesktopWallpaperPosition.Fit, DesktopWallpaperPosition.Fill, DesktopWallpaperPosition.Span })
        {
            var bounds = DesktopWallpaperLayout.GetImageBounds(image, target, position);
            AssertFinite(bounds);
            Assert.True(bounds.Width > 0 && bounds.Height > 0);
            AssertAspectRatio(image, bounds);
            AssertApproximately(target.X + (target.Width / 2d), bounds.X + (bounds.Width / 2d),
                Math.Max(bounds.Width, target.Width) * 0.000001);
            AssertApproximately(target.Y + (target.Height / 2d), bounds.Y + (bounds.Height / 2d),
                Math.Max(bounds.Height, target.Height) * 0.000001);
            if (position == DesktopWallpaperPosition.Fit)
            {
                Assert.True(bounds.Width <= target.Width * 1.000001);
                Assert.True(bounds.Height <= target.Height * 1.000001);
            }
            else
            {
                Assert.True(bounds.Width >= target.Width * 0.999999);
                Assert.True(bounds.Height >= target.Height * 0.999999);
            }
        }
    }

    [Theory]
    [InlineData(0, 10, 100, 100)]
    [InlineData(10, 0, 100, 100)]
    [InlineData(-1, 10, 100, 100)]
    [InlineData(10, -1, 100, 100)]
    [InlineData(10, 10, 0, 100)]
    [InlineData(10, 10, 100, 0)]
    [InlineData(10, 10, -1, 100)]
    [InlineData(10, 10, 100, -1)]
    public void InvalidImageOrTarget_ReturnsEmptyForEveryPosition(int imageWidth, int imageHeight, int width, int height)
    {
        foreach (var position in Enum.GetValues<DesktopWallpaperPosition>())
        {
            var bounds = DesktopWallpaperLayout.GetImageBounds(new Size(imageWidth, imageHeight),
                new Rectangle(-100, -50, width, height), position);
            Assert.Equal(RectangleF.Empty, bounds);
            AssertFinite(bounds);
        }
    }

    [Fact]
    public void UnknownPosition_ReturnsEmptyWithoutGuessingPlacement()
    {
        Assert.Equal(RectangleF.Empty, DesktopWallpaperLayout.GetImageBounds(new Size(100, 100),
            new Rectangle(0, 0, 1920, 1080), (DesktopWallpaperPosition)99));
    }

    private static void AssertBounds(RectangleF actual, double x, double y, double width, double height)
    {
        AssertApproximately(x, actual.X);
        AssertApproximately(y, actual.Y);
        AssertApproximately(width, actual.Width);
        AssertApproximately(height, actual.Height);
        AssertFinite(actual);
    }

    private static void AssertAspectRatio(Size image, RectangleF bounds) =>
        AssertApproximately((double)image.Width / image.Height, (double)bounds.Width / bounds.Height);

    private static void AssertApproximately(double expected, double actual, double minimumTolerance = 0.001)
    {
        var tolerance = Math.Max(minimumTolerance, Math.Abs(expected) * 0.000001);
        Assert.InRange(actual, expected - tolerance, expected + tolerance);
    }

    private static void AssertFinite(RectangleF bounds)
    {
        Assert.True(float.IsFinite(bounds.X));
        Assert.True(float.IsFinite(bounds.Y));
        Assert.True(float.IsFinite(bounds.Width));
        Assert.True(float.IsFinite(bounds.Height));
    }
}
