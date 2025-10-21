namespace Netty.NET.Common;

public class NoopRecyclerEnhancedHandle<T> : RecyclerEnhancedHandle<T>
{
    public override void unguardedRecycle(object obj)
    {
        // NOOP
    }

    public override void recycle(T self)
    {
        // NOOP
    }

    public override string ToString()
    {
        return "NOOP_HANDLE";
    }
}