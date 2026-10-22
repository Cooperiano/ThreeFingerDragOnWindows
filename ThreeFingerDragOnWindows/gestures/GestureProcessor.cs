using System;
using System.Collections.Generic;
using System.Linq;

namespace ThreeFingerDragOnWindows.gestures;

public readonly record struct GestureContact(int Id, double X, double Y);

public readonly record struct GestureDelta(double X, double Y) {
    public double Length => Math.Sqrt((X * X) + (Y * Y));
}

public enum GestureState {
    Idle,
    ThreeFingerCandidate,
    Dragging,
    FourFingerCandidate,
    FourFingerOverview,
    LockedUntilLift
}

public enum GestureActionKind {
    DragStarted,
    DragMoved,
    DragEnded,
    FourFingerSwipe
}

public enum FourFingerGesture {
    None,
    PreviousDesktop,
    NextDesktop,
    TaskView,
    ShowDesktop,
    SelectLeft,
    SelectRight,
    SelectUp,
    SelectDown,
    ActivateSelection,
    CancelOverview
}

public readonly record struct GestureAction(
    GestureActionKind Kind,
    double DeltaX = 0,
    double DeltaY = 0,
    FourFingerGesture FourFingerGesture = FourFingerGesture.None,
    double VelocityDistance = -1,
    long VelocityElapsedMs = 0);

public sealed class GestureOptions {
    public double DragActivationDistance { get; set; } = 32;
    public double DragIntentDistance { get; set; } = 8;
    public double DragIntentVelocity { get; set; } = 0.25;
    public double DragQuickActivationRatio { get; set; } = 0.68;
    public double DragQuickActivationVelocity { get; set; } = 1.0;
    public int DragMinimumContactDurationMs { get; set; } = 28;
    public double FourFingerSwipeDistance { get; set; } = 90;
    public double FourFingerQuickSwipeDistance { get; set; } = 42;
    public double FourFingerQuickSwipeVelocity { get; set; } = 1.25;
    public double FourFingerTapMaxDistance { get; set; } = 35;
    public double FourFingerSelectionStep { get; set; } = 55;
    public int FourFingerTapMaxDurationMs { get; set; } = 350;
    public int FourFingerOverviewSettleMs { get; set; } = 130;
    public int FourFingerSelectionMinIntervalMs { get; set; } = 85;
    public double AxisDominanceRatio { get; set; } = 1.25;
}

/// <summary>
/// Converts complete touchpad frames into mutually-exclusive drag and four-finger actions.
/// It uses the median per-contact translation so one noisy contact cannot move the cursor.
/// </summary>
public sealed class GestureProcessor {
    private readonly GestureOptions _options;
    private readonly Queue<long> _recentFrameIntervals = new();
    private GestureContact[] _origin = Array.Empty<GestureContact>();
    private GestureContact[] _previous = Array.Empty<GestureContact>();
    private long? _lastTimestamp;
    private long _threeFingerStartedAt;
    private long _threeFingerPreviousTimestamp;
    private long _fourFingerPreviousTimestamp;
    private long _overviewEnteredAt;
    private long _lastSelectionAt;
    private double _selectionX;
    private double _selectionY;
    private long? _fourFingerDropoutStartedAt;
    private bool _cancelledUntilAllLift;
    // Tracks an emitted open request, not whether the helper has actually shown
    // its window. It survives hand lift so an explicit stop can still cancel it.
    private bool _overviewRequested;

    public GestureProcessor(GestureOptions? options = null) {
        _options = options ?? new GestureOptions();
    }

    public GestureState State { get; private set; } = GestureState.Idle;

    public int AdaptiveReleaseTimeoutMs {
        get {
            if(_recentFrameIntervals.Count == 0) return 40;
            var values = _recentFrameIntervals.OrderBy(value => value).ToArray();
            var median = values[values.Length / 2];
            return (int)Math.Clamp(median * 4, 35, 120);
        }
    }

