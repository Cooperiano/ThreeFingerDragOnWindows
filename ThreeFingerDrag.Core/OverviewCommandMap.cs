using ThreeFingerDragOnWindows.gestures;

namespace TouchpadControl.Protocol;

public static class OverviewCommandMap {
    // Lifecycle commands have their own IDs: opening an already visible
    // overview must not become SelectUp. The v1 registered message is retained.
    public static int GetCommand(FourFingerGesture gesture) => gesture switch {
        FourFingerGesture.PreviousDesktop => 0x4D45,
        FourFingerGesture.NextDesktop => 0x4D44,
        FourFingerGesture.TaskView => 0x4D4A,
        FourFingerGesture.ShowDesktop => 0x4D4B,
        FourFingerGesture.SelectLeft => 0x4D44,
        FourFingerGesture.SelectRight => 0x4D45,
        FourFingerGesture.SelectUp => 0x4D46,
        FourFingerGesture.SelectDown => 0x4D47,
        FourFingerGesture.ActivateSelection => 0x4D48,
        FourFingerGesture.CancelOverview => 0x4D49,
        _ => 0
    };
}
