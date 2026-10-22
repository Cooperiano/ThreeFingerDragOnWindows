using ThreeFingerDragOnWindows.gestures;
using Xunit;

namespace ThreeFingerDragEngine.Tests;

public class GestureProcessorTests {
    private static GestureContact[] Frame(params (int id, double x, double y)[] points) =>
        points.Select(p => new GestureContact(p.id, p.x, p.y)).ToArray();

    [Fact]
    public void RobustDelta_UsesMedianAndRejectsOneFingerJump() {
        var previous = Frame((1, 0, 0), (2, 10, 10), (3, 20, 20));
        var current = Frame((1, 10, 4), (2, 21, 15), (3, 1020, -800));

        var delta = GestureProcessor.GetRobustDelta(previous, current);

        Assert.Equal(11, delta.X);
        Assert.Equal(4, delta.Y);
    }

    [Fact]
    public void ThreeFingerMovement_StartsDragOnceAndMovesUsingRobustDelta() {
        var processor = new GestureProcessor(new GestureOptions {
            DragActivationDistance = 20,
            DragMinimumContactDurationMs = 20
        });

        Assert.Empty(processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0))));
        var start = processor.Process(20, Frame((1, 25, 0), (2, 35, 1), (3, 45, -1)));
        var move = processor.Process(30, Frame((1, 31, 3), (2, 41, 4), (3, 51, 2)));

        Assert.Single(start, action => action.Kind == GestureActionKind.DragStarted);
        Assert.DoesNotContain(start, action => action.Kind == GestureActionKind.DragMoved);
        var moved = Assert.Single(move, action => action.Kind == GestureActionKind.DragMoved);
        Assert.Equal(6, moved.DeltaX);
        Assert.Equal(3, moved.DeltaY);
        Assert.InRange(moved.VelocityDistance, 6.70, 6.71);
        Assert.Equal(10, moved.VelocityElapsedMs);
    }

    [Fact]
    public void AddingFourthFinger_EndsDragBeforeTrackingSwipe() {
        var processor = new GestureProcessor(new GestureOptions {
            DragActivationDistance = 5,
            DragMinimumContactDurationMs = 0,
            FourFingerSwipeDistance = 30
        });
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0)));
        processor.Process(10, Frame((1, 8, 0), (2, 18, 0), (3, 28, 0)));

        var handoff = processor.Process(20, Frame((1, 8, 0), (2, 18, 0), (3, 28, 0), (4, 38, 0)));
        var swipe = processor.Process(30, Frame((1, -32, 0), (2, -22, 0), (3, -12, 0), (4, -2, 0)));

        Assert.Single(handoff, action => action.Kind == GestureActionKind.DragEnded);
        Assert.DoesNotContain(handoff, action => action.Kind == GestureActionKind.DragMoved);
        Assert.Equal(FourFingerGesture.NextDesktop,
            Assert.Single(swipe, action => action.Kind == GestureActionKind.FourFingerSwipe).FourFingerGesture);
    }

    [Fact]
    public void ThreeFingerMovement_WaitsForStableContactBeforeMouseDown() {
        var processor = new GestureProcessor(new GestureOptions {
            DragActivationDistance = 32,
            DragMinimumContactDurationMs = 28
        });
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0)));

        var early = processor.Process(15, Frame((1, 50, 0), (2, 60, 0), (3, 70, 0)));
        var fourthFinger = processor.Process(20, Frame((1, 50, 0), (2, 60, 0), (3, 70, 0), (4, 80, 0)));

        Assert.Empty(early);
        Assert.DoesNotContain(fourthFinger, action => action.Kind == GestureActionKind.DragStarted);
    }

    [Fact]
    public void ThreeFingerMovement_QuickCoherentIntentStartsEarlyAndCarriesMotion() {
        var processor = new GestureProcessor(new GestureOptions {
            DragActivationDistance = 32,
            DragQuickActivationRatio = 0.68,
            DragQuickActivationVelocity = 1,
            DragMinimumContactDurationMs = 28
        });
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0)));
        Assert.Empty(processor.Process(14, Frame((1, 10, 0), (2, 20, 0), (3, 30, 0))));

        var actions = processor.Process(28, Frame((1, 25, 0), (2, 35, 0), (3, 45, 0)));

        Assert.Single(actions, action => action.Kind == GestureActionKind.DragStarted);
        Assert.DoesNotContain(actions, action => action.Kind == GestureActionKind.DragMoved);
    }

    [Fact]
    public void ThreeFingerMovement_DoesNotReplayCandidateMotionAfterCommit() {
        var processor = new GestureProcessor(new GestureOptions {
            DragActivationDistance = 20,
            DragMinimumContactDurationMs = 20,
            DragIntentDistance = 8,
            DragIntentVelocity = 0.25
        });
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0)));

        var commit = processor.Process(20, Frame((1, 12, 0), (2, 22, 0), (3, 32, 0)));
        var stationary = processor.Process(30, Frame((1, 12, 0), (2, 22, 0), (3, 32, 0)));
        var realtime = processor.Process(40, Frame((1, 17, 0), (2, 27, 0), (3, 37, 0)));

        Assert.Single(commit, action => action.Kind == GestureActionKind.DragStarted);
        Assert.DoesNotContain(commit, action => action.Kind == GestureActionKind.DragMoved);
        Assert.Empty(stationary);
        Assert.Equal(5, Assert.Single(realtime).DeltaX);
    }

    [Fact]
    public void PointerVelocityFilter_UsesFullDurationAndRejectsOneFrameSpike() {
        var filter = new PointerVelocityFilter();

        var first = filter.Update(20, 20);
        var afterOneMillisecondSpike = filter.Update(100, 1);

        Assert.InRange(first, 0.81, 0.82);
        Assert.InRange(afterOneMillisecondSpike, 1.70, 1.73);
        Assert.True(afterOneMillisecondSpike < 4);
    }

    [Fact]
    public void PointerVelocityFilter_IsStableAcrossDifferentReportIntervals() {
        var filter = new PointerVelocityFilter();

        var fiveMs = filter.Update(5, 5);
        var tenMs = filter.Update(10, 10);
        var twentyMs = filter.Update(20, 20);

        Assert.True(fiveMs < tenMs);
        Assert.True(tenMs < twentyMs);
        Assert.InRange(twentyMs, 0.94, 0.95);
    }

    [Fact]
    public void PointerVelocityFilter_StationaryReportsRemoveOldAccelerationBeforeRestart() {
        var filter = new PointerVelocityFilter();
        for(var index = 0; index < 10; index++) filter.Update(40, 10);
        var peak = filter.Update(40, 10);
        for(var index = 0; index < 10; index++) filter.Update(0, 10);
        var restarted = filter.Update(1, 10);
        Assert.True(peak > 3.9);
        Assert.InRange(restarted, 0, 0.12);
    }

    [Fact]
    public void ThreeFingerMovement_ReversalDoesNotUseQuickActivation() {
        var processor = new GestureProcessor(new GestureOptions {
            DragActivationDistance = 40,
            DragQuickActivationRatio = 0.5,
            DragQuickActivationVelocity = 1,
            DragMinimumContactDurationMs = 20
        });
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0)));
        Assert.Empty(processor.Process(10, Frame((1, 35, 0), (2, 45, 0), (3, 55, 0))));

        var reversal = processor.Process(20, Frame((1, 25, 0), (2, 35, 0), (3, 45, 0)));

        Assert.Empty(reversal);
        Assert.Equal(GestureState.ThreeFingerCandidate, processor.State);
    }

    [Theory]
    [InlineData(-40, 0, FourFingerGesture.NextDesktop)]
    [InlineData(40, 0, FourFingerGesture.PreviousDesktop)]
    [InlineData(0, -40, FourFingerGesture.TaskView)]
    [InlineData(0, 40, FourFingerGesture.ShowDesktop)]
    public void FourFingerSwipe_MapsAllWindowsDirections(double dx, double dy, FourFingerGesture expected) {
        var processor = new GestureProcessor(new GestureOptions {
            FourFingerSwipeDistance = 30,
            FourFingerSelectionStep = 20,
            FourFingerOverviewSettleMs = 0,
            FourFingerSelectionMinIntervalMs = 0
        });
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0)));

        var actions = processor.Process(10, Frame((1, dx, dy), (2, 10 + dx, dy), (3, 20 + dx, dy), (4, 30 + dx, dy)));

        Assert.Equal(expected, Assert.Single(actions).FourFingerGesture);
        var continued = processor.Process(20, Frame((1, 2 * dx, 2 * dy), (2, 10 + 2 * dx, 2 * dy), (3, 20 + 2 * dx, 2 * dy), (4, 30 + 2 * dx, 2 * dy)));
        if(expected == FourFingerGesture.TaskView) {
            Assert.Equal(FourFingerGesture.SelectUp, Assert.Single(continued).FourFingerGesture);
        } else {
            Assert.Empty(continued);
        }
    }

    [Fact]
    public void FourFingerSwipe_RejectsShortAndDiagonalMovement() {
        var processor = new GestureProcessor(new GestureOptions { FourFingerSwipeDistance = 30, AxisDominanceRatio = 1.25 });
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0)));

        Assert.Empty(processor.Process(10, Frame((1, 20, 0), (2, 30, 0), (3, 40, 0), (4, 50, 0))));
        Assert.Empty(processor.Process(20, Frame((1, 35, 32), (2, 45, 32), (3, 55, 32), (4, 65, 32))));
    }

    [Fact]
    public void FourFingerSwipe_DefaultThresholdRespondsWithoutLongTravel() {
        var processor = new GestureProcessor();
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0)));

        var actions = processor.Process(
            10,
            Frame((1, 0, -95), (2, 10, -95), (3, 20, -95), (4, 30, -95)));

        Assert.Equal(FourFingerGesture.TaskView, Assert.Single(actions).FourFingerGesture);
    }

    [Fact]
    public void FourFingerSwipe_FastIntentTriggersBeforeFullDistance() {
        var processor = new GestureProcessor();
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0)));

        Assert.Empty(processor.Process(
            12,
            Frame((1, 0, -20), (2, 10, -20), (3, 20, -20), (4, 30, -20))));
        var actions = processor.Process(
            24,
            Frame((1, 0, -48), (2, 10, -48), (3, 20, -48), (4, 30, -48)));

        Assert.Equal(FourFingerGesture.TaskView, Assert.Single(actions).FourFingerGesture);
    }

    [Fact]
    public void FourFingerSwipe_SlowShortMovementDoesNotFalseTrigger() {
        var processor = new GestureProcessor();
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0)));

        Assert.Empty(processor.Process(
            60,
            Frame((1, 0, -25), (2, 10, -25), (3, 20, -25), (4, 30, -25))));
        Assert.Empty(processor.Process(
            120,
            Frame((1, 0, -50), (2, 10, -50), (3, 20, -50), (4, 30, -50))));
    }

    [Fact]
    public void FourFingerSwipe_QuickReversalDoesNotTriggerFromStaleDistance() {
        var processor = new GestureProcessor();
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0)));
        Assert.Empty(processor.Process(
            60,
            Frame((1, 0, -55), (2, 10, -55), (3, 20, -55), (4, 30, -55))));

        Assert.Empty(processor.Process(
            70,
            Frame((1, 0, -45), (2, 10, -45), (3, 20, -45), (4, 30, -45))));
    }

    [Fact]
    public void FourFingerGesture_DoesNotFallThroughIntoThreeFingerDrag() {
        var processor = new GestureProcessor(new GestureOptions { DragActivationDistance = 5, FourFingerSwipeDistance = 20 });
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0)));
        processor.Process(10, Frame((1, -25, 0), (2, -15, 0), (3, -5, 0), (4, 5, 0)));

        var afterFingerLift = processor.Process(20, Frame((1, -30, 0), (2, -20, 0), (3, -10, 0)));

        Assert.Empty(afterFingerLift);
        Assert.Equal(GestureState.LockedUntilLift, processor.State);
        processor.Process(30, Array.Empty<GestureContact>());
        Assert.Equal(GestureState.Idle, processor.State);
    }

    [Fact]
    public void FourFingerTap_DoesNotImplicitlyActivateSelectedWindow() {
        var processor = new GestureProcessor();
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0)));

        var actions = processor.Process(120, Array.Empty<GestureContact>());

        Assert.Empty(actions);
        Assert.Equal(GestureState.Idle, processor.State);
    }

    [Fact]
    public void FourFingerLongPress_DoesNotActivateSelectedWindow() {
        var processor = new GestureProcessor(new GestureOptions { FourFingerTapMaxDurationMs = 300 });
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0)));

        Assert.Empty(processor.Process(500, Array.Empty<GestureContact>()));
    }

    [Fact]
    public void FourFingerOverview_MovementSelectsWindowAndLiftLeavesOverviewRequested() {
        var processor = new GestureProcessor(new GestureOptions {
            FourFingerSwipeDistance = 30,
            FourFingerSelectionStep = 20,
            FourFingerOverviewSettleMs = 0,
            FourFingerSelectionMinIntervalMs = 0
        });
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0)));

        var overview = processor.Process(
            10,
            Frame((1, 0, -35), (2, 10, -35), (3, 20, -35), (4, 30, -35)));
        var selection = processor.Process(
            20,
            Frame((1, 25, -35), (2, 35, -35), (3, 45, -35), (4, 55, -35)));
        var release = processor.Process(30, Array.Empty<GestureContact>());

        Assert.Equal(FourFingerGesture.TaskView, Assert.Single(overview).FourFingerGesture);
        Assert.Equal(FourFingerGesture.SelectRight, Assert.Single(selection).FourFingerGesture);
        Assert.Empty(release);
        Assert.Equal(GestureState.Idle, processor.State);
        Assert.Empty(processor.Process(40, Array.Empty<GestureContact>()));
    }

    [Fact]
    public void FourFingerOverview_FullLiftDoesNotCloseAndFreshUpSwipeRequestsOverviewAgain() {
        var processor = new GestureProcessor(new GestureOptions { FourFingerSwipeDistance = 30 });
        var origin = Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0));
        var upward = Frame((1, 0, -35), (2, 10, -35), (3, 20, -35), (4, 30, -35));
        Assert.Empty(processor.Process(0, origin));
        Assert.Equal(FourFingerGesture.TaskView, Assert.Single(processor.Process(10, upward)).FourFingerGesture);

        Assert.Empty(processor.Process(20, Array.Empty<GestureContact>()));
        Assert.Equal(GestureState.Idle, processor.State);
        Assert.Empty(processor.Process(30, origin));
        Assert.Equal(FourFingerGesture.TaskView, Assert.Single(processor.Process(40, upward)).FourFingerGesture);
        Assert.Empty(processor.Process(50, Array.Empty<GestureContact>()));
    }

    [Fact]
    public void FourFingerOverview_ShortRecontactAndLiftDoesNotActivateOrCancelRequestedOverview() {
        var processor = new GestureProcessor(new GestureOptions { FourFingerSwipeDistance = 30 });
        var origin = Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0));
        processor.Process(0, origin);
        processor.Process(10, Frame((1, 0, -35), (2, 10, -35), (3, 20, -35), (4, 30, -35)));
        Assert.Empty(processor.Process(20, Array.Empty<GestureContact>()));

        Assert.Empty(processor.Process(30, origin));
        Assert.Empty(processor.Process(40, Frame((1, 0, -5), (2, 10, -5), (3, 20, -5), (4, 30, -5))));
        Assert.Empty(processor.Process(50, Array.Empty<GestureContact>()));
        Assert.Equal(FourFingerGesture.CancelOverview, Assert.Single(processor.ForceRelease()).FourFingerGesture);
    }

    [Theory]
    [InlineData(-25, 0, FourFingerGesture.SelectLeft)]
    [InlineData(25, 0, FourFingerGesture.SelectRight)]
    [InlineData(0, -25, FourFingerGesture.SelectUp)]
    [InlineData(0, 25, FourFingerGesture.SelectDown)]
    public void FourFingerOverview_ContinuedNavigationRemainsCompatibleAndLiftDoesNotConfirm(
        double dx, double dy, FourFingerGesture expected) {
        var processor = new GestureProcessor(new GestureOptions {
            FourFingerSwipeDistance = 30,
            FourFingerSelectionStep = 20,
            FourFingerOverviewSettleMs = 0,
            FourFingerSelectionMinIntervalMs = 0
        });
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0)));
        processor.Process(10, Frame((1, 0, -35), (2, 10, -35), (3, 20, -35), (4, 30, -35)));

        var selection = processor.Process(20, Frame(
            (1, dx, -35 + dy), (2, 10 + dx, -35 + dy),
            (3, 20 + dx, -35 + dy), (4, 30 + dx, -35 + dy)));

        Assert.Equal(expected, Assert.Single(selection).FourFingerGesture);
        Assert.Empty(processor.Process(30, Array.Empty<GestureContact>()));
    }

    [Fact]
    public void FourFingerOverview_IgnoresOpeningSwipeMomentumDuringSettlePeriod() {
        var processor = new GestureProcessor(new GestureOptions {
            FourFingerSwipeDistance = 30,
            FourFingerSelectionStep = 20,
            FourFingerOverviewSettleMs = 120,
            FourFingerSelectionMinIntervalMs = 0
        });
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0)));
        processor.Process(10, Frame((1, 0, -35), (2, 10, -35), (3, 20, -35), (4, 30, -35)));

        Assert.Empty(processor.Process(
            40,
            Frame((1, 0, -120), (2, 10, -120), (3, 20, -120), (4, 30, -120))));
        Assert.Empty(processor.Process(
            130,
            Frame((1, 0, -120), (2, 10, -120), (3, 20, -120), (4, 30, -120))));
        var selection = processor.Process(
            145,
            Frame((1, 25, -120), (2, 35, -120), (3, 45, -120), (4, 55, -120)));

        Assert.Equal(FourFingerGesture.SelectRight, Assert.Single(selection).FourFingerGesture);
    }

    [Fact]
    public void FourFingerOverview_RateLimitsSelectionBursts() {
        var processor = new GestureProcessor(new GestureOptions {
            FourFingerSwipeDistance = 30,
            FourFingerSelectionStep = 20,
            FourFingerOverviewSettleMs = 0,
            FourFingerSelectionMinIntervalMs = 80
        });
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0)));
        processor.Process(10, Frame((1, 0, -35), (2, 10, -35), (3, 20, -35), (4, 30, -35)));

        Assert.Empty(processor.Process(
            20,
            Frame((1, 80, -35), (2, 90, -35), (3, 100, -35), (4, 110, -35))));
        Assert.Empty(processor.Process(
            50,
            Frame((1, 160, -35), (2, 170, -35), (3, 180, -35), (4, 190, -35))));
        var selection = processor.Process(
            90,
            Frame((1, 161, -35), (2, 171, -35), (3, 181, -35), (4, 191, -35)));

        Assert.Equal(FourFingerGesture.SelectRight, Assert.Single(selection).FourFingerGesture);
    }

    [Fact]
    public void FourFingerOverview_ToleratesSingleContactReportingBlip() {
        var processor = new GestureProcessor(new GestureOptions {
            FourFingerSwipeDistance = 30,
            FourFingerSelectionStep = 20,
            FourFingerOverviewSettleMs = 0,
            FourFingerSelectionMinIntervalMs = 0
        });
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0)));
        processor.Process(10, Frame((1, 0, -35), (2, 10, -35), (3, 20, -35), (4, 30, -35)));

        Assert.Empty(processor.Process(20, Frame((1, 0, -35), (2, 10, -35), (3, 20, -35))));
        Assert.Equal(GestureState.FourFingerOverview, processor.State);
        Assert.Empty(processor.Process(
            30,
            Frame((1, 0, -35), (2, 10, -35), (3, 20, -35), (4, 30, -35))));
        var release = processor.Process(40, Array.Empty<GestureContact>());

        Assert.Empty(release);
    }

    [Fact]
    public void AdaptiveReleaseTimeout_TracksCadenceWithinSafeBounds() {
        var processor = new GestureProcessor();
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0)));
        processor.Process(8, Frame((1, 1, 0), (2, 11, 0), (3, 21, 0)));
        processor.Process(17, Frame((1, 2, 0), (2, 12, 0), (3, 22, 0)));

        Assert.InRange(processor.AdaptiveReleaseTimeoutMs, 35, 45);

        processor.Process(500, Frame((1, 3, 0), (2, 13, 0), (3, 23, 0)));
        Assert.InRange(processor.AdaptiveReleaseTimeoutMs, 35, 120);
    }

    [Fact]
    public void FourFingerOverview_TwoContactBlipDoesNotPrematurelyActivateWindow() {
        var processor = new GestureProcessor(new GestureOptions {
            FourFingerSwipeDistance = 30, FourFingerOverviewSettleMs = 0,
            FourFingerSelectionStep = 20, FourFingerSelectionMinIntervalMs = 0
        });
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0)));
        processor.Process(10, Frame((1, 0, -35), (2, 10, -35), (3, 20, -35), (4, 30, -35)));
        Assert.Empty(processor.Process(20, Frame((1, 0, -35), (2, 10, -35))));
        Assert.Equal(GestureState.FourFingerOverview, processor.State);
        Assert.Equal(FourFingerGesture.SelectRight, Assert.Single(processor.Process(30,
            Frame((1, 25, -35), (2, 35, -35), (3, 45, -35), (4, 55, -35)))).FourFingerGesture);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FourFingerOverview_SustainedContactLossLeavesOverviewRequestedAndLocksUntilAllLift(int contacts) {
        var processor = new GestureProcessor(new GestureOptions {
            FourFingerSwipeDistance = 30,
            DragActivationDistance = 5,
            DragMinimumContactDurationMs = 0
        });
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0)));
        processor.Process(10, Frame((1, 0, -35), (2, 10, -35), (3, 20, -35), (4, 30, -35)));
        var partial = Frame((1, 0, -35), (2, 10, -35), (3, 20, -35)).Take(contacts).ToArray();
        Assert.Empty(processor.Process(20, partial));
        Assert.Empty(processor.Process(100, partial));
        Assert.Equal(GestureState.LockedUntilLift, processor.State);

        Assert.Empty(processor.Process(110, Frame((1, 0, -35))));
        Assert.Equal(GestureState.LockedUntilLift, processor.State);
        Assert.Empty(processor.Process(120, Frame((1, 100, -35), (2, 110, -35))));
        Assert.Equal(GestureState.LockedUntilLift, processor.State);
        Assert.Empty(processor.Process(130, Frame((1, 100, -35), (2, 110, -35), (3, 120, -35))));
        Assert.Empty(processor.Process(140, Frame((1, 100, -135), (2, 110, -135), (3, 120, -135), (4, 130, -135))));
        Assert.Equal(GestureState.LockedUntilLift, processor.State);

        Assert.Empty(processor.Process(150, Array.Empty<GestureContact>()));
        Assert.Equal(GestureState.Idle, processor.State);
        Assert.Empty(processor.Process(160, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0))));
        Assert.Equal(GestureActionKind.DragStarted,
            Assert.Single(processor.Process(170, Frame((1, 10, 0), (2, 20, 0), (3, 30, 0)))).Kind);
    }

    [Fact]
    public void FourFingerCandidate_SustainedContactLossDoesNotImplicitlyConfirmAndRequiresAllLift() {
        var processor = new GestureProcessor();
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0), (4, 30, 0)));
        Assert.Empty(processor.Process(10, Frame((1, 0, 0), (2, 10, 0))));
        Assert.Empty(processor.Process(150, Frame((1, 0, 0), (2, 10, 0))));
        Assert.Equal(GestureState.LockedUntilLift, processor.State);
        Assert.Empty(processor.Process(160, Frame((1, 0, 0))));
        Assert.Equal(GestureState.LockedUntilLift, processor.State);
        Assert.Empty(processor.Process(170, Array.Empty<GestureContact>()));
        Assert.Equal(GestureState.Idle, processor.State);
        Assert.Empty(processor.ForceRelease());
    }

    [Fact]
    public void ForceRelease_IsIdempotent() {
        var processor = new GestureProcessor(new GestureOptions {
            DragActivationDistance = 5,
            DragMinimumContactDurationMs = 0
        });
        processor.Process(0, Frame((1, 0, 0), (2, 10, 0), (3, 20, 0)));
        processor.Process(10, Frame((1, 10, 0), (2, 20, 0), (3, 30, 0)));

        Assert.Equal(GestureActionKind.DragEnded, Assert.Single(processor.ForceRelease()).Kind);
        Assert.Empty(processor.ForceRelease());
    }
}
