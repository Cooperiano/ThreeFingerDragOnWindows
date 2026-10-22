namespace TouchpadControl;

public static class StartupRegistrationPolicy
{
    public const string TaskName = "Touchpad Control - Start at Logon";
    public const string Arguments = "--watchdog";

    public static bool IsActionValid(string? executable, string? arguments, string registeredAlias,
        int actionCount, bool hasLogonTrigger) => actionCount == 1 && hasLogonTrigger
        && string.Equals(executable?.Trim().Trim('"'), registeredAlias, StringComparison.OrdinalIgnoreCase)
        && string.Equals(arguments?.Trim(), Arguments, StringComparison.OrdinalIgnoreCase);
}
