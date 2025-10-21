using System;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Common.Concurrent;

public class ScheduledActionAsyncTask : ScheduledAsyncTask<Void>
{
    private readonly Action _action;

    public ScheduledActionAsyncTask(AbstractScheduledEventExecutor executor, Action action, long deadline, CancellationToken cancellationToken)
        : base(executor, new TaskCompletionSource<Void>(), deadline, cancellationToken)
    {
        _action = action;
    }

    public override void run() => _action.Invoke();
 
}