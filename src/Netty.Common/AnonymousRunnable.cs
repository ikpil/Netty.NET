using System;

namespace Netty.NET.Common;

public class AnonymousRunnable : IRunnable
{
    private readonly Action _action;

    public AnonymousRunnable(Action action)
    {
        _action = action;
    }

    public void run()
    {
        _action.Invoke();
    }
}