using ThreeFingerDragOnWindows.gestures;
using Xunit;

namespace ThreeFingerDragEngine.Tests;

public class ContactFrameAssemblerTests {
    private static GestureContact Contact(int id, double x = 0) => new(id, x, 0);
    private static ContactFrameAssembler<GestureContact> Create() => new(contact => contact.Id);

    [Fact]
    public void HybridReports_EmitOnlyAfterFourRealContactsArrive() {
        var assembler = Create();
        Assert.False(assembler.TryAssemble(4, 10, [Contact(1), Contact(2)], out _));
        Assert.True(assembler.TryAssemble(0, 10, [Contact(3), Contact(4)], out var frame));
        Assert.Equal(new[] { 1, 2, 3, 4 }, frame.Select(contact => contact.Id));
        Assert.False(assembler.HasPendingFrame);
    }

    [Fact]
    public void NewFrame_DiscardsIncompleteFrameWithoutSyntheticFingers() {
        var assembler = Create();
        Assert.False(assembler.TryAssemble(4, 10, [Contact(1), Contact(2)], out _));
        Assert.True(assembler.TryAssemble(3, 11, [Contact(1, 5), Contact(2, 5), Contact(3, 5)], out var frame));
        Assert.Equal(3, frame.Length);
        Assert.All(frame, contact => Assert.Equal(5, contact.X));
        Assert.False(assembler.TryAssemble(0, 10, [Contact(4)], out _));
    }

    [Fact]
    public void ScanTimeChange_DoesNotMixContactsAcrossFrames() {
        var assembler = Create();
        Assert.False(assembler.TryAssemble(4, 10, [Contact(1), Contact(2)], out _));
        Assert.False(assembler.TryAssemble(0, 11, [Contact(3), Contact(4)], out _));
        Assert.False(assembler.HasPendingFrame);
    }

    [Fact]
    public void EmptyRelease_ReachesGestureProcessorAndClearsPendingContacts() {
        var assembler = Create();
        assembler.TryAssemble(4, 10, [Contact(1)], out _);
        Assert.True(assembler.TryAssemble(0, 11, [], out var frame));
        Assert.Empty(frame);
        Assert.False(assembler.HasPendingFrame);
        var processor = new GestureProcessor(new GestureOptions { DragActivationDistance = 5, DragMinimumContactDurationMs = 0 });
        processor.Process(0, [Contact(1), Contact(2), Contact(3)]);
        processor.Process(20, [Contact(1, 10), Contact(2, 10), Contact(3, 10)]);
        Assert.Equal(GestureActionKind.DragEnded, Assert.Single(processor.Process(30, frame)).Kind);
    }

    [Fact]
    public void DuplicateId_UpdatesCoordinateWithoutChangingFingerCount() {
        var assembler = Create();
        assembler.TryAssemble(4, 10, [Contact(1), Contact(2)], out _);
        Assert.False(assembler.TryAssemble(0, 10, [Contact(2, 9), Contact(3)], out _));
        Assert.True(assembler.TryAssemble(0, 10, [Contact(4)], out var frame));
        Assert.Equal(9, frame.Single(contact => contact.Id == 2).X);
    }

    [Fact]
    public void OrphanContinuation_DoesNotReplayACompletedFrame() {
        var assembler = Create();
        Assert.True(assembler.TryAssemble(1, 10, [Contact(1)], out _));
        Assert.False(assembler.TryAssemble(0, 10, [Contact(2)], out _));
    }

    [Fact]
    public void MissingScanTime_SupportsHybridHardwareWithoutOptionalField() {
        var assembler = Create();
        Assert.False(assembler.TryAssemble(3, null, [Contact(1)], out _));
        Assert.True(assembler.TryAssemble(0, null, [Contact(2), Contact(3)], out var frame));
        Assert.Equal(3, frame.Length);
    }

    [Fact]
    public void ExcessContacts_RejectsAmbiguousPacket() {
        var assembler = Create();
        Assert.False(assembler.TryAssemble(3, 10, [Contact(1), Contact(2), Contact(3), Contact(4)], out _));
        Assert.False(assembler.HasPendingFrame);
    }
}
