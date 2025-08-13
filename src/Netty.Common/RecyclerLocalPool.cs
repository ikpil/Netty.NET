using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Netty.NET.Common.Collections;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common;

public class RecyclerLocalPool<T> : IConsumer<RecyclerDefaultHandle<T>>
{
    private readonly int _ratioInterval;
    private readonly int _chunkSize;
    internal readonly Queue<RecyclerDefaultHandle<T>> _batch;
    internal volatile Thread _owner;
    internal volatile IQueue<RecyclerDefaultHandle<T>> _pooledHandles;
    private int _ratioCounter;

    //("unchecked")
    public RecyclerLocalPool(int maxCapacity, int ratioInterval, int chunkSize)
    {
        _ratioInterval = ratioInterval;
        _chunkSize = chunkSize;
        _batch = new Queue<RecyclerDefaultHandle<T>>(chunkSize);
        Thread currentThread = Thread.CurrentThread;
        _owner = !Recycler.BATCH_FAST_TL_ONLY || FastThreadLocalThread.currentThreadHasFastThreadLocal()
            ? currentThread
            : null;

        if (Recycler.BLOCKING_POOL)
        {
            _pooledHandles = new BlockingQueue<RecyclerDefaultHandle<T>>(maxCapacity);
        }
        else
        {
            _pooledHandles = new MpscArrayQueue<RecyclerDefaultHandle<T>>(maxCapacity);
        }

        _ratioCounter = ratioInterval; // Start at interval so the first one will be recycled.
    }

    public RecyclerDefaultHandle<T> claim()
    {
        var handles = _pooledHandles;
        if (handles == null)
        {
            return null;
        }

        if (0 >= _batch.Count)
        {
            handles.Drain(this, _chunkSize);
        }

        RecyclerDefaultHandle<T> handle = _batch.Dequeue();
        if (null != handle)
        {
            handle.toClaimed();
        }

        return handle;
    }

    public void release(RecyclerDefaultHandle<T> handle, bool guarded)
    {
        if (guarded)
        {
            handle.toAvailable();
        }
        else
        {
            handle.unguardedToAvailable();
        }

        var owner = _owner;
        if (owner != null && Thread.CurrentThread == owner && _batch.Count < _chunkSize)
        {
            accept(handle);
        }
        else if (owner != null && isTerminated(owner))
        {
            _owner = null;
            _pooledHandles = null;
        }
        else
        {
            var handles = _pooledHandles;
            if (handles != null)
            {
                handles.TryEnqueue(handle);
            }
        }
    }

    private static bool isTerminated(Thread owner)
    {
        // Do not use `Thread.getState()` in J9 JVM because it's known to have a performance issue.
        // See: https://github.com/netty/netty/issues/13347#issuecomment-1518537895
        return !owner.IsAlive;
    }

    public RecyclerDefaultHandle<T> newHandle()
    {
        if (++_ratioCounter >= _ratioInterval)
        {
            _ratioCounter = 0;
            return new RecyclerDefaultHandle<T>(this);
        }

        return null;
    }

    public void accept(RecyclerDefaultHandle<T> e)
    {
        _batch.Enqueue(e);
    }
}