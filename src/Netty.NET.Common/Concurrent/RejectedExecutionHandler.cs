using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public class RejectedExecutionHandler : IRejectedExecutionHandler
{
    public void Rejected(IRunnable task, SingleThreadEventExecutor executor)
    {
        throw new RejectedExecutionException();
    }
}