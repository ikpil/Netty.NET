using System;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Common.Concurrent;

public class ScheduledStateActionAsyncTask<T> : ScheduledAsyncTask<T>
{
    private readonly Action<object> _action;

    public ScheduledStateActionAsyncTask(AbstractScheduledEventExecutor executor, Action<object> action, object state, long deadline, CancellationToken cancellationToken)
        : base(executor, new TaskCompletionSource<T>(state), deadline, cancellationToken)
    {
        _action = action;
    }

    protected override void Execute() => _action.Invoke(Completion.AsyncState);
}