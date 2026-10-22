using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using TouchpadControl;
using Microsoft.UI.Dispatching;
using ThreeFingerDragEngine.utils;
using ThreeFingerDragOnWindows.settings;
using ThreeFingerDragOnWindows.touchpad;
using ThreeFingerDragOnWindows.utils;

namespace ThreeFingerDragOnWindows;

public partial class App {

    public readonly DispatcherQueue DispatcherQueue;

    public static App Instance;
    public static SettingsData SettingsData;
    public static SettingsWindow SettingsWindow;

    public HandlerWindow HandlerWindow;
    internal bool IsExiting { get; private set; }
    internal static bool LastElevationFailureRestored { get; private set; } = true;

    public App(){
        Instance = this;
        DispatcherQueue = DispatcherQueue.GetForCurrentThread();
        UnhandledException += (_, eventArgs) => {
            RuntimeDiagnostics.Write("xaml-unhandled", eventArgs.Exception.ToString());
        };
        SettingsData = SettingsData.load();
        localization.UiStrings.SetLanguage(SettingsData.Language);
        RuntimeDiagnostics.Write("language", $"choice={localization.UiStrings.Choice}; resolved={localization.UiStrings.ResolvedLanguage}");

        if(SettingsData.RunElevated && !Utils.IsAppRunningAsAdministrator()){
            if(RestartElevated(backgroundLaunch: Program.InitialLaunchIntent == LaunchIntent.Background)) return;
        }
        Logger.Log("Starting ThreeFingerDragOnWindows...");
        InitializeComponent();
        // The unified tracking control reads Windows on demand. Do not verify
        // or reapply the retired fixed-speed preset on startup. Its preferences
        // remain untouched so the previous installed version can be restored.
        if(SettingsData.FourFingerGestures){
            // Warm the hidden overview companion before the first gesture so a
            // four-finger swipe never pays process-startup latency.
            _ = MissionControlBridge.EnsureRunningAsync();
        }

        bool openOtherSettings = false;
        Logger.Log("StartupAction: " + SettingsData.StartupAction);
        if(SettingsData.StartupAction != SettingsData.StartupActionType.NONE){
            openOtherSettings = true;
            _ = CompleteStartupActionAsync(SettingsData.StartupAction);
        }

        if(SettingsData.DidVersionChanged || openOtherSettings){
            Logger.Log("First run detected, or StartupAction not NONE.");
            OpenSettingsWindow(openOtherSettings);
            Utils.runOnMainThreadAfter(3000, () => {
                if(!IsExiting) HandlerWindow = new HandlerWindow(this);
            });
        } else{
            HandlerWindow = new HandlerWindow(this);
        }
        RuntimeDiagnostics.RegisterProcess();
        if(!Environment.CommandLine.Contains("--preview", StringComparison.OrdinalIgnoreCase))
            AppWatchdog.EnsureStarted();
    }


    public void OpenSettingsWindow(bool openOtherSettings = false){
        if(SuiteSettingsBridge.TryOpenHost()) return;
        Logger.Log("Opening SettingsWindow...");
        RuntimeDiagnostics.Write("settings-window", "creating or reopening");
        SettingsWindow ??= new SettingsWindow(this, openOtherSettings);
        RuntimeDiagnostics.Write("settings-window", "activating");
        SettingsWindow.Activate();
        RuntimeDiagnostics.Write("settings-window", "activated");
        Utils.FocusWindow(SettingsWindow);
    }

    public void OnClosePrefsWindow(){
        SettingsWindow = null;
    }

    public void Quit(){
        if(IsExiting) return;
        IsExiting = true;
        RuntimeDiagnostics.MarkIntentionalExit();
        MissionControlBridge.Stop();
        HandlerWindow?.Close();
        SettingsWindow?.Close();
        // An elevated restart may happen before any window exists. Queue Exit so
        // the constructor can return before the Application.Start message loop ends.
        DispatcherQueue.TryEnqueue(() => Exit());
    }

    public static bool RestartElevated(SettingsData.StartupActionType startupActionType = SettingsData.StartupActionType.NONE,
        bool? requestedRunElevated = null, bool backgroundLaunch = false){
        LastElevationFailureRestored = true;
        var previousAction = SettingsData.StartupAction;
        bool previousElevated = SettingsData.RunElevated;
        var preparation = SettingsData.TryCommit(settings => {
            settings.StartupAction = startupActionType;
            if(requestedRunElevated.HasValue) settings.RunElevated = requestedRunElevated.Value;
        });
        if(!preparation.Success){
            RuntimeDiagnostics.Write("elevation-prepare", preparation.Error);
            return false;
        }

        var path = Utils.GetElevatorPath();
        Logger.Log("Running the Elevator app at " + path);
        ProcessStartInfo processInfo = new ProcessStartInfo{
            Verb = "runas",
            UseShellExecute = true,
            FileName = path,
            Arguments = backgroundLaunch ? "--background" : ""
        };

        try{
            Process.Start(processInfo);
        } catch(Exception ex){
            // Probably the user canceled the UAC window,
            var restored = SettingsData.TryCommit(settings => {
                settings.StartupAction = previousAction;
                settings.RunElevated = previousElevated;
            });
            LastElevationFailureRestored = restored.Success;
            if(!restored.Success) RuntimeDiagnostics.Write("elevation-restore", restored.Error);
            Logger.Log(ex.ToString());
            return false;
        }
        // Close app
        Instance.Quit();
        return true;
    }

    private static async Task CompleteStartupActionAsync(SettingsData.StartupActionType action){
        try {
            if(!Enum.IsDefined(action) || action == SettingsData.StartupActionType.NONE)
                throw new InvalidOperationException("The saved startup action is invalid; no task was changed.");
            if(!Utils.IsAppRunningAsAdministrator())
                throw new UnauthorizedAccessException("The startup action did not receive administrator approval.");
            bool enabled = action != SettingsData.StartupActionType.DISABLE_ELEVATED_STARTUP;
            bool elevated = action == SettingsData.StartupActionType.DISABLE_ELEVATED_RUN_WITH_STARTUP
                ? false : SettingsData.RunElevated;
            if(action == SettingsData.StartupActionType.ENABLE_ELEVATED_RUN_WITH_STARTUP) elevated = true;
            await StartupManager.SetStartupAsync(enabled, elevated, () => {
                var result = SettingsData.TryCommit(settings => {
                    if(action == SettingsData.StartupActionType.ENABLE_ELEVATED_RUN_WITH_STARTUP) settings.RunElevated = true;
                    if(action == SettingsData.StartupActionType.DISABLE_ELEVATED_RUN_WITH_STARTUP) settings.RunElevated = false;
                    settings.StartupAction = SettingsData.StartupActionType.NONE;
                });
                if(!result.Success) throw new IOException(result.Error);
            });
        } catch(Exception exception){
            StartupManager.RecordFailure(exception);
            RuntimeDiagnostics.Write("startup-action", exception.ToString());
            // A failed/canceled operation must not be retried silently at every subsequent launch.
            var cleared = SettingsData.TryCommit(settings => settings.StartupAction = SettingsData.StartupActionType.NONE);
            if(!cleared.Success) RuntimeDiagnostics.Write("startup-action-clear", cleared.Error);
        }
    }

    public void OnTouchpadContact(IntPtr currentDevice, TouchpadContact[] contacts){
        SettingsWindow?.OnTouchpadContact(currentDevice, contacts);
    }
    public void OnTouchpadInitialized(){
        SettingsWindow?.OnTouchpadInitialized();
    }
}
