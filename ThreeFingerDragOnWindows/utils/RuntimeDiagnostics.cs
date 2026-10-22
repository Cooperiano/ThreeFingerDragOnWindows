using System;
using System.IO;
using Windows.Storage;

namespace ThreeFingerDragOnWindows.utils;

internal static class RuntimeDiagnostics {
    private static readonly object Sync = new();
    internal static string Folder {
        get {
            try { return ApplicationData.Current.LocalFolder.Path; }
            catch {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ThreeFingerDragOnWindowsPreview");
            }
        }
    }
    internal static string PidPath => Path.Combine(Folder, "runtime.pid");
    internal static string QuitPath => Path.Combine(Folder, "intentional-exit.flag");

    internal static void Write(string category, string message) {
        try {
            lock(Sync) {
                Directory.CreateDirectory(Folder);
                var path = Path.Combine(Folder, "runtime.log");
                if(File.Exists(path) && new FileInfo(path).Length > 1_000_000)
                    File.Move(path, path + ".previous", overwrite: true);
                File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] [{Environment.ProcessId}] {category}: {message}{Environment.NewLine}");
            }
        } catch { }
    }

    internal static void RegisterProcess() {
        Directory.CreateDirectory(Folder);
        File.Delete(QuitPath);
        File.WriteAllText(PidPath, Environment.ProcessId.ToString());
        Write("startup", $"version={typeof(RuntimeDiagnostics).Assembly.GetName().Version}; path={Environment.ProcessPath}");
    }

    internal static void MarkIntentionalExit() {
        try { File.WriteAllText(QuitPath, Environment.ProcessId.ToString()); } catch { }
        Write("shutdown", "user requested exit");
    }
}
