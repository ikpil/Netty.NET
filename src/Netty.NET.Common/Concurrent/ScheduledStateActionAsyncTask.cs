using System;
using System.Threading;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public sealed class ScheduledStateActionAsyncTask<T> : ScheduledAsyncTask<T>
{
    public ScheduledStateActionAsyncTask(AbstractScheduledEventExecutor executor, Action<object> action, object state, long deadline, CancellationToken cancellationToken)
        : base(executor, new AnonymousCallable<T>(() => { action(state); return default; }), deadline, cancellationToken) { }
}
