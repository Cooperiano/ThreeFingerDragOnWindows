using System;

namespace ThreeFingerDragOnWindows.gestures;

public enum DragMouseButton { None, Left, Right, Middle }

/// <summary>Pairs successful button-down input with the same button-up input.</summary>
public sealed class DragButtonLatch {
    private readonly object _gate = new();
    private DragMouseButton? _pressedButton;
    private bool _transitioning;

    public bool IsPressed { get { lock(_gate) return _pressedButton.HasValue; } }

    public bool TryPress(DragMouseButton button, Func<DragMouseButton, bool> sendDown) {
        ArgumentNullException.ThrowIfNull(sendDown);
        if(button is not (DragMouseButton.Left or DragMouseButton.Right or DragMouseButton.Middle)) return false;
        lock(_gate) {
            if(_transitioning || _pressedButton.HasValue) return false;
            _transitioning = true;
            try {
                if(!sendDown(button)) return false;
                _pressedButton = button;
                return true;
            } finally { _transitioning = false; }
        }
    }

    public bool TryRelease(Func<DragMouseButton, bool> sendUp) {
        ArgumentNullException.ThrowIfNull(sendUp);
        lock(_gate) {
            if(_transitioning || !_pressedButton.HasValue) return false;
            _transitioning = true;
            try {
                if(!sendUp(_pressedButton.Value)) return false;
                _pressedButton = null;
                return true;
            } finally { _transitioning = false; }
        }
    }
}
