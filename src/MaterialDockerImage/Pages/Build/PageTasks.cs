using MaterialDockerImage.Services;

namespace MaterialDockerImage.Pages;

/// <summary>Fire-and-forget helper for UI event handlers: runs the task and reports failures as notifications.</summary>
public static class PageTasks
{
    public static async void Fire(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) when (e is not OutOfMemoryException) { AppHost.Current?.Notify.Error(e.Message); }
    }
}
