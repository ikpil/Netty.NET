using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common;

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