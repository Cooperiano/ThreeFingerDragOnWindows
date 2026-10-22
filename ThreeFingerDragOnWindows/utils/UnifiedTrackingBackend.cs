using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using TouchpadControl.Pointer;

namespace ThreeFingerDragOnWindows.utils;

internal sealed class UnifiedTrackingBackend : ITrackingBackend {
    [StructLayout(LayoutKind.Sequential)] private struct Parameters {
        public uint Version, Contacts, LegacyFeatures, StatusFlags, UserFlags;
        public uint Sensitivity, CursorSpeed, FeedbackIntensity, ClickForce, RightZoneWidth, RightZoneHeight;
    }
    private sealed record Snapshot(Parameters Touchpad, int[] Mouse, bool ChangeAcceleration);
    private const uint MouseAccelerationHonored = 0x40;
    private static Parameters ReadTouchpad() {
        var value = new Parameters { Version = 1 };
        Check(TouchpadInfo(0xAE, (uint)Marshal.SizeOf<Parameters>(), ref value, 0));
        if((value.StatusFlags & 1) == 0) throw new InvalidOperationException("No Precision Touchpad is present.");
        return value;
    }
    public TrackingSettings Read() {
        var value = ReadTouchpad();
        var mouse = ReadMouse();
        return new(checked((int)value.CursorSpeed), (value.UserFlags & MouseAccelerationHonored) == 0 || mouse[2] != 0);
    }
    private static int[] ReadMouse() { var mouse = new int[3]; Check(MouseInfo(3, 0, mouse, 0)); return mouse; }
    public object Capture(bool changeAcceleration) => new Snapshot(ReadTouchpad(), changeAcceleration ? ReadMouse() : null, changeAcceleration);
    public void Apply(TrackingSettings desired, bool changeAcceleration) {
        if(desired.Speed < 1 || desired.Speed > 20) throw new ArgumentOutOfRangeException(nameof(desired));
        var value = ReadTouchpad();
        if(changeAcceleration) {
            var mouse = ReadMouse();
            mouse[2] = desired.Acceleration ? 1 : 0;
            Check(MouseInfo(4, 0, mouse, 3));
            // Windows PTP otherwise always accelerates, regardless of EPP.
            value.UserFlags |= MouseAccelerationHonored;
        }
        value.CursorSpeed = (uint)desired.Speed;
        Check(TouchpadInfo(0xAF, (uint)Marshal.SizeOf<Parameters>(), ref value, 3));
    }
    public void Restore(object snapshot) {
        var before = (Snapshot)snapshot;
        if(before.ChangeAcceleration) Check(MouseInfo(4, 0, before.Mouse, 3));
        // Restore owned fields only; never overwrite unrelated user gestures.
        var current = ReadTouchpad();
        current.CursorSpeed = before.Touchpad.CursorSpeed;
        if(before.ChangeAcceleration)
            current.UserFlags = (current.UserFlags & ~MouseAccelerationHonored) | (before.Touchpad.UserFlags & MouseAccelerationHonored);
        Check(TouchpadInfo(0xAF, (uint)Marshal.SizeOf<Parameters>(), ref current, 3));
    }
    public bool Matches(object snapshot) {
        var before = (Snapshot)snapshot;
        var current = ReadTouchpad();
        if(current.CursorSpeed != before.Touchpad.CursorSpeed) return false;
        if(!before.ChangeAcceleration) return true;
        var mouse = ReadMouse();
        return (current.UserFlags & MouseAccelerationHonored) == (before.Touchpad.UserFlags & MouseAccelerationHonored)
            && mouse[0] == before.Mouse[0] && mouse[1] == before.Mouse[1] && mouse[2] == before.Mouse[2];
    }
    private static void Check(bool result) { if(!result) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TouchpadInfo(uint action, uint size, ref Parameters value, uint flags);
    [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MouseInfo(uint action, uint size, int[] value, uint flags);
}
