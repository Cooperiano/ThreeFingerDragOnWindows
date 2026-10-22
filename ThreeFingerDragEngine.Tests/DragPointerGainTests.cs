using ThreeFingerDragOnWindows.gestures;
using Xunit;

namespace ThreeFingerDragEngine.Tests;

public class DragPointerGainTests {
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void DisabledAcceleration_ReturnsUnitGain(double velocity) =>
        Assert.Equal(1f, DragPointerGain.Calculate(velocity, 0));

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 10)]
    [InlineData(4, 10)]
    [InlineData(2, 30)]
    public void ExistingAccelerationCurve_IsPreserved(double velocity, float acceleration) {
        var a = acceleration / 10f;
        var expected = (float)(0.7 + 0.8 / (1 + Math.Exp(-(2.6 * a * (velocity - 1 + (3 - Math.Log2(0.8 / 0.3 - 1)) / (2.6 * a)) - 3))));

        Assert.Equal(expected, DragPointerGain.Calculate(velocity, acceleration));
    }
}
