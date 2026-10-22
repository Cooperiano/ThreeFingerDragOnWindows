using TouchpadControl.Pointer;
using Xunit;

public sealed class TouchpadInputSourcePolicyTests {
    [Theory]
    [InlineData(@"\\?\Microsoft HID RID\000D_0005\8", true)]
    [InlineData(@"\\?\microsoft hid rid\000d_0005\12", true)]
    [InlineData(@"\\?\HID#ELAN1203&Col02#5&145612dd&0&0001", false)]
    [InlineData(@"\\?\HID#VID_0000&PID_0000", false)]
    [InlineData(@"\\?\Microsoft HID RID\000D_0004\8", false)]
    [InlineData(@"\\?\Microsoft HID RID\000D_00050\8", false)]
    [InlineData(@"\\?\Microsoft HID RID\000D_0005", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void RejectsOnlyExactVirtualTouchpadSource(string? path, bool expected) =>
        Assert.Equal(expected, TouchpadInputSourcePolicy.IsSyntheticTouchpadPath(path));
}
