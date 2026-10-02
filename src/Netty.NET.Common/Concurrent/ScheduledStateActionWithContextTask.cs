using System;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public sealed class ScheduledStateActionWithContextTask<T> : ScheduledTask<T>
{
    public ScheduledStateActionWithContextTask(AbstractScheduledEventExecutor executor, Action<object, object> action, object context, object state, long deadline)
        : base(executor, new AnonymousCallable<T>(() => { action(context, state); return default; }), deadline) { }
}
