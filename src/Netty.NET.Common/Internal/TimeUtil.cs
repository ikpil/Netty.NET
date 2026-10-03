using System;

namespace Netty.NET.Common.Internal;

internal static class TimeUtil
{
    // TimeSpan has integer 100 ns ticks. Saturation follows TimeUnit.toNanos;
    // the clock itself still wraps, so never saturate timestamp accumulation.
    internal static long ToNanoseconds(TimeSpan duration) => SaturatingMultiply(duration.Ticks, 100);

    internal static long MillisecondsToNanoseconds(long milliseconds) => SaturatingMultiply(milliseconds, 1_000_000);

    private static long SaturatingMultiply(long value, long multiplier)
    {
        if (value > long.MaxValue / multiplier) return long.MaxValue;
        if (value < long.MinValue / multiplier) return long.MinValue;
        return value * multiplier;
    }
}
