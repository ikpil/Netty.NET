using System;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public sealed class ScheduledStateActionTask<T> : ScheduledTask<T>
{
    public ScheduledStateActionTask(AbstractScheduledEventExecutor executor, Action<object> action, object state, long deadline)
        : base(executor, new AnonymousCallable<T>(() => { action(state); return default; }), deadline) { }
}
