using System;
using System.Diagnostics;

namespace Netty.NET.Common;

public class RecyclerDefaultHandle<T> : RecyclerEnhancedHandle<T>
{
    private static readonly int STATE_CLAIMED = 0;
    private static readonly int STATE_AVAILABLE = 1;
    private static readonly AtomicIntegerFieldUpdater<RecyclerDefaultHandle<?>> STATE_UPDATER;

    static {
        AtomicIntegerFieldUpdater < ?> updater = AtomicIntegerFieldUpdater.newUpdater(typeof(DefaultHandle), "state");
        //noinspection unchecked
        STATE_UPDATER = (AtomicIntegerFieldUpdater < DefaultHandle < ?>>) updater;
    }

    private volatile int state; // State is initialised to STATE_CLAIMED (aka. 0) so they can be released.
    private readonly RecyclerLocalPool<T> localPool;
    private T value;

    RecyclerDefaultHandle(RecyclerLocalPool<T> localPool)
    {
        this.localPool = localPool;
    }

    public override void recycle(T obj)
    {
        if (!obj.Equals(value))
        {
            throw new ArgumentException("object does not belong to handle");
        }

        localPool.release(this, true);
    }

    public override void unguardedRecycle(object obj)
    {
        if (!obj.Equals(value))
        {
            throw new ArgumentException("object does not belong to handle");
        }

        localPool.release(this, false);
    }

    public T get()
    {
        return value;
    }

    public void set(T value)
    {
        this.value = value;
    }

    public void toClaimed()
    {
        Debug.Assert(state == STATE_AVAILABLE);
        STATE_UPDATER.lazySet(this, STATE_CLAIMED);
    }

    public void toAvailable()
    {
        int prev = STATE_UPDATER.getAndSet(this, STATE_AVAILABLE);
        if (prev == STATE_AVAILABLE)
        {
            throw new InvalidOperationException("object has been recycled already.");
        }
    }

    public void unguardedToAvailable()
    {
        int prev = state;
        if (prev == STATE_AVAILABLE)
        {
            throw new InvalidOperationException("object has been recycled already.");
        }

        STATE_UPDATER.lazySet(this, STATE_AVAILABLE);
    }
}