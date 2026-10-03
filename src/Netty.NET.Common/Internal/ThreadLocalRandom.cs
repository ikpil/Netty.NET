using System;
using System.Diagnostics;
using System.Threading;

namespace Netty.NET.Common.Internal;

public static class ThreadLocalRandom
{
    // The seed deliberately keeps the low 32 timestamp bits, including the sign bit.
    private static int _seed = unchecked((int)(Stopwatch.GetTimestamp() & 0xFFFFFFFF));

    [ThreadStatic]
    private static Random _rnd;

    private static Random CreateOrGetLocalRandom()
    {
        if (null != _rnd)
            return _rnd;
        
        var next = Interlocked.Increment(ref _seed);
        var rnd = new Random(next);
        _rnd = rnd;
        return _rnd;
    }

    public static Random current() => CreateOrGetLocalRandom();
}
