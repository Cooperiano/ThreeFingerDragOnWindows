using ThreeFingerDragOnWindows.gestures;
using TouchpadControl.Pointer;
using Xunit;

namespace ThreeFingerDragEngine.Tests;

public sealed class PointerProfileTransactionTests
{
    private static readonly PointerAccelerationProfile Original = new(10, 6, 10, 1, 12);
    private sealed class Backend : IPointerProfileBackend
    {
        public PointerAccelerationProfile Current = Original;
        public List<PointerAccelerationProfile> Writes = [];
        public bool FailRead;
        public bool FailFirstApply;
        public bool IgnoreFirstApply;
        public bool BreakRollback;
        public PointerAccelerationProfile Read() => FailRead ? throw new IOException("read failed") : Current;
        public void Apply(PointerAccelerationProfile value)
        {
            Writes.Add(value);
            if(Writes.Count == 1 && FailFirstApply)
            {
                Current = Current with { PointerSpeed = value.PointerSpeed };
                throw new IOException("partial apply");
            }
            if(Writes.Count == 1 && IgnoreFirstApply || Writes.Count > 1 && BreakRollback) return;
            Current = value;
        }
    }

    [Fact]
    public async Task Apply_ReadsBackAndSavesBackupBeforeWriting()
    {
        var backend = new Backend();
        bool backedUp = false;
        var result = await new PointerProfileTransaction(backend).ApplyAsync(PointerAccelerationProfile.MacLike, before =>
        {
            Assert.Equal(Original, before);
            Assert.Empty(backend.Writes);
            backedUp = true;
            return Task.CompletedTask;
        });
        Assert.True(backedUp);
        Assert.True(result.Success);
        Assert.Equal(PointerAccelerationProfile.MacLike, result.Observed);
    }

    [Fact]
    public async Task ReadFailure_DoesNotWriteOrInventDefaults()
    {
        var backend = new Backend { FailRead = true };
        var result = await new PointerProfileTransaction(backend).ApplyAsync(PointerAccelerationProfile.MacLike);
        Assert.Equal(PointerProfileOutcome.ReadFailed, result.Outcome);
        Assert.Empty(backend.Writes);
    }

    [Fact]
    public async Task BackupFailure_DoesNotModifyWindows()
    {
        var backend = new Backend();
        var result = await new PointerProfileTransaction(backend).ApplyAsync(PointerAccelerationProfile.MacLike,
            _ => throw new IOException("backup cannot be saved"));
        Assert.False(result.Success);
        Assert.Empty(backend.Writes);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task PartialFailureOrFalseSuccess_RollsBackAndReportsFailure(bool fail, bool ignore)
    {
        var backend = new Backend { FailFirstApply = fail, IgnoreFirstApply = ignore };
        var result = await new PointerProfileTransaction(backend).ApplyAsync(PointerAccelerationProfile.MacLike);
        Assert.Equal(PointerProfileOutcome.ApplyFailed, result.Outcome);
        Assert.True(result.RollbackVerified);
        Assert.Equal(Original, backend.Current);
        Assert.Equal(2, backend.Writes.Count);
    }

    [Fact]
    public async Task RollbackFailure_IsNotReportedAsRestored()
    {
        var backend = new Backend { FailFirstApply = true, BreakRollback = true };
        var result = await new PointerProfileTransaction(backend).ApplyAsync(PointerAccelerationProfile.MacLike);
        Assert.Equal(PointerProfileOutcome.RollbackFailed, result.Outcome);
        Assert.False(result.RollbackVerified);
    }

    [Fact]
    public async Task PreferenceCommitFailure_RollsBackWindowsAndDoesNotClaimSuccess()
    {
        var backend = new Backend();
        var result = await new PointerProfileTransaction(backend).ApplyAsync(PointerAccelerationProfile.MacLike,
            afterVerify: () => throw new IOException("commit failed"));
        Assert.Equal(PointerProfileOutcome.ApplyFailed, result.Outcome);
        Assert.True(result.RollbackVerified);
        Assert.Equal(Original, backend.Current);
    }

    [Fact]
    public async Task InvalidReadback_DoesNotModifyWindows()
    {
        var backend = new Backend { Current = Original with { PointerSpeed = 99 } };
        var result = await new PointerProfileTransaction(backend).ApplyAsync(PointerAccelerationProfile.MacLike);
        Assert.Equal(PointerProfileOutcome.ReadFailed, result.Outcome);
        Assert.Empty(backend.Writes);
    }

    [Fact]
    public async Task AlreadyApplied_VerifiesWithoutRewriting()
    {
        var backend = new Backend { Current = PointerAccelerationProfile.MacLike };
        Assert.True((await new PointerProfileTransaction(backend).ApplyAsync(backend.Current)).Success);
        Assert.Empty(backend.Writes);
    }

    [Fact]
    public async Task Restoration_UsesSavedProfileNotHardCodedDefaults()
    {
        var backend = new Backend { Current = PointerAccelerationProfile.MacLike };
        var result = await new PointerProfileTransaction(backend).ApplyAsync(Original);
        Assert.True(result.Success);
        Assert.Equal(Original, backend.Current);
    }

    [Fact]
    public async Task InvalidBackup_IsRejectedWithoutWriting()
    {
        var backend = new Backend();
        var result = await new PointerProfileTransaction(backend).ApplyAsync(Original with { TouchpadCursorSpeed = 99 });
        Assert.Equal(PointerProfileOutcome.InvalidProfile, result.Outcome);
        Assert.Empty(backend.Writes);
    }

    [Fact]
    public async Task ReadOnlyVerification_FailureDoesNotWrite()
    {
        var backend = new Backend { FailRead = true };
        var result = await new PointerProfileTransaction(backend).VerifyAsync(Original);
        Assert.Equal(PointerProfileOutcome.ReadFailed, result.Outcome);
        Assert.Empty(backend.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadOnlyVerification_DetectsExternalChangesWithoutOverwritingThem(bool matches)
    {
        var backend = new Backend();
        var target = matches ? Original : PointerAccelerationProfile.MacLike;
        var result = await new PointerProfileTransaction(backend).VerifyAsync(target);
        Assert.Equal(matches ? PointerProfileOutcome.Verified : PointerProfileOutcome.Mismatch, result.Outcome);
        Assert.Equal(Original, backend.Current);
        Assert.Empty(backend.Writes);
    }

    [Fact]
    public async Task ConcurrentRequest_IsRejectedUntilFirstFinishes()
    {
        var backend = new Backend();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transaction = new PointerProfileTransaction(backend);
        var first = transaction.ApplyAsync(PointerAccelerationProfile.MacLike, async _ =>
        {
            entered.SetResult();
            await release.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PointerProfileOutcome.Busy, (await transaction.ApplyAsync(Original)).Outcome);
        Assert.Equal(PointerProfileOutcome.Busy, (await transaction.VerifyAsync(Original)).Outcome);
        release.SetResult();
        Assert.True((await first).Success);
        Assert.True((await transaction.ApplyAsync(Original)).Success);
    }
}
