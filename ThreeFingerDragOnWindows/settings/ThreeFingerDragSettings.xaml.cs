using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ThreeFingerDragOnWindows.localization;
using TouchpadControl.Pointer;
using TouchpadControl.Settings;
using ThreeFingerDragOnWindows.touchpad;
using ThreeFingerDragOnWindows.utils;

namespace ThreeFingerDragOnWindows.settings;

public sealed partial class ThreeFingerDragSettings : INotifyPropertyChanged{
    private ObservableCollection<MouseSpeedSettings> _deviceSettings;
    public ThreeFingerDragSettings(){
        InitializeComponent();
        Loaded += (_, _) => {
            SettingsData.PersistenceChanged += OnPersistenceChanged;
            OnPersistenceChanged();
        };
        Unloaded += (_, _) => {
            SettingsData.PersistenceChanged -= OnPersistenceChanged;
        };
    }

    private void OpenSettings(object sender, RoutedEventArgs e){
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:devices-touchpad"));
    }

    private async void OpenOverview(object sender, RoutedEventArgs e){
        try { await MissionControlBridge.ShowOverviewAsync(); }
        catch(Exception exception) { RuntimeDiagnostics.Write("overview-test", exception.ToString()); }
    }


    public event PropertyChangedEventHandler PropertyChanged;
    public string NativeMotionStatusProperty => UiStrings.Get(
        !NativeTouchpadSink.IsSupported ? "S192" : TouchpadHelper.GetAllDeivceInfos().All(d => d.Geometry.IsValid)
            && TouchpadHelper.GetAllDeivceInfos().Count > 0 ? "S191" : "S193");

    private void OnPropertyChanged(string propertyName){
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }


    public bool EnabledProperty {
        get{ return App.SettingsData.ThreeFingerDrag; }
        set{
            if(value == App.SettingsData.ThreeFingerDrag) return;
            if(App.SettingsData.TryCommit(s => s.ThreeFingerDrag = value).Success)
                App.Instance.HandlerWindow?.OnEnabledSettingsChanged();
            OnPropertyChanged(nameof(EnabledProperty));
        }
    }

    public bool FourFingerEnabledProperty {
        get{ return App.SettingsData.FourFingerGestures; }
        set{
            if(value == App.SettingsData.FourFingerGestures) return;
            if(App.SettingsData.TryCommit(s => s.FourFingerGestures = value).Success)
                App.Instance.HandlerWindow?.OnEnabledSettingsChanged();
            OnPropertyChanged(nameof(FourFingerEnabledProperty));
        }
    }

    public double FourFingerSwipeThresholdProperty {
        get{ return App.SettingsData.FourFingerSwipeThreshold; }
        set{
            if(value == App.SettingsData.FourFingerSwipeThreshold) return;
            CommitNumber(value, 40, 1000, (s, n) => s.FourFingerSwipeThreshold = n, nameof(FourFingerSwipeThresholdProperty));
        }
    }

    public string SaveStatusMessageProperty => UiStrings.Get(SettingsData.LastPersistenceResult?.Success != false
        ? "Changes are saved as you edit. They apply without restarting."
        : "The change could not be saved. Your previous settings were kept. Check the value or try again.");
    public InfoBarSeverity SaveStatusSeverityProperty => SettingsData.LastPersistenceResult?.Success != false
        ? InfoBarSeverity.Informational : InfoBarSeverity.Error;

    private void OnPersistenceChanged() {
        if(!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(OnPersistenceChanged); return; }
        OnPropertyChanged(nameof(SaveStatusMessageProperty));
        OnPropertyChanged(nameof(SaveStatusSeverityProperty));
        OnPropertyChanged(nameof(EnabledProperty));
        OnPropertyChanged(nameof(FourFingerEnabledProperty));
        OnPropertyChanged(nameof(FourFingerSwipeThresholdProperty));
        OnPropertyChanged(nameof(ButtonTypeProperty));
        OnPropertyChanged(nameof(StartDragThresholdProperty));
        OnPropertyChanged(nameof(MaxFingerMoveDistanceProperty));
    }

    private void CommitNumber(double value, int minimum, int maximum, Action<SettingsData, int> edit, string property) {
        // Validate the NumberBox's double before conversion; an empty box is NaN.
        if(!SettingsValueRules.TryInt(value, minimum, maximum, out int number)) {
            SettingsData.RejectEdit(UiStrings.Get("Enter a valid number in the allowed range."));
            OnPropertyChanged(property);
            return;
        }
        App.SettingsData.TryCommit(s => edit(s, number));
        OnPropertyChanged(property);
    }

    public int ButtonTypeProperty {
        get{ return (int) App.SettingsData.ThreeFingerDragButton; }
        set{
            if(value == (int)App.SettingsData.ThreeFingerDragButton) return;
            if(value < 0 || value > 3) { OnPropertyChanged(nameof(ButtonTypeProperty)); return; }
            App.SettingsData.TryCommit(s => s.ThreeFingerDragButton = (SettingsData.ThreeFingerDragButtonType)value);
            OnPropertyChanged(nameof(ButtonTypeProperty));
        }
    }

    
    public ObservableCollection<MouseSpeedSettings> MouseSpeedSettingItems
    {
        get
        {
            if(_deviceSettings != null) return _deviceSettings;
            ObservableCollection<MouseSpeedSettings> settings = new ObservableCollection<MouseSpeedSettings>(new Collection<MouseSpeedSettings>());
            List<TouchpadDeviceInfo> allDeviceInfos = TouchpadHelper.GetAllDeivceInfos();
            foreach (TouchpadDeviceInfo device in allDeviceInfos)
            {
                var mouseSetting = new MouseSpeedSettings(
                    App.SettingsData.ThreeFingerDeviceDragCursorConfigs.GetValueOrDefault(device.deviceId,
                        new SettingsData.ThreeFingerDragConfig()), device);
                settings.Add(mouseSetting);
            }

            _deviceSettings = settings;
            return _deviceSettings;
        }
    }
    

    public double StartDragThresholdMaximum => Math.Max(200, App.SettingsData.ThreeFingerDragStartThreshold);
    public double MaxFingerMoveDistanceMaximum => Math.Max(200, App.SettingsData.ThreeFingerDragMaxFingerMoveDistance);

    public double StartDragThresholdProperty {
        get{ return App.SettingsData.ThreeFingerDragStartThreshold; }
        set{
            if(value == App.SettingsData.ThreeFingerDragStartThreshold) return;
            CommitNumber(value, 0, 1000, (s, n) => {
                s.ThreeFingerDragStartThreshold = n;
                s.ThreeFingerDragStopThreshold = Math.Min(s.ThreeFingerDragStopThreshold, n);
            }, nameof(StartDragThresholdProperty));
        }
    }
    public double MaxFingerMoveDistanceProperty {
        get{ return App.SettingsData.ThreeFingerDragMaxFingerMoveDistance; }
        set{
            if(value == App.SettingsData.ThreeFingerDragMaxFingerMoveDistance) return;
            CommitNumber(value, 0, 10000, (s, n) => s.ThreeFingerDragMaxFingerMoveDistance = n, nameof(MaxFingerMoveDistanceProperty));
        }
    }
}
