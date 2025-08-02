using System.Diagnostics;

namespace Netty.NET.Common;

public static class NanoTimer
{
    private static readonly double TickToNano = 1_000_000_000.0 / Stopwatch.Frequency;

    public static long nanoTime()
    {
        return (long)(Stopwatch.GetTimestamp() * TickToNano);
    }
}