    public IReadOnlyList<GestureAction> Process(
        long timestampMs,
        IReadOnlyList<GestureContact> contacts,
        bool threeFingerEnabled = true,
        bool fourFingerEnabled = true) {
        ObserveFrameInterval(timestampMs);
        var frame = contacts.OrderBy(contact => contact.Id).ToArray();
        var actions = new List<GestureAction>(2);

        // Explicitly disabled features are cancelled before processing contacts.
        // Ordinary hand lift below only ends tracking, never confirms a window.
        actions.AddRange(CancelDisabledGestures(threeFingerEnabled, fourFingerEnabled));
        if(_cancelledUntilAllLift) {
            if(frame.Length == 0) Reset();
            return actions;
        }
        if(State == GestureState.LockedUntilLift) {
            if(frame.Length == 0) Reset();
            return actions;
        }

        if((State == GestureState.FourFingerCandidate || State == GestureState.FourFingerOverview)
           && frame.Length > 0 && frame.Length < 4) {
            _fourFingerDropoutStartedAt ??= timestampMs;
            if(timestampMs - _fourFingerDropoutStartedAt.Value < AdaptiveReleaseTimeoutMs) return actions;
            Reset();
            State = GestureState.LockedUntilLift;
            return actions;
        }
        _fourFingerDropoutStartedAt = null;

        if(frame.Length < 3) {
            if(State == GestureState.Dragging) actions.Add(new GestureAction(GestureActionKind.DragEnded));
            Reset();
            return actions;
        }

        if(frame.Length == 4 && fourFingerEnabled) {
            if(State == GestureState.Dragging) actions.Add(new GestureAction(GestureActionKind.DragEnded));
            if(State == GestureState.FourFingerOverview) {
                UpdateFourFingerOverview(timestampMs, frame.Take(4).ToArray(), actions);
            } else if(State != GestureState.FourFingerCandidate) {
                Begin(GestureState.FourFingerCandidate, frame.Take(4).ToArray());
                _fourFingerPreviousTimestamp = timestampMs;
            }
            else TryCompleteFourFingerSwipe(timestampMs, frame.Take(4).ToArray(), actions);
            return actions;
        }

        if(frame.Length != 3 || !threeFingerEnabled) {
            if(State == GestureState.Dragging) actions.Add(new GestureAction(GestureActionKind.DragEnded));
            State = GestureState.LockedUntilLift;
            return actions;
        }

        if(State == GestureState.FourFingerCandidate) {
            // Precision touchpads can briefly report 3 contacts while all 4 fingers
            // are still down. Keep the candidate alive until the actual release.
            return actions;
        }

        if(State == GestureState.FourFingerOverview) {
            // Contact dropout is handled above without confirming or closing.
            return actions;
        }

        if(State == GestureState.Idle) {
            Begin(GestureState.ThreeFingerCandidate, frame);
            _threeFingerStartedAt = timestampMs;
            _threeFingerPreviousTimestamp = timestampMs;
            return actions;
        }

        if(!HasCommonContacts(_previous, frame, 3)) {
            if(State == GestureState.Dragging) actions.Add(new GestureAction(GestureActionKind.DragEnded));
            Begin(GestureState.ThreeFingerCandidate, frame);
            _threeFingerStartedAt = timestampMs;
            _threeFingerPreviousTimestamp = timestampMs;
            return actions;
        }

        if(State == GestureState.ThreeFingerCandidate) {
            var total = GetRobustDelta(_origin, frame);
            var frameDelta = GetRobustDelta(_previous, frame);
            var frameElapsedMs = Math.Max(1, timestampMs - _threeFingerPreviousTimestamp);
            var contactDurationMs = timestampMs - _threeFingerStartedAt;
            var quickDistance = _options.DragActivationDistance * _options.DragQuickActivationRatio;
            var isCoherent = total.Length == 0
                || ((total.X * frameDelta.X) + (total.Y * frameDelta.Y)) >= 0;
            var isQuickIntent = total.Length >= quickDistance
                && frameDelta.Length / frameElapsedMs >= _options.DragQuickActivationVelocity
                && isCoherent;
            var isNormalIntent = total.Length >= _options.DragActivationDistance;
            var intentDistance = Math.Min(_options.DragActivationDistance, _options.DragIntentDistance);
            var isRealtimeIntent = total.Length >= intentDistance
                && frameDelta.Length / frameElapsedMs >= _options.DragIntentVelocity
                && isCoherent;

            if(contactDurationMs >= _options.DragMinimumContactDurationMs
               && (isNormalIntent || isQuickIntent || isRealtimeIntent)) {
                State = GestureState.Dragging;
                actions.Add(new GestureAction(GestureActionKind.DragStarted));
            }
        } else if(State == GestureState.Dragging) {
            var delta = GetRobustDelta(_previous, frame);
            var frameElapsedMs = Math.Max(1, timestampMs - _threeFingerPreviousTimestamp);
            if(delta.X != 0 || delta.Y != 0) {
                actions.Add(new GestureAction(
                    GestureActionKind.DragMoved,
                    delta.X,
                    delta.Y,
                    VelocityDistance: delta.Length,
                    VelocityElapsedMs: frameElapsedMs));
            }
        }

        _previous = frame;
        _threeFingerPreviousTimestamp = timestampMs;
        return actions;
    }

    public IReadOnlyList<GestureAction> ForceRelease() {
        if(_cancelledUntilAllLift && !_overviewRequested) return Array.Empty<GestureAction>();
        return CancelCurrentGesture();
    }

