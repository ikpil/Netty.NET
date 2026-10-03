using System;

namespace Netty.NET.Common;

public static class SystemTimer
{
    public const long NanosecondsPerSecond = 1_000_000_000L;
    private static readonly TimeProvider Clock = TimeProvider.System;
    private static readonly long Frequency = Clock.TimestampFrequency;
    private static readonly long NanosMultiplier = NanosecondsPerSecond % Frequency == 0 ?
        NanosecondsPerSecond / Frequency : 0;
    public static readonly double NanoPerFrequency = NanosecondsPerSecond / (double)Frequency;
    public static readonly double MilliPerFrequency = 1_000.0 / Frequency;

    // System.nanoTime()
    public static long nanoTime()
    {
        long timestamp = Clock.GetTimestamp();
        return NanosMultiplier != 0 ? unchecked(timestamp * NanosMultiplier) :
            ScaleTimestamp(timestamp, NanosecondsPerSecond);
    }

    public static long millis()
    {
        return ScaleTimestamp(Clock.GetTimestamp(), 1_000);
    }

    public static long seconds()
    {
        return Clock.GetTimestamp() / Frequency;
    }

    internal static long ScaleTimestamp(long timestamp, long unitsPerSecond)
    {
        // Common frequencies use ordinary integer arithmetic on the hot path.
        // Wrap after scaling, as System.nanoTime does; a floating cast can lose
        // precision or turn a distant positive timestamp into long.MinValue.
        if (unitsPerSecond % Frequency == 0)
            return unchecked(timestamp * (unitsPerSecond / Frequency));
        if (Frequency % unitsPerSecond == 0)
            return timestamp / (Frequency / unitsPerSecond);
        return unchecked((long)((Int128)timestamp * unitsPerSecond / Frequency));
    }
}
