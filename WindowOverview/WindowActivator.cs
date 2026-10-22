namespace MissionControlPrototype;

internal static class WindowActivator
{
    public static void Activate(IntPtr handle)
    {
        // SW_RESTORE also turns maximized windows into ordinary centered windows.
        // Restore only genuinely minimized windows and preserve every other placement.
        if (NativeMethods.IsIconic(handle))
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SwRestore);
        }

        NativeMethods.SetForegroundWindow(handle);
    }
}
