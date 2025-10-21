using System;
using System.Threading.Tasks;

namespace Netty.NET.Common.Concurrent;

public class ScheduledActionTask : ScheduledTask<Void>
{
    private readonly Action _action;

    public ScheduledActionTask(AbstractScheduledEventExecutor executor, Action action, long deadline)
        : base(executor, new TaskCompletionSource<Void>(), deadline)
    {
        this._action = action;
    }

    public override void run() => _action.Invoke();
}