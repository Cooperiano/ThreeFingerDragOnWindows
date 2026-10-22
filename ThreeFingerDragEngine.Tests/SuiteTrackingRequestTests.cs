using TouchpadControl.Pointer;
using Xunit;

public class SuiteTrackingRequestTests {
    private sealed class Backend : ITrackingBackend {
        public TrackingSettings Value = new(12,true); public int Applies;
        public TrackingSettings Read()=>Value;
        public object Capture(bool acceleration)=>Value;
        public void Apply(TrackingSettings desired,bool acceleration){Value=desired;Applies++;}
        public void Restore(object snapshot)=>Value=(TrackingSettings)snapshot;
        public bool Matches(object snapshot)=>Value==(TrackingSettings)snapshot;
    }
    [Fact] public void ReadAndNoOpNeverMutate() {
        var backend=new Backend();
        Assert.True(SuiteTrackingRequest.Execute(backend,null,null,false).Success);
        Assert.True(SuiteTrackingRequest.Execute(backend,new(12,true),new(12,true),false).Success);
        Assert.Equal(0,backend.Applies);
    }
    [Fact] public void ExternalChangeRejectsStaleDraft() {
        var backend=new Backend();
        Assert.False(SuiteTrackingRequest.Execute(backend,new(11,true),new(15,true),false).Success);
        Assert.Equal(0,backend.Applies);
    }
    [Fact] public void AccelerationRequiresSeparateExplicitConfirmation() {
        var backend=new Backend();
        Assert.False(SuiteTrackingRequest.Execute(backend,new(12,true),new(12,false),false).Success);
        Assert.Equal(0,backend.Applies);
        Assert.True(SuiteTrackingRequest.Execute(backend,new(12,true),new(12,false),true).Success);
        Assert.Equal(new(12,false),backend.Value);
    }
    [Theory] [InlineData(0)] [InlineData(21)]
    public void InvalidSpeedNeverMutates(int speed) {
        var backend=new Backend();
        Assert.False(SuiteTrackingRequest.Execute(backend,new(12,true),new(speed,true),true).Success);
        Assert.Equal(0,backend.Applies);
    }
}
