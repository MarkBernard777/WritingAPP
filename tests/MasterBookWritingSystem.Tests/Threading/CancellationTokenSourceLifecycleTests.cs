using MasterBookWritingSystem.Core.Threading;

namespace MasterBookWritingSystem.Tests.Threading;

public sealed class CancellationTokenSourceLifecycleTests
{
    [Fact]
    public void CancelAndDispose_Twice_DoesNotThrow()
    {
        CancellationTokenSource? field = new();
        CancellationTokenSourceLifecycle.CancelAndDispose(ref field);
        Assert.Null(field);
        CancellationTokenSourceLifecycle.CancelAndDispose(ref field);
        Assert.Null(field);
    }

    [Fact]
    public void Replace_CancelsPrevious_AndLeavesNewActive()
    {
        CancellationTokenSource? field = null;
        var first = CancellationTokenSourceLifecycle.Replace(ref field);
        var firstToken = first.Token;
        var second = CancellationTokenSourceLifecycle.Replace(ref field);
        Assert.True(firstToken.IsCancellationRequested);
        Assert.False(second.Token.IsCancellationRequested);
        Assert.Same(second, field);
        CancellationTokenSourceLifecycle.CancelAndDispose(ref field);
    }

    [Fact]
    public async Task CancelAndDispose_WhileDelayPending_CancelsWithoutThrowing()
    {
        CancellationTokenSource? field = null;
        var cts = CancellationTokenSourceLifecycle.Replace(ref field);
        var delayed = Task.Delay(TimeSpan.FromSeconds(30), cts.Token);
        CancellationTokenSourceLifecycle.CancelAndDispose(ref field);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delayed);
        CancellationTokenSourceLifecycle.CancelAndDispose(ref field);
    }
}
