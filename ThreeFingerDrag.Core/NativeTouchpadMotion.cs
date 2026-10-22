namespace TouchpadControl.Pointer;

/// <summary>Descriptor units, not a per-device user speed multiplier.</summary>
public readonly record struct TouchpadAxis(int LogicalMinimum, int LogicalMaximum, double HimetricExtent)
{
    public bool IsValid => LogicalMaximum > LogicalMinimum && double.IsFinite(HimetricExtent)
        && HimetricExtent >= 1000 && HimetricExtent <= 50000;
    public double ConvertDelta(double delta) => delta * HimetricExtent / ((double)LogicalMaximum - LogicalMinimum);
    public double ConvertPosition(double position) => ConvertDelta(position - LogicalMinimum);
    public static TouchpadAxis FromHid(int logicalMin, int logicalMax, int physicalMin, int physicalMax,
        uint units, uint exponent)
    {
        int exp = (int)(exponent & 15);
        if(exp >= 8) exp -= 16;
        // HID SI linear length = centimetres, English linear length = inches.
        double scale = units switch { 0x11 => 1000, 0x13 => 2540, _ => double.NaN };
        return new(logicalMin, logicalMax, ((double)physicalMax - physicalMin) * Math.Pow(10, exp) * scale);
    }
}

public readonly record struct TouchpadGeometry(TouchpadAxis X, TouchpadAxis Y)
{
    public bool IsValid => X.IsValid && Y.IsValid;
}
public enum TouchpadContactPhase { Down, Move, Cancel }
public interface INativeTouchpadSink : IDisposable
{
    bool Create(TouchpadGeometry geometry);
    bool Inject(TouchpadContactPhase phase, int xHimetric, int yHimetric);
}

/// <summary>One physical-motion stream. Speed and acceleration belong to Windows.</summary>
public sealed class NativeTouchpadMotion(INativeTouchpadSink sink) : IDisposable
{
    private TouchpadGeometry _geometry;
    private double _x, _y;
    public bool Active { get; private set; }
    public bool Begin(TouchpadGeometry geometry, double rawX, double rawY)
    {
        End();
        if(!geometry.IsValid || !double.IsFinite(rawX) || !double.IsFinite(rawY)) return false;
        _geometry = geometry;
        _x = geometry.X.ConvertPosition(rawX);
        _y = geometry.Y.ConvertPosition(rawY);
        if(!InBounds(_x, _y)) return false;
        try { if(!sink.Create(geometry)) { sink.Dispose(); return false; } }
        catch { SafeDispose(); return false; }
        Active = true;
        try { if(sink.Inject(TouchpadContactPhase.Down, (int)Math.Round(_x), (int)Math.Round(_y))) return true; }
        catch { }
        End();
        return false;
    }
    public bool Move(double rawDx, double rawDy)
    {
        if(!Active) return false;
        var nextX = _x + _geometry.X.ConvertDelta(rawDx);
        var nextY = _y + _geometry.Y.ConvertDelta(rawDy);
        if(!double.IsFinite(nextX) || !double.IsFinite(nextY) || !InBounds(nextX, nextY)) { End(); return false; }
        try {
            if(!sink.Inject(TouchpadContactPhase.Move, (int)Math.Round(nextX), (int)Math.Round(nextY))) { End(); return false; }
        } catch { End(); return false; }
        _x = nextX;
        _y = nextY;
        return true;
    }
    private bool InBounds(double x, double y) => x >= 0 && y >= 0
        && x <= _geometry.X.HimetricExtent && y <= _geometry.Y.HimetricExtent;
    public void End()
    {
        if(!Active) return;
        Active = false;
        try { sink.Inject(TouchpadContactPhase.Cancel, (int)Math.Round(_x), (int)Math.Round(_y)); }
        catch { }
        finally { SafeDispose(); }
    }
    private void SafeDispose() { try { sink.Dispose(); } catch { } }
    public void Dispose() { End(); SafeDispose(); }
}
