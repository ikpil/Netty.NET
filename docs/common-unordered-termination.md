# Unordered executor termination ownership

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.
Scope: common and its tests; transport consumers are evidence only.

## Evidence and decision

EventExecutorGroup.java documents terminationFuture as notification that every
managed executor has terminated. SingleThreadEventExecutor publishes completion
after cleanup, and MultithreadEventExecutorGroup counts each child's completion.
Transport BootstrapTest and LocalTransportThreadModelTest await the group signal
during cleanup; they require a usable lifetime boundary. The pinned unordered
executor instead calls terminationFuture.trySuccess in shutdown/shutdownNow,
immediately after the inherited pool receives the request. Its awaitTermination
and isTerminated are separate inherited pool observations. That early signal is
an implementation inconsistency with the documented group contract, not an
ordering guarantee needed by those consumers.

The CLR pool now completes its persistent, producer-owned Termination Task when
shutdown has been requested, its queue is empty, and its worker set and pending
thread-creation reservations are empty. Pool mutations publish this condition
under the same gate used by isTerminated/awaitTermination. The TCS keeps
RunContinuationsAsynchronously, so user continuations cannot execute inline
during pool-state publication. Repeated shutdown requests return the same Task;
canceling a WaitAsync observer never cancels shutdown or this Task.

This deliberately changes the pinned unordered shutdown signal. A consumer that
only needs request acknowledgement can call shutdown synchronously; awaiting
ShutdownGracefullyAsync now waits for the executor's lifetime boundary. An
immediate shutdown used CLR interruption at this checkpoint. The subsequent
common-unordered-cooperative-stop.md replaces it with explicit StopToken cooperation
and includes cancellation notification drain/failures. Neither path forces
noncooperating user code to return. Neither normal nor immediate shutdown completes while such a
worker still owns an invocation. A reentrant factory's creation reservation also
prevents completion until its call returns, even when it returns no Thread.

Queue clear transfers no work handles to the caller. It therefore cancels removed
native submission/scheduling reservations before publishing an empty queue.
The subsequent native scheduling migration removes legacy Future queue results.
The subsequent graceful-shutdown review refuses queue insertion at admission
closure and prevents detached periodic work from requeueing after Termination.
Open-admission transfer/removal/reinsertion contracts remain exercised.

The lifetime boundary covers executor queue work and worker-loop ownership. It
does not join extra code a custom thread factory runs after workerLoop returns,
or asynchronous delegate bodies after their invocation yields. Consumers still
own and await those returned operation Tasks. No Task.Run/Task.Delay replaces
the executor's workers, deadlines, cancellation ownership or execution location.

## Verification and remaining backend work

Six new cases failed before the repair (unordered-termination-before.trx).
They cover two active workers under both shutdown modes, observer cancellation,
workerless delayed cancellation, shutdown-policy removal, reentrant creation and
permanent termination. A seventh case independently reproduced queue clear
completing shutdown while native result Tasks stayed pending
(unordered-termination-clear-before.trx). All seven pass after the repair.

The affected native/original selection passes 156 cases with zero failures/skips:
unordered-termination-final-contracts-v2.trx. An intermediate selection exposed
one existing CLR test expecting the pinned early success; its continued-periodic
workload now verifies pending termination until policy removal and worker release.
The original five unordered fixture identities and comments are unchanged. Full
default Debug and Release each discover 1348 cases: 1334 passed / zero failed /
14 unchanged skips (unordered-termination-full-debug.trx and
unordered-termination-full-release.trx, Windows/net10.0). All 759 original fixture
identities and skipped identities match the preceding native submission checkpoint
in both configurations. The two affected upstream source files retain all 31
original comments; all 105 verified source/test entries also have zero missing.
Evidence: unordered-termination-comment-audit.json. See common-porting.md for
the current matrix and historical checkpoints.

This is one lifecycle correction within the unfinished backend review. The subsequent
bulk review removes invokeAll/invokeAny (common-native-bulk-composition.md);
the subsequent unordered scheduling migration removes concrete Java scheduler/JDK
result wrappers (common-native-unordered-scheduling-migration.md). The subsequent
fixture migration retires plain Future/Promise (common-native-future-retirement.md).
The subsequent native queue review removes public mutators and uses owned token
withdrawal with PendingTaskCount diagnostics; see common-unordered-native-queue.md.
The subsequent native configuration review retires inherited settings and statistics;
see common-unordered-native-configuration.md. Concrete immediate stop is implemented
in common-unordered-cooperative-stop.md; shared/group immediate API and private queue costs remain open.
Search of all pinned modules finds bulk invocation only in forwarding/guard
implementations, common/transport blocking-guard tests and benchmark stubs; no
production operation requires a custom bulk-result API. Useful composition now
uses native SubmitAsync plus BCL Task composition and owned cancellation.
The subsequent graceful-shutdown review implements quietPeriod/timeout admission
from the documented EventExecutorGroup contract, then applies existing drain
policies; see common-unordered-graceful-shutdown.md. Queue insertion is now refused
at admission closure, including before Termination completes. The subsequent queue
review removes that public mutation surface altogether. The configuration follow-up
replaces inherited settings with immutable constructors and owned cancellation.
All original Java comments stay
beside the mapped implementation or its provenance, including the upstream TODO.

The subsequent CLR worker replacement failure boundary is implemented in
[common-unordered-worker-failure.md](common-unordered-worker-failure.md).
