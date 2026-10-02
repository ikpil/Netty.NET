using System.Threading;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

public static class Executors
{
    public static IThreadFactory defaultThreadFactory()
    {
        return new NativeDefaultThreadFactory();
    }

    // CLR counterpart of the JDK default factory used by HashedWheelTimer.
    // Capture the creator's logical group once, and create ordinary native
    // foreground threads at normal priority, without fast-thread-local ownership.
    private sealed class NativeDefaultThreadFactory : IThreadFactory
    {
        private static int poolNumber;
        private int threadNumber;
        private readonly ThreadGroup group = ThreadGroup.currentThreadGroup();
        private readonly string prefix = "pool-" + Interlocked.Increment(ref poolNumber) + "-thread-";

        public Thread newThread(IRunnable runnable)
        {
            Thread thread = group.newThread(runnable);
            thread.Name = prefix + Interlocked.Increment(ref threadNumber);
            thread.IsBackground = false;
            thread.Priority = ThreadPriority.Normal;
            return thread;
        }
    }

}
