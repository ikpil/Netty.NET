using System;

namespace Netty.NET.Common.Functional;

public class AnonymousRunnable : IRunnable
{
    private readonly Action _action;

    public static AnonymousRunnable Create(Action action)
    {
        return new AnonymousRunnable(action);
    }

    private AnonymousRunnable(Action action)
    {
        _action = action;
    }

    public void run()
    {
        _action.Invoke();
    }
}