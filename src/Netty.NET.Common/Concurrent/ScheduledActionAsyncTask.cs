using System;
using System.Threading;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public sealed class ScheduledActionAsyncTask : ScheduledAsyncTask<Void>
{
    public ScheduledActionAsyncTask(AbstractScheduledEventExecutor executor, Action action, long deadline, CancellationToken cancellationToken)
        : base(executor, new AnonymousCallable<Void>(() => { action(); return default; }), deadline, cancellationToken) { }
}
