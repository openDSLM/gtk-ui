using System;
using System.Collections.Generic;
using SysTask = System.Threading.Tasks.Task;

public sealed class ActionDispatcher
{
    private readonly Dictionary<AppActionId, List<Func<object?, SysTask>>> _handlers = new();

    public void Register(AppActionId actionId, Func<SysTask> handler)
    {
        Register(actionId, _ => handler());
    }

    public void Register(AppActionId actionId, Action handler)
    {
        Register(actionId, _ =>
        {
            handler();
            return SysTask.CompletedTask;
        });
    }

    public void Register<TPayload>(AppActionId actionId, Func<TPayload, SysTask> handler)
    {
        Register(actionId, payload => handler(CastPayload<TPayload>(actionId, payload)));
    }

    public void Register<TPayload>(AppActionId actionId, Action<TPayload> handler)
    {
        Register(actionId, payload =>
        {
            handler(CastPayload<TPayload>(actionId, payload));
            return SysTask.CompletedTask;
        });
    }

    public void Register(AppActionId actionId, Func<object?, SysTask> handler)
    {
        if (!_handlers.TryGetValue(actionId, out var list))
        {
            list = new List<Func<object?, SysTask>>();
            _handlers[actionId] = list;
        }
        list.Add(handler);
    }

    public SysTask DispatchAsync(AppActionId actionId, object? payload = null)
    {
        if (!_handlers.TryGetValue(actionId, out var handlers) || handlers.Count == 0)
        {
            return SysTask.CompletedTask;
        }

        return InvokeHandlersAsync(handlers, payload);
    }

    public void FireAndForget(AppActionId actionId, object? payload = null)
    {
        _ = DispatchAsync(actionId, payload).ContinueWith(t =>
        {
            if (t.Exception != null)
            {
                Console.Error.WriteLine($"Action {actionId} failed: {t.Exception.Flatten().InnerException}");
            }
        });
    }

    private static async SysTask InvokeHandlersAsync(List<Func<object?, SysTask>> handlers, object? payload)
    {
        foreach (var handler in handlers)
        {
            await handler(payload).ConfigureAwait(false);
        }
    }

    private static TPayload CastPayload<TPayload>(AppActionId actionId, object? payload)
    {
        if (payload is null)
        {
            if (default(TPayload) is null)
            {
                return default!;
            }
            throw new InvalidCastException($"Action {actionId} expects payload of type {typeof(TPayload).Name}, but got null.");
        }

        if (payload is TPayload typed)
        {
            return typed;
        }

        throw new InvalidCastException($"Action {actionId} expects payload of type {typeof(TPayload).Name}, but received {payload.GetType().Name}.");
    }
}
