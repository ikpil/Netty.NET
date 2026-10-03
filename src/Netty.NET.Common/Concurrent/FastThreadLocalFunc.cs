using System;

namespace Netty.NET.Common.Concurrent;

public class FastThreadLocalFunc<V> : FastThreadLocal<V> where V : class
{
    private readonly Func<V> _func;

    public FastThreadLocalFunc(Func<V> func)
    {
        _func = func;
    }

    protected override V InitialValue()
    {
        return _func.Invoke();
    }
}