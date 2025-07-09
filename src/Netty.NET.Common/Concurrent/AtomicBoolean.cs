using System.Threading;

namespace Netty.NET.Common.Concurrent;

public class AtomicBoolean
{
    private int _location;

    public AtomicBoolean() : this(false)
    {
    }

    public AtomicBoolean(bool initialValue)
    {
        _location = initialValue ? 1 : 0;
    }

    public bool read()
    {
        return Volatile.Read(ref _location) != 0;
    }

    // true if successful, otherwise false if the witness value was not the same as the expectedValue.
    public bool compareAndSet(bool expectedValue, bool newValue)
    {
        var expectedInt = expectedValue ? 1 : 0;
        var newInt = newValue ? 1 : 0;
        var original = Interlocked.CompareExchange(ref _location, newInt, expectedInt);
        return original == expectedInt;
    }
}