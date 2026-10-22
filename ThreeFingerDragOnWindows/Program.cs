using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using ThreeFingerDragOnWindows.utils;
using TouchpadControl;
using Application = Microsoft.UI.Xaml.Application;
using Utils = ThreeFingerDragOnWindows.utils.Utils;

namespace ThreeFingerDragOnWindows;

public class Program {
    private static bool _previewMode;
    private static bool _missionControlMode;
    internal static LaunchIntent InitialLaunchIntent { get; private set; }

    [STAThread]
    static int Main(string[] args){
        if(Array.Exists(args,arg=>arg.StartsWith("--suite-tracking-",StringComparison.Ordinal)))
            return SuiteSettingsBridge.Tracking(args);
        if(Array.Exists(args, arg => arg.Equals("--startup-read", StringComparison.OrdinalIgnoreCase)))
            return ReadStartupDiagnostic();
        if(Array.Exists(args, arg => arg.Equals("--native-motion-read", StringComparison.OrdinalIgnoreCase)))
            return ReadNativeMotionDiagnostic();
        // Explicit diagnostics exercise the installed native backend without
        // creating a second UI, input receiver or watchdog.
        if(Array.Exists(args, arg => arg.Equals("--pointer-read", StringComparison.OrdinalIgnoreCase)))
            return PointerProfileProbe.RunAsync(false).GetAwaiter().GetResult();
        if(Array.Exists(args, arg => arg.Equals("--pointer-roundtrip", StringComparison.OrdinalIgnoreCase)))
            return PointerProfileProbe.RunAsync(true).GetAwaiter().GetResult();
        if(Array.Exists(args, arg => arg.Equals("--watchdog", StringComparison.OrdinalIgnoreCase))
            && !Array.Exists(args, arg => arg.Equals("--exit", StringComparison.OrdinalIgnoreCase)))
            return AppWatchdog.Run();
        // A Task-returning Main produces a compiler-generated entry point that
        // does not carry STAThread. WinUI and its automation providers require
        // the real entry point to initialize the UI in an STA.
        RuntimeDiagnostics.Write("startup-apartment", Thread.CurrentThread.GetApartmentState().ToString());
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            RuntimeDiagnostics.Write("unhandled", eventArgs.ExceptionObject.ToString());
        TaskScheduler.UnobservedTaskException += (_, eventArgs) => {
            RuntimeDiagnostics.Write("background-task", eventArgs.Exception.ToString());
            eventArgs.SetObserved();
        };
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Allows contributors to launch a local build alongside the Store version
        // while reviewing the settings UI.
        _previewMode = Array.Exists(args, arg => arg.Equals("--preview", StringComparison.OrdinalIgnoreCase));
        _missionControlMode = Array.Exists(args, arg => arg.Equals("--mission-control", StringComparison.OrdinalIgnoreCase));
        InitialLaunchIntent = LaunchIntentPolicy.Resolve(args,
            AppInstance.GetCurrent().GetActivatedEventArgs().Kind == ExtendedActivationKind.StartupTask);
        if(LaunchIntentPolicy.ShouldForwardToSuite(InitialLaunchIntent,_previewMode) && SuiteSettingsBridge.TryOpenHost())
            return 0;
        if(InitialLaunchIntent == LaunchIntent.Exit){
            // An exit request must never initialize input, elevation or a watchdog.
            var (target, _) = FindExistingInstance();
            return target == null ? 0 : RedirectActivation(target);
        }
        if(_previewMode){
            StartApp();
            return 0;
        }

        (AppInstance existingInstance, bool existingInstanceIsAdmin) = FindExistingInstance();

        if(existingInstance != null){
            if(Utils.IsAppRunningAsAdministrator() && !existingInstanceIsAdmin && TerminateOldInstance(existingInstance.ProcessId)){
                Logger.Log("Unelevated instance found and killed. Starting the app");
                StartApp();
            } else{
                Logger.Log("Instance found, redirecting activation.");
                return RedirectActivation(existingInstance);
            }
        } else{
            Logger.Log("No instance found, starting the app.");
            StartApp();
        }
        return 0;
    }

