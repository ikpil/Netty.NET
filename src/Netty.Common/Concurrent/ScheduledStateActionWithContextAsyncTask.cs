using System;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Common.Concurrent;

public class ScheduledStateActionWithContextAsyncTask : ScheduledAsyncTask
{
    private readonly Action<object, object> _action;
    private readonly object _context;

    public ScheduledStateActionWithContextAsyncTask(AbstractScheduledEventExecutor executor, Action<object, object> action, object context, object state, long deadline, CancellationToken cancellationToken)
        : base(executor, deadline, new TaskCompletionSource(state), cancellationToken)
    {
        _action = action;
        _context = context;
    }

    protected override void Execute() => _action.Invoke(_context, Completion.AsyncState);
}