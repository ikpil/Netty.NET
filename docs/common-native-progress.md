# Native Task and progress notification

Baseline: `e66ce34777f9c4a0c57ac74bb97396ca2f54b43c` in `../netty`.
Scope: common notification policy and its CLR contracts, not the channel or
chunked-write implementations in other modules.

## Original evidence and consumers

- `common/.../concurrent/DefaultProgressivePromise.java` validates nonnegative
  progress, a known total at least as large as progress, and an unfinished
  operation. Every negative total means unknown length. It does not require
  successive reports to increase.
- `DefaultPromise.java` snapshots progressive listeners, invokes them inline
  in the event loop or dispatches through the executor, and logs observer
  exceptions without changing the operation's result.
- `transport/.../channel/ChannelOutboundBuffer.java` accumulates bytes written
  and reports them against the entry's total through ChannelProgressivePromise.
- `handler/.../stream/ChunkedWriteHandler.java` reports chunk progress. Its
  PendingWrite.success reports the final `(total, total)` before trySuccess.
- `transport/.../channel/DefaultChannelProgressivePromise.java` supplies the
  channel executor. This is an execution policy requirement, not a reason to
  duplicate the operation's result in a second public promise hierarchy.

Task represents the producer-owned outcome; IProgress represents reporting.
Progress<T>'s captured SynchronizationContext alone does not specify Netty
executor affinity, report/completion ordering or callback failure isolation.
ExecutorProgress implements those policies independently of the result Task.

## Native API and ownership

TransferProgress is an immutable value with `long Completed` and nullable
`long? Total`. Negative raw totals normalize to null. Negative completed values
and values exceeding a known total are rejected. Zero-length transfers and full
long precision are valid; reports are not forcibly monotonic.

ExecutorProgress accepts an existing Task, optionally with initial
progress/completion delegates. Register adds detachable subscriptions while the
operation is open; each admitted report snapshots active registration membership.
Multicast invocation lists are captured at registration. The producer keeps
its TaskCompletionSource or asynchronous method and is the only result owner.
The reporter does not complete, cancel or replace that operation.

```csharp
var source = new TaskCompletionSource<int>(
    TaskCreationOptions.RunContinuationsAsynchronously);
using var notifications = new ExecutorProgress(executor, source.Task,
    progress => UpdateTransferDisplay(progress.Completed, progress.Total),
    completed => OnTransferCompleted(completed));
IProgress<TransferProgress> progressSink = notifications;

// The producer can accept the standard IProgress interface.
progressSink.Report(new TransferProgress(3, 10));
progressSink.Report(new TransferProgress(7, 10));
progressSink.Report(new TransferProgress(10, 10));
source.SetResult(10); // Final progress must be reported before completion.
int result = await source.Task;
await notifications.NotificationsCompleted;
```

NotificationsCompleted is a separate notification-lifetime Task, not a copy of
the operation outcome. It succeeds after terminal callback dispatch even when
the operation failed or was canceled; those outcomes remain on the original
Task supplied to the completion callback. Observer exceptions are logged and
isolated so that later observers still execute.

## Dispatch, admission and lifetime

Reports and terminal notification use one FIFO and one draining work item per
reporter, including when the selected executor has multiple workers. Callback
invocations do not overlap. In-loop reporting can drain inline; recursive reports
append after all observers of the current report. The Java implementation can
recursively notify progress inline. The native API deliberately bounds the stack
and specifies FIFO reentrancy; a 10,001-report regression verifies that change.

Reports admitted before the terminal marker drain before completion observers.
Producers must emit final progress before completing their Task, as the original
chunked-write consumer does. Task completion and reporter admission are separate
operations, so this API does not make a multi-threaded producer's reporting and
TaskCompletionSource mutation into one atomic operation.

TryReport returns false for invalid raw counts, a closed/completed observation,
or exhausted pending capacity. Report throws when valid reports cannot be
admitted. The default pending capacity is int.MaxValue; maxPendingReports can
bound buffered progress independently of the executor's queue. The terminal
marker is admitted even when the report capacity is full. Successful admission
does not guarantee dispatch: executor rejection faults NotificationsCompleted
and leaves the underlying Task's outcome untouched.

Constructor/producer ExecutionContext is not captured. Each synchronous observer
runs under the executor's ambient context, scoped so that AsyncLocal changes do
not leak to other observers or the executor. Callback affinity covers synchronous
invocation only; an async-void delegate cannot provide observable asynchronous
completion and is not a supported substitute for a Task-returning consumer.

Dispose closes this observation, cancels its notification Task and releases
queued reports and callback references. It does not request cancellation of the
producer. A callback already claimed for invocation may finish. A weak completion
continuation permits an abandoned reporter to be collected while the operation
is pending. Retaining NotificationsCompleted keeps a pending dispatcher alive;
after terminal dispatch, rejection or disposal it releases the operation,
observers and executor so retaining that notification Task does not retain a
completed operation's result.

## Verification and remaining migration

ExecutorProgressContractTest has 26 passing cases: actual Task/IProgress chunked
transfer ordering, bounds, unknown length, success/fault/cancel identity,
multicast failure isolation, bounded reentrancy, executor context isolation,
capacity, rejection, disposal, concurrent ordered/unordered execution and weak
reference lifetimes, plus four native queue-removal cases. Those four initially
timed out when immediate shutdown removed a pending progress/terminal drain on
a direct pool or an ordered NonSticky child. Each drain now has a native queue
reservation; removal cancels NotificationsCompleted without altering the source
Task. Evidence: native-progress-removal-before.trx and
native-completion-final-contracts.trx (222 coupled cases passed).
The original progress checkpoint passed the full default suite in
Debug and Release: 1226 passed / 0 failed / 14 skipped, 1240 total, on
Windows/net10.0. Logs: native-progress-contracts-final.trx and
native-progress-full-debug/release.trx in the ignored TestResults directory.
Later full-suite results are recorded in common-porting.md.

Dynamic progress registration/removal now uses unique disposable handles;
the legacy progressive hierarchy and factories have been removed. Original
comments and the native registration, claim and lifetime decisions are preserved
in [common-progress-subscriptions.md](common-progress-subscriptions.md).
Native completion-only registration/removal is implemented separately in
ExecutorCompletion; see common-native-completion.md. Remaining plain
Future/Promise producers/callers and executor backend decisions still require
review. No allocation or throughput improvement is claimed without measurements.
