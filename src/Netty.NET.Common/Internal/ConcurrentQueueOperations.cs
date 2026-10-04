using System.Collections.Concurrent;
using System.Threading;

namespace Netty.NET.Common.Internal;

// Resource handoffs must finish despite CLR monitor interruption. These two
// operations retain the native ConcurrentQueue; capacity and lifetime belong to its owner.
internal static class ConcurrentQueueOperations
{
    internal static void EnqueueUninterruptibly<T>(ConcurrentQueue<T> queue, T value)
    {
        bool interrupted = false;
        try
        {
            for (;;)
            {
                try { queue.Enqueue(value); return; }
                catch (ThreadInterruptedException) { interrupted = true; }
            }
        }
        finally
        {
            // ConcurrentQueue segment transitions can wait on a CLR monitor.
            // An interrupted transition has not published this value. Keep its
            // one capacity reservation and restore the interrupt after publication.
            if (interrupted) Thread.CurrentThread.Interrupt();
        }
    }

    internal static bool TryDequeueUninterruptibly<T>(ConcurrentQueue<T> queue, out T value)
    {
        bool interrupted = false;
        try
        {
            for (;;)
            {
                try { return queue.TryDequeue(out value); }
                catch (ThreadInterruptedException) { interrupted = true; }
            }
        }
        finally
        {
            // The CLR can interrupt before an empty segment is advanced, but
            // never after the returned item is claimed. Retry without a second claim.
            if (interrupted) Thread.CurrentThread.Interrupt();
        }
    }
}
