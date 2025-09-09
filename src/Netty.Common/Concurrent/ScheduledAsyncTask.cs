using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Common.Concurrent;

public abstract class ScheduledAsyncTask : ScheduledTask
{
    private readonly CancellationToken _cancellationToken;
    private readonly CancellationTokenRegistration _cancellationTokenRegistration;

    protected ScheduledAsyncTask(AbstractScheduledEventExecutor executor, long deadline, TaskCompletionSource promise, CancellationToken cancellationToken)
        : base(executor, deadline, promise)
    {
        _cancellationToken = cancellationToken;
        _cancellationTokenRegistration = cancellationToken.Register(s => ((ScheduledAsyncTask)s).cancel(), this);
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