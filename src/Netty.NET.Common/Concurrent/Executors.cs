using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public static class Executors
{
    public static IThreadFactory defaultThreadFactory()
    {
        // todo!!
        return null;
    }

    public static IExecutorService newFixedThreadPool(int thread)
    {
        return null;
    }

    public static ICallable<Void> callable(IRunnable runnable)
    {
        return null;
    }
}