using System;

namespace ThreeFingerDragOnWindows.gestures;

/// <summary>
/// Estimates physical pointer velocity over a short, time-based window.
/// A time constant keeps the response independent of touchpad report rate.
/// </summary>
public sealed class PointerVelocityFilter {
    private readonly double _attackTimeConstantMs;
    private readonly double _releaseTimeConstantMs;
    private readonly long _minimumSampleDurationMs;
    private readonly double _maximumVelocity;
    private double _velocity;

    public PointerVelocityFilter(
        double attackTimeConstantMs = 12,
        double releaseTimeConstantMs = 28,
        long minimumSampleDurationMs = 4,
        double maximumVelocity = 4) {
        _attackTimeConstantMs = Math.Max(1, attackTimeConstantMs);
        _releaseTimeConstantMs = Math.Max(1, releaseTimeConstantMs);
        _minimumSampleDurationMs = Math.Max(1, minimumSampleDurationMs);
        _maximumVelocity = Math.Max(0.1, maximumVelocity);
    }

    public double Update(double distance, long elapsedMs) {
        // HID stacks can deliver the first reports in a short burst. Treating a
        // 1 ms scheduling interval as physical motion creates a false speed peak.
        var duration = Math.Max(_minimumSampleDurationMs, elapsedMs);
        var instantaneousVelocity = Math.Min(
            Math.Max(0, distance) / duration,
            _maximumVelocity);
        var timeConstant = instantaneousVelocity > _velocity
            ? _attackTimeConstantMs
            : _releaseTimeConstantMs;
        var blend = 1 - Math.Exp(-duration / timeConstant);
        _velocity += (instantaneousVelocity - _velocity) * blend;
        return _velocity;
    }

    public void Reset() {
        _velocity = 0;
    }
}
