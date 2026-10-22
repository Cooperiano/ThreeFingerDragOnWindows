using System;
using System.Runtime.InteropServices;
using TouchpadControl.Pointer;

namespace ThreeFingerDragOnWindows.utils;

/// <summary>PT_TOUCHPAD through OS motion processing; no SetCursorPos or extra gain.</summary>
internal sealed class NativeTouchpadSink : INativeTouchpadSink
{
    private IntPtr _device;
    internal static bool IsSupported => NativeLibrary.TryLoad("user32.dll", out var module) && HasApi(module);
    private static bool HasApi(IntPtr module) {
        try { return NativeLibrary.TryGetExport(module, "CreateSyntheticPointerDevice2", out _); }
        finally { NativeLibrary.Free(module); }
    }
    internal int LastError { get; private set; }
    public bool Create(TouchpadGeometry geometry)
    {
        Dispose();
        if(!geometry.IsValid) return false;
        try {
            var parameters = new CreationParameters {
                Type = 5, Count = 1, Feedback = 3, Options = 1,
                Width = (uint)Math.Round(geometry.X.HimetricExtent),
                Height = (uint)Math.Round(geometry.Y.HimetricExtent)
            };
            _device = CreateSyntheticPointerDevice2(ref parameters);
            LastError = _device == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
            return _device != IntPtr.Zero;
        } catch(EntryPointNotFoundException) { LastError = 127; return false; }
    }
    public bool Inject(TouchpadContactPhase phase, int xHimetric, int yHimetric)
    {
        if(_device == IntPtr.Zero) return false;
        uint flags = phase switch {
            TouchpadContactPhase.Down => 0x10000 | 0x2 | 0x4 | 0x4000,
            TouchpadContactPhase.Move => 0x20000 | 0x2 | 0x4 | 0x4000,
            _ => 0x40000 | 0x8000 // Cancel+Up: never synthesize an extra tap.
        };
        var frame = new[] { new PointerTypeInfo {
            Type = 5,
            Touch = new TouchInfo { Pointer = new PointerInfo {
                Type = 5, Id = 1, Flags = flags,
                Himetric = new NativePoint(xHimetric, yHimetric),
                HimetricRaw = new NativePoint(xHimetric, yHimetric)
            }}
        }};
        bool success = InjectSyntheticPointerInput(_device, frame, 1);
        LastError = success ? 0 : Marshal.GetLastWin32Error();
        return success;
    }
    public void Dispose()
    {
        if(_device == IntPtr.Zero) return;
        DestroySyntheticPointerDevice(_device);
        _device = IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] private struct CreationParameters {
        public uint Type, Count, Feedback;
        public IntPtr Monitor;
        public uint Width, Height, Options;
    }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativePoint(int x, int y) { public readonly int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct PointerInfo {
        public uint Type, Id, Frame, Flags;
        public IntPtr Source, Target;
        public NativePoint Pixel, Himetric, PixelRaw, HimetricRaw;
        public uint Time, History;
        public int InputData;
        public uint Keys;
        public ulong PerformanceCount;
        public uint ButtonChange;
    }
    [StructLayout(LayoutKind.Sequential)] private struct TouchInfo {
        public PointerInfo Pointer;
        public uint Flags, Mask;
        public NativeRect Contact, ContactRaw;
        public uint Orientation, Pressure;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PointerTypeInfo {
        public uint Type;
        public TouchInfo Touch; // Largest union member; correct native alignment.
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CreateSyntheticPointerDevice2(ref CreationParameters parameters);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InjectSyntheticPointerInput(IntPtr device, PointerTypeInfo[] frames, uint count);
    [DllImport("user32.dll")] private static extern void DestroySyntheticPointerDevice(IntPtr device);
}
