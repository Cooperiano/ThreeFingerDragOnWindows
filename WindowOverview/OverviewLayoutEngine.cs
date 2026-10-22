using System.Drawing;

namespace MissionControlPrototype;

/// <summary>
/// Packs window previews at one shared scale so their real aspect ratios remain
/// recognizable. More windows reduce the shared scale instead of being cropped
/// into fixed grid cells.
/// </summary>
internal static class OverviewLayoutEngine
{
    public static void MoveItem<T>(IList<T> items, int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex)
        {
            return;
        }

        var item = items[fromIndex];
        items.RemoveAt(fromIndex);
        items.Insert(toIndex, item);
    }

    public static IReadOnlyList<Rectangle> Arrange(
        IReadOnlyList<Size> windowSizes,
        Rectangle available,
        int gap,
        int horizontalChrome,
        int verticalChrome)
    {
        if (windowSizes.Count == 0)
        {
            return Array.Empty<Rectangle>();
        }

        var low = 0.01d;
        var high = 1d;
        List<LayoutRow>? bestRows = null;
        for (var iteration = 0; iteration < 44; iteration++)
        {
            var scale = (low + high) / 2d;
            if (TryBuildRows(windowSizes, available, gap, horizontalChrome, verticalChrome, scale, out var rows))
            {
                low = scale;
                bestRows = rows;
            }
            else
            {
                high = scale;
            }
        }

        TryBuildRows(windowSizes, available, gap, horizontalChrome, verticalChrome, low, out var finalRows);
        bestRows = finalRows.Count > 0 ? finalRows : bestRows ?? [];

        var totalHeight = bestRows.Sum(row => row.Height) + gap * Math.Max(0, bestRows.Count - 1);
        var y = available.Top + Math.Max(0, (available.Height - totalHeight) / 2);
        var result = new Rectangle[windowSizes.Count];
        foreach (var row in bestRows)
        {
            var rowWidth = row.Items.Sum(item => item.Width) + gap * Math.Max(0, row.Items.Count - 1);
            var x = available.Left + Math.Max(0, (available.Width - rowWidth) / 2);
            foreach (var item in row.Items)
            {
                result[item.Index] = new Rectangle(
                    x,
                    y + (row.Height - item.Height) / 2,
                    item.Width,
                    item.Height);
                x += item.Width + gap;
            }
            y += row.Height + gap;
        }

        return result;
    }

    private static bool TryBuildRows(
        IReadOnlyList<Size> windowSizes,
        Rectangle available,
        int gap,
        int horizontalChrome,
        int verticalChrome,
        double scale,
        out List<LayoutRow> rows)
    {
        rows = [];
        LayoutRow? row = null;
        for (var index = 0; index < windowSizes.Count; index++)
        {
            var source = windowSizes[index];
            var width = Math.Max(24, (int)Math.Round(Math.Max(1, source.Width) * scale) + horizontalChrome);
            var height = Math.Max(24, (int)Math.Round(Math.Max(1, source.Height) * scale) + verticalChrome);
            if (width > available.Width || height > available.Height)
            {
                return false;
            }

            if (row is null || row.Width + gap + width > available.Width)
            {
                row = new LayoutRow();
                rows.Add(row);
            }

            row.Add(new LayoutItem(index, width, height), gap);
        }

        var totalHeight = rows.Sum(candidate => candidate.Height) + gap * Math.Max(0, rows.Count - 1);
        return totalHeight <= available.Height;
    }

    private sealed class LayoutRow
    {
        public List<LayoutItem> Items { get; } = [];
        public int Width { get; private set; }
        public int Height { get; private set; }

        public void Add(LayoutItem item, int gap)
        {
            if (Items.Count > 0)
            {
                Width += gap;
            }
            Items.Add(item);
            Width += item.Width;
            Height = Math.Max(Height, item.Height);
        }
    }

    private readonly record struct LayoutItem(int Index, int Width, int Height);
}
