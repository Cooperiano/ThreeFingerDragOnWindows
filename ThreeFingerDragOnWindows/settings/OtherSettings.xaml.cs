using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ThreeFingerDragOnWindows.utils;
using ThreeFingerDragOnWindows.localization;

namespace ThreeFingerDragOnWindows.settings;

public sealed partial class OtherSettings
{
    private readonly Timer _timer = new(1000);
    private bool _stopped;
    private bool _initializingLanguage = true;
    private bool _refreshingControls;
    private bool _startupBusy;
    private bool _queryBusy;
    private bool _logsBusy;
    private StartupStatus _startupState;
    private string _startupFailureMessage;

    public OtherSettings()
    {
        InitializeComponent();
        LanguagePicker.SelectedItem = LanguagePicker.Items.Cast<ComboBoxItem>()
            .First(item => (string)item.Tag == UiStrings.Choice);
        _initializingLanguage = false;
        VersionText.Text = UiStrings.Format("Version {0}", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!);
        RefreshPreferenceControls();
        RunAtStartup.IsEnabled = false;
        RunElevated.IsEnabled = false;
        ElevatedStartupInformation.Visibility = Visibility.Collapsed;
        ElevatedStatus.Title = Utils.IsAppRunningAsAdministrator()
            ? UiStrings.Get("The app is currently elevated (running with administrator privileges).")
            : UiStrings.Get("The app is currently not elevated (not running with administrator privileges).");
        if(Utils.IsAppRunningAsAdministrator()) ElevatedStatus.Severity = InfoBarSeverity.Success;
        SettingsData.PersistenceChanged += OnPersistenceChanged;
        UpdateStartupStatus();
        _timer.Elapsed += (_, _) => DispatcherQueue.TryEnqueue(UpdateStartupStatus);
        _timer.AutoReset = true;
        _timer.Start();
        Unloaded += (_, _) => StopUpdates();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        StopUpdates();
    }

