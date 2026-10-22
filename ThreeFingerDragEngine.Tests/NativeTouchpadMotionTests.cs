using TouchpadControl.Pointer;
using Xunit;

public sealed class NativeTouchpadMotionTests {
    private static readonly TouchpadGeometry Pad = new(new(0, 12000, 12000), new(0, 8000, 8000));
    [Theory]
    [InlineData(0x11u, 14u, 1200, 12000)]
    [InlineData(0x11u, 0xFFFFFFFEu, 1200, 12000)]
    [InlineData(0x13u, 14u, 500, 12700)]
    public void HidLengthUnitsConvertToHimetric(uint unit, uint exp, int physicalMax, int expected) {
        var axis = TouchpadAxis.FromHid(0, 10000, 0, physicalMax, unit, exp);
        Assert.True(axis.IsValid);
        Assert.Equal(expected, axis.HimetricExtent, 6);
    }
    [Theory]
    [InlineData(0u)] [InlineData(0x12u)] [InlineData(0x21u)]
    public void UnknownUnitsCannotInventSpeed(uint unit) => Assert.False(TouchpadAxis.FromHid(0, 10000, 0, 12000, unit, 14).IsValid);
    [Fact] public void LogicalMinimumAndPerAxisResolutionAreNormalized() {
        var sink = new Sink();
        using var motion = new NativeTouchpadMotion(sink);
        Assert.True(motion.Begin(new(new(100, 6100, 12000), new(200, 16200, 8000)), 3100, 8200));
        Assert.True(motion.Move(5, 20));
        Assert.Equal((TouchpadContactPhase.Move, 6010, 4010), sink.Frames.Last());
    }
    [Fact] public void StartDoesNotMoveAndEndCancelsWithoutNormalTapLift() {
        var sink = new Sink();
        using var motion = new NativeTouchpadMotion(sink);
        Assert.True(motion.Begin(Pad, 6000, 4000));
        Assert.Single(sink.Frames);
        Assert.Equal((TouchpadContactPhase.Down, 6000, 4000), sink.Frames[0]);
        motion.End(); motion.End();
        Assert.Equal(TouchpadContactPhase.Cancel, sink.Frames[1].Item1);
        Assert.Equal(2, sink.Frames.Count);
        Assert.False(motion.Active);
    }
    [Fact] public void SubHimetricMovementIsNotDiscarded() {
        var sink = new Sink();
        using var motion = new NativeTouchpadMotion(sink);
        Assert.True(motion.Begin(Pad, 6000, 4000));
        for(int n = 0; n < 10; n++) Assert.True(motion.Move(.1, -.1));
        Assert.Equal((TouchpadContactPhase.Move, 6001, 3999), sink.Frames.Last());
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void DownFailureCancelsAndDisposes(bool throws) {
        var sink = new Sink { Fail = TouchpadContactPhase.Down, Throws = throws };
        using var motion = new NativeTouchpadMotion(sink);
        Assert.False(motion.Begin(Pad, 6000, 4000));
        Assert.False(motion.Active);
        Assert.Equal(TouchpadContactPhase.Cancel, sink.Frames.Last().Item1);
        Assert.True(sink.DisposeCount > 0);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void MoveFailureTerminatesNativeContact(bool throws) {
        var sink = new Sink();
        using var motion = new NativeTouchpadMotion(sink);
        Assert.True(motion.Begin(Pad, 6000, 4000));
        sink.Fail = TouchpadContactPhase.Move; sink.Throws = throws;
        Assert.False(motion.Move(1, 1));
        Assert.False(motion.Active);
        Assert.Equal(TouchpadContactPhase.Cancel, sink.Frames.Last().Item1);
    }
    [Fact] public void CancelFailureStillDisposesAndCannotLeakException() {
        var sink = new Sink();
        using var motion = new NativeTouchpadMotion(sink);
        Assert.True(motion.Begin(Pad, 6000, 4000));
        sink.Fail = TouchpadContactPhase.Cancel; sink.Throws = true;
        motion.End();
        Assert.False(motion.Active);
        Assert.True(sink.DisposeCount > 0);
    }
    [Fact] public void MissingGeometryDoesNotCreateOrInject() {
        var sink = new Sink(); using var motion = new NativeTouchpadMotion(sink);
        Assert.False(motion.Begin(default, 10, 10)); Assert.Empty(sink.Frames); Assert.Equal(0, sink.CreateCount);
    }
    [Theory] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(100000)]
    public void InvalidOrOutOfBoundsDeltaCancels(double delta) {
        var sink = new Sink(); using var motion = new NativeTouchpadMotion(sink);
        Assert.True(motion.Begin(Pad, 6000, 4000)); Assert.False(motion.Move(delta, 0)); Assert.False(motion.Active);
    }
    [Fact] public void BeginAgainCancelsOldSessionBeforeNewDown() {
        var sink = new Sink(); using var motion = new NativeTouchpadMotion(sink);
        Assert.True(motion.Begin(Pad, 6000, 4000)); Assert.True(motion.Begin(Pad, 3000, 2000));
        Assert.Equal(new[] { TouchpadContactPhase.Down, TouchpadContactPhase.Cancel, TouchpadContactPhase.Down }, sink.Frames.Select(f => f.Item1));
    }
    private sealed class Sink : INativeTouchpadSink {
        public int CreateCount, DisposeCount;
        public TouchpadContactPhase? Fail;
        public bool Throws;
        public List<(TouchpadContactPhase, int, int)> Frames = new();
        public bool Create(TouchpadGeometry geometry) { CreateCount++; return true; }
        public bool Inject(TouchpadContactPhase phase, int x, int y) {
            Frames.Add((phase, x, y)); if(phase != Fail) return true;
            if(Throws) throw new InvalidOperationException("failure"); return false;
        }
        public void Dispose() => DisposeCount++;
    }
}
