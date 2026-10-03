using Netty.NET.Common.Internal;

namespace Netty.NET.Common;

[UnstableApi]
public abstract class RecyclerEnhancedHandle<T> : IRecyclerHandle<T>
{
    private protected RecyclerEnhancedHandle() { }
    public abstract void UnguardedRecycle(object obj);
    public abstract void Recycle(T self);
}
