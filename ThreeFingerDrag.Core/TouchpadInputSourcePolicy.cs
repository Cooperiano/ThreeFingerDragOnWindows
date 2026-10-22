namespace TouchpadControl.Pointer;

/// <summary>Native touchpad output must never re-enter physical gesture recognition.</summary>
public static class TouchpadInputSourcePolicy {
    // Observed from CreateSyntheticPointerDevice2 on this machine. This exact
    // HID usage boundary avoids rejecting physical HID devices or zero VID/PID.
    public static bool IsSyntheticTouchpadPath(string? path) => path?.StartsWith(
        @"\\?\Microsoft HID RID\000D_0005\", StringComparison.OrdinalIgnoreCase) == true;
}
