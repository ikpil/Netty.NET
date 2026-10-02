using System.Threading;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

// CLR cancellation-token wrapper. The action and completion use the Netty scheduled promise state.
public abstract class ScheduledAsyncTask<T> : ScheduledTask<T>
{
    private readonly CancellationToken _cancellationToken;
    private readonly CancellationTokenRegistration _registration;
    protected ScheduledAsyncTask(AbstractScheduledEventExecutor executor, ICallable<T> callable, long deadline, CancellationToken cancellationToken)
        : base(executor, callable, deadline)
    {
        _cancellationToken = cancellationToken;
        _registration = cancellationToken.Register(s => ((ScheduledAsyncTask<T>)s).cancel(), this);
        if (isDone()) _registration.Dispose();
    }
    public override bool cancel(bool mayInterruptIfRunning)
    {
        bool cancelled = base.cancel(mayInterruptIfRunning);
        if (cancelled) _registration.Dispose();
        return cancelled;
    }
    public override bool cancelWithoutRemove(bool mayInterruptIfRunning)
    {
        bool cancelled = base.cancelWithoutRemove(mayInterruptIfRunning);
        if (cancelled) _registration.Dispose();
        return cancelled;
    }
    public override void run()
    {
        try
        {
            if (_cancellationToken.IsCancellationRequested) cancel();
            else base.run();
        }
        finally { if (isDone()) _registration.Dispose(); }
    }
}
