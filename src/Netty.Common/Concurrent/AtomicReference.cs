using System.Threading;

namespace Netty.NET.Common.Concurrent;

public class AtomicReference<T> where T : class
{
    private T _location;

    public AtomicReference(T location = null)
    {
        _location = location;
    }

    public T get()
    {
        return Volatile.Read(ref _location);
    }

    public void set(T newValue)
    {
        Volatile.Write(ref _location, newValue);
    }

    public T getAndSet(T value)
    {
        return Interlocked.Exchange(ref _location, value);
    }

    public bool compareAndSet(T expectedValue, T newValue)
    {
        var original = Interlocked.CompareExchange(ref _location, newValue, expectedValue);
        return original == expectedValue;
    }

    public override string ToString()
    {
        var value = get();
        return value?.ToString() ?? "null";
    }
}