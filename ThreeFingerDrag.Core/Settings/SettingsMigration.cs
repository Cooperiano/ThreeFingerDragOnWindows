using System;

namespace TouchpadControl.Settings;

public readonly record struct SettingsMigrationResult(
    int Version, int FourFingerThreshold, int StartDragThreshold, bool RequiresSave);

/// <summary>All migration decisions use the original version, never an intermediate save.</summary>
public static class SettingsMigration
{
    public const int CurrentVersion = 9;

    public static SettingsMigrationResult Apply(int originalVersion, int fourFingerThreshold, int startDragThreshold)
    {
        if(originalVersion < 0 || originalVersion > CurrentVersion)
            throw new ArgumentOutOfRangeException(nameof(originalVersion), "Unsupported settings version.");

        if(originalVersion < 8 && fourFingerThreshold == 180) fourFingerThreshold = 90;
        if(originalVersion < 9 && startDragThreshold == 100) startDragThreshold = 32;
        return new(CurrentVersion, fourFingerThreshold, startDragThreshold, originalVersion != CurrentVersion);
    }
}
