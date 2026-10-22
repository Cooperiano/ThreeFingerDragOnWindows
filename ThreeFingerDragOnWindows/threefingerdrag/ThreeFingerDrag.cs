using System;
using System.Linq;
using System.Timers;
using ThreeFingerDragEngine.utils;
using ThreeFingerDragOnWindows.gestures;
using ThreeFingerDragOnWindows.settings;
using ThreeFingerDragOnWindows.touchpad;
using ThreeFingerDragOnWindows.utils;
using TouchpadControl.Pointer;

namespace ThreeFingerDragOnWindows.threefingerdrag;

/// <summary>Adapts raw touchpad frames to gesture actions and Windows input.</summary>
public sealed class ThreeFingerDrag : IDisposable {
    // Retained for the legacy speed helper; release timing is adaptive in GestureProcessor.
    public const int RELEASE_FINGERS_THRESHOLD_MS = 40;
    private readonly GestureOptions _options = new();
    private readonly GestureProcessor _processor;
    private readonly NativeTouchpadSink _nativeSink = new();
    private readonly NativeTouchpadMotion _nativeMotion;
    private double _rawX, _rawY;
    private readonly Timer _dragEndTimer = new() { AutoReset = false };
    private readonly object _stateLock = new();
    private IntPtr _currentDevice;
    private long _lastTimestamp;
    private readonly DragButtonLatch _buttonLatch = new();
    private bool _dragSessionActive;
    private bool _disposed;

    public ThreeFingerDrag() {
        _nativeMotion = new NativeTouchpadMotion(_nativeSink);
        _processor = new GestureProcessor(_options);
        _dragEndTimer.Elapsed += OnTimerElapsed;
    }

    public void OnTouchpadContact(IntPtr currentDevice, TouchpadContact[] contacts, long timestampMs) {
        lock(_stateLock) {
            if(_disposed) return;
            if(_currentDevice != currentDevice && (_dragSessionActive || _buttonLatch.IsPressed)) {
                foreach(var ending in _processor.EndDragTracking()) Execute(ending, 0);
                ReleaseMouseButton();
            }
            _currentDevice = currentDevice;
            if(contacts.Length > 0) {
                var xs = contacts.Select(c => (double)c.X).OrderBy(x => x).ToArray();
                var ys = contacts.Select(c => (double)c.Y).OrderBy(y => y).ToArray();
                _rawX = xs[xs.Length / 2];
                _rawY = ys[ys.Length / 2];
            }
            _options.DragActivationDistance = App.SettingsData.ThreeFingerDragStartThreshold;
            _options.FourFingerSwipeDistance = App.SettingsData.FourFingerSwipeThreshold;

            var frame = contacts
                .Select(contact => new GestureContact(contact.ContactId, contact.X, contact.Y))
                .ToArray();
            var elapsedMs = _lastTimestamp == 0 ? 10 : Math.Max(1, timestampMs - _lastTimestamp);
            _lastTimestamp = timestampMs;

            var actions = _processor.Process(
                timestampMs,
                frame,
                App.SettingsData.ThreeFingerDrag,
                App.SettingsData.FourFingerGestures);

            foreach(var action in actions) Execute(action, elapsedMs);
            if(actions.Count == 0 && _processor.State == GestureState.Dragging && _nativeMotion.Active)
                if(!_nativeMotion.Move(0, 0)) {
                    _processor.EndDragTracking();
                    ReleaseMouseButton();
                }
            if(_dragSessionActive || _buttonLatch.IsPressed) ArmFailSafeRelease();
        }
    }

    public void OnEnabledSettingsChanged() {
        lock(_stateLock) {
            if(_disposed) return;
            foreach(var action in _processor.CancelDisabledGestures(
                App.SettingsData.ThreeFingerDrag, App.SettingsData.FourFingerGestures)) Execute(action, 0);
        }
    }

    public void ForceRelease() {
        lock(_stateLock) {
            if(_disposed) return;
            _dragEndTimer.Stop();
            foreach(var action in _processor.ForceRelease()) Execute(action, 0);
            if(_buttonLatch.IsPressed || _dragSessionActive) ReleaseMouseButton();
        }
    }

    public void Dispose() {
        lock(_stateLock) {
            if(_disposed) return;
            ForceRelease();
            _disposed = true;
            _nativeMotion.Dispose();
            _dragEndTimer.Dispose();
        }
    }

