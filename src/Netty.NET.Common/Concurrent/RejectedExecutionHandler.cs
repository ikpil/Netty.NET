using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public class RejectedExecutionHandler : IRejectedExecutionHandler
{
    public void rejected(IRunnable task, SingleThreadEventExecutor executor)
    {
        throw new RejectedExecutionException();
    }
}