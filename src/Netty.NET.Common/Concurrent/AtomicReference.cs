using System.Threading;

namespace Netty.NET.Common.Concurrent;

public class AtomicReference<T> where T : class
{
    private T _location;

    public AtomicReference(T location = null)
    {
        _location = location;
    }

    public T Get()
    {
        return Volatile.Read(ref _location);
    }

    public void Set(T newValue)
    {
        Volatile.Write(ref _location, newValue);
    }

    public T GetAndSet(T value)
    {
        return Interlocked.Exchange(ref _location, value);
    }

    public bool CompareAndSet(T expectedValue, T newValue)
    {
        var original = Interlocked.CompareExchange(ref _location, newValue, expectedValue);
        return original == expectedValue;
    }

    public override string ToString()
    {
        var value = Get();
        return value?.ToString() ?? "null";
    }
}