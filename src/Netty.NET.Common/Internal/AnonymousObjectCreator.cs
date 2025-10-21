using System;

namespace Netty.NET.Common.Internal;

public class AnonymousObjectCreator<T> : IObjectCreator<T>
{
    private readonly Func<IObjectPoolHandle<T>, T> _factory;

    public AnonymousObjectCreator(Func<IObjectPoolHandle<T>, T> factory)
    {
        _factory = factory;
    }

    public T newObject(IObjectPoolHandle<T> handle)
    {
        return _factory.Invoke(handle);
    }
}