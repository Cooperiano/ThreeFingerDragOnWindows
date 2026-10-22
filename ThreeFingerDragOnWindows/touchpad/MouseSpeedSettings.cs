#nullable enable
using System;
using ThreeFingerDragOnWindows.settings;
using ThreeFingerDragOnWindows.localization;
using TouchpadControl.Settings;

namespace ThreeFingerDragOnWindows.touchpad;

public class MouseSpeedSettings : DevicePointerEditModel
{
    public TouchpadDeviceInfo TouchpadDevice { get; }
    public string Header { get; }
    public double CursorSpeedMaximum { get; }
    public double CursorAccelerationMaximum { get; }

    public MouseSpeedSettings(SettingsData.ThreeFingerDragConfig dragConfig, TouchpadDeviceInfo device)
        : base(new DevicePointerValues(dragConfig.ThreeFingerDragCursorMove,
            dragConfig.ThreeFingerDragCursorSpeed, dragConfig.ThreeFingerDragCursorAcceleration),
            candidate => App.SettingsData.TryCommit(settings =>
                settings.ThreeFingerDeviceDragCursorConfigs[device.deviceId ?? "default"] = new SettingsData.ThreeFingerDragConfig(
                    candidate.CursorMove, candidate.Speed, candidate.Acceleration)),
            error => SettingsData.RejectEdit(error))
    {
        // Reading this page must not register defaults or activate acceleration.
        TouchpadDevice = device;
        Header = UiStrings.Format("Enable three-finger pointer movement ({0})", device);
        CursorSpeedMaximum = Math.Max(200, dragConfig.ThreeFingerDragCursorSpeed);
        CursorAccelerationMaximum = Math.Max(30, dragConfig.ThreeFingerDragCursorAcceleration);
    }

    // Read globally published values during persistence notifications as well.
    public override bool CursorMoveProperty
    {
        get => CurrentConfig?.ThreeFingerDragCursorMove ?? base.CursorMoveProperty;
        set => base.CursorMoveProperty = value;
    }

    public override float CursorSpeedProperty
    {
        get => CurrentConfig?.ThreeFingerDragCursorSpeed ?? base.CursorSpeedProperty;
        set => base.CursorSpeedProperty = value;
    }

    public override float CursorAccelerationProperty
    {
        get => CurrentConfig?.ThreeFingerDragCursorAcceleration ?? base.CursorAccelerationProperty;
        set => base.CursorAccelerationProperty = value;
    }

    private SettingsData.ThreeFingerDragConfig? CurrentConfig =>
        App.SettingsData.ThreeFingerDeviceDragCursorConfigs.TryGetValue(TouchpadDevice.deviceId ?? "default", out var config)
            ? config : null;
}
