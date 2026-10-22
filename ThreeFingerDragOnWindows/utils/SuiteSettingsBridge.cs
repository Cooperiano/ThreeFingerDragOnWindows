using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using TouchpadControl.Pointer;
using TouchpadControl.Settings;

namespace ThreeFingerDragOnWindows.utils;

internal static class SuiteSettingsBridge {
    internal static bool TryOpenHost() {
        try {
            var local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var pointer=Path.Combine(local,"Programs","Stats Desktop","suite-host.txt");
            if(!File.Exists(pointer)) return false;
            var host=File.ReadAllText(pointer).Trim();
            if(!Path.IsPathFullyQualified(host) || !string.Equals(Path.GetFileName(host),"seelen-ui.exe",StringComparison.OrdinalIgnoreCase) || !File.Exists(host)) return false;
            Process.Start(new ProcessStartInfo(host) { UseShellExecute=false, CreateNoWindow=true,
                WorkingDirectory=Path.GetDirectoryName(host), Arguments="--settings-route touchpad" });
            return true;
        } catch(Exception error) { RuntimeDiagnostics.Write("suite-settings",error.Message);return false; }
    }

    // Runs before WinUI, input initialization, elevation and watchdog.
    internal static int Tracking(string[] args) {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        string requestId=args.Length>1?args[1]:"";
        try {
            _=Guid.ParseExact(requestId,"N");
            using var gate=new Mutex(false,"Local\\TouchpadControl.UnifiedTracking");
            bool acquired;
            try { acquired=gate.WaitOne(3000); } catch(AbandonedMutexException) { acquired=true; }
            if(!acquired) throw new TimeoutException("Another tracking change is still running.");
            try {
                TrackingSettings? expected=null,desired=null;
                bool confirmed=false;
                if(args.Length==7 && args[0]=="--suite-tracking-apply") {
                    expected=new(int.Parse(args[2]),bool.Parse(args[3]));
                    desired=new(int.Parse(args[4]),bool.Parse(args[5]));confirmed=bool.Parse(args[6]);
                } else if(args.Length!=2 || args[0]!="--suite-tracking-read") {
                    throw new ArgumentException("Invalid tracking request.");
                }
                var result=SuiteTrackingRequest.Execute(new UnifiedTrackingBackend(),expected,desired,confirmed);
                WriteResult(requestId,result);return result.Success?0:1;
            } finally { gate.ReleaseMutex(); }
        } catch(Exception error) {
            WriteResult(requestId,new(false,null,false,error.Message));return 1;
        }
    }
    private static void WriteResult(string requestId,TrackingChangeResult result) {
        Directory.CreateDirectory(RuntimeDiagnostics.Folder);
        AtomicSettingsFile.Write(Path.Combine(RuntimeDiagnostics.Folder,"suite-tracking.json"),JsonSerializer.Serialize(new { RequestId=requestId, Result=result }));
    }
}
