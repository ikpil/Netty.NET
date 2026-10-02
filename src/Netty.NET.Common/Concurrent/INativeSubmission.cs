using System;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Concurrent;

// Queue removal must finish native submitted work without introducing another
// result owner. These members describe invocation admission and cancellation.
internal interface INativeSubmission : IRunnable
{
    bool IsCanceled { get; }
    void CancelForShutdown();
    void Reject(Exception error);
}

// A forwarding executor may hide the actual queue. Bind each runner reservation
// to the pool that admits it so shutdown policy follows that reservation.
internal interface IQueueBoundNativeSubmission : INativeSubmission
{
    void BindQueueOwner(UnorderedThreadPoolEventExecutor owner);
}
