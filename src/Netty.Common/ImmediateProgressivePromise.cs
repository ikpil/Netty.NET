using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common;

public class DefaultProgressivePromise<T> : DefaultPromise<T>
{
    public DefaultProgressivePromise(IEventExecutor executor) : base(executor)
    {
    }
}

public class ImmediateProgressivePromise<V> : DefaultProgressivePromise<V>
{
    public ImmediateProgressivePromise(IEventExecutor executor)
        : base(executor)
    {
    }

    protected override void checkDeadLock()
    {
        // No check
    }
}