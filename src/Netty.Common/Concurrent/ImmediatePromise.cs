namespace Netty.NET.Common.Concurrent;

public class ImmediatePromise<V> : DefaultPromise<V>
{
    public ImmediatePromise(IEventExecutor executor)
        : base(executor)
    {
    }

    protected override void checkDeadLock()
    {
        // No check
    }
}