    /// <summary>Ends only drag tracking, without cancelling a persistent overview request.</summary>
    public IReadOnlyList<GestureAction> EndDragTracking() {
        return State == GestureState.ThreeFingerCandidate || State == GestureState.Dragging
            ? CancelCurrentGesture(cancelOverview: false)
            : Array.Empty<GestureAction>();
    }

    /// <summary>Immediately ends only a gesture whose feature was disabled.</summary>
    public IReadOnlyList<GestureAction> CancelDisabledGestures(bool threeFingerEnabled, bool fourFingerEnabled) {
        var cancelled = (!threeFingerEnabled && (State == GestureState.ThreeFingerCandidate || State == GestureState.Dragging))
            || (!fourFingerEnabled && (State == GestureState.FourFingerCandidate || State == GestureState.FourFingerOverview));
        if(cancelled) return CancelCurrentGesture(cancelOverview: !fourFingerEnabled);
        if(!fourFingerEnabled && _overviewRequested) {
            _overviewRequested = false;
            // The overview can outlive its original contacts. Closing that
            // request must not reset an unrelated, still-enabled drag session.
            return new[] { new GestureAction(
                GestureActionKind.FourFingerSwipe, FourFingerGesture: FourFingerGesture.CancelOverview) };
        }
        return Array.Empty<GestureAction>();
    }

    private IReadOnlyList<GestureAction> CancelCurrentGesture(bool cancelOverview = true) {
        var priorState = State;
        var cancelRequestedOverview = cancelOverview && _overviewRequested;
        if(cancelRequestedOverview) _overviewRequested = false;
        Reset();
        if(priorState != GestureState.Idle) {
            State = GestureState.LockedUntilLift;
            _cancelledUntilAllLift = true;
        }

        var actions = new List<GestureAction>(2);
        if(priorState == GestureState.Dragging) actions.Add(new GestureAction(GestureActionKind.DragEnded));
        if(cancelRequestedOverview) actions.Add(new GestureAction(
            GestureActionKind.FourFingerSwipe, FourFingerGesture: FourFingerGesture.CancelOverview));
        return actions;
    }

    public static GestureDelta GetRobustDelta(
        IReadOnlyList<GestureContact> previous,
        IReadOnlyList<GestureContact> current) {
        var priorById = previous.ToDictionary(contact => contact.Id);
        var deltas = current
            .Where(contact => priorById.ContainsKey(contact.Id))
            .Select(contact => new GestureDelta(
                contact.X - priorById[contact.Id].X,
                contact.Y - priorById[contact.Id].Y))
            .ToArray();

        if(deltas.Length == 0) return default;
        return new GestureDelta(Median(deltas.Select(delta => delta.X)), Median(deltas.Select(delta => delta.Y)));
    }

    private void TryCompleteFourFingerSwipe(
        long timestampMs,
        GestureContact[] frame,
        List<GestureAction> actions) {
        if(!HasCommonContacts(_origin, frame, 4)) {
            Begin(GestureState.FourFingerCandidate, frame);
            _fourFingerPreviousTimestamp = timestampMs;
            return;
        }

        var delta = GetRobustDelta(_origin, frame);
        var frameDelta = GetRobustDelta(_previous, frame);
        var elapsedMs = Math.Max(1, timestampMs - _fourFingerPreviousTimestamp);
        var absX = Math.Abs(delta.X);
        var absY = Math.Abs(delta.Y);
        var xIsDominant = absX >= absY * _options.AxisDominanceRatio;
        var yIsDominant = absY >= absX * _options.AxisDominanceRatio;
        var quickHorizontal = xIsDominant
            && absX >= _options.FourFingerQuickSwipeDistance
            && Math.Abs(frameDelta.X) / elapsedMs >= _options.FourFingerQuickSwipeVelocity
            && Math.Sign(frameDelta.X) == Math.Sign(delta.X);
        var quickVertical = yIsDominant
            && absY >= _options.FourFingerQuickSwipeDistance
            && Math.Abs(frameDelta.Y) / elapsedMs >= _options.FourFingerQuickSwipeVelocity
            && Math.Sign(frameDelta.Y) == Math.Sign(delta.Y);
        FourFingerGesture gesture = FourFingerGesture.None;

        if(xIsDominant && (absX >= _options.FourFingerSwipeDistance || quickHorizontal)) {
            gesture = delta.X < 0 ? FourFingerGesture.NextDesktop : FourFingerGesture.PreviousDesktop;
        } else if(yIsDominant && (absY >= _options.FourFingerSwipeDistance || quickVertical)) {
            gesture = delta.Y < 0 ? FourFingerGesture.TaskView : FourFingerGesture.ShowDesktop;
        }

        if(gesture != FourFingerGesture.None) {
            actions.Add(new GestureAction(GestureActionKind.FourFingerSwipe, FourFingerGesture: gesture));
            if(gesture == FourFingerGesture.TaskView) {
                _overviewRequested = true;
                State = GestureState.FourFingerOverview;
                _overviewEnteredAt = timestampMs;
                _lastSelectionAt = timestampMs;
                _selectionX = 0;
                _selectionY = 0;
            } else {
                State = GestureState.LockedUntilLift;
            }
        }

        _previous = frame;
        _fourFingerPreviousTimestamp = timestampMs;
    }

