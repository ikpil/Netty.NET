using System;
using System.Diagnostics;
using System.Threading;

namespace Netty.NET.Common.Internal;

public static class ThreadLocalRandom
{
    private static int _seed = (int)(Stopwatch.GetTimestamp() & 0xFFFFFFFF);
    
    [ThreadStatic]
    private static readonly Random RND = CreateThreadLocalRandom();

    private static Random CreateThreadLocalRandom()
    {
        if (null != RND)
            return RND;
        
        var next = Interlocked.Increment(ref _seed);
        var rnd = new Random(next);
        return rnd;
    }

    public static Random current() => RND;
}