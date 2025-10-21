using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Common.Concurrent;

public abstract class ScheduledAsyncTask<T> : ScheduledTask<T>
{
    private readonly CancellationToken _cancellationToken;
    private readonly CancellationTokenRegistration _cancellationTokenRegistration;

    protected ScheduledAsyncTask(AbstractScheduledEventExecutor executor, TaskCompletionSource<T> promise, long deadline, CancellationToken cancellationToken)
        : base(executor, promise, deadline)
    {
        _cancellationToken = cancellationToken;
        _cancellationTokenRegistration = cancellationToken.Register(s => ((ScheduledAsyncTask<T>)s).cancel(), this);
    }

    public override void run()
    {
        _cancellationTokenRegistration.Dispose();
        if (_cancellationToken.IsCancellationRequested)
        {
            Promise.TrySetCanceled();
        }
        else
        {
            base.run();
        }
    }
}