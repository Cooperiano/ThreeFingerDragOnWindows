using ThreeFingerDragOnWindows.gestures;
using Xunit;

namespace ThreeFingerDragEngine.Tests;

public sealed class PointerAccelerationProfileTests {
    [Fact]
    public void WindowsNativeProfile_UsesAnEnabledAccelerationSettingNotAnUnverifiedStrongerCurve() {
        var profile = PointerAccelerationProfile.WindowsNative;

        Assert.Equal(8, profile.PointerSpeed);
        Assert.Equal(2, profile.Threshold1);
        Assert.Equal(5, profile.Threshold2);
        Assert.Equal(1, profile.AccelerationLevel);
        Assert.Equal(9, profile.TouchpadCursorSpeed);
    }

    [Fact]
    public void Normalize_ClampsUnsafeValuesAndOrdersThresholds() {
        var profile = new PointerAccelerationProfile(99, 30, -4, 8, 0).Normalize();

        Assert.Equal(20, profile.PointerSpeed);
        Assert.Equal(20, profile.Threshold1);
        Assert.Equal(20, profile.Threshold2);
        Assert.Equal(2, profile.AccelerationLevel);
        Assert.Equal(1, profile.TouchpadCursorSpeed);
    }
}
