using System;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common;

public class AnonymousExecutor : IExecutor
{
    private readonly Action<IRunnable> _action;

    public AnonymousExecutor(Action<IRunnable> action)
    {
        _action = action;
    }

    public void execute(IRunnable command)
    {
        _action.Invoke(command);
    }
}