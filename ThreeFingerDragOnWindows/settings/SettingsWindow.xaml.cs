using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Windows.Graphics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ThreeFingerDragOnWindows.utils;
using Windows.ApplicationModel;
using Windows.Foundation.Metadata;
using System.Reflection;
using ThreeFingerDragEngine.utils;
using ThreeFingerDragOnWindows.localization;
using TouchpadControl.Localization;

namespace ThreeFingerDragOnWindows.settings;

public sealed partial class SettingsWindow {
    private readonly App _app;
    private bool _closed;
    private bool _quitDialogOpen;

    /*private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
    {
        var regex = new Regex("[^0-9]+");
        e.Handled = regex.IsMatch(e.Text);
    }*/

    public SettingsWindow(App app, bool openOtherSettings){
        RuntimeDiagnostics.Write("settings-window", "constructor begin");
        _app = app;
        Logger.Log("Starting SettingsWindow...");


        InitializeComponent();
        RuntimeDiagnostics.Write("settings-window", "XAML initialized");
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico"));
        // A focused settings window is easier to scan than a maximized diagnostic surface.
        AppWindow.Resize(new SizeInt32(1180, 780));

        ExtendsContentIntoTitleBar = true; // enable custom titlebar
        SetTitleBar(TitleBar); // set TitleBar element as titlebar

        ApplyShellLanguage();

        NavigationView.SelectedItem = openOtherSettings ? OtherSettings : Touchpad;
        RuntimeDiagnostics.Write("settings-window", "constructor completed");
    }


