using TouchpadControl.Pointer;
using Xunit;

public sealed class UnifiedTrackingSettingsTests {
    [Theory] [InlineData(0)] [InlineData(21)]
    public void InvalidSpeedDoesNotTouchSystem(int speed) {
        var backend = new Backend();
        Assert.False(TrackingSettingsTransaction.Change(backend, new(speed, true), false).Success);
        Assert.Equal(0, backend.Captures); Assert.Equal(0, backend.Applies);
    }
    [Fact] public void SpeedOnlyDoesNotChangeAcceleration() {
        var backend = new Backend();
        var result = TrackingSettingsTransaction.Change(backend, new(20, false), false);
        Assert.True(result.Success); Assert.Equal(new TrackingSettings(20, true), backend.State); Assert.False(backend.AccelerationChanged);
        Assert.False(backend.AccelerationCaptured);
    }
    [Fact] public void ExplicitAccelerationUsesSameSharedState() {
        var backend = new Backend(); var result = TrackingSettingsTransaction.Change(backend, new(12, false), true);
        Assert.True(result.Success); Assert.Equal(new TrackingSettings(12, false), backend.State);
        Assert.True(backend.AccelerationCaptured);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void PartialFailureOrMismatchRestoresOldState(bool throws) {
        var backend = new Backend { Throws = throws, Mismatch = !throws };
        var result = TrackingSettingsTransaction.Change(backend, new(12, false), true);
        Assert.False(result.Success); Assert.True(result.RollbackVerified); Assert.Equal(new TrackingSettings(9, true), backend.State);
    }
    [Fact] public void RollbackFailureIsNeverReportedAsSuccess() {
        var backend = new Backend { Throws = true, RollbackThrows = true };
        var result = TrackingSettingsTransaction.Change(backend, new(12, false), true);
        Assert.False(result.Success); Assert.False(result.RollbackVerified);
    }
    private sealed class Backend : ITrackingBackend {
        public TrackingSettings State = new(9, true);
        public int Captures, Applies;
        public bool AccelerationChanged, AccelerationCaptured, Throws, Mismatch, RollbackThrows;
        public TrackingSettings Read() => State;
        public object Capture(bool changeAcceleration) { Captures++; AccelerationCaptured = changeAcceleration; return State; }
        public void Apply(TrackingSettings desired, bool changeAcceleration) {
            Applies++; AccelerationChanged = changeAcceleration;
            State = new(Mismatch ? 10 : desired.Speed, changeAcceleration ? desired.Acceleration : State.Acceleration);
            if(Throws) throw new IOException("partial write");
        }
        public void Restore(object snapshot) { if(RollbackThrows) throw new IOException(); State = (TrackingSettings)snapshot; }
        public bool Matches(object snapshot) => State == (TrackingSettings)snapshot;
    }
}
