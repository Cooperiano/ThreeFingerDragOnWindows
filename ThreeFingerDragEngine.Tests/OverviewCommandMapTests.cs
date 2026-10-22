using ThreeFingerDragOnWindows.gestures;
using TouchpadControl.Protocol;
using Xunit;

public class OverviewCommandMapTests {
    [Theory]
    [InlineData(FourFingerGesture.PreviousDesktop, 0x4D45)]
    [InlineData(FourFingerGesture.NextDesktop, 0x4D44)]
    [InlineData(FourFingerGesture.TaskView, 0x4D4A)]
    [InlineData(FourFingerGesture.ShowDesktop, 0x4D4B)]
    [InlineData(FourFingerGesture.SelectLeft, 0x4D44)]
    [InlineData(FourFingerGesture.SelectRight, 0x4D45)]
    [InlineData(FourFingerGesture.SelectUp, 0x4D46)]
    [InlineData(FourFingerGesture.SelectDown, 0x4D47)]
    [InlineData(FourFingerGesture.ActivateSelection, 0x4D48)]
    [InlineData(FourFingerGesture.CancelOverview, 0x4D49)]
    [InlineData(FourFingerGesture.None, 0)]
    [InlineData((FourFingerGesture)123, 0)]
    public void CommandsMatchCompanionProtocol(FourFingerGesture gesture, int expected) {
        Assert.Equal(expected, OverviewCommandMap.GetCommand(gesture));
    }

    [Fact]
    public void OverviewLifecycleIsNotDirectionalSelection() {
        Assert.NotEqual(OverviewCommandMap.GetCommand(FourFingerGesture.TaskView),
            OverviewCommandMap.GetCommand(FourFingerGesture.SelectUp));
        Assert.NotEqual(OverviewCommandMap.GetCommand(FourFingerGesture.ShowDesktop),
            OverviewCommandMap.GetCommand(FourFingerGesture.SelectDown));
    }
}
