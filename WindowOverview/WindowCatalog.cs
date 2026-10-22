using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MissionControlPrototype;

internal sealed record WindowInfo(
    IntPtr Handle,
    string Title,
    string ProcessName,
    Rectangle Bounds,
    Icon? Icon);

internal static class WindowCatalog
{
    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow"
    };

    public static IReadOnlyList<WindowInfo> GetCurrentDesktopWindows()
    {
        var windows = new List<WindowInfo>();
        var currentProcessId = (uint)Environment.ProcessId;
        var desktopManager = TryCreateDesktopManager();

        NativeMethods.EnumWindows((handle, _) =>
        {
            try
            {
                if (!IsCandidate(handle, currentProcessId, desktopManager))
                {
                    return true;
                }

                var title = ReadWindowText(handle);
                var bounds = ReadWindowBounds(handle);
                if (string.IsNullOrWhiteSpace(title) || bounds.Width < 80 || bounds.Height < 50)
                {
                    return true;
                }

                NativeMethods.GetWindowThreadProcessId(handle, out var processId);
                var processName = string.Empty;
                Icon? icon = null;

                try
                {
                    using var process = Process.GetProcessById((int)processId);
                    processName = process.ProcessName;
                    var executable = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(executable))
                    {
                        icon = Icon.ExtractAssociatedIcon(executable);
                    }
                }
                catch
                {
                    // Protected and elevated processes can deny metadata access.
                }

                windows.Add(new WindowInfo(handle, title, processName, bounds, icon));
            }
            catch
            {
                // One unusual window must not prevent the overview from opening.
            }

            return true;
        }, IntPtr.Zero);

        if (desktopManager is not null && Marshal.IsComObject(desktopManager))
        {
            Marshal.FinalReleaseComObject(desktopManager);
        }

        return windows;
    }

    private static bool IsCandidate(
        IntPtr handle,
        uint currentProcessId,
        NativeMethods.IVirtualDesktopManager? desktopManager)
    {
        if (!NativeMethods.IsWindowVisible(handle))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        if (processId == currentProcessId)
        {
            return false;
        }

        var exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        if ((exStyle & NativeMethods.WsExToolWindow) != 0)
        {
            return false;
        }

        var className = new StringBuilder(256);
        NativeMethods.GetClassName(handle, className, className.Capacity);
        if (ExcludedClasses.Contains(className.ToString()))
        {
            return false;
        }

        int cloaked;
        if (NativeMethods.DwmGetWindowAttribute(
                handle,
                NativeMethods.DwmwaCloaked,
                out cloaked,
                sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        if (desktopManager is not null &&
            desktopManager.IsWindowOnCurrentVirtualDesktop(handle, out var onCurrentDesktop) == 0 &&
            !onCurrentDesktop)
        {
            return false;
        }

        return NativeMethods.GetWindowTextLength(handle) > 0;
    }

    private static string ReadWindowText(IntPtr handle)
    {
        var length = Math.Min(NativeMethods.GetWindowTextLength(handle) + 1, 2048);
        var text = new StringBuilder(Math.Max(length, 2));
        NativeMethods.GetWindowText(handle, text, text.Capacity);
        return text.ToString().Trim();
    }

    private static Rectangle ReadWindowBounds(IntPtr handle)
    {
        NativeMethods.NativeRect nativeBounds;
        if (NativeMethods.DwmGetWindowAttribute(
                handle,
                NativeMethods.DwmwaExtendedFrameBounds,
                out nativeBounds,
                Marshal.SizeOf<NativeMethods.NativeRect>()) == 0)
        {
            return nativeBounds.ToRectangle();
        }

        return Rectangle.Empty;
    }

    private static NativeMethods.IVirtualDesktopManager? TryCreateDesktopManager()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(
                new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"),
                throwOnError: false);
            return type is null
                ? null
                : (NativeMethods.IVirtualDesktopManager?)Activator.CreateInstance(type);
        }
        catch
        {
            return null;
        }
    }
}
