using System;
using System.Threading.Tasks;

namespace Netty.NET.Common.Concurrent;

public class ScheduledActionTask : ScheduledTask
{
    private readonly Action _action;

    public ScheduledActionTask(AbstractScheduledEventExecutor executor, Action action, long deadline)
        : base(executor, deadline, new TaskCompletionSource())
    {
        this._action = action;
    }

    protected override void Execute() => _action.Invoke();
}