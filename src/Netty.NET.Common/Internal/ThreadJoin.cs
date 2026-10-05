using System;
using System.Diagnostics;
using System.Threading;

namespace Netty.NET.Common.Internal;

internal static class ThreadJoin
{
    internal static bool Join(Thread thread, TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
        if (thread == null) return true;
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            thread.Join();
            return true;
        }

        // CLR Join(0) polls. Round a positive remainder up, and share one monotonic
        // budget across Int32-millisecond chunks without allocating a Stopwatch.
        long started = Stopwatch.GetTimestamp();
        long ticks = timeout.Ticks;
        for (;;)
        {
            long elapsed = Stopwatch.GetElapsedTime(started).Ticks;
            long remaining = elapsed >= ticks ? 0 : ticks - elapsed;
            int milliseconds = remaining == 0 ? 0 :
                (int)Math.Min(int.MaxValue, 1 + (remaining - 1) / TimeSpan.TicksPerMillisecond);
            if (thread.Join(milliseconds)) return true;
            if (remaining == 0 || Stopwatch.GetElapsedTime(started).Ticks >= ticks)
            {
                return !thread.IsAlive;
            }
        }
    }
}
