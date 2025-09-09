using System;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Common.Concurrent;

public class ScheduledActionAsyncTask : ScheduledAsyncTask
{
    private readonly Action _action;

    public ScheduledActionAsyncTask(AbstractScheduledEventExecutor executor, Action action, long deadline, CancellationToken cancellationToken)
        : base(executor, deadline, new TaskCompletionSource(), cancellationToken)
    {
        _action = action;
    }

    protected override void Execute() => _action.Invoke();
 
}