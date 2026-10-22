using System;
using System.Runtime.InteropServices;

namespace ThreeFingerDragOnWindows.touchpad;

internal static class Interop {
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate IntPtr SubclassProcDelegate(IntPtr hwnd, uint message, IntPtr wParam,
        IntPtr lParam, UIntPtr subclassId, UIntPtr referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProcDelegate callback,
        UIntPtr subclassId, UIntPtr referenceData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProcDelegate callback, UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    internal static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
