using System;
using System.Threading.Tasks;

namespace Netty.NET.Common.Concurrent;

public class ScheduledStateActionTask<T> : ScheduledTask<T>
{
    private readonly Action<object> _action;

    public ScheduledStateActionTask(AbstractScheduledEventExecutor executor, Action<object> action, object state, long deadline)
        : base(executor, new TaskCompletionSource<T>(state), deadline)
    {
        _action = action;
    }

    protected override void Execute() => _action.Invoke(Completion.AsyncState);
}