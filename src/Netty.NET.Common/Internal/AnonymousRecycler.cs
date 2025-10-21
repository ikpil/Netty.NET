namespace Netty.NET.Common.Internal;

public class AnonymousRecycler<T> : Recycler<T>
{
    private readonly IObjectCreator<T> _creator;

    public AnonymousRecycler(IObjectCreator<T> creator)
    {
        _creator = creator;
    }

    protected override T newObject(IRecyclerHandle<T> handle)
    {
        return _creator.newObject(handle);
    }
}