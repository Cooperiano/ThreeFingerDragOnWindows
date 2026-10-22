using System;

namespace TouchpadControl.Settings;

/// <summary>Shared input rules. Do not coerce an empty NumberBox into a real setting.</summary>
public static class SettingsValueRules
{
    public const int CursorSpeedMinimum = 0;
    public const int CursorSpeedMaximum = 100000;
    public const int CursorAccelerationMinimum = 0;
    public const int CursorAccelerationMaximum = 1000;
    public const int FourFingerThresholdMinimum = 40;
    public const int FourFingerThresholdMaximum = 1000;
    public const int DragThresholdMaximum = 1000;
    public const int MaximumMoveDistance = 10000;
    public const int ReleaseDelayMaximum = 2000;
    public const int AveragingMinimum = 1;
    public const int AveragingMaximum = 10;

    public static bool IsFiniteInRange(double value, double minimum, double maximum) =>
        double.IsFinite(value) && value >= minimum && value <= maximum;

    public static bool TryInt(double value, int minimum, int maximum, out int result)
    {
        result = default;
        if(!IsFiniteInRange(value, minimum, maximum) || value != Math.Truncate(value)) return false;
        result = (int)value;
        return true;
    }
}
