using System.Threading.Tasks;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public class ScheduledCallableTask<T> : ScheduledTask<T>
{
    private readonly ICallable<T> _action;

    public ScheduledCallableTask(AbstractScheduledEventExecutor executor, ICallable<T> callable, long deadlineNanos)
        : base(executor, new TaskCompletionSource<T>(), deadlineNanos, 0)
    {
        _action = callable;
    }



    public override void run() => _action.run();
}