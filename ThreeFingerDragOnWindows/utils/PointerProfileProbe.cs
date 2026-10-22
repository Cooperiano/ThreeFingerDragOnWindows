#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using TouchpadControl.Pointer;

namespace ThreeFingerDragOnWindows.utils;

/// <summary>Opt-in native API diagnostic, not a substitute for physical input acceptance.</summary>
internal static class PointerProfileProbe
{
    internal static async Task<int> RunAsync(bool roundtrip)
    {
        var backend = new WindowsPointerProfileBackend();
        var transaction = new PointerProfileTransaction(backend);
        var output = Path.Combine(RuntimeDiagnostics.Folder,
            roundtrip ? "pointer-native-roundtrip.json" : "pointer-native-read.json");
        Directory.CreateDirectory(RuntimeDiagnostics.Folder);
        try
        {
            var before = await Task.Run(backend.Read);
            var unrelated = backend.ReadUnrelatedTouchpadSettings();
            if(!roundtrip)
            {
                File.WriteAllText(output, JsonSerializer.Serialize(new {
                    Success = true, Transport = backend.Transport, Profile = before,
                    UnrelatedSettings = unrelated, Version = typeof(PointerProfileProbe).Assembly.GetName().Version?.ToString()
                }));
                return 0;
            }
            var target = ThreeFingerDragOnWindows.gestures.PointerAccelerationProfile.WindowsNative;
            PointerProfileResult? changed = null;
            PointerProfileResult restored;
            string after;
            try { changed = await transaction.ApplyAsync(target); }
            finally
            {
                // Always restore the live pre-test values, not defaults or the
                // application's older saved backup.
                restored = await transaction.ApplyAsync(before);
                after = backend.ReadUnrelatedTouchpadSettings();
            }
            var success = changed?.Success == true && restored.Success && unrelated == after;
            File.WriteAllText(output, JsonSerializer.Serialize(new {
                Success = success, Transport = backend.Transport, Before = before, Target = target,
                Changed = changed, Restored = restored, UnrelatedSettingsPreserved = unrelated == after,
                Version = typeof(PointerProfileProbe).Assembly.GetName().Version?.ToString()
            }));
            return success ? 0 : 1;
        }
        catch(Exception error)
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new { Success = false, Error = error.Message }));
            return 1;
        }
    }
}
