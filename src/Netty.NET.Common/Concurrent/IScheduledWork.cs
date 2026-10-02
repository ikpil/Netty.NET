using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

/// <summary>Deadline-queue membership, independent of any asynchronous result API.</summary>
/// <remarks>The executor owns deadline and sequence changes. Cancellation may be requested from any thread.</remarks>
public interface IScheduledWork : IRunnable
{
    bool IsCanceled { get; }
    void CancelForShutdown();
    long deadlineNanos();
    long delayNanos(long now);
    long delayNanos();
    long getId();
    void AssignId(long id);
    void setConsumed();
}
