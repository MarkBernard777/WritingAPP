namespace MasterBookWritingSystem.Core.Threading;

/// <summary>
/// Thread-safe helpers for replacing and disposing <see cref="CancellationTokenSource"/> fields.
/// Always exchanges the field to null (or the replacement) before cancel/dispose so repeated
/// cleanup cannot throw <see cref="ObjectDisposedException"/>.
/// </summary>
public static class CancellationTokenSourceLifecycle
{
    public static void CancelAndDispose(ref CancellationTokenSource? field)
    {
        var cts = Interlocked.Exchange(ref field, null);
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cts.Dispose();
    }

    public static CancellationTokenSource Replace(ref CancellationTokenSource? field)
    {
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref field, next);
        if (previous is not null)
        {
            try
            {
                previous.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            previous.Dispose();
        }

        return next;
    }
}
