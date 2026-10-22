using System.Drawing;
using MissionControlPrototype;
using ThreeFingerDragOnWindows.gestures;
using TouchpadControl.Protocol;
using Xunit;

public class OverviewInteractionTests
{
    [Fact]
    public void OpenRequestWhenHiddenShowsOverview()
    {
        var command = OverviewCommandMap.GetCommand(FourFingerGesture.TaskView);
        Assert.True(OverviewLifecycle.IsCommand(command));
        Assert.Equal(OverviewLifecycleAction.Open, OverviewLifecycle.Resolve(command, false));
    }

    [Fact]
    public void RepeatedOpenDoesNotMoveSelectionOrRebuildVisibleOverview()
    {
        var command = OverviewCommandMap.GetCommand(FourFingerGesture.TaskView);
        Assert.Equal(OverviewLifecycleAction.None, OverviewLifecycle.Resolve(command, true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DownSwipeReturnsFromOverviewBeforeShowingDesktop(bool visible)
    {
        var command = OverviewCommandMap.GetCommand(FourFingerGesture.ShowDesktop);
        var expected = visible ? OverviewLifecycleAction.Hide : OverviewLifecycleAction.ShowDesktop;
        Assert.Equal(expected, OverviewLifecycle.Resolve(command, visible));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitCancelOnlyHidesAndNeverActivates(bool visible)
    {
        var expected = visible ? OverviewLifecycleAction.Hide : OverviewLifecycleAction.None;
        Assert.Equal(expected, OverviewLifecycle.Resolve(
            OverviewCommandMap.GetCommand(FourFingerGesture.CancelOverview), visible));
    }

    [Theory]
    [InlineData(FourFingerGesture.SelectUp)]
    [InlineData(FourFingerGesture.SelectDown)]
    [InlineData(FourFingerGesture.ActivateSelection)]
    public void LegacySelectionCommandsDoNotChangeLifecycle(FourFingerGesture gesture)
    {
        Assert.False(OverviewLifecycle.IsCommand(OverviewCommandMap.GetCommand(gesture)));
    }

    [Fact]
    public void SameOverlapHitRemainsOnTopCardRegardlessOfPreviousSelection()
    {
        var cards = new[] { new Rectangle(0, 0, 100, 100), new Rectangle(20, 20, 100, 100) };
        var point = new Point(50, 50);
        for (var move = 0; move < 10; move++)
            Assert.Equal(1, OverviewGeometry.HitTest(cards, point, static bounds => bounds));
        Assert.Equal(0, OverviewGeometry.HitTest(cards, new Point(10, 10), static bounds => bounds));
        Assert.Equal(-1, OverviewGeometry.HitTest(cards, new Point(150, 150), static bounds => bounds));
    }

    [Fact]
    public void EmptyOverviewHasNoPointerTarget()
    {
        Assert.Equal(-1, OverviewGeometry.HitTest(Array.Empty<Rectangle>(), Point.Empty, static bounds => bounds));
    }

    [Theory]
    [InlineData(0, false, 0, true)]
    [InlineData(0, false, 1, false)]
    [InlineData(0, false, -1, false)]
    [InlineData(0, true, 0, false)]
    [InlineData(-1, false, -1, false)]
    [InlineData(-1, false, 0, false)]
    public void OnlyAnUncancelledClickOnItsOriginalCardOpens(int pressed, bool moved, int released, bool expected)
    {
        Assert.Equal(expected, OverviewGeometry.IsClickRelease(pressed, moved, released));
    }

    [Fact]
    public void OpeningAnimationStartsAtActualWindowAndEndsAtLayoutTarget()
    {
        var source = new Rectangle(-300, 60, 1280, 900);
        var target = new Rectangle(200, 200, 480, 330);
        Assert.Equal(source, OverviewGeometry.Interpolate(source, target, 0));
        Assert.Equal(target, OverviewGeometry.Interpolate(source, target, 1));
        var early = OverviewGeometry.Interpolate(source, target, 0.05);
        Assert.InRange(early.Left, source.Left, target.Left);
        Assert.InRange(early.Width, target.Width, source.Width);
    }
}
