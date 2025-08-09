using System;

namespace Netty.NET.Common.Concurrent;

public class GenericEventExecutorChooser : IEventExecutorChooser
{
    // Use a 'long' counter to avoid non-round-robin behaviour at the 32-bit overflow boundary.
    // The 64-bit long solves this by placing the overflow so far into the future, that no system
    // will encounter this in practice.
    private readonly AtomicLong idx = new AtomicLong();
    private readonly IEventExecutor[] executors;

    internal GenericEventExecutorChooser(IEventExecutor[] executors)
    {
        this.executors = executors;
    }

    public IEventExecutor next()
    {
        return executors[(int)Math.Abs(idx.getAndIncrement() % executors.Length)];
    }
}