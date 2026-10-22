namespace TouchpadControl;

public enum LaunchIntent { Settings, Background, Overview, Exit }

/// <summary>Distinguishes an explicit open from a login/recovery activation.</summary>
public static class LaunchIntentPolicy
{
    public static bool ShouldForwardToSuite(LaunchIntent intent, bool preview) => intent == LaunchIntent.Settings && !preview;
    public static LaunchIntent Resolve(IEnumerable<string> arguments, bool isStartupActivation = false)
    {
        var values = arguments.ToArray();
        if (values.Contains("--exit", StringComparer.OrdinalIgnoreCase)) return LaunchIntent.Exit;
        if (values.Contains("--preview", StringComparer.OrdinalIgnoreCase)) return LaunchIntent.Settings;
        if (values.Contains("--mission-control", StringComparer.OrdinalIgnoreCase)) return LaunchIntent.Overview;
        if (isStartupActivation || values.Contains("--background", StringComparer.OrdinalIgnoreCase)
            || values.Contains("--watchdog", StringComparer.OrdinalIgnoreCase)) return LaunchIntent.Background;
        return LaunchIntent.Settings;
    }
}
