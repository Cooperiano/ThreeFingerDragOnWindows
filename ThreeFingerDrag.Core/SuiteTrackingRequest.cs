namespace TouchpadControl.Pointer;

public static class SuiteTrackingRequest {
    public static TrackingChangeResult Execute(ITrackingBackend backend, TrackingSettings? expected,
        TrackingSettings? desired, bool accelerationConfirmed) {
        try {
            var current=backend.Read();
            if(desired is null) return new(true,current,false,"");
            if(expected is null || expected.Value!=current)
                return new(false,current,false,"Windows tracking settings changed. Reload before saving.");
            if(desired.Value.Speed is <1 or >20) return new(false,current,false,"Invalid tracking speed.");
            if(desired.Value==current) return new(true,current,false,"");
            bool changeAcceleration=current.Acceleration!=desired.Value.Acceleration;
            if(changeAcceleration && !accelerationConfirmed)
                return new(false,current,false,"Acceleration changes require explicit confirmation because they also affect mouse acceleration.");
            return TrackingSettingsTransaction.Change(backend,desired.Value,changeAcceleration);
        } catch(Exception error) { return new(false,null,false,error.Message); }
    }
}
