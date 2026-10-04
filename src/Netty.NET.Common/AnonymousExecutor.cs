using System;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common;

public class AnonymousExecutor : IExecutor
{
    private readonly Action<Action> _action;

    public AnonymousExecutor(Action<Action> action)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public void Execute(Action command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _action.Invoke(command);
    }
}
