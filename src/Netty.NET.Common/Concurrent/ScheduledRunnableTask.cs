using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public sealed class ScheduledRunnableTask : ScheduledTask<Void>
{
    public ScheduledRunnableTask(AbstractScheduledEventExecutor executor, IRunnable action, long deadlineNanos)
        : base(executor, action, deadlineNanos) { }
    public ScheduledRunnableTask(AbstractScheduledEventExecutor executor, IRunnable action, long deadlineNanos, long periodNanos)
        : base(executor, action, deadlineNanos, periodNanos) { }
}
