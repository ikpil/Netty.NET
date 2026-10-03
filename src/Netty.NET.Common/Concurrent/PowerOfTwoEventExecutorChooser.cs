namespace Netty.NET.Common.Concurrent;

public class PowerOfTwoEventExecutorChooser : IEventExecutorChooser
{
    private readonly AtomicInteger idx = new AtomicInteger();
    private readonly IEventExecutor[] executors;

    internal PowerOfTwoEventExecutorChooser(IEventExecutor[] executors)
    {
        this.executors = executors;
    }

    public IEventExecutor Next()
    {
        return executors[idx.GetAndIncrement() & executors.Length - 1];
    }
}