using MasterBookWritingSystem.Core.Abstractions;

namespace MasterBookWritingSystem.Infrastructure.Story;

public sealed class StoryChangeNotifier : IStoryChangeNotifier
{
    public event EventHandler<StoryChangeEventArgs>? Changed;

    public void Publish(StoryChangeEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Changed?.Invoke(this, change);
    }
}
