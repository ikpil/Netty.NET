using System;
using System.Threading;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public class RejectedBackOffExecutionHandler : IRejectedExecutionHandler
{
    private readonly int _retries;
    private readonly TimeSpan _backoffAmount;
    
    public RejectedBackOffExecutionHandler(int retries, TimeSpan backoffAmount)
    {
        _retries = retries;
        _backoffAmount = backoffAmount;
    }
    
    public void rejected(IRunnable task, SingleThreadEventExecutor executor)
    {
        long backOffNanos = (long)_backoffAmount.TotalNanoseconds;
        if (!executor.inEventLoop())
        {
            for (int i = 0; i < _retries; i++)
            {
                // Try to wake up the executor so it will empty its task queue.
                executor.wakeup(false);

                TimeSpan timeout = TimeSpan.FromTicks(backOffNanos / 100); // 100
                Thread.Sleep(timeout);
                //LockSupport.parkNanos(backOffNanos);
                if (executor.offerTask(task))
                {
                    return;
                }
            }
        }

        // Either we tried to add the task from within the EventLoop or we was not able to add it even with
        // backoff.
        throw new RejectedExecutionException();
    }
}