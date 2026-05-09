// SPDX-License-Identifier: GPL-3.0-only

namespace Clip.Core.Platform;

public interface IUiDispatcher
{
    bool HasAccess { get; }
    void Invoke(Action action);
    void Post(Action action);
    Task InvokeAsync(Action action);
    Task InvokeAsync(Func<Task> action);
}

public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public static ImmediateUiDispatcher Instance { get; } = new();

    private ImmediateUiDispatcher()
    {
    }

    public bool HasAccess => true;

    public void Invoke(Action action) => action();

    public void Post(Action action) => action();

    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public Task InvokeAsync(Func<Task> action) => action();
}
