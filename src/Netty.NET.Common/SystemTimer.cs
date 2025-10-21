using System.Diagnostics;

namespace Netty.NET.Common;

public static class SystemTimer
{
    public const long NanosecondsPerSecond = 1_000_000_000L;
    public static readonly double NanoPerFrequency = NanosecondsPerSecond / (double)Stopwatch.Frequency;
    public static readonly double MilliPerFrequency = 1_000.0 / Stopwatch.Frequency;

    // System.nanoTime()
    public static long nanoTime()
    {
        return (long)(Stopwatch.GetTimestamp() * NanoPerFrequency);
    }

    public static long millis()
    {
        return (long)(Stopwatch.GetTimestamp() * MilliPerFrequency);
    }

    public static long seconds()
    {
        return Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    }
}