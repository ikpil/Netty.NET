using System;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public sealed class ScheduledActionTask : ScheduledTask<Void>
{
    public ScheduledActionTask(AbstractScheduledEventExecutor executor, Action action, long deadline)
        : base(executor, Runnables.Create(action), deadline) { }
}
