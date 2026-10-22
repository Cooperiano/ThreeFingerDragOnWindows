using System;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ThreeFingerDragOnWindows.utils;
using ThreeFingerDragOnWindows.localization;

namespace ThreeFingerDragOnWindows.settings;

public sealed partial class TouchpadSettings {

    public TouchpadSettings(){
        InitializeComponent();
        if(App.Instance.HandlerWindow == null || !App.Instance.HandlerWindow.TouchpadInitialized){
            Loader.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            TouchpadStatus.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            ContactsDebug.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        } else{
            OnTouchpadInitialized();
        }
    }

    public void UpdateContactsText(string text){
        RawInputText.Text = text;
        LiveInputTitle.Text = UiStrings.Get("Input detected");
        LiveInputSubtitle.Text = UiStrings.Get("Touchpad events are arriving normally.");
        LiveIndicator.Fill = new SolidColorBrush(Colors.ForestGreen);
    }

    private void OpenDragSettings(object sender, RoutedEventArgs e){
        App.SettingsWindow?.NavigateToControls();
    }

    private void ActionRow_SizeChanged(object sender, SizeChangedEventArgs e){
        if(sender is not Microsoft.UI.Xaml.Controls.Grid row) return;
        var action = row.Children.OfType<Microsoft.UI.Xaml.Controls.Button>().FirstOrDefault();
        if(action == null) return;
        bool compact = e.NewSize.Width < 600;
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(action, compact ? 1 : 2);
        Microsoft.UI.Xaml.Controls.Grid.SetRow(action, compact ? 1 : 0);
        action.HorizontalAlignment = HorizontalAlignment.Left;
        action.Margin = compact ? new Thickness(0,12,0,4) : new Thickness(0);
    }

    private void OpenWindowsTouchpadSettings(object sender, RoutedEventArgs e){
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:devices-touchpad"));
    }

    public void OnTouchpadInitialized(){
        Loader.Visibility = Visibility.Collapsed;
        TouchpadStatus.Visibility = Visibility.Visible;
        
        if(App.Instance.HandlerWindow.TouchpadExists){
            if(App.Instance.HandlerWindow.InputReceiverInstalled) {
                string deviceInfosString = String.Join("\n", TouchpadHelper.GetAllDeivceInfos().Select(deviceInfo => deviceInfo.ToString()));
                StatusTitleText.Text = UiStrings.Get("Touchpad ready");
                StatusDescriptionText.Text = UiStrings.Get("A compatible precision touchpad is connected and sending input.");
                StatusIcon.Glyph = "\uE73E";
                ConnectionStepDescription.Text = UiStrings.Get("Detected and ready");
                RawInputText.Text = UiStrings.Format("Device: {0}\nNo input received yet.", deviceInfosString);
                ContactsDebug.Visibility = Visibility.Visible;
            } else{
                StatusTitleText.Text = UiStrings.Get("Touchpad found, but input is unavailable");
                StatusDescriptionText.Text = UiStrings.Get("Restart the app and check that another gesture utility is not using the device.");
                StatusIcon.Glyph = "\uE7BA";
                TouchpadStatus.Background = (Brush)Application.Current.Resources["WarningTintBrush"];
                ConnectionStepDescription.Text = UiStrings.Get("Input receiver needs attention");
                ConnectionStepIcon.Glyph = "\uE7BA";
                ContactsDebug.Visibility = Visibility.Collapsed;
            } 
        } else{
            StatusTitleText.Text = UiStrings.Get("No compatible touchpad detected");
            StatusDescriptionText.Text = UiStrings.Get("Connect a Windows Precision Touchpad, then reopen this page.");
            StatusIcon.Glyph = "\uEA39";
            TouchpadStatus.Background = (Brush)Application.Current.Resources["WarningTintBrush"];
            ConnectionStepDescription.Text = UiStrings.Get("Not detected");
            ConnectionStepIcon.Glyph = "\uEA39";
            ContactsDebug.Visibility = Visibility.Collapsed;
        }
    }

}
