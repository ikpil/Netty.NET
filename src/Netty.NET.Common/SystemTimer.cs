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
    public static long NanoTime()
    {
        long timestamp = Clock.GetTimestamp();
        return NanosMultiplier != 0 ? unchecked(timestamp * NanosMultiplier) :
            ScaleTimestamp(timestamp, NanosecondsPerSecond);
    }

    public static long Millis()
    {
        return ScaleTimestamp(Clock.GetTimestamp(), 1_000);
    }

    public static long Seconds()
    {
        return Clock.GetTimestamp() / Frequency;
    }

    internal static long ScaleTimestamp(long timestamp, long unitsPerSecond)
        => ScaleTimestamp(timestamp, unitsPerSecond, Frequency);

    internal static long ScaleTimestamp(long timestamp, long unitsPerSecond, long frequency)
    {
        // Common frequencies use ordinary integer arithmetic on the hot path.
        // Wrap after scaling, as System.nanoTime does; a floating cast can lose
        // precision or turn a distant positive timestamp into long.MinValue.
        if (unitsPerSecond % frequency == 0)
            return unchecked(timestamp * (unitsPerSecond / frequency));
        if (frequency % unitsPerSecond == 0)
            return timestamp / (frequency / unitsPerSecond);
        return unchecked((long)((Int128)timestamp * unitsPerSecond / frequency));
    }
}
