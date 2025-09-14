using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

// This is a special wrapper which we will be used in execute(...) to wrap the submitted IRunnable. This is needed as
// ScheduledThreadPoolExecutor.execute(...) will delegate to submit(...) which will then use decorateTask(...).
// The problem with this is that decorateTask(...) needs to ensure we only do our own decoration if we not call
// from execute(...) as otherwise we may end up creating an endless loop because DefaultPromise will call
// IEventExecutor.execute(...) when notify the listeners of the promise.
//
// See https://github.com/netty/netty/issues/6507
internal sealed class NonNotifyRunnable : IRunnable
{
    private readonly IRunnable task;

    public NonNotifyRunnable(IRunnable task)
    {
        this.task = task;
    }

    public void run()
    {
        task.run();
    }
}