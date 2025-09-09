using System;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Common.Concurrent;

public class ScheduledStateActionAsyncTask : ScheduledAsyncTask
{
    private readonly Action<object> _action;

    public ScheduledStateActionAsyncTask(AbstractScheduledEventExecutor executor, Action<object> action, object state, long deadline, CancellationToken cancellationToken)
        : base(executor, deadline, new TaskCompletionSource(state), cancellationToken)
    {
        _action = action;
    }

    protected override void Execute() => _action.Invoke(Completion.AsyncState);
}