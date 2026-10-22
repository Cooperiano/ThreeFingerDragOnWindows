using System.Drawing;
using MissionControlPrototype;
using Xunit;

namespace ThreeFingerDragEngine.Tests;

public class OverviewLayoutEngineTests {
    [Fact]
    public void Arrange_PreservesPreviewAspectRatiosAndAvoidsOverlap() {
        var sizes = new[] {
            new Size(1600, 900),
            new Size(900, 1200),
            new Size(1200, 800),
            new Size(700, 700)
        };
        var available = new Rectangle(40, 80, 1800, 900);

        var result = OverviewLayoutEngine.Arrange(sizes, available, 24, 20, 48);

        Assert.Equal(sizes.Length, result.Count);
        for(var index = 0; index < result.Count; index++) {
            Assert.True(available.Contains(result[index]));
            var previewRatio = (result[index].Width - 20d) / (result[index].Height - 48d);
            var sourceRatio = sizes[index].Width / (double)sizes[index].Height;
            Assert.InRange(Math.Abs(previewRatio - sourceRatio), 0, 0.015);
        }

        for(var first = 0; first < result.Count; first++) {
            for(var second = first + 1; second < result.Count; second++) {
                Assert.False(result[first].IntersectsWith(result[second]));
            }
        }
    }

    [Fact]
    public void Arrange_ShrinksCardsAsWindowCountGrows() {
        var available = new Rectangle(0, 0, 1600, 850);
        var few = OverviewLayoutEngine.Arrange(
            Enumerable.Repeat(new Size(1200, 800), 3).ToArray(),
            available,
            24,
            20,
            48);
        var many = OverviewLayoutEngine.Arrange(
            Enumerable.Repeat(new Size(1200, 800), 12).ToArray(),
            available,
            24,
            20,
            48);

        Assert.True(many.Average(card => card.Width) < few.Average(card => card.Width));
        Assert.All(many, card => Assert.True(available.Contains(card)));
    }

    [Fact]
    public void MoveItem_ReordersDraggedWindowWithoutLosingCards() {
        var order = new List<string> { "one", "two", "three", "four" };

        OverviewLayoutEngine.MoveItem(order, 0, 2);

        Assert.Equal(new[] { "two", "three", "one", "four" }, order);
    }
}
