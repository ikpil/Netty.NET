namespace Netty.NET.Common.Internal;

public class AnonymousRecycler<T> : Recycler<T> where T : class
{
    private readonly IObjectCreator<T> _creator;

    public AnonymousRecycler(IObjectCreator<T> creator)
    {
        _creator = creator;
    }

    protected override T NewObject(IRecyclerHandle<T> handle)
    {
        return _creator.NewObject(handle);
    }
}