    private void Execute(GestureAction action, long elapsedMs) {
        switch(action.Kind) {
            case GestureActionKind.DragStarted:
                if(!_dragSessionActive) {
                    var device = TouchpadHelper.GetDeivceInfo(_currentDevice);
                    var move = !App.SettingsData.ThreeFingerDeviceDragCursorConfigs.TryGetValue(device?.deviceId ?? "default", out var setting)
                        || setting.ThreeFingerDragCursorMove;
                    if(move && !_nativeMotion.Begin(device?.Geometry ?? default, _rawX, _rawY)) {
                        _processor.EndDragTracking();
                        RuntimeDiagnostics.Write("unified-pointer", $"native start rejected; geometry={device?.Geometry.IsValid}; error={_nativeSink.LastError}");
                        break;
                    }
                    var button = (DragMouseButton)(int)App.SettingsData.ThreeFingerDragButton;
                    // None is a supported pointer-only mode, not an actual
                    // pressed button. Other modes latch only successful input.
                    if(button != DragMouseButton.None && !_buttonLatch.TryPress(button, MouseOperations.ThreeFingersDragMouseDown)) {
                        _processor.EndDragTracking();
                        Logger.Log("GESTURE: mouse down failed; drag cancelled");
                        _nativeMotion.End();
                        break;
                    }
                    _dragSessionActive = true;
                    ArmFailSafeRelease();
                    Logger.Log("GESTURE: three-finger drag started");
                    RuntimeDiagnostics.Write("unified-pointer", "three-finger drag uses Windows PT_TOUCHPAD; legacy speed/acceleration ignored");
                }
                break;
            case GestureActionKind.DragMoved:
                MoveCursor(action, elapsedMs);
                break;
            case GestureActionKind.DragEnded:
                ReleaseMouseButton();
                Logger.Log("GESTURE: three-finger drag ended");
                break;
            case GestureActionKind.FourFingerSwipe:
                // A persistent overview can be cancelled independently of an
                // enabled three-finger drag. Only actual four-finger input
                // transitions release its button; explicit global stop does
                // that through DragEnded / ForceRelease instead.
                if(action.FourFingerGesture != FourFingerGesture.CancelOverview)
                    ReleaseMouseButton();
                MouseOperations.SendFourFingerGesture(action.FourFingerGesture);
                Logger.Log("GESTURE: four-finger " + action.FourFingerGesture);
                break;
        }
    }

    private void MoveCursor(GestureAction action, long elapsedMs) {
        var deviceInfo = TouchpadHelper.GetDeivceInfo(_currentDevice);
        if(!App.SettingsData.ThreeFingerDeviceDragCursorConfigs.TryGetValue(deviceInfo?.deviceId ?? "default", out var config)) {
            config = new SettingsData.ThreeFingerDragConfig();
        }
        if(!config.ThreeFingerDragCursorMove) { _nativeMotion.End(); return; }

        var raw = new Point((float)action.DeltaX, (float)action.DeltaY);
        var distance = Math.Sqrt((raw.x * raw.x) + (raw.y * raw.y));
        if(App.SettingsData.ThreeFingerDragMaxFingerMoveDistance > 0 &&
           distance > App.SettingsData.ThreeFingerDragMaxFingerMoveDistance) {
            Logger.Log($"GESTURE: discarded noisy move ({raw.x}, {raw.y})");
            return;
        }

        if(!_nativeMotion.Move(raw.x, raw.y)) {
            RuntimeDiagnostics.Write("unified-pointer", $"native move rejected; error={_nativeSink.LastError}; releasing drag");
            _processor.EndDragTracking();
            ReleaseMouseButton();
        }
    }

    private void ArmFailSafeRelease() {
        _dragEndTimer.Stop();
        _dragEndTimer.Interval = App.SettingsData.ThreeFingerDragAllowReleaseAndRestart
            ? Math.Max(App.SettingsData.ThreeFingerDragReleaseDelay, _processor.AdaptiveReleaseTimeoutMs)
            : _processor.AdaptiveReleaseTimeoutMs;
        _dragEndTimer.Start();
    }

    private void OnTimerElapsed(object sender, ElapsedEventArgs e) {
        lock(_stateLock) {
            if(_disposed) return;
            // This timer protects a drag button, not the lifetime of a window
            // overview that intentionally stays open after all fingers lift.
            _dragEndTimer.Stop();
            foreach(var action in _processor.EndDragTracking()) Execute(action, 0);
            if(_buttonLatch.IsPressed || _dragSessionActive) ReleaseMouseButton();
        }
    }

    private void ReleaseMouseButton() {
        _nativeMotion.End();
        _dragEndTimer.Stop();
        _dragSessionActive = false;
        if(!_buttonLatch.IsPressed) return;
        if(!_buttonLatch.TryRelease(MouseOperations.ThreeFingersDragMouseUp)) {
            Logger.Log("GESTURE: mouse up failed; original button retained for retry");
            ArmFailSafeRelease();
        }
    }
}
