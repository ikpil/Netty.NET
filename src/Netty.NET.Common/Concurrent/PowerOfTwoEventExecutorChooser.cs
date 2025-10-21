namespace Netty.NET.Common.Concurrent;

public class PowerOfTwoEventExecutorChooser : IEventExecutorChooser
{
    private readonly AtomicInteger idx = new AtomicInteger();
    private readonly IEventExecutor[] executors;

    internal PowerOfTwoEventExecutorChooser(IEventExecutor[] executors)
    {
        this.executors = executors;
    }

    public IEventExecutor next()
    {
        return executors[idx.getAndIncrement() & executors.Length - 1];
    }
}