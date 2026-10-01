using System;

namespace Netty.NET.Common.Functional;

public static class Runnables
{
    public static readonly IRunnable Empty = new AnonymousRunnable(() => { });

    public static AnonymousRunnable Create(Action action)
    {
        return new AnonymousRunnable(action);
    }
}
