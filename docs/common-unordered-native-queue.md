# Native unordered queue ownership and diagnostics

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.
Scope: common and tests; other modules are read-only evidence.

## Required behavior and public decision

The pinned unordered executor inherits JDK ScheduledThreadPoolExecutor's mutable
queue. An all-module pinned Java search finds getQueue only in
UnorderedThreadPoolEventExecutorTest.java line 69, checking that the queue remains
empty 10000 times after completion notifications. It finds no unordered queue
remove, clear, dequeue, drain or reinsertion consumer. Constructor factory/rejection
arguments and executor work/termination contracts are separate required behavior.
The inherited settings were a separate unfinished review at this checkpoint;
common-unordered-native-configuration.md records their subsequent retirement.
Their absence from actual Netty consumers does not replace dedicated workers with Task.Run.

The native API exposes PendingTaskCount, a gate-protected momentary count of queued
invocation reservations, including future deadlines and excluding active work.
The original 10000-check scenario now reads that property. It does not provide a
capacity bound, an admission guarantee, an operation count or a consistency promise
across several separate reads. No snapshot of runnable handles is exposed.
getQueue, public remove(IRunnable), the CLR QueueView implementation and its
IQueue dependency are removed rather than made internal or wrapped in a second
mutable API. Raw execution, native Task submission/scheduling and owned cancellation
remain the supported admission/cancellation operations. The original comments are
preserved in their mapped implementation and pinned scheduling provenance.

The private BCL PriorityQueue and its owner gate remain: immutable admission IDs
break equal-deadline ties, signed-distance comparison handles nanoTime wrapping,
and membership changes are atomic with worker/start reservation and shutdown state.
The private unordered scheduling adapter no longer implements IScheduledWork or
exports unused metadata-forwarding members after removal of the queue surface.
NativeScheduledWork still owns its single TCS result, invocation claim and deadline.

## Cancellation membership defect and repair

Porting the former queue-clear/remove probes to owned tokens reproduces two failures
(unordered-native-queue-before.trx): a queued SubmitAsync result canceled successfully
but its pool queue entry remained. With no worker, the canceled entry could keep
Termination pending forever. Native scheduled cancellation already removed its
membership; direct submission needed the same ownership boundary.

An internal ICancelableNativeSubmission hook binds a caller-cancelable submission
to the one backend reservation admitted by its pool. SubmittedTask's existing TCS
still supplies the only result. Cancellation wins the existing invocation claim,
releases the delegate/context and publishes cancellation before consuming its
membership hook. The binder checks already-published cancellation, so cancellation
before/during binding also removes membership. Invocation/rejection clear the hook;
claimed work remains cooperative and cannot have a normal result overwritten by
owner cancellation. CancellationToken callbacks and the binder consume one hook
with Interlocked; no second promise, extra completion Task or public handle is added.

Removal and pool-state publication use the pool gate. Immediate shutdown iterates
an owned snapshot, because cancellation removal can reenter and mutate the BCL queue.
The first post-repair selection independently exposed live-enumerator invalidation
(unordered-native-queue-contracts-debug.trx); snapshot iteration fixes it without
suppressing failure. Cancellation preserves the original token and clears registration
and callback references. Two added cases exercise 256 admission/cancellation/claim
races and collection of a workerless pool while its canceled Task and token stay alive.

## Explicit CLR probe mappings

Original Java fixture names/workloads/comments are retained. The following nine
Porting-only probes are adapted because the inherited mutable API is removed.

