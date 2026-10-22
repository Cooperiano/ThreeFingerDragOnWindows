using System;

namespace ThreeFingerDragOnWindows.gestures;

/// <summary>
/// User-adjustable native Windows pointer settings. This is not an independent
/// velocity-dependent curve and never synthesizes a second cursor stream.
/// </summary>
public readonly record struct PointerAccelerationProfile(
    int PointerSpeed,
    int Threshold1,
    int Threshold2,
    int AccelerationLevel,
    int TouchpadCursorSpeed) {

    // Use a single enabled acceleration level. On the validated Windows build
    // the old requested level 2 reads back as 1, so it cannot be asserted as a
    // separately supported stronger acceleration curve.
    public static PointerAccelerationProfile WindowsNative => new(
        PointerSpeed: 8,
        Threshold1: 2,
        Threshold2: 5,
        AccelerationLevel: 1,
        TouchpadCursorSpeed: 9);

    // Retain the internal compatibility name, not a claim of Apple semantics.
    public static PointerAccelerationProfile MacLike => WindowsNative;

    public PointerAccelerationProfile Normalize() => new(
        PointerSpeed: Math.Clamp(PointerSpeed, 1, 20),
        Threshold1: Math.Clamp(Threshold1, 0, 20),
        Threshold2: Math.Clamp(Threshold2, Math.Clamp(Threshold1, 0, 20), 40),
        AccelerationLevel: Math.Clamp(AccelerationLevel, 0, 2),
        TouchpadCursorSpeed: Math.Clamp(TouchpadCursorSpeed, 1, 20));
}
