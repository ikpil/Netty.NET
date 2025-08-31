using System;

namespace Netty.NET.Common.Functional;

public class AnonymousCallable<T> : ICallable<T>
{
    private readonly Func<T> _func;

    public AnonymousCallable(Func<T> func)
    {
        _func = func;
    }

    public T call()
    {
        return _func.Invoke();
    }
}