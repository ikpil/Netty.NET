using System;

namespace Netty.NET.Common.Concurrent;

/// <summary>Deadline-queue membership, independent of any asynchronous result API.</summary>
/// <remarks>The executor owns deadline and sequence changes. Cancellation may be requested from any thread.</remarks>
public interface IScheduledWork
{
    /// <summary>The stable callback shared by submission, due transfer and removal.</summary>
    /// <remarks>Return the same non-null Action instance throughout this membership's lifetime.</remarks>
    Action QueueCallback { get; }
    bool IsCanceled { get; }
    void CancelForShutdown();
    long DeadlineNanos();
    long DelayNanos(long now);
    long DelayNanos();
    long GetId();
    void AssignId(long id);
    void SetConsumed();
}
