using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ThreeFingerDragOnWindows.localization;
using ThreeFingerDragOnWindows.utils;
using TouchpadControl.Pointer;

namespace ThreeFingerDragOnWindows.settings;

public sealed partial class UnifiedTrackingControl : UserControl {
    // Shared across settings windows: no concurrent native transactions.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly UnifiedTrackingBackend _backend = new();
    private TrackingSettings? _observed;
    private bool _busy;
    public UnifiedTrackingControl() { InitializeComponent(); Loaded += async (_, _) => await ReadAsync(); }
    private void SetBusy(bool busy) {
        _busy = busy;
        Refresh.IsEnabled = !busy;
        Apply.IsEnabled = Speed.IsEnabled = Acceleration.IsEnabled = !busy && _observed.HasValue;
    }
    private void Show(string key, InfoBarSeverity severity = InfoBarSeverity.Informational) {
        Status.Message = UiStrings.Get(key); Status.Severity = severity;
    }
    private void Update(TrackingSettings value) { _observed = value; Speed.Value = value.Speed; Acceleration.IsOn = value.Acceleration; }
    private async Task ReadAsync() {
        if(_busy) return;
        SetBusy(true);
        await Gate.WaitAsync();
        try { Update(await Task.Run(_backend.Read)); Show("S182"); }
        catch(Exception e) { _observed = null; Show("S183", InfoBarSeverity.Error); RuntimeDiagnostics.Write("unified-tracking-read", e.Message); }
        finally { Gate.Release(); SetBusy(false); }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReadAsync();
    private async void Apply_Click(object sender, RoutedEventArgs e) {
        if(_busy || !_observed.HasValue) return;
        var desired = new TrackingSettings((int)Math.Round(Speed.Value), Acceleration.IsOn);
        SetBusy(true);
        await Gate.WaitAsync();
        try {
            // Re-read before mutation; an external change must never be silently
            // folded into this control's stale draft.
            var current = await Task.Run(_backend.Read);
            if(!IsLoaded) return;
            if(current != _observed.Value) { Update(current); Show("S184", InfoBarSeverity.Warning); return; }
            if(current == desired) { Update(current); Show("S182"); return; }
            bool changeAcceleration = current.Acceleration != desired.Acceleration;
            if(changeAcceleration) {
                var dialog = new ContentDialog { XamlRoot = XamlRoot, RequestedTheme = ActualTheme,
                    Title = UiStrings.Get("S178"), Content = UiStrings.Get("S185"),
                    PrimaryButtonText = UiStrings.Get("S179"), CloseButtonText = UiStrings.Get("Cancel"),
                    DefaultButton = ContentDialogButton.Close };
                if(await dialog.ShowAsync() != ContentDialogResult.Primary || !IsLoaded) { Update(current); return; }
                // Confirmed settings can change while the dialog is open.
                var confirmed = await Task.Run(_backend.Read);
                if(!IsLoaded) return;
                if(confirmed != current) { Update(confirmed); Show("S184", InfoBarSeverity.Warning); return; }
            }
            var result = await Task.Run(() => TrackingSettingsTransaction.Change(_backend, desired, changeAcceleration));
            if(result.Observed is { } actual) Update(actual); else _observed = null;
            Show(result.Success ? "S186" : result.RollbackVerified ? "S187" : "S188",
                result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
            RuntimeDiagnostics.Write("unified-tracking-apply", $"success={result.Success}; rollback={result.RollbackVerified}; error={result.Error}");
        } catch(Exception error) { _observed = null; Show("S188", InfoBarSeverity.Error); RuntimeDiagnostics.Write("unified-tracking-apply", error.Message); }
        finally { Gate.Release(); SetBusy(false); }
    }
}
