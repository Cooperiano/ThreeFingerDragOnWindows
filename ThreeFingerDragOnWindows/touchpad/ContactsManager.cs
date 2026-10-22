using System;
using System.Linq;
using ThreeFingerDragEngine.utils;
using ThreeFingerDragOnWindows.gestures;
using ThreeFingerDragOnWindows.utils;
using WinRT.Interop;

namespace ThreeFingerDragOnWindows.touchpad;

public sealed class ContactsManager : IDisposable {
    private readonly HandlerWindow _source;
    private readonly IntPtr _hwnd;
    private readonly Interop.SubclassProcDelegate _callback;
    private readonly ContactFrameAssembler<TouchpadContact> _assembler = new(contact => contact.ContactId);
    private IntPtr _pendingDevice;
    private bool _disposed;
    private int _lastActiveCount = -1;
    private const uint WM_NCDESTROY = 0x0082;
    private static readonly UIntPtr SubclassId = new(0x544350);

    public ContactsManager(HandlerWindow source) {
        _source = source;
        _hwnd = WindowNative.GetWindowHandle(source);
        _callback = WindowProcess;
        if(!Interop.SetWindowSubclass(_hwnd, _callback, SubclassId, UIntPtr.Zero))
            throw new InvalidOperationException("Could not register the touchpad input callback.");
    }

    public void InitializeSource() {
        if(_disposed) return;
        _source.OnTouchpadInitialized(TouchpadHelper.Exists(), TouchpadHelper.RegisterInput(_hwnd));
    }

    private IntPtr WindowProcess(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam,
        UIntPtr subclassId, UIntPtr referenceData) {
        try {
            if(!_disposed && message == TouchpadHelper.WM_INPUT) {
                var (device, contacts, count, scanTime) = TouchpadHelper.ParseInput(lParam);
                if(contacts is not null && !TouchpadHelper.IsSyntheticTouchpadDevice(device)) {
                    if(_pendingDevice != device) {
                        _assembler.Reset();
                        _pendingDevice = device;
                    }
                    if(count == 0 && !_assembler.HasPendingFrame && contacts.All(contact => !contact.IsActive))
                        DeliverFrame(device, Array.Empty<TouchpadContact>());
                    else if(_assembler.TryAssemble(count, scanTime, contacts, out var frame))
                        DeliverFrame(device, frame);
                }
            } else if(!_disposed && message == TouchpadHelper.WM_INPUT_DEVICE_CHANGE) {
                // Creating our PT_TOUCHPAD output device also emits raw-input
                // arrival/removal. Neither may reset the physical frame or
                // cancel the drag that just created it.
                if(wParam == new IntPtr(1)) TouchpadHelper.ForgetDevice(lParam);
                if(TouchpadHelper.IsSyntheticTouchpadDevice(lParam)) {
                    if(wParam == new IntPtr(2)) TouchpadHelper.ForgetDevice(lParam);
                    return Interop.DefSubclassProc(hwnd, message, wParam, lParam);
                }
                _assembler.Reset();
                _source.OnInputInterrupted();
                // RegisterRawInputDevices itself emits arrival notifications for
                // existing devices. Re-registering here creates an endless loop.
                if(wParam == new IntPtr(1)) TouchpadHelper.Exists(lParam);
                if(wParam == new IntPtr(2)) TouchpadHelper.ForgetDevice(lParam);
                _source.OnTouchpadInitialized(TouchpadHelper.Exists(), _source.InputReceiverInstalled);
            } else if(message == WM_NCDESTROY) {
                Dispose();
            }
        } catch(Exception exception) {
            RuntimeDiagnostics.Write("input-callback", exception.ToString());
            _assembler.Reset();
            try { _source.OnInputInterrupted(); } catch { }
        }
        return Interop.DefSubclassProc(hwnd, message, wParam, lParam);
    }

    public void Dispose() {
        if(_disposed) return;
        _disposed = true;
        Interop.RemoveWindowSubclass(_hwnd, _callback, SubclassId);
        _assembler.Reset();
        GC.KeepAlive(_callback);
    }

    private void DeliverFrame(IntPtr device, TouchpadContact[] contacts) {
        var active = contacts.Where(contact => contact.IsActive).ToList();
        if(active.Count != _lastActiveCount) {
            _lastActiveCount = active.Count;
            RuntimeDiagnostics.Write("contacts", $"active={active.Count}; reported={contacts.Length}");
        }
        _source.OnTouchpadContact(device, active);
    }
}
