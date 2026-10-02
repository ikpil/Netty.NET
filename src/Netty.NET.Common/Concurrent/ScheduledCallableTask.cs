using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public sealed class ScheduledCallableTask<T> : ScheduledTask<T>
{
    public ScheduledCallableTask(AbstractScheduledEventExecutor executor, ICallable<T> callable, long deadlineNanos)
        : base(executor, callable, deadlineNanos) { }
}
