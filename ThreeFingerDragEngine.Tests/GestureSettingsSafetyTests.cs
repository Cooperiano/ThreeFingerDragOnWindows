using ThreeFingerDragOnWindows.gestures;
using Xunit;

namespace ThreeFingerDragEngine.Tests;

public class GestureSettingsSafetyTests {
    private static GestureContact[] Frame(int count, double x = 0, double y = 0) =>
        Enumerable.Range(1, count).Select(id => new GestureContact(id, id * 10 + x, y)).ToArray();

    private static GestureProcessor OpenOverview() {
        var processor = new GestureProcessor(new GestureOptions { FourFingerSwipeDistance = 30 });
        processor.Process(0, Frame(4));
        Assert.Equal(FourFingerGesture.TaskView, Assert.Single(processor.Process(10, Frame(4, y: -35))).FourFingerGesture);
        return processor;
    }

    [Fact]
    public void DisabledFourFingerCandidate_LiftDoesNotActivateSelection() {
        var processor = new GestureProcessor();
        processor.Process(0, Frame(4));

        Assert.Empty(processor.Process(120, Frame(0), fourFingerEnabled: false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void DisabledOverview_CancelsRatherThanActivatingRegardlessOfNextContactCount(int contacts) {
        var processor = OpenOverview();

        var action = Assert.Single(processor.Process(20, Frame(contacts, y: -35), fourFingerEnabled: false));

        Assert.Equal(FourFingerGesture.CancelOverview, action.FourFingerGesture);
        Assert.DoesNotContain(processor.Process(30, Frame(0), fourFingerEnabled: false),
            item => item.FourFingerGesture == FourFingerGesture.ActivateSelection);
    }

    [Fact]
    public void ForceReleaseOverview_CancelsExactlyOnceAndDoesNotActivateSelection() {
        var processor = OpenOverview();

        Assert.Equal(FourFingerGesture.CancelOverview, Assert.Single(processor.ForceRelease()).FourFingerGesture);
        Assert.Empty(processor.ForceRelease());
        Assert.Empty(processor.Process(30, Frame(0)));
    }

    [Fact]
    public void DisabledThreeFingerDrag_EndsOnTheNextFrame() {
        var processor = new GestureProcessor(new GestureOptions { DragActivationDistance = 5, DragMinimumContactDurationMs = 0 });
        processor.Process(0, Frame(3));
        processor.Process(10, Frame(3, x: 10));

        Assert.Equal(GestureActionKind.DragEnded,
            Assert.Single(processor.Process(20, Frame(3, x: 20), threeFingerEnabled: false)).Kind);
    }

    [Fact]
    public void CancelDisabledGestures_ImmediatelyCancelsOnlyTheDisabledFeature() {
        var processor = OpenOverview();
        Assert.Empty(processor.CancelDisabledGestures(threeFingerEnabled: false, fourFingerEnabled: true));
        Assert.Equal(GestureState.FourFingerOverview, processor.State);

        Assert.Equal(FourFingerGesture.CancelOverview,
            Assert.Single(processor.CancelDisabledGestures(threeFingerEnabled: false, fourFingerEnabled: false)).FourFingerGesture);
        Assert.Empty(processor.CancelDisabledGestures(false, false));
    }

    [Fact]
    public void CancelledSession_CannotRestartWithRemainingContactsOrReenabledToggle() {
        var processor = OpenOverview();
        processor.CancelDisabledGestures(true, false);

        Assert.Empty(processor.Process(20, Frame(2)));
        Assert.Empty(processor.Process(40, Frame(3, x: 100)));
        Assert.Empty(processor.Process(60, Frame(4, y: -100)));
        Assert.Equal(GestureState.LockedUntilLift, processor.State);
        Assert.Empty(processor.Process(70, Frame(0)));
        Assert.Equal(GestureState.Idle, processor.State);
        Assert.Empty(processor.Process(80, Frame(4)));
        Assert.Equal(FourFingerGesture.TaskView, Assert.Single(processor.Process(90, Frame(4, y: -35))).FourFingerGesture);
    }

    [Fact]
    public void CancellingFourFinger_DoesNotEndAnEnabledThreeFingerDrag() {
        var processor = new GestureProcessor(new GestureOptions { DragActivationDistance = 5, DragMinimumContactDurationMs = 0 });
        processor.Process(0, Frame(3));
        processor.Process(10, Frame(3, x: 10));

        Assert.Empty(processor.CancelDisabledGestures(true, false));
        Assert.Equal(GestureState.Dragging, processor.State);
        Assert.Equal(GestureActionKind.DragEnded, Assert.Single(processor.CancelDisabledGestures(false, false)).Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisablingFourFinger_AfterTrackingEndsCancelsRequestedOverviewExactlyOnce(bool partialDropout) {
        var processor = OpenOverview();
        if(partialDropout) {
            Assert.Empty(processor.Process(20, Frame(2, y: -35)));
            Assert.Empty(processor.Process(100, Frame(2, y: -35)));
            Assert.Equal(GestureState.LockedUntilLift, processor.State);
        } else {
            Assert.Empty(processor.Process(20, Frame(0)));
            Assert.Equal(GestureState.Idle, processor.State);
        }

        Assert.Empty(processor.CancelDisabledGestures(false, true));
        Assert.Equal(FourFingerGesture.CancelOverview,
            Assert.Single(processor.CancelDisabledGestures(true, false)).FourFingerGesture);
        Assert.Empty(processor.CancelDisabledGestures(true, false));
        Assert.Empty(processor.ForceRelease());
        Assert.Empty(processor.Process(110, Frame(0)));
        Assert.Equal(GestureState.Idle, processor.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForceRelease_AfterTrackingEndsCancelsRequestedOverviewExactlyOnce(bool partialDropout) {
        var processor = OpenOverview();
        if(partialDropout) {
            Assert.Empty(processor.Process(20, Frame(3, y: -35)));
            Assert.Empty(processor.Process(100, Frame(3, y: -35)));
        } else {
            Assert.Empty(processor.Process(20, Frame(0)));
        }

        Assert.Equal(FourFingerGesture.CancelOverview, Assert.Single(processor.ForceRelease()).FourFingerGesture);
        Assert.Empty(processor.ForceRelease());
        Assert.Empty(processor.Process(110, Frame(0)));
        Assert.Equal(GestureState.Idle, processor.State);
        Assert.Empty(processor.Process(120, Frame(4)));
        Assert.Equal(FourFingerGesture.TaskView,
            Assert.Single(processor.Process(130, Frame(4, y: -35))).FourFingerGesture);
    }

    [Fact]
    public void DisablingFourFinger_WithRequestedOverviewDoesNotInterruptEnabledThreeFingerDrag() {
        var processor = OpenOverview();
        Assert.Empty(processor.Process(20, Frame(0)));
        Assert.Empty(processor.Process(30, Frame(3)));
        Assert.Equal(GestureActionKind.DragStarted, Assert.Single(processor.Process(70, Frame(3, x: 40))).Kind);

        Assert.Equal(FourFingerGesture.CancelOverview,
            Assert.Single(processor.CancelDisabledGestures(true, false)).FourFingerGesture);
        Assert.Equal(GestureState.Dragging, processor.State);
        var moved = Assert.Single(processor.Process(80, Frame(3, x: 45), fourFingerEnabled: false));
        Assert.Equal(GestureActionKind.DragMoved, moved.Kind);
        Assert.Equal(5, moved.DeltaX);
        Assert.Equal(GestureActionKind.DragEnded, Assert.Single(processor.ForceRelease()).Kind);
        Assert.Empty(processor.ForceRelease());
    }

    [Fact]
    public void DisablingThreeFinger_LeavesRequestedOverviewForExplicitGlobalCancellation() {
        var processor = OpenOverview();
        Assert.Empty(processor.Process(20, Frame(0)));
        Assert.Empty(processor.Process(30, Frame(3)));
        Assert.Equal(GestureActionKind.DragStarted, Assert.Single(processor.Process(70, Frame(3, x: 40))).Kind);

        Assert.Equal(GestureActionKind.DragEnded, Assert.Single(processor.CancelDisabledGestures(false, true)).Kind);
        Assert.Equal(GestureState.LockedUntilLift, processor.State);
        Assert.Equal(FourFingerGesture.CancelOverview, Assert.Single(processor.ForceRelease()).FourFingerGesture);
        Assert.Empty(processor.ForceRelease());
        Assert.Empty(processor.Process(80, Frame(0)));
    }

    [Fact]
    public void ForceRelease_EndsThreeFingerDragAndCancelsRequestedOverviewTogether() {
        var processor = OpenOverview();
        Assert.Empty(processor.Process(20, Frame(0)));
        Assert.Empty(processor.Process(30, Frame(3)));
        Assert.Equal(GestureActionKind.DragStarted, Assert.Single(processor.Process(70, Frame(3, x: 40))).Kind);

        var cancelled = processor.ForceRelease();

        Assert.Equal(2, cancelled.Count);
        Assert.Equal(GestureActionKind.DragEnded, cancelled[0].Kind);
        Assert.Equal(FourFingerGesture.CancelOverview, cancelled[1].FourFingerGesture);
        Assert.Empty(processor.ForceRelease());
        Assert.Empty(processor.Process(80, Frame(0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EndDragTracking_LeavesRequestedOverviewForExplicitGlobalCancellation(bool startedDrag) {
        var processor = OpenOverview();
        Assert.Empty(processor.Process(20, Frame(0)));
        Assert.Empty(processor.Process(30, Frame(3)));
        if(startedDrag) {
            Assert.Equal(GestureActionKind.DragStarted, Assert.Single(processor.Process(70, Frame(3, x: 40))).Kind);
        }

        var ended = processor.EndDragTracking();

        if(startedDrag) Assert.Equal(GestureActionKind.DragEnded, Assert.Single(ended).Kind);
        else Assert.Empty(ended);
        Assert.Equal(GestureState.LockedUntilLift, processor.State);
        Assert.Empty(processor.EndDragTracking());
        Assert.Equal(FourFingerGesture.CancelOverview, Assert.Single(processor.ForceRelease()).FourFingerGesture);
        Assert.Empty(processor.ForceRelease());
        Assert.Empty(processor.Process(80, Frame(0)));
    }

    [Fact]
    public void EndDragTracking_DoesNotInterruptFourFingerOverviewNavigation() {
        var processor = OpenOverview();

        Assert.Empty(processor.EndDragTracking());
        Assert.Equal(GestureState.FourFingerOverview, processor.State);
        Assert.Equal(FourFingerGesture.SelectRight,
            Assert.Single(processor.Process(150, Frame(4, x: 60, y: -35))).FourFingerGesture);
        Assert.Equal(FourFingerGesture.CancelOverview, Assert.Single(processor.ForceRelease()).FourFingerGesture);
    }

    [Fact]
    public void EndDragTracking_DoesNotInterruptFourFingerCandidate() {
        var processor = new GestureProcessor(new GestureOptions { FourFingerSwipeDistance = 30 });
        processor.Process(0, Frame(4));

        Assert.Empty(processor.EndDragTracking());
        Assert.Equal(GestureState.FourFingerCandidate, processor.State);
        Assert.Equal(FourFingerGesture.TaskView,
            Assert.Single(processor.Process(10, Frame(4, y: -35))).FourFingerGesture);
    }

    [Fact]
    public void EndDragTracking_WhileIdleDoesNotCancelRequestedOverview() {
        var processor = OpenOverview();
        Assert.Empty(processor.Process(20, Frame(0)));

        Assert.Empty(processor.EndDragTracking());
        Assert.Equal(GestureState.Idle, processor.State);
        Assert.Equal(FourFingerGesture.CancelOverview, Assert.Single(processor.ForceRelease()).FourFingerGesture);
    }
}
