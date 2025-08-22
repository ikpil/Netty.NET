namespace Netty.NET.Common.Internal;

public class RecyclerObjectPool<T> : ObjectPool<T>
{
    private readonly Recycler<T> _recycler;

    public RecyclerObjectPool(IObjectCreator<T> creator)
    {
        _recycler = new AnonymousRecycler<T>(creator);
    }

    public override T get()
    {
        return _recycler.get();
    }
}