    private static int ReadNativeMotionDiagnostic(){
        // Descriptor/settings inspection only. No synthetic contact, mouse
        // button, settings mutation, UI, watchdog or second input receiver.
        try {
            bool detected = TouchpadHelper.Exists();
            var devices = TouchpadHelper.GetAllDeivceInfos();
            var payload = JsonSerializer.Serialize(new {
                Version = typeof(Program).Assembly.GetName().Version?.ToString(),
                Route = "PT_TOUCHPAD", Detected = detected,
                ApiSupported = NativeTouchpadSink.IsSupported,
                Tracking = new UnifiedTrackingBackend().Read(),
                Geometry = devices.ConvertAll(d => d.Geometry),
                PhysicalMotionVerified = false
            });
            Directory.CreateDirectory(RuntimeDiagnostics.Folder);
            File.WriteAllText(Path.Combine(RuntimeDiagnostics.Folder, "native-motion-read.json"), payload);
            Console.WriteLine(payload);
            return 0;
        } catch(Exception error) {
            RuntimeDiagnostics.Write("native-motion-read", error.Message);
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static int ReadStartupDiagnostic(){
        WinRT.ComWrappersSupport.InitializeComWrappers();
        string payload;
        int result;
        try {
            var state = StartupManager.GetStatusAsync().GetAwaiter().GetResult();
            payload = JsonSerializer.Serialize(new {
                Success = true, Version = typeof(StartupManager).Assembly.GetName().Version?.ToString(),
                TaskName = StartupRegistrationPolicy.TaskName, RegisteredAlias = StartupManager.RegisteredAlias, State = state
            });
            result = 0;
        } catch(Exception exception){
            payload = JsonSerializer.Serialize(new { Success = false, Error = exception.Message });
            result = 1;
        }
        try {
            Directory.CreateDirectory(RuntimeDiagnostics.Folder);
            File.WriteAllText(Path.Combine(RuntimeDiagnostics.Folder, "startup-read.json"), payload);
        } catch(Exception exception){
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        Console.WriteLine(payload);
        return result;
    }

    [DllImport("ole32.dll")]
    private static extern int CoWaitForMultipleObjects(uint flags, uint timeout,
        uint handleCount, IntPtr[] handles, out uint index);

    private static int RedirectActivation(AppInstance instance){
        AppActivationArguments args = AppInstance.GetCurrent().GetActivatedEventArgs();
        RuntimeDiagnostics.Write("activation", $"redirect to pid={instance.ProcessId}");
        using var completed = new ManualResetEvent(false);
        // Redirect on a worker while COM continues servicing the STA. Blocking
        // an STA on Task.Result would deadlock an activation marshalled to it.
        Task redirect = Task.Run(async () => {
            try { await instance.RedirectActivationToAsync(args); }
            finally { completed.Set(); }
        });
        int result = CoWaitForMultipleObjects(0, uint.MaxValue, 1,
            new[] { completed.SafeWaitHandle.DangerousGetHandle() }, out _);
        Marshal.ThrowExceptionForHR(result);
        redirect.GetAwaiter().GetResult();
        return 0;
    }

    private static void StartApp(){
        AppInstance.FindOrRegisterForKey(_previewMode
            ? "ThreeFingerDragOnWindows-Preview"
            : "ThreeFingerDragOnWindows-SingleInstance-" + (Utils.IsAppRunningAsAdministrator() ? "Admin" : "User"));
        AppInstance.GetCurrent().Activated += OnActivated;

        Application.Start((p) => {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            App app = new App();
            if(app.IsExiting) return;
            if(InitialLaunchIntent == LaunchIntent.Settings) app.OpenSettingsWindow();
            if(InitialLaunchIntent == LaunchIntent.Overview) _ = MissionControlBridge.ShowOverviewAsync();
        });
    }

    private static (AppInstance, bool) FindExistingInstance(){
        foreach(AppInstance appInstance in AppInstance.GetInstances()){
            if(appInstance.IsCurrent) continue;
            if(appInstance.Key.Equals("ThreeFingerDragOnWindows-SingleInstance-User")){
                return (appInstance, false);
            }

            if(appInstance.Key.Equals("ThreeFingerDragOnWindows-SingleInstance-Admin")){
                return (appInstance, true);
            }
        }

        return (null, false);
    }

    private static void OnActivated(object sender, AppActivationArguments args){
        RuntimeDiagnostics.Write("activation", "received");
        string arguments = args.Data switch {
            Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launchArgs => launchArgs.Arguments,
            Windows.ApplicationModel.Activation.ICommandLineActivatedEventArgs commandArgs => commandArgs.Operation.Arguments,
            _ => ""
        };
        LaunchIntent intent = LaunchIntentPolicy.Resolve(arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            args.Kind == ExtendedActivationKind.StartupTask);
        (Application.Current as App)?.DispatcherQueue.TryEnqueue(() => {
            if(intent == LaunchIntent.Exit) {
                (Application.Current as App)?.Quit();
                return;
            }
            if(intent == LaunchIntent.Overview) {
                _ = MissionControlBridge.ShowOverviewAsync();
                return;
            }
            if(intent == LaunchIntent.Settings) (Application.Current as App)?.OpenSettingsWindow();
        });
    }

    private static bool TerminateOldInstance(uint processId){
        try{
            Process oldInstance = Process.GetProcessById((int) processId);
            oldInstance.Kill();
        } catch(Exception ex){
            Logger.Log(ex.ToString());
            return false;
        }

        return true;
    }
}
