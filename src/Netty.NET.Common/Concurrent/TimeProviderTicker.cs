using System;

namespace Netty.NET.Common.Concurrent;

// TimeProvider owns timestamps. The executor continues to own callback dispatch,
// deadline order and waiting; a timer callback is not an event-loop invocation.
internal sealed class TimeProviderTicker : Ticker
{
    private readonly TimeProvider provider;
    private readonly long frequency;
    private readonly long startTimestamp;
    private readonly long initialNanos;
    private readonly long nanosMultiplier;

    internal TimeProviderTicker(TimeProvider timeProvider)
    {
        provider = timeProvider;
        frequency = provider.TimestampFrequency;
        if (frequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeProvider), "TimestampFrequency must be positive.");
        startTimestamp = provider.GetTimestamp();
        initialNanos = SystemTimer.ScaleTimestamp(startTimestamp, SystemTimer.NanosecondsPerSecond, frequency);
        nanosMultiplier = SystemTimer.NanosecondsPerSecond % frequency == 0 ?
            SystemTimer.NanosecondsPerSecond / frequency : 0;
    }

    public override long initialNanoTime() => initialNanos;

    public override long nanoTime()
    {
        // Subtract native ticks before conversion. This preserves native clock
        // wrap and avoids rounding a fractional frequency's absolute origin twice.
        long elapsed = unchecked(provider.GetTimestamp() - startTimestamp);
        return nanosMultiplier != 0 ? unchecked(elapsed * nanosMultiplier) :
            SystemTimer.ScaleTimestamp(elapsed, SystemTimer.NanosecondsPerSecond, frequency);
    }

    public override void sleep(long delayNanos) => throw new NotSupportedException(
        "A timestamp provider does not supply synchronous sleep. Drive the executor or use an explicit wait policy.");
}
