using System.Drawing;

namespace MissionControlPrototype;

internal static class OverviewGeometry
{
    // Match the stable, back-to-front card drawing order. Never exclude the
    // current selection: that made identical overlap hit tests alternate.
    public static int HitTest<T>(IReadOnlyList<T> cards, Point point, Func<T, Rectangle> bounds)
    {
        for (var index = cards.Count - 1; index >= 0; index--)
        {
            if (bounds(cards[index]).Contains(point)) return index;
        }
        return -1;
    }

    public static bool IsClickRelease(int pressedIndex, bool moved, int releasedIndex) =>
        pressedIndex >= 0 && !moved && releasedIndex == pressedIndex;

    public static Rectangle Interpolate(Rectangle start, Rectangle end, double progress) =>
        new(
            Lerp(start.Left, end.Left, progress),
            Lerp(start.Top, end.Top, progress),
            Math.Max(1, Lerp(start.Width, end.Width, progress)),
            Math.Max(1, Lerp(start.Height, end.Height, progress)));

    private static int Lerp(int start, int end, double progress) =>
        (int)Math.Round(start + (end - start) * progress);
}
