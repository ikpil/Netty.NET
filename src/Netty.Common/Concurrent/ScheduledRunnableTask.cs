using System.Threading.Tasks;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public class ScheduledRunnableTask : ScheduledTask
{
    private readonly IRunnable _action;

    public ScheduledRunnableTask(AbstractScheduledEventExecutor executor, IRunnable action, long deadline)
        : base(executor, deadline, new TaskCompletionSource())
    {
        this._action = action;
    }

    protected override void Execute() => _action.run();
}