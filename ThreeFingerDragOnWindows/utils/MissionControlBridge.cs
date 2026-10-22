using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
using ThreeFingerDragOnWindows.gestures;
using TouchpadControl.Protocol;

namespace ThreeFingerDragOnWindows.utils;

/// <summary>
/// Owns the bundled Mission Control companion and opens its overview through
/// a private, acknowledged window-message protocol. User hotkeys are not used
/// as an internal transport. Windows never receives Win+Tab when it is ready.
/// </summary>
internal static class MissionControlBridge {
    private const string CompanionProcessName = "MissionControlPrototype";
    private static readonly object Sync = new();
    private static readonly SemaphoreSlim GestureGate = new(1, 1);
    private static bool _stopped;
    private static bool IsStopping => Volatile.Read(ref _stopped) || App.Instance?.IsExiting == true;
    private static readonly uint CommandMessage = RegisterWindowMessage("TouchpadControl.MissionControl.Gesture.v1");
    private static string CompanionPath => Path.Combine(AppContext.BaseDirectory, "MissionControl", "MissionControlPrototype.exe");

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam,
        IntPtr lParam, uint flags, uint timeout, out UIntPtr result);

    private static bool IsOwnedCompanion(Process process) {
        try {
            using var current = Process.GetCurrentProcess();
            return !process.HasExited && process.SessionId == current.SessionId &&
                string.Equals(process.MainModule?.FileName, CompanionPath, StringComparison.OrdinalIgnoreCase);
        } catch { return false; }
    }

    private static bool SendCommand(int command) {
        if(IsStopping || CommandMessage == 0 || command == 0) return false;
        IntPtr window = IntPtr.Zero;
        for(int attempt = 0; attempt < 64; attempt++) {
            window = FindWindowEx(new IntPtr(-3), window, null, "MissionControlPrototype.HotkeyWindow");
            if(window == IntPtr.Zero) return false;
            GetWindowThreadProcessId(window, out uint processId);
            try {
                using var target = Process.GetProcessById((int)processId);
                if(!IsOwnedCompanion(target)) continue;
                // Only integer values cross the process boundary. Bounded wait
                // runs on a worker, so input processing never blocks on layout.
                IntPtr sent = SendMessageTimeout(window, CommandMessage, new UIntPtr((uint)command),
                    new IntPtr(1), 0x23, 1000, out UIntPtr result);
                if(sent != IntPtr.Zero && result == new UIntPtr(1)) return true;
                Trace("command not acknowledged; error=" + Marshal.GetLastWin32Error());
                return false; // Never replay a command whose outcome is unknown.
            } catch(Exception exception) { Trace("command failed: " + exception); return false; }
        }
        return false;
    }

    private static void Trace(string message) {
        Logger.Log("MISSION_CONTROL: " + message);
        try {
            File.AppendAllText(Path.Combine(ApplicationData.Current.LocalFolder.Path, "mission-control.log"),
                $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
        } catch {
            // Diagnostics must never break gesture handling.
        }
    }
    private static bool IsRunning() {
        var processes = Process.GetProcessesByName(CompanionProcessName);
        try {
            return processes.Any(IsOwnedCompanion);
        } finally {
            foreach(var process in processes) process.Dispose();
        }
    }

    public static async Task<bool> EnsureRunningAsync() {
        lock(Sync) {
            if(IsStopping) return false;
            if(IsRunning()) {
                Trace("companion already running");
                return true;
            }
        }

        try {
            string companionPath = Path.Combine(AppContext.BaseDirectory, "MissionControl", "MissionControlPrototype.exe");
            // Serialize launch with Stop: work queued before quitting must not
            // start a new companion after the shutdown sweep.
            lock(Sync) {
                if(IsStopping) return false;
                Process.Start(new ProcessStartInfo {
                    FileName = companionPath,
                    Arguments = "--companion",
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(companionPath)!
                });
            }
            Trace("direct companion launch requested");
            await Task.Delay(450);
            if(IsStopping) return false;
            if(IsRunning()) return true;
        } catch(Exception exception) {
            Trace("direct launch failed: " + exception);
        }

        try {
            Windows.Foundation.IAsyncAction launch;
            lock(Sync) {
                if(IsStopping) return false;
                launch = FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync("MissionControlCompanion");
            }
            await launch;
            if(IsStopping) { Stop(); return false; }
            Trace("package full-trust launch requested");
            // RegisterHotKey is installed during companion startup. This delay is
            // only paid after an unexpected companion restart or the first gesture.
            await Task.Delay(450);
            if(IsStopping) { Stop(); return false; }
            bool running = IsRunning();
            Trace(running ? "companion ready" : "companion did not start");
            return running;
        } catch(Exception exception) {
            Trace("package launch failed: " + exception);
            return false;
        }
    }

    public static async Task HandleGestureAsync(FourFingerGesture gesture) {
        if(IsStopping) return;
        await GestureGate.WaitAsync();
        try {
            if(IsStopping) return;
            Trace("gesture requested: " + gesture);
            if(gesture == FourFingerGesture.CancelOverview) {
                // Do not launch a new companion, or send Escape to an unrelated
                // foreground window, just to cancel a previous gesture.
                bool cancelled = IsRunning() && await Task.Run(() => SendCommand(OverviewCommandMap.GetCommand(gesture)));
                Trace(cancelled ? "gesture command acknowledged: " + gesture : "gesture cancellation unavailable: " + gesture);
                return;
            }
            if(await EnsureRunningAsync()) {
            if(IsStopping) return;
            int command = OverviewCommandMap.GetCommand(gesture);
            if(command != 0) {
                bool accepted = await Task.Run(() => SendCommand(command));
                Trace(accepted ? "gesture command acknowledged: " + gesture : "gesture command unavailable: " + gesture);
            }
            return;
            }

            if(IsStopping) return;
            // Keep the original Windows actions useful if the companion cannot launch.
            switch(gesture) {
                case FourFingerGesture.PreviousDesktop:
                    MouseOperations.SendKeyboardChord(MouseOperations.VK_LWIN, MouseOperations.VK_CONTROL, MouseOperations.VK_LEFT);
                    break;
                case FourFingerGesture.NextDesktop:
                    MouseOperations.SendKeyboardChord(MouseOperations.VK_LWIN, MouseOperations.VK_CONTROL, MouseOperations.VK_RIGHT);
                    break;
                case FourFingerGesture.TaskView:
                    MouseOperations.SendKeyboardChord(MouseOperations.VK_LWIN, MouseOperations.VK_TAB);
                    break;
                case FourFingerGesture.ShowDesktop:
                    MouseOperations.SendKeyboardChord(MouseOperations.VK_LWIN, MouseOperations.VK_D);
                    break;
                case FourFingerGesture.SelectLeft:
                    MouseOperations.SendKeyboardChord(MouseOperations.VK_LEFT);
                    break;
                case FourFingerGesture.SelectRight:
                    MouseOperations.SendKeyboardChord(MouseOperations.VK_RIGHT);
                    break;
                case FourFingerGesture.SelectUp:
                    MouseOperations.SendKeyboardChord(MouseOperations.VK_UP);
                    break;
                case FourFingerGesture.SelectDown:
                    MouseOperations.SendKeyboardChord(MouseOperations.VK_DOWN);
                    break;
                case FourFingerGesture.ActivateSelection:
                    MouseOperations.SendKeyboardChord(MouseOperations.VK_RETURN);
                    break;
            }
        } finally {
            GestureGate.Release();
        }
    }

    public static Task ShowOverviewAsync() => HandleGestureAsync(FourFingerGesture.TaskView);

    public static void Stop() {
        lock(Sync) {
            Volatile.Write(ref _stopped, true);
            try {
                foreach(var process in Process.GetProcessesByName(CompanionProcessName)) {
                    using(process) {
                        if(!IsOwnedCompanion(process)) continue;
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(1500);
                    }
                }
            } catch(Exception exception) {
                Trace("failed to stop companion: " + exception);
            }
        }
    }
}
