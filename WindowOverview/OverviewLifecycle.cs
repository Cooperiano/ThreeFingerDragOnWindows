namespace MissionControlPrototype;

internal enum OverviewLifecycleAction
{
    None,
    Open,
    Hide,
    ShowDesktop
}

internal static class OverviewLifecycle
{
    public const int CancelCommand = 0x4D49;
    public const int OpenCommand = 0x4D4A;
    public const int ReturnOrDesktopCommand = 0x4D4B;

    public static bool IsCommand(int command) =>
        command is CancelCommand or OpenCommand or ReturnOrDesktopCommand;

    public static OverviewLifecycleAction Resolve(int command, bool visible) => command switch
    {
        OpenCommand => visible ? OverviewLifecycleAction.None : OverviewLifecycleAction.Open,
        CancelCommand => visible ? OverviewLifecycleAction.Hide : OverviewLifecycleAction.None,
        ReturnOrDesktopCommand => visible ? OverviewLifecycleAction.Hide : OverviewLifecycleAction.ShowDesktop,
        _ => OverviewLifecycleAction.None
    };
}
