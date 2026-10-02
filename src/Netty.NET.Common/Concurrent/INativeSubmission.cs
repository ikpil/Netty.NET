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

// Only operations with caller-owned pre-invocation cancellation need this hook.
// The queue owns membership; the submission's existing TCS still owns its result.
internal interface ICancelableNativeSubmission : INativeSubmission
{
    void SetCancellationRemoval(Action remove);
}

// A forwarding executor may hide the actual queue. Bind each runner reservation
// to the pool that admits it so shutdown policy follows that reservation.
internal interface IQueueBoundNativeSubmission : INativeSubmission
{
    void BindQueueOwner(UnorderedThreadPoolEventExecutor owner);
}
