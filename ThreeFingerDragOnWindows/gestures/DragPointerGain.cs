using System;

namespace ThreeFingerDragOnWindows.gestures;

/// <summary>The existing three-finger gain curve, independent of device registration.</summary>
public static class DragPointerGain {
    public static float Calculate(double mouseVelocity, float acceleration) {
        var a = acceleration / 10f;
        if(a == 0) return 1;
        var value = 2.6 * a * (mouseVelocity - 1 + (3 - Math.Log2(0.8 / 0.3 - 1)) / (2.6 * a)) - 3;
        return (float)(0.7 + 0.8 / (1.0 + Math.Exp(-value)));
    }
}
