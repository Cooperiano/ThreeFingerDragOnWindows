using ThreeFingerDragOnWindows.gestures;
using TouchpadControl.Pointer;
using Xunit;

public class PointerProfileObservationTests
{
    [Fact]
    public async Task ReadWithoutRestorePointObservesWindowsWithoutApplying()
    {
        var backend = new ObservationBackend { Profile = new(8, 2, 5, 1, 20) };
        var result = await new PointerProfileTransaction(backend).ReadAsync();
        Assert.True(result.Success);
        Assert.Equal(backend.Profile, result.Observed);
        Assert.Equal(1, backend.Reads);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task InvalidReadbackDoesNotLookVerified()
    {
        var backend = new ObservationBackend { Profile = new(8, 2, 5, 1, 21) };
        var result = await new PointerProfileTransaction(backend).ReadAsync();
        Assert.Equal(PointerProfileOutcome.ReadFailed, result.Outcome);
        Assert.Equal(0, backend.Writes);
    }

    [Fact]
    public async Task ReadFailureDoesNotAttemptRestoration()
    {
        var backend = new ObservationBackend { FailRead = true };
        var result = await new PointerProfileTransaction(backend).ReadAsync();
        Assert.Equal(PointerProfileOutcome.ReadFailed, result.Outcome);
        Assert.Equal(0, backend.Writes);
    }

    private sealed class ObservationBackend : IPointerProfileBackend
    {
        public PointerAccelerationProfile Profile { get; init; }
        public bool FailRead { get; init; }
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public PointerAccelerationProfile Read()
        {
            Reads++;
            if(FailRead) throw new IOException("Unavailable");
            return Profile;
        }
        public void Apply(PointerAccelerationProfile profile) => Writes++;
    }
}
