#nullable enable
using System;
using System.Threading.Tasks;
using ThreeFingerDragOnWindows.gestures;
using ThreeFingerDragOnWindows.settings;
using TouchpadControl.Pointer;

namespace ThreeFingerDragOnWindows.utils;

/// <summary>UI-owned intent and independently verified native settings.</summary>
public static class PointerTuning
{
    public enum OperationKind { Observation, Apply, Restore, PreferenceOnly }
    public static OperationKind LastOperation { get; private set; }
    private static readonly WindowsPointerProfileBackend Backend = new();
    private static readonly PointerProfileTransaction Transaction = new(Backend);
    public static bool IsBusy { get; private set; }
    public static bool? PendingEnabled { get; private set; }
    public static bool? VerifiedEnabled { get; private set; }
    public static PointerProfileResult? LastResult { get; private set; }
    public static event Action? StateChanged;

    public static async Task VerifyAsync(SettingsData settings)
    {
        if(IsBusy) return;
        IsBusy = true;
        LastOperation = OperationKind.Observation;
        VerifiedEnabled = null;
        LastResult = null;
        try
        {
            StateChanged?.Invoke();
            if(!settings.MacLikePointerTuning && !settings.PointerBackupCaptured)
            {
                LastResult = await Transaction.ReadAsync();
                if(LastResult.Success) VerifiedEnabled = false;
                return;
            }
            var target = settings.MacLikePointerTuning ? PointerAccelerationProfile.WindowsNative
                : new PointerAccelerationProfile(settings.PointerBackupSpeed, settings.PointerBackupThreshold1,
                    settings.PointerBackupThreshold2, settings.PointerBackupAccelerationLevel, settings.PointerBackupTouchpadSpeed);
            LastResult = await Transaction.VerifyAsync(target);
            if(LastResult.Success) VerifiedEnabled = settings.MacLikePointerTuning;
            RuntimeDiagnostics.Write("pointer-check", $"outcome={LastResult.Outcome}; transport={Backend.Transport}; observed={LastResult.Observed}");
        }
        catch(Exception error)
        {
            LastResult = new(PointerProfileOutcome.ReadFailed, Error: error.Message);
            RuntimeDiagnostics.Write("pointer-check", error.ToString());
        }
        finally { IsBusy = false; StateChanged?.Invoke(); }
    }

    public static async Task<bool> ChangeAsync(SettingsData settings, bool enable)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if(IsBusy) return false;
        IsBusy = true;
        PendingEnabled = enable;
        VerifiedEnabled = null;
        LastResult = null;
        var previousIntent = settings.MacLikePointerTuning;
        LastOperation = enable ? OperationKind.Apply : OperationKind.Restore;
        try
        {
            StateChanged?.Invoke();
            if(!enable && !settings.PointerBackupCaptured)
            {
                // No profile was applied by this app, so there is nothing to
                // restore. Commit only the off choice; leave Windows untouched.
                LastOperation = OperationKind.PreferenceOnly;
                var committed = settings.TryCommit(candidate => candidate.MacLikePointerTuning = false);
                if(!committed.Success)
                {
                    LastResult = new(PointerProfileOutcome.ApplyFailed, Error: committed.Error);
                    return false;
                }
                LastResult = await Transaction.ReadAsync();
                if(LastResult.Success) VerifiedEnabled = false;
                return true;
            }
            var target = enable ? PointerAccelerationProfile.WindowsNative : new PointerAccelerationProfile(
                settings.PointerBackupSpeed, settings.PointerBackupThreshold1, settings.PointerBackupThreshold2,
                settings.PointerBackupAccelerationLevel, settings.PointerBackupTouchpadSpeed);
            LastResult = await Transaction.ApplyAsync(target, before =>
            {
                if(enable && !settings.PointerBackupCaptured)
                {
                    settings.PointerBackupSpeed = before.PointerSpeed;
                    settings.PointerBackupThreshold1 = before.Threshold1;
                    settings.PointerBackupThreshold2 = before.Threshold2;
                    settings.PointerBackupAccelerationLevel = before.AccelerationLevel;
                    settings.PointerBackupTouchpadSpeed = before.TouchpadCursorSpeed;
                    settings.PointerBackupCaptured = true;
                    try { settings.save(); }
                    catch { settings.PointerBackupCaptured = false; throw; }
                }
                return Task.CompletedTask;
            }, () =>
            {
                settings.MacLikePointerTuning = enable;
                try { if(previousIntent != enable) settings.save(); }
                catch { settings.MacLikePointerTuning = previousIntent; throw; }
                return Task.CompletedTask;
            });
            if(LastResult.Success) VerifiedEnabled = enable;
            RuntimeDiagnostics.Write("pointer-profile",
                $"outcome={LastResult.Outcome}; enabled={enable}; rollback={LastResult.RollbackVerified}; " +
                $"transport={Backend.Transport}; observed={LastResult.Observed}; error={LastResult.Error}");
            return LastResult.Success;
        }
        catch(Exception error)
        {
            settings.MacLikePointerTuning = previousIntent;
            LastResult = new(PointerProfileOutcome.ApplyFailed, Error: error.Message);
            RuntimeDiagnostics.Write("pointer-profile", error.ToString());
            return false;
        }
        finally
        {
            IsBusy = false;
            PendingEnabled = null;
            StateChanged?.Invoke();
        }
    }
}
