using System;
using System.ComponentModel;

namespace TouchpadControl.Settings;

public readonly record struct DevicePointerValues(bool CursorMove, float Speed, float Acceleration);

/// <summary>A read-only constructor and validated, transactional device edits.</summary>
public class DevicePointerEditModel : INotifyPropertyChanged
{
    private DevicePointerValues _values;
    private readonly Func<DevicePointerValues, SettingsCommitResult> _commit;
    private readonly Action<string>? _reject;

    public DevicePointerEditModel(DevicePointerValues initial,
        Func<DevicePointerValues, SettingsCommitResult> commit, Action<string>? reject = null)
    {
        if(!ValidSpeed(initial.Speed) || !ValidAcceleration(initial.Acceleration))
            throw new ArgumentOutOfRangeException(nameof(initial), "Invalid device pointer settings.");
        _values = initial;
        _commit = commit ?? throw new ArgumentNullException(nameof(commit));
        _reject = reject;
    }

    public virtual bool CursorMoveProperty
    {
        get => _values.CursorMove;
        set { if(value != CursorMoveProperty) Commit(CurrentValues with { CursorMove = value }, nameof(CursorMoveProperty)); }
    }

    public virtual float CursorSpeedProperty
    {
        get => _values.Speed;
        set
        {
            if(!ValidSpeed(value)) { Reject(nameof(CursorSpeedProperty)); return; }
            if(value != CursorSpeedProperty) Commit(CurrentValues with { Speed = value }, nameof(CursorSpeedProperty));
        }
    }

    public virtual float CursorAccelerationProperty
    {
        get => _values.Acceleration;
        set
        {
            if(!ValidAcceleration(value)) { Reject(nameof(CursorAccelerationProperty)); return; }
            if(value != CursorAccelerationProperty) Commit(CurrentValues with { Acceleration = value }, nameof(CursorAccelerationProperty));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // Derived UI models read already-published global values. Compare those getters,
    // not a stale backing field, when a persistence event refreshes two-way controls.
    private DevicePointerValues CurrentValues => new(CursorMoveProperty, CursorSpeedProperty, CursorAccelerationProperty);

    private static bool ValidSpeed(float value) => SettingsValueRules.IsFiniteInRange(value,
        SettingsValueRules.CursorSpeedMinimum, SettingsValueRules.CursorSpeedMaximum);
    private static bool ValidAcceleration(float value) => SettingsValueRules.IsFiniteInRange(value,
        SettingsValueRules.CursorAccelerationMinimum, SettingsValueRules.CursorAccelerationMaximum);

    private void Reject(string propertyName)
    {
        _reject?.Invoke("Enter a valid number in the allowed range.");
        Notify(propertyName);
    }

    private void Commit(DevicePointerValues candidate, string propertyName)
    {
        if(_commit(candidate).Success) _values = candidate;
        // Failed writes and invalid inputs reset the control to the committed value.
        Notify(propertyName);
    }

    private void Notify(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
