using System;
using System.Runtime.InteropServices;
using ThreeFingerDragOnWindows.gestures;
using ThreeFingerDragOnWindows.settings;

namespace ThreeFingerDragOnWindows.utils;

public class MouseOperations {
    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;
    private const int MOUSEEVENTF_MOVE = 0x0001;
    private const int KEYEVENTF_KEYUP = 0x0002;
    internal const ushort VK_CONTROL = 0x11;
    internal const ushort VK_ALT = 0x12;
    internal const ushort VK_SHIFT = 0x10;
    internal const ushort VK_SPACE = 0x20;
    internal const ushort VK_RETURN = 0x0D;
    internal const ushort VK_TAB = 0x09;
    internal const ushort VK_LEFT = 0x25;
    internal const ushort VK_UP = 0x26;
    internal const ushort VK_RIGHT = 0x27;
    internal const ushort VK_DOWN = 0x28;
    internal const ushort VK_D = 0x44;
    internal const ushort VK_LWIN = 0x5B;
    public const int MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const int MOUSEEVENTF_LEFTUP = 0x0004;
    public const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const int MOUSEEVENTF_RIGHTUP = 0x0010;
    public const int MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const int MOUSEEVENTF_MIDDLEUP = 0x0040;

    // moving the cursor does not work with floating point values
    // decimal parts are kept and then added to be taken in account
    private static float _decimalX;
    private static float _decimalY;


    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out IntMousePoint lpFIntMousePoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);


    public static IntMousePoint GetCursorPosition(){
        IntMousePoint currentIntMousePoint;
        var gotPoint = GetCursorPos(out currentIntMousePoint);
        if(!gotPoint) currentIntMousePoint = new IntMousePoint(0, 0);
        return currentIntMousePoint;
    }

    public static void ShiftCursorPosition(float x, float y){
        var intX = (int) (x + _decimalX);
        var intY = (int) (y + _decimalY);
        _decimalX = x + _decimalX - intX;
        _decimalY = y + _decimalY - intY;

        MoveMouse(intX, intY);
    }

    /// <summary>
    /// Applies an already-accelerated delta without sending it through the
    /// Windows relative-pointer acceleration curve a second time.
    /// </summary>
    public static void ShiftCursorPositionUnaccelerated(float x, float y){
        var intX = (int)(x + _decimalX);
        var intY = (int)(y + _decimalY);
        _decimalX = x + _decimalX - intX;
        _decimalY = y + _decimalY - intY;
        if(intX == 0 && intY == 0) return;
        if(!GetCursorPos(out var current)) return;
        SetCursorPos(current.x + intX, current.y + intY);
    }

    public static void SetCursorPosition(float x, float y){
        var intX = (int) (x + _decimalX);
        var intY = (int) (y + _decimalY);
        _decimalX = x + _decimalX - intX;
        _decimalY = y + _decimalY - intY;
        var point = GetCursorPosition();

        MoveMouse(intX - point.x, intY - point.y);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    private static void MoveMouse(int dx, int dy){
        var input = new Input[1];
        input[0].type = INPUT_MOUSE;
        input[0].u.mi.dx = dx;
        input[0].u.mi.dy = dy;
        input[0].u.mi.dwFlags = MOUSEEVENTF_MOVE;

        var result = SendInput(1, input, Marshal.SizeOf(typeof(Input)));
        if(result == 0) Console.WriteLine("Failed to move the mouse. Error code: " + Marshal.GetLastWin32Error());
    }

    public static bool MouseClick(int mouseEventFlag){
        var input = new Input[1];
        input[0].type = INPUT_MOUSE;
        input[0].u.mi.dwFlags = mouseEventFlag;

        var result = SendInput(1, input, Marshal.SizeOf(typeof(Input)));

        if(result == 0) Console.WriteLine("Failed to send mouse click. Error code: " + Marshal.GetLastWin32Error());
        return result == 1;
    }

    public static bool ThreeFingersDragMouseDown(DragMouseButton button){
        return button switch {
            DragMouseButton.Left => MouseClick(MOUSEEVENTF_LEFTDOWN),
            DragMouseButton.Right => MouseClick(MOUSEEVENTF_RIGHTDOWN),
            DragMouseButton.Middle => MouseClick(MOUSEEVENTF_MIDDLEDOWN),
            _ => false
        };
    }

    public static bool ThreeFingersDragMouseUp(DragMouseButton button){
        return button switch {
            DragMouseButton.Left => MouseClick(MOUSEEVENTF_LEFTUP),
            DragMouseButton.Right => MouseClick(MOUSEEVENTF_RIGHTUP),
            DragMouseButton.Middle => MouseClick(MOUSEEVENTF_MIDDLEUP),
            _ => false
        };
    }

    public static void SendFourFingerGesture(FourFingerGesture gesture){
        _ = MissionControlBridge.HandleGestureAsync(gesture);
    }

    internal static void SendKeyboardChord(params ushort[] keys){
        var inputs = new Input[keys.Length * 2];
        for(var index = 0; index < keys.Length; index++){
            inputs[index].type = INPUT_KEYBOARD;
            inputs[index].u.ki.wVk = keys[index];
        }
        for(var index = 0; index < keys.Length; index++){
            var inputIndex = keys.Length + index;
            inputs[inputIndex].type = INPUT_KEYBOARD;
            inputs[inputIndex].u.ki.wVk = keys[keys.Length - index - 1];
            inputs[inputIndex].u.ki.dwFlags = KEYEVENTF_KEYUP;
        }

        var result = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if(result != inputs.Length){
            Console.WriteLine("Failed to send keyboard chord. Error code: " + Marshal.GetLastWin32Error());
        }
    }


    [StructLayout(LayoutKind.Sequential)]
    private struct Input {
        public int type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion {
        [FieldOffset(0)] public MouseInput mi;
        [FieldOffset(0)] public KeyboardInput ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput {
        public int dx;
        public int dy;
        public int mouseData;
        public int dwFlags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput {
        public ushort wVk;
        public ushort wScan;
        public int dwFlags;
        public int time;
        public IntPtr dwExtraInfo;
    }
}

public struct IntMousePoint {
    public int x;
    public int y;

    public IntMousePoint(int x, int y){
        this.x = x;
        this.y = y;
    }
}
