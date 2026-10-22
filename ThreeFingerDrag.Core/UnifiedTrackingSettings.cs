namespace TouchpadControl.Pointer;

public readonly record struct TrackingSettings(int Speed, bool Acceleration);
public interface ITrackingBackend {
    TrackingSettings Read();
    object Capture(bool changeAcceleration);
    void Apply(TrackingSettings desired, bool changeAcceleration);
    void Restore(object snapshot);
    bool Matches(object snapshot);
}
public readonly record struct TrackingChangeResult(bool Success, TrackingSettings? Observed, bool RollbackVerified, string Error);
public static class TrackingSettingsTransaction {
    public static TrackingChangeResult Change(ITrackingBackend backend, TrackingSettings desired, bool changeAcceleration) {
        if(desired.Speed < 1 || desired.Speed > 20) return new(false, null, false, "Invalid speed");
        object snapshot;
        try { snapshot = backend.Capture(changeAcceleration); } catch(Exception e) { return new(false, null, false, e.Message); }
        try {
            backend.Apply(desired, changeAcceleration);
            var actual = backend.Read();
            if(actual.Speed != desired.Speed || (changeAcceleration && actual.Acceleration != desired.Acceleration))
                throw new InvalidOperationException("Windows readback differs from the requested values.");
            return new(true, actual, false, "");
        } catch(Exception e) {
            bool restored = false;
            try { backend.Restore(snapshot); restored = backend.Matches(snapshot); } catch { }
            TrackingSettings? actual = null;
            try { actual = backend.Read(); } catch { }
            return new(false, actual, restored, e.Message);
        }
    }
}