    private void UpdateFourFingerOverview(
        long timestampMs,
        GestureContact[] frame,
        List<GestureAction> actions) {
        if(!HasCommonContacts(_previous, frame, 4)) {
            _previous = frame;
            _selectionX = 0;
            _selectionY = 0;
            return;
        }

        var delta = GetRobustDelta(_previous, frame);
        if(timestampMs - _overviewEnteredAt < _options.FourFingerOverviewSettleMs) {
            // Do not reinterpret the tail of the opening swipe as window navigation.
            _selectionX = 0;
            _selectionY = 0;
            _previous = frame;
            return;
        }

        _selectionX += delta.X;
        _selectionY += delta.Y;

        if(Math.Abs(_selectionX) >= Math.Abs(_selectionY) * _options.AxisDominanceRatio) {
            TryAddSelectionStep(
                timestampMs,
                actions,
                ref _selectionX,
                _selectionX < 0 ? FourFingerGesture.SelectLeft : FourFingerGesture.SelectRight);
            _selectionY = 0;
        } else if(Math.Abs(_selectionY) >= Math.Abs(_selectionX) * _options.AxisDominanceRatio) {
            TryAddSelectionStep(
                timestampMs,
                actions,
                ref _selectionY,
                _selectionY < 0 ? FourFingerGesture.SelectUp : FourFingerGesture.SelectDown);
            _selectionX = 0;
        }

        _previous = frame;
    }

    private void TryAddSelectionStep(
        long timestampMs,
        List<GestureAction> actions,
        ref double accumulatedDistance,
        FourFingerGesture gesture) {
        if(Math.Abs(accumulatedDistance) < _options.FourFingerSelectionStep) return;

        var direction = Math.Sign(accumulatedDistance);
        if(timestampMs - _lastSelectionAt < _options.FourFingerSelectionMinIntervalMs) {
            // Preserve one pending step, but discard a large raw-coordinate burst.
            accumulatedDistance = direction * _options.FourFingerSelectionStep;
            return;
        }

        actions.Add(new GestureAction(
            GestureActionKind.FourFingerSwipe,
            FourFingerGesture: gesture));
        accumulatedDistance -= direction * _options.FourFingerSelectionStep;
        accumulatedDistance = Math.Clamp(
            accumulatedDistance,
            -_options.FourFingerSelectionStep,
            _options.FourFingerSelectionStep);
        _lastSelectionAt = timestampMs;
    }

    private void Begin(GestureState state, GestureContact[] frame) {
        State = state;
        _origin = frame;
        _previous = frame;
    }

    private void Reset() {
        // This resets input tracking only. An overview open request persists
        // until an explicit feature cancellation or global release consumes it.
        State = GestureState.Idle;
        _origin = Array.Empty<GestureContact>();
        _previous = Array.Empty<GestureContact>();
        _threeFingerStartedAt = 0;
        _threeFingerPreviousTimestamp = 0;
        _fourFingerPreviousTimestamp = 0;
        _overviewEnteredAt = 0;
        _lastSelectionAt = 0;
        _selectionX = 0;
        _selectionY = 0;
        _fourFingerDropoutStartedAt = null;
        _cancelledUntilAllLift = false;
    }

    private void ObserveFrameInterval(long timestampMs) {
        if(_lastTimestamp is { } last && timestampMs > last) {
            var interval = timestampMs - last;
            // Long pauses are contact breaks, not a sample of the hardware report cadence.
            if(interval <= 100) {
                _recentFrameIntervals.Enqueue(interval);
                while(_recentFrameIntervals.Count > 9) _recentFrameIntervals.Dequeue();
            }
        }
        _lastTimestamp = timestampMs;
    }

    private static bool HasCommonContacts(
        IReadOnlyList<GestureContact> first,
        IReadOnlyList<GestureContact> second,
        int expectedCount) {
        var secondIds = second.Select(contact => contact.Id).ToHashSet();
        return first.Count(contact => secondIds.Contains(contact.Id)) >= expectedCount;
    }

    private static double Median(IEnumerable<double> values) {
        var sorted = values.OrderBy(value => value).ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2
            : sorted[middle];
    }
}
