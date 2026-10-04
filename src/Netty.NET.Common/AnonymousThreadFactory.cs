using System;
using System.Threading;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common;

public class AnonymousThreadFactory : IThreadFactory
{
    private readonly Func<Action, Thread> _factory;

    public AnonymousThreadFactory(Func<Action, Thread> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public Thread NewThread(Action r)
    {
        return _factory.Invoke(r);
    }
}