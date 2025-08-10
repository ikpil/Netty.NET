using System;
using System.Threading;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common;

public class AnonymousThreadFactory : IThreadFactory
{
    private readonly Func<IRunnable, Thread> _factory;

    public AnonymousThreadFactory(Func<IRunnable, Thread> factory)
    {
        _factory = factory;
    }

    public Thread newThread(IRunnable r)
    {
        return _factory.Invoke(r);
    }
}