using System;
using System.Threading.Tasks;

namespace Netty.NET.Common.Concurrent;

public class ScheduledStateActionWithContextTask<T> : ScheduledTask<T>
{
    private readonly Action<object, object> _action;
    private readonly object _context;

    public ScheduledStateActionWithContextTask(AbstractScheduledEventExecutor executor, Action<object, object> action, object context, object state, long deadline)
        : base(executor, new TaskCompletionSource<T>(state), deadline)
    {
        _action = action;
        _context = context;
    }

    protected override void Execute() => _action.Invoke(_context, Completion.AsyncState);
}