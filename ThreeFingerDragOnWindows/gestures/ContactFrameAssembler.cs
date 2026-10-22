using System;
using System.Collections.Generic;
using System.Linq;

namespace ThreeFingerDragOnWindows.gestures;

/// <summary>Assembles Precision Touchpad hybrid reports without inventing contacts.</summary>
public sealed class ContactFrameAssembler<T>(Func<T, int> contactId) {
    private readonly Dictionary<int, T> _pending = new();
    private uint _expectedCount;
    private uint? _scanTime;
    public bool HasPendingFrame => _expectedCount > 0;

    public bool TryAssemble(uint count, uint? scanTime, IReadOnlyList<T> contacts, out T[] frame) {
        frame = Array.Empty<T>();
        if(count > 0) {
            Reset();
            if(count > 32) return false;
            _expectedCount = count;
            _scanTime = scanTime;
        } else if(contacts.Count == 0) {
            Reset();
            return true;
        } else if(_expectedCount == 0) {
            return false;
        } else if(_scanTime.HasValue && scanTime.HasValue && _scanTime != scanTime) {
            Reset();
            return false;
        }

        foreach(var contact in contacts) _pending[contactId(contact)] = contact;
        if(_pending.Count > _expectedCount) {
            Reset();
            return false;
        }
        if(_pending.Count != _expectedCount) return false;
        frame = _pending.Values.OrderBy(contactId).ToArray();
        Reset();
        return true;
    }

    public void Reset() {
        _pending.Clear();
        _expectedCount = 0;
        _scanTime = null;
    }
}