| Previous probe | Native counterpart |
| --- | --- |
| RemovedPeriodicReservationCancelsWhenShutdownPolicyPreventsReentry | OwnerCancellationPreventsPeriodicReentryAfterShutdown |
| RawQueueRemovalAndNativeCancellationNeedNoFutureDecorationPolicy | NativeOwnerCancellationWithdrawsQueuedWorkWithoutInterruptingItsWorker |
| NativeQueueCanRemoveAndReinsertMembershipWithoutUsingTaskIdentity | PendingCountTracksCancellationAndAReplacementAdmission |
| AnotherPoolCannotAdoptAReservationWithDifferentCancellationOwnership | NativeCancellationWithdrawsOnlyItsOwnersReservation |
| ClearedRawHandleCannotExecuteAndAClaimedHandleCannotExecuteTwice | ShutdownNowRawHandlesCannotExecuteRemovedCallbacks |
| ClearingTheQueueSettlesNativeWorkBeforeCompletingShutdown | OwnerCancellationSettlesWorkerlessWorkBeforeCompletingShutdown |
| ACompletedLifecycleCannotBeReopenedThroughItsQueueView | ACompletedLifecycleRejectsNewWorkAndRemovedHandleInvocation |
| ClosedAdmissionCannotBeReopenedThroughAQueueHandleBeforeTermination | ClosedAdmissionRejectsReplacementWorkWhileOwnedCancellationDrains |
| DetachedPeriodicReservationCannotRestartATerminatedPool | ShutdownNowPeriodicHandlesCannotRestartATerminatedPool |

Only shutdownNow's already-canceled IRunnable list remains on the legacy executor
service interface; probes use those returned handles to ensure removed callbacks
cannot run or revive termination. Native callers cancel their owned tokens. No
queue handle can be transferred between live pools or reinserted after shutdown.
Large-duration probes use public noncompletion/pending-count observations; exact
saturated nanosecond math remains covered by SchedulingContractTest and
NativeSchedulingContractTest's controlled ordered clock. Unordered empty/claim/
owner-cancellation outcomes remain independently exercised on real dedicated workers.

## Verification and next step

The initial affected Debug selection fails two of 175 cases before submission
membership repair. After snapshot repair 175 pass; the expanded cancellation,
notification/progress, scheduling and NonSticky selection passes 250 cases with
zero failures/skips (unordered-native-queue-final-contracts-v2-debug.trx).
Full Debug discovers 1380 cases: 1366 passed, zero failed, 14 unchanged skips
(unordered-native-queue-full-debug.trx). The initial full Release has one failure
in the existing real-time AutoScalingEventExecutorChooserFactoryTest.testScaleUpDoesNotExceedMaxThreads
at its high-load scale-down assertion (unordered-native-queue-full-release.trx).
Pinned Java has the same real-time spin/sleep scenario; no chooser/test source is
modified in this unit. The focused Release run of all seven original auto-scaling
fixtures passes (unordered-native-queue-autoscaling-release.trx). Keep the failed
run as evidence; successful reruns alone do not establish this scenario's timing
stability. The unchanged full Release rerun passes 1366 cases with zero failures
and the same 14 skips (unordered-native-queue-final-full-release.trx).
All 759 non-Porting identities and skip identities match the graceful checkpoint;
differences are exactly nine documented CLR-only mappings plus the two new
race/lifetime cases. Debug/Release outcomes all match
(unordered-native-queue-identity-comparison.json). All 98 verified source/test
comment entries, 21 unordered source, ten group-contract and eight original
unordered test comments have zero missing (unordered-native-queue-comment-audit.json).
All implementation paths exist and the pinned source/test inventory matches all
271 manifest entries. git diff --check passes; compiler/analyzer warnings remain.
No whole-common or final unordered backend completion is claimed.

The subsequent pool-configuration review uses that pinned all-module search to
retire inherited settings and statistics in favor of immutable constructor settings,
owned cancellation and native diagnostics; see common-unordered-native-configuration.md.
Its probe mapping also records further changes to the historical table above.
Concrete immediate stop is subsequently implemented in common-unordered-cooperative-stop.md.
Shared/group immediate API and private queue costs remain open. The remaining pending upstream source
reviews and the remaining common design work stay within the full goal. The
observed real-time auto-scaling stability issue prompted the subsequent
common-autoscaling-monitor-windows.md review. Controlled cases prove repeated
catch-up sampling can fabricate idle windows; phase-preserving coalescing repairs
that defect. Its causal relation to the retained real-time failure remains unproven.
The original high-load/max-count expectation, waits and thresholds remain unchanged.

The subsequent CLR worker replacement failure boundary is implemented in
[common-unordered-worker-failure.md](common-unordered-worker-failure.md).
