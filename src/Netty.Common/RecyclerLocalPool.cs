using System.Collections.Generic;
using System.Threading;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Functional;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

public class RecyclerLocalPool<T> : IConsumer<RecyclerDefaultHandle<T>> 
{
    private readonly int ratioInterval;
    private readonly int chunkSize;
    internal readonly Queue<RecyclerDefaultHandle<T>> batch;
    internal volatile Thread owner;
    internal volatile IMessagePassingQueue<RecyclerDefaultHandle<T>> pooledHandles;
    private int ratioCounter;

    RecyclerLocalPool("unchecked")
    public RecyclerLocalPool(int maxCapacity, int ratioInterval, int chunkSize) {
        this.ratioInterval = ratioInterval;
        this.chunkSize = chunkSize;
        batch = new ArrayDeque<RecyclerDefaultHandle<T>>(chunkSize);
        Thread currentThread = Thread.currentThread();
        owner = !BATCH_FAST_TL_ONLY || FastThreadLocalThread.currentThreadHasFastThreadLocal()
                ? currentThread : null;
        if (BLOCKING_POOL) {
            pooledHandles = new Common.BlockingMessageQueue<RecyclerDefaultHandle<T>>(maxCapacity);
        } else {
            pooledHandles = (MessagePassingQueue<RecyclerDefaultHandle<T>>) newMpscQueue(chunkSize, maxCapacity);
        }
        ratioCounter = ratioInterval; // Start at interval so the first one will be recycled.
    }

    public RecyclerDefaultHandle<T> claim() {
        IMessagePassingQueue<RecyclerDefaultHandle<T>> handles = pooledHandles;
        if (handles == null) {
            return null;
        }
        if (batch.isEmpty()) {
            handles.drain(this, chunkSize);
        }
        RecyclerDefaultHandle<T> handle = batch.pollLast();
        if (null != handle) {
            handle.toClaimed();
        }
        return handle;
    }

    public void release(RecyclerDefaultHandle<T> handle, bool guarded) {
        if (guarded) {
            handle.toAvailable();
        } else {
            handle.unguardedToAvailable();
        }
        Thread owner = this.owner;
        if (owner != null && Thread.CurrentThread == owner && batch.size() < chunkSize) {
            accept(handle);
        } else if (owner != null && isTerminated(owner)) {
            this.owner = null;
            pooledHandles = null;
        } else {
            IMessagePassingQueue<RecyclerDefaultHandle<T>> handles = pooledHandles;
            if (handles != null) {
                handles.relaxedOffer(handle);
            }
        }
    }

    private static bool isTerminated(Thread owner) {
        // Do not use `Thread.getState()` in J9 JVM because it's known to have a performance issue.
        // See: https://github.com/netty/netty/issues/13347#issuecomment-1518537895
        return PlatformDependent.isJ9Jvm() ? !owner.isAlive() : owner.getState() == Thread.State.TERMINATED;
    }

    public RecyclerDefaultHandle<T> newHandle() {
        if (++ratioCounter >= ratioInterval) {
            ratioCounter = 0;
            return new RecyclerDefaultHandle<T>(this);
        }
        return null;
    }

    @Override
    public void accept(RecyclerDefaultHandle<T> e) {
        batch.addLast(e);
    }
}
