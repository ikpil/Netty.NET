using System.Threading.Tasks;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public class ScheduledRunnableTask : ScheduledTask<Void>
{
    private readonly IRunnable _action;

    public ScheduledRunnableTask(AbstractScheduledEventExecutor executor, IRunnable action, long deadlineNanos)
        : base(executor, new TaskCompletionSource<Void>(), deadlineNanos, 0)
    {
        _action = action;
    }
    
    public ScheduledRunnableTask(AbstractScheduledEventExecutor executor, IRunnable action, long deadlineNanos, long periodNanos)
        : base(executor, new TaskCompletionSource<Void>(), deadlineNanos, periodNanos)
    {
        _action = action;
    }



    protected override void Execute() => _action.run();
}