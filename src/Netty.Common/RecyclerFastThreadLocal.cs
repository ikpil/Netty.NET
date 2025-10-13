using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common;

public class RecyclerFastThreadLocal<T> : FastThreadLocal<RecyclerLocalPool<T>>
{
    private readonly int _maxCapacityPerThread;
    private readonly int _interval;
    private readonly int _chunkSize;

    public RecyclerFastThreadLocal(int maxCapacityPerThread, int interval, int chunkSize)
    {
        _maxCapacityPerThread = maxCapacityPerThread;
        _interval = interval;
        _chunkSize = chunkSize;
    }

    protected override RecyclerLocalPool<T> initialValue()
    {
        return new RecyclerLocalPool<T>(_maxCapacityPerThread, _interval, _chunkSize);
    }

    protected override void onRemoval(RecyclerLocalPool<T> value)
    {
        base.onRemoval(value);
        var handles = value._pooledHandles;
        value._pooledHandles = null;
        value._owner = null;
        handles.clear();
    }
}