    private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e){
        if(_closed || e.SelectedItem is null) return;
        Type pageType;
        string header;
        if(e.SelectedItem.Equals(Touchpad)) {
            pageType = typeof(TouchpadSettings);
            header = "Touchpad dashboard";
        } else if(e.SelectedItem.Equals(ThreeFingerDrag)) {
            pageType = typeof(ThreeFingerDragSettings);
            header = "Pointer & gestures";
        } else if(e.SelectedItem.Equals(OtherSettings)) {
            pageType = typeof(OtherSettings);
            header = "System & diagnostics";
        } else return;

        sender.Header = UiStrings.Get(header);
        if(ContentFrame.Content?.GetType() != pageType) {
            ContentFrame.Navigate(pageType, null, e.RecommendedNavigationTransitionInfo);
            // This is a flat settings hierarchy, not an ever-growing browser history.
            ContentFrame.BackStack.Clear();
        }
        if(sender.DisplayMode != NavigationViewDisplayMode.Expanded) sender.IsPaneOpen = false;
    }


    ////////// Close & quit //////////

    public void NavigateToControls(){
        if(!_closed) NavigationView.SelectedItem = ThreeFingerDrag;
    }

    private void ApplyShellLanguage(){
        Touchpad.Content = UiStrings.Get("Touchpad dashboard");
        ThreeFingerDrag.Content = UiStrings.Get("Pointer & gestures");
        OtherSettings.Content = UiStrings.Get("System & diagnostics");
        NavigationView.PaneTitle = UiStrings.Get("Navigation");
        ToolTipService.SetToolTip(Touchpad, Touchpad.Content);
        ToolTipService.SetToolTip(ThreeFingerDrag, ThreeFingerDrag.Content);
        ToolTipService.SetToolTip(OtherSettings, UiStrings.Get("Language"));
        AppMenuLabel.Text = UiStrings.Get("Application menu");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AppMenuButton, AppMenuLabel.Text);
        ToolTipService.SetToolTip(AppMenuButton, AppMenuLabel.Text);
        CloseApp.Text = UiStrings.Get("Hide window");
        ToolTipService.SetToolTip(CloseApp, UiStrings.Get("Touchpad Control keeps running in the background."));
        QuitApp.Text = UiStrings.Get("Quit Touchpad Control");
    }

    private void AppMenuButton_SizeChanged(object sender, SizeChangedEventArgs e){
        if(AppMenuLabel != null)
            AppMenuLabel.Visibility = e.NewSize.Width >= 150 ? Visibility.Visible : Visibility.Collapsed;
    }

    public bool TryChangeLanguage(string requestedChoice){
        if(_closed) return false;
        string choice = LanguageCatalog.NormalizeChoice(requestedChoice);
        if(choice == UiStrings.Choice) return true;
        string previous = App.SettingsData.Language;
        App.SettingsData.Language = choice;
        try { App.SettingsData.save(); }
        catch(Exception exception){
            App.SettingsData.Language = previous;
            RuntimeDiagnostics.Write("language-save", exception.ToString());
            return false;
        }
        UiStrings.SetLanguage(choice);
        ApplyShellLanguage();
        App.Instance.HandlerWindow?.ApplyLanguage();
        // Recreate only the current settings page; keep the input engine and background process alive.
        Type pageType = ContentFrame.Content?.GetType() ?? typeof(TouchpadSettings);
        ContentFrame.Navigate(pageType);
        ContentFrame.BackStack.Clear();
        NavigationView.Header = UiStrings.Get(pageType == typeof(TouchpadSettings) ? "Touchpad dashboard"
            : pageType == typeof(ThreeFingerDragSettings) ? "Pointer & gestures" : "System & diagnostics");
        RuntimeDiagnostics.Write("language", $"choice={choice}; resolved={UiStrings.ResolvedLanguage}; settings refreshed");
        return true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e){
        Close();
    }

    private async void QuitButton_Click(object sender, RoutedEventArgs e){
        if(_closed || _quitDialogOpen) return;
        _quitDialogOpen = true;
        try {
            var dialog = new ContentDialog {
                XamlRoot = Content.XamlRoot,
                RequestedTheme = (Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
                Title = UiStrings.Get("Quit Touchpad Control?"),
                Content = UiStrings.Get("Gestures and window overview will stop until you reopen Touchpad Control."),
                PrimaryButtonText = UiStrings.Get("Quit Touchpad Control"),
                CloseButtonText = UiStrings.Get("Cancel"),
                DefaultButton = ContentDialogButton.Close
            };
            if(await dialog.ShowAsync() == ContentDialogResult.Primary && !_closed) {
                Logger.Log("Quitting SettingsWindow...");
                _app.Quit();
            }
        } catch(Exception exception) {
            RuntimeDiagnostics.Write("quit-dialog", exception.ToString());
        } finally { _quitDialogOpen = false; }
    }

    private void Window_Closed(object sender, WindowEventArgs e){
        _closed = true;
        Logger.Log("Hiding SettingsWindow; edits were committed individually.");
        if(ContentFrame.Content is OtherSettings systemSettings) systemSettings.StopUpdates();

        _app.OnClosePrefsWindow();
    }

    private int _inputCount;
    private long _lastContact;
    private long _lastEventSpeed;
    private long _lastDiagnosticUpdate;
    public void OnTouchpadContact(IntPtr currentDevice, TouchpadContact[] contacts){
        if(_closed) return;
        _inputCount++;

        // Event speed is an average over 20 inputs calls (usually about 200 ms)
        if(_inputCount >= 20){
            _inputCount = 0;
            _lastEventSpeed = (Ctms() - _lastContact) / 20;
            _lastContact = Ctms();
        }
        // Keep diagnostics from doing layout/string/brush work for every HID
        // report. Gesture processing remains full-rate in the handler window.
        long now = Environment.TickCount64;
        if(now - _lastDiagnosticUpdate < 100) return;
        _lastDiagnosticUpdate = now;
        Page currentPage = ContentFrame.Content as Page;
        if(currentPage is TouchpadSettings touchpadSettings){
            touchpadSettings.UpdateContactsText(UiStrings.Format("Touchpad device: {0}\n{1}\nEvent interval: {2} ms",
                TouchpadHelper.GetDeivceInfo(currentDevice)?.ToString() ?? UiStrings.Get("unknown"),
                string.Join('\n', contacts.Select(c => c.ToString())), _lastEventSpeed));
        }
    }
    public void OnTouchpadInitialized(){
        if(_closed) return;
        Page currentPage = ContentFrame.Content as Page;
        if(currentPage is TouchpadSettings touchpadSettings){
            touchpadSettings.OnTouchpadInitialized();
        }
    }

    private long Ctms(){
        return Environment.TickCount64;
    }
}
