using ThreeFingerDragOnWindows.gestures;

namespace TouchpadControl.Pointer;

public interface IPointerProfileBackend
{
    PointerAccelerationProfile Read();
    void Apply(PointerAccelerationProfile profile);
}

public enum PointerProfileOutcome { Verified, ReadFailed, ApplyFailed, RollbackFailed, Busy, InvalidProfile, Mismatch }

public sealed record PointerProfileResult(PointerProfileOutcome Outcome,
    PointerAccelerationProfile? Observed = null, bool RollbackVerified = false, string? Error = null)
{
    public bool Success => Outcome == PointerProfileOutcome.Verified;
}

/// <summary>A successful request means a fresh OS read matches the requested values.
/// No preference is committed here. Callers persist intent only after verification.</summary>
public sealed class PointerProfileTransaction(IPointerProfileBackend backend)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<PointerProfileResult> ReadAsync()
    {
        if(!await gate.WaitAsync(0)) return new(PointerProfileOutcome.Busy);
        try
        {
            var observed = await Task.Run(backend.Read);
            return observed == observed.Normalize()
                ? new(PointerProfileOutcome.Verified, observed)
                : new(PointerProfileOutcome.ReadFailed, observed, Error: "OS returned unsupported pointer values.");
        }
        catch(Exception error) { return new(PointerProfileOutcome.ReadFailed, Error: error.Message); }
        finally { gate.Release(); }
    }

    public async Task<PointerProfileResult> VerifyAsync(PointerAccelerationProfile target)
    {
        if(target != target.Normalize()) return new(PointerProfileOutcome.InvalidProfile);
        if(!await gate.WaitAsync(0)) return new(PointerProfileOutcome.Busy);
        try
        {
            var observed = await Task.Run(backend.Read);
            if(observed != observed.Normalize())
                return new(PointerProfileOutcome.ReadFailed, observed, Error: "OS returned unsupported pointer values.");
            return new(observed == target ? PointerProfileOutcome.Verified : PointerProfileOutcome.Mismatch, observed);
        }
        catch(Exception error) { return new(PointerProfileOutcome.ReadFailed, Error: error.Message); }
        finally { gate.Release(); }
    }

    public async Task<PointerProfileResult> ApplyAsync(PointerAccelerationProfile target,
        Func<PointerAccelerationProfile, Task>? beforeApply = null, Func<Task>? afterVerify = null)
    {
        // Refuse corrupt backups rather than silently normalizing restoration.
        if(target != target.Normalize()) return new(PointerProfileOutcome.InvalidProfile);
        if(!await gate.WaitAsync(0)) return new(PointerProfileOutcome.Busy);
        try
        {
            PointerAccelerationProfile before;
            try { before = await Task.Run(backend.Read); }
            catch(Exception error) { return new(PointerProfileOutcome.ReadFailed, Error: error.Message); }
            if(before != before.Normalize())
                return new(PointerProfileOutcome.ReadFailed, before, Error: "OS returned unsupported pointer values.");
            try
            {
                // Backup persistence happens before the first OS write, on the
                // caller's context, not on a worker modifying UI-owned settings.
                if(beforeApply != null) await beforeApply(before);
            }
            catch(Exception error) { return new(PointerProfileOutcome.ReadFailed, before, Error: error.Message); }

            if(before == target)
            {
                try { if(afterVerify != null) await afterVerify(); }
                catch(Exception error) { return new(PointerProfileOutcome.ApplyFailed, before, true, error.Message); }
                return new(PointerProfileOutcome.Verified, before);
            }
            string failure;
            try
            {
                await Task.Run(() => backend.Apply(target));
                var observed = await Task.Run(backend.Read);
                if(observed == target)
                {
                    if(afterVerify != null) await afterVerify();
                    return new(PointerProfileOutcome.Verified, observed);
                }
                failure = "OS readback differs from requested values.";
            }
            catch(Exception error) { failure = error.Message; }

            // A failed API may already have changed a subset of the values.
            // Roll back the pre-request snapshot and independently verify it.
            try
            {
                await Task.Run(() => backend.Apply(before));
                var observed = await Task.Run(backend.Read);
                return observed == before
                    ? new(PointerProfileOutcome.ApplyFailed, observed, true, failure)
                    : new(PointerProfileOutcome.RollbackFailed, observed, false, failure);
            }
            catch(Exception rollbackError)
            {
                return new(PointerProfileOutcome.RollbackFailed, Error: failure + " Rollback: " + rollbackError.Message);
            }
        }
        finally { gate.Release(); }
    }
}
