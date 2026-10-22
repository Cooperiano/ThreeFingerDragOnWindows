using ThreeFingerDragOnWindows.gestures;
using Xunit;

namespace ThreeFingerDragEngine.Tests;

public class DragButtonLatchTests {
    [Theory]
    [InlineData(DragMouseButton.Left, DragMouseButton.Right)]
    [InlineData(DragMouseButton.Right, DragMouseButton.Middle)]
    [InlineData(DragMouseButton.Middle, DragMouseButton.Left)]
    public void Release_UsesActuallyPressedButtonNotChangedPreference(DragMouseButton pressed, DragMouseButton changed) {
        var latch = new DragButtonLatch();
        var events = new List<(string kind, DragMouseButton button)>();
        Assert.True(latch.TryPress(pressed, button => { events.Add(("down", button)); return true; }));
        Assert.False(latch.TryPress(changed, button => { events.Add(("down", button)); return true; }));

        Assert.True(latch.TryRelease(button => { events.Add(("up", button)); return true; }));

        Assert.Equal(new[] { ("down", pressed), ("up", pressed) }, events);
        Assert.False(latch.IsPressed);
        Assert.False(latch.TryRelease(_ => throw new InvalidOperationException("Repeated release must not send input")));
    }

    [Fact]
    public void FailedPress_IsNotReportedAsHeld() {
        var latch = new DragButtonLatch();
        Assert.False(latch.TryPress(DragMouseButton.Left, _ => false));
        Assert.False(latch.IsPressed);
        Assert.False(latch.TryRelease(_ => throw new InvalidOperationException("No successful down")));
    }

    [Fact]
    public void FailedRelease_RetainsOriginalButtonForRetry() {
        var latch = new DragButtonLatch();
        latch.TryPress(DragMouseButton.Left, _ => true);
        Assert.False(latch.TryRelease(_ => false));
        Assert.True(latch.IsPressed);
        Assert.False(latch.TryPress(DragMouseButton.Right, _ => true));
        DragMouseButton? released = null;

        Assert.True(latch.TryRelease(button => { released = button; return true; }));

        Assert.Equal(DragMouseButton.Left, released);
        Assert.False(latch.IsPressed);
    }

    [Fact]
    public void ReentrantCallbacks_CannotDuplicateOrReplaceButton() {
        var latch = new DragButtonLatch();
        Assert.True(latch.TryPress(DragMouseButton.Left, _ => {
            Assert.False(latch.TryPress(DragMouseButton.Right, _ => throw new InvalidOperationException()));
            Assert.False(latch.TryRelease(_ => throw new InvalidOperationException()));
            return true;
        }));
        Assert.True(latch.TryRelease(button => {
            Assert.Equal(DragMouseButton.Left, button);
            Assert.False(latch.TryRelease(_ => throw new InvalidOperationException()));
            Assert.False(latch.TryPress(DragMouseButton.Right, _ => throw new InvalidOperationException()));
            return true;
        }));
    }

    [Fact]
    public void ThrowingRelease_RetainsButtonAndAllowsRetry() {
        var latch = new DragButtonLatch();
        latch.TryPress(DragMouseButton.Middle, _ => true);
        Assert.Throws<InvalidOperationException>(() => latch.TryRelease(_ => throw new InvalidOperationException()));
        Assert.True(latch.IsPressed);
        Assert.True(latch.TryRelease(button => button == DragMouseButton.Middle));
    }

    [Fact]
    public void ThrowingPress_ResetsTransitionAndAllowsNextPress() {
        var latch = new DragButtonLatch();
        Assert.Throws<InvalidOperationException>(() => latch.TryPress(DragMouseButton.Left, _ => throw new InvalidOperationException()));
        Assert.False(latch.IsPressed);
        Assert.True(latch.TryPress(DragMouseButton.Right, _ => true));
    }

    [Fact]
    public void InvalidButton_DoesNotCallInput() {
        var latch = new DragButtonLatch();
        Assert.False(latch.TryPress((DragMouseButton)99, _ => throw new InvalidOperationException()));
        Assert.False(latch.TryPress(DragMouseButton.None, _ => throw new InvalidOperationException()));
    }
}
