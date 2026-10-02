using System;
using System.Threading;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public sealed class ScheduledStateActionWithContextAsyncTask<T> : ScheduledAsyncTask<T>
{
    public ScheduledStateActionWithContextAsyncTask(AbstractScheduledEventExecutor executor, Action<object, object> action, object context, object state, long deadline, CancellationToken cancellationToken)
        : base(executor, new AnonymousCallable<T>(() => { action(context, state); return default; }), deadline, cancellationToken) { }
}
