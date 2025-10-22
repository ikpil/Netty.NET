using System;
using System.Threading;

namespace Netty.NET.Common;

public static class RecyclerDefaultHandle
{
    public static readonly int STATE_CLAIMED = 0;
    public static readonly int STATE_AVAILABLE = 1;
}

public class RecyclerDefaultHandle<T> : RecyclerEnhancedHandle<T>
{
    private volatile int _state; // State is initialised to STATE_CLAIMED (aka. 0) so they can be released.
    private readonly RecyclerLocalPool<T> _localPool;
    private T _value;

    public RecyclerDefaultHandle(RecyclerLocalPool<T> localPool)
    {
        _localPool = localPool;
    }

    public override void recycle(T obj)
    {
        if (!obj.Equals(_value))
        {
            throw new ArgumentException("object does not belong to handle");
        }

        _localPool.release(this, true);
    }

    public override void unguardedRecycle(object obj)
    {
        if (!obj.Equals(_value))
        {
            throw new ArgumentException("object does not belong to handle");
        }

        _localPool.release(this, false);
    }

    public T get()
    {
        return _value;
    }

    public void set(T value)
    {
        _value = value;
    }

    public void toClaimed()
    {
        if (_state != RecyclerDefaultHandle.STATE_AVAILABLE)
        {
            throw new InvalidOperationException("State is not AVAILABLE");
        }

        // Equivalent to lazySet: volatile write
        _state = RecyclerDefaultHandle.STATE_CLAIMED;
    }

    public void toAvailable()
    {
        int prev = Interlocked.Exchange(ref _state, RecyclerDefaultHandle.STATE_AVAILABLE);
        if (prev == RecyclerDefaultHandle.STATE_AVAILABLE)
        {
            throw new InvalidOperationException("object has been recycled already.");
        }
    }

    public void unguardedToAvailable()
    {
        int prev = _state;
        if (prev == RecyclerDefaultHandle.STATE_AVAILABLE)
        {
            throw new InvalidOperationException("object has been recycled already.");
        }

        // Equivalent to lazySet: volatile write
        _state = RecyclerDefaultHandle.STATE_AVAILABLE;
    }
}