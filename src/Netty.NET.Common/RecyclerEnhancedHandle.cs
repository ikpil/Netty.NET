using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

[UnstableApi]
public abstract class RecyclerEnhancedHandle<T> : IRecyclerHandle<T>
{
    public abstract void unguardedRecycle(object obj);
    public abstract void recycle(T self);
}