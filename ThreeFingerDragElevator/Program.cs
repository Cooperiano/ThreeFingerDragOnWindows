using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

if(args.Length == 6 && args[0] == "ApplyPointer") {
    Environment.ExitCode = ApplyPointerProfile(args[1..]) ? 0 : 1;
    return;
}

ProcessStartInfo processInfo = new ProcessStartInfo
{
    UseShellExecute = true,
    FileName = "ThreeFingerDragOnWindows.exe",
    Arguments = args.Contains("--background", StringComparer.OrdinalIgnoreCase)
        ? "FromElevator --background" : "FromElevator"
};
try{
    Process.Start(processInfo);
}catch(Win32Exception ex){
    Debug.WriteLine(ex);
}

static bool ApplyPointerProfile(string[] values) {
    if(!int.TryParse(values[0], out var speed) ||
       !int.TryParse(values[1], out var threshold1) ||
       !int.TryParse(values[2], out var threshold2) ||
       !int.TryParse(values[3], out var acceleration) ||
       !int.TryParse(values[4], out var touchpadSpeed)) {
        return false;
    }

    const uint updateAndBroadcast = 0x0001 | 0x0002;
    var mouse = new[] { threshold1, threshold2, acceleration };
    var mouseApplied = NativeMethods.SystemParametersInfo(0x0004, 0, mouse, updateAndBroadcast);
    var speedApplied = NativeMethods.SystemParametersInfo(
        0x0071,
        0,
        new IntPtr(speed),
        updateAndBroadcast);

    try {
        using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad",
            true);
        key?.SetValue("CursorSpeed", touchpadSpeed, RegistryValueKind.DWord);
        return mouseApplied && speedApplied && key != null;
    } catch {
        return false;
    }
}

internal static class NativeMethods {
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(uint action, uint parameter, int[] value, uint flags);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(uint action, uint parameter, IntPtr value, uint flags);
}