    private void LanguagePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if(_initializingLanguage || _stopped || LanguagePicker.SelectedItem is not ComboBoxItem item) return;
        if(!_startupBusy && !StartupManager.IsBusy && App.SettingsWindow?.TryChangeLanguage((string)item.Tag) == true) return;
        _initializingLanguage = true;
        LanguagePicker.SelectedItem = LanguagePicker.Items.Cast<ComboBoxItem>()
            .First(option => (string)option.Tag == UiStrings.Choice);
        _initializingLanguage = false;
        if(_startupBusy || StartupManager.IsBusy) return;
        LanguageStatus.Title = UiStrings.Get("Could not save the language. Your previous choice is unchanged.");
        LanguageStatus.IsOpen = true;
    }

    public void StopUpdates()
    {
        if(_stopped) return;
        _stopped = true;
        _timer.Stop();
        _timer.Dispose();
        SettingsData.PersistenceChanged -= OnPersistenceChanged;
    }

    private void OnPersistenceChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if(!_stopped) RefreshPreferenceControls();
    });

    private void RefreshPreferenceControls()
    {
        _refreshingControls = true;
        try
        {
            RunElevated.IsOn = App.SettingsData.RunElevated;
            RecordLogs.IsOn = App.SettingsData.RecordLogs;
        }
        finally { _refreshingControls = false; }
    }

    private void SetStartupToggle(bool enabled)
    {
        _refreshingControls = true;
        try { RunAtStartup.IsOn = enabled; }
        finally { _refreshingControls = false; }
    }

    private void SetStartupBusy(bool busy)
    {
        _startupBusy = busy;
        if(_stopped) return;
        RunAtStartup.IsEnabled = !busy && _startupState != null && !StartupManager.IsSuiteManaged;
        RunElevated.IsEnabled = !busy && _startupState != null;
        LanguagePicker.IsEnabled = !busy && !StartupManager.IsBusy;
        if(busy)
        {
            _startupFailureMessage = null;
            StartupStatus.Title = UiStrings.Get("Checking and saving startup settings…");
            StartupStatus.Severity = InfoBarSeverity.Informational;
        }
    }

    private async void UpdateStartupStatus()
    {
        if(_stopped || _startupBusy || _queryBusy) return;
        if(StartupManager.IsBusy)
        {
            ShowStartupBusyIndicators();
            return;
        }
        _queryBusy = true;
        try
        {
            StartupStatus state = await StartupManager.GetStatusAsync();
            if(_stopped || _startupBusy) return;
            if(StartupManager.IsBusy)
            {
                ShowStartupBusyIndicators();
                return;
            }
            _startupState = state;
            SetStartupToggle(state.Enabled);
            RunAtStartup.IsEnabled = !StartupManager.IsSuiteManaged;
            RunElevated.IsEnabled = true;
            LanguagePicker.IsEnabled = true;
            if(StartupManager.IsSuiteManaged)
            {
                StartupStatus.Title = UiStrings.Get("Sign-in startup and component switches are managed in Seelen Desktop Suite settings.");
                StartupStatus.Severity = InfoBarSeverity.Informational;
            }
            else if(_startupFailureMessage != null || StartupManager.LastChangeFailed)
            {
                StartupStatus.Title = UiStrings.Get(_startupFailureMessage
                    ?? "Could not save startup settings. Check the current state before trying again.");
                StartupStatus.Severity = InfoBarSeverity.Error;
            }
            else if(state.HasLegacyEnabled || (state.CanonicalEnabled && (!state.ActionValid || state.Elevated != App.SettingsData.RunElevated)))
            {
                StartupStatus.Title = UiStrings.Get("Startup entries do not agree. Turn startup off and on to use one verified Touchpad Control task.");
                StartupStatus.Severity = InfoBarSeverity.Warning;
            }
            else
            {
                StartupStatus.Title = UiStrings.Get(state.Enabled
                    ? "Touchpad Control will start quietly at sign-in. The registered task has been verified."
                    : "Startup is off. Touchpad Control will not start automatically at sign-in.");
                StartupStatus.Severity = state.Enabled ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
            }
        }
        catch(Exception exception)
        {
            if(_stopped) return;
            RuntimeDiagnostics.Write("startup-status", exception.ToString());
            StartupStatus.Title = UiStrings.Get("Could not read startup settings. No changes were made.");
            StartupStatus.Severity = InfoBarSeverity.Error;
            RunAtStartup.IsEnabled = false;
            RunElevated.IsEnabled = false;
            LanguagePicker.IsEnabled = true;
        }
        finally { _queryBusy = false; }
    }

    private async void RunAtStartup_Toggled(object sender, RoutedEventArgs e)
    {
        if(_refreshingControls || _stopped || _startupBusy || _startupState == null) return;
        if(StartupManager.IsBusy)
        {
            SetStartupToggle(_startupState.Enabled);
            ShowStartupBusyIndicators();
            return;
        }
        bool desired = RunAtStartup.IsOn;
        if(desired == _startupState.Enabled) return;
        bool previous = _startupState.Enabled;
        SetStartupBusy(true);
        try
        {
            bool needsAdministrator = App.SettingsData.RunElevated || _startupState.Elevated || _startupState.LegacyElevatedEnabled;
            if(needsAdministrator && !Utils.IsAppRunningAsAdministrator())
            {
                SetStartupToggle(previous);
                bool restarted = App.RestartElevated(desired
                    ? SettingsData.StartupActionType.ENABLE_ELEVATED_STARTUP
                    : SettingsData.StartupActionType.DISABLE_ELEVATED_STARTUP);
                if(!restarted) ShowElevationFailure();
                return;
            }
            await StartupManager.SetStartupAsync(desired, App.SettingsData.RunElevated);
            _startupState = await StartupManager.GetStatusAsync();
            if(!_stopped) SetStartupToggle(_startupState.Enabled);
        }
        catch(Exception exception)
        {
            RuntimeDiagnostics.Write("startup-change", exception.ToString());
            if(!_stopped) SetStartupToggle(previous);
            ShowStartupFailure("Could not save startup settings. Check the current state before trying again.");
        }
        finally { SetStartupBusy(false); }
    }

    private async void RunElevated_Toggled(object sender, RoutedEventArgs e)
    {
        if(_refreshingControls || _stopped || _startupBusy || _startupState == null) return;
        if(StartupManager.IsBusy)
        {
            RefreshPreferenceControls();
            ShowStartupBusyIndicators();
            return;
        }
        bool desired = RunElevated.IsOn;
        bool previous = App.SettingsData.RunElevated;
        if(desired == previous) return;
        SetStartupBusy(true);
        try
        {
            if(!Utils.IsAppRunningAsAdministrator() && (desired || _startupState.Elevated || _startupState.LegacyElevatedEnabled))
            {
                RefreshPreferenceControls();
                SettingsData.StartupActionType action = !_startupState.Enabled ? SettingsData.StartupActionType.NONE
                    : desired ? SettingsData.StartupActionType.ENABLE_ELEVATED_RUN_WITH_STARTUP
                    : SettingsData.StartupActionType.DISABLE_ELEVATED_RUN_WITH_STARTUP;
                bool restarted = App.RestartElevated(action,
                    requestedRunElevated: action == SettingsData.StartupActionType.NONE ? desired : null);
                if(!restarted) ShowElevationFailure();
                return;
            }
            Action commit = () =>
            {
                var result = App.SettingsData.TryCommit(settings => settings.RunElevated = desired);
                if(!result.Success) throw new IOException(result.Error);
            };
            if(_startupState.Enabled) await StartupManager.SetStartupAsync(true, desired, commit);
            else commit();
            RefreshPreferenceControls();
            if(!desired && Utils.IsAppRunningAsAdministrator())
            {
                ElevatedStatus.Title = UiStrings.Get("Administrator preference saved. Restart Touchpad Control to run without administrator privileges.");
                ElevatedStatus.Severity = InfoBarSeverity.Informational;
            }
        }
        catch(Exception exception)
        {
            RuntimeDiagnostics.Write("elevation-change", exception.ToString());
            if(!_stopped) RefreshPreferenceControls();
            ShowStartupFailure("Could not save startup settings. Check the current state before trying again.");
        }
        finally { SetStartupBusy(false); }
    }

    private void ShowStartupFailure(string message)
    {
        if(_stopped) return;
        _startupFailureMessage = message;
        StartupStatus.Title = UiStrings.Get(message);
        StartupStatus.Severity = InfoBarSeverity.Error;
    }

    private void ShowStartupBusyIndicators()
    {
        if(_stopped) return;
        RunAtStartup.IsEnabled = false;
        RunElevated.IsEnabled = false;
        LanguagePicker.IsEnabled = false;
        StartupStatus.Title = UiStrings.Get("Checking and saving startup settings…");
        StartupStatus.Severity = InfoBarSeverity.Informational;
    }

    private void ShowElevationFailure() => ShowStartupFailure(App.LastElevationFailureRestored
        ? "Administrator approval was canceled or failed. Your previous settings are unchanged."
        : "Could not save startup settings. Check the current state before trying again.");

    private void RecordLogs_Toggled(object sender, RoutedEventArgs e)
    {
        if(_refreshingControls || _stopped) return;
        bool desired = RecordLogs.IsOn;
        if(desired == App.SettingsData.RecordLogs) return;
        var result = App.SettingsData.TryCommit(settings => settings.RecordLogs = desired);
        if(result.Success) return;
        RefreshPreferenceControls();
        LogStatus.Title = UiStrings.Get("Could not save logging settings. Your previous choice is unchanged.");
        LogStatus.Message = "";
        LogStatus.Severity = InfoBarSeverity.Error;
        LogStatus.IsOpen = true;
    }

    private async void SaveLogsButton_Click(object sender, RoutedEventArgs e)
    {
        if(_logsBusy || _stopped) return;
        _logsBusy = true;
        SaveLogsButton.IsEnabled = false;
        LogStatus.Message = "";
        try
        {
            string path = Path.Combine(KnownFolders.GetPath(KnownFolder.Downloads),
                "TouchpadControl-Logs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".txt");
            await Logger.ExportLogsAsync(path);
            if(_stopped) return;
            LogStatus.Title = UiStrings.Get("Logs saved");
            LogStatus.Message = UiStrings.Get("The logs have been saved to the Downloads folder.");
            LogStatus.Severity = InfoBarSeverity.Success;
            LogStatus.IsOpen = true;
        }
        catch(Exception exception)
        {
            RuntimeDiagnostics.Write("log-export", exception.ToString());
            if(_stopped) return;
            LogStatus.Title = UiStrings.Get("Could not save the log file. Check the Downloads folder and try again.");
            LogStatus.Message = "";
            LogStatus.Severity = InfoBarSeverity.Error;
            LogStatus.IsOpen = true;
        }
        finally
        {
            _logsBusy = false;
            if(!_stopped) SaveLogsButton.IsEnabled = true;
        }
    }
}
