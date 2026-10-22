using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace ThreeFingerDragOnWindows.utils;

/// <summary>Runs independently of WinUI so a native UI failure cannot stop recovery.</summary>
internal static class AppWatchdog {
    internal static void EnsureStarted() {
        try {
            using var process = Process.Start(new ProcessStartInfo {
                FileName = Utils.GetAppPath(), Arguments = "--watchdog",
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            });
        } catch(Exception exception) { RuntimeDiagnostics.Write("watchdog-launch", exception.ToString()); }
    }

    internal static int Run() {
        using var gate = new Mutex(false, "Local\\TouchpadControl-Watchdog-" + Process.GetCurrentProcess().SessionId);
        bool acquired;
        try { acquired = gate.WaitOne(0); }
        catch(AbandonedMutexException) { acquired = true; }
        if(!acquired) return 0;
        try {
            RuntimeDiagnostics.Write("watchdog", "started");
            var consecutiveFailures = 0;
            while(true) {
                var startedAt = Stopwatch.StartNew();
                try {
                    using var child = FindRegisteredProcess() ?? Process.Start(new ProcessStartInfo {
                        FileName = Utils.GetAppPath(), Arguments = "--background",
                        UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                    });
                    if(child is null) throw new InvalidOperationException("Application did not launch.");
                    RuntimeDiagnostics.Write("watchdog", $"observing pid={child.Id}");
                    child.WaitForExit();
                    if(File.Exists(RuntimeDiagnostics.QuitPath)
                       && File.ReadAllText(RuntimeDiagnostics.QuitPath).Trim() == child.Id.ToString()) {
                        RuntimeDiagnostics.Write("watchdog", "intentional exit; recovery stopped");
                        return 0;
                    }
                    using var active = FindRegisteredProcess();
                    if(child.ExitCode == 0 && active is null) {
                        RuntimeDiagnostics.Write("watchdog", "clean exit; recovery stopped");
                        return 0;
                    }
                    if(active is not null) continue;
                    RuntimeDiagnostics.Write("watchdog", $"unexpected exit=0x{child.ExitCode:X8}; restarting");
                } catch(Exception exception) { RuntimeDiagnostics.Write("watchdog-error", exception.ToString()); }
                consecutiveFailures = startedAt.Elapsed.TotalSeconds >= 60 ? 1 : consecutiveFailures + 1;
                Thread.Sleep(Math.Min(30_000, 2_000 * Math.Min(consecutiveFailures, 15)));
            }
        } finally { gate.ReleaseMutex(); }
    }

    private static Process FindRegisteredProcess() {
        try {
            if(!File.Exists(RuntimeDiagnostics.PidPath)
               || !int.TryParse(File.ReadAllText(RuntimeDiagnostics.PidPath), out var pid)
               || pid == Environment.ProcessId) return null;
            var process = Process.GetProcessById(pid);
            try {
                if(!process.HasExited && process.SessionId == Process.GetCurrentProcess().SessionId
                   && string.Equals(process.MainModule?.FileName, Utils.GetAppPath(), StringComparison.OrdinalIgnoreCase))
                    return process;
            } catch { }
            process.Dispose();
        } catch { }
        return null;
    }
}
