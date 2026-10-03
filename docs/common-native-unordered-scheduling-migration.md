# Native unordered scheduling and raw queue work

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.
Scope: common and tests; other modules are read only.

## Evidence and design

UnorderedThreadPoolEventExecutor.java extends JDK ScheduledThreadPoolExecutor.
Its decorator maintains an outer PromiseTask separately from the JDK future.
Callable results are recovered through future.get (issue 11072), while Runnable
failures can become successful outer results and periodic failure/cancellation
can leave that outer promise pending. NonNotifyRunnable avoids recursive Promise
listener decoration in raw execute (issue 6507). These wrapper accidents are not
native result contracts. The five original unordered fixtures require finite
listener dispatch, repeated callbacks, typed success, exact failure and worker
recognition; all retain their names, workloads and original comments.

Actual consumers require cancellation of connect/acquisition/idle/write deadlines
and repeating traffic/utilization monitoring. Pinned IdleStateHandler,
WriteTimeoutHandler, FixedChannelPool, AbstractNioChannel and TrafficCounter are
read-only evidence, not ported modules. Native executor scheduling preserves queue
invocation and cancelable reservations for those consumers. Task.Delay/Task.Run
alone do not replace that execution contract. An all-module pinned search finds
getQueue only in the original unordered empty-queue test, and no removeOnCancel
or purge consumer. Those inherited JDK cancellation-maintenance APIs are removed:
native token cancellation already removes pending work, including decorated work.
The subsequent native queue review removes mutable handles in favor of
PendingTaskCount and owned token cancellation; see common-unordered-native-queue.md.
The subsequent inherited configuration review replaces those APIs with immutable
constructor settings and native diagnostics; see common-unordered-native-configuration.md.

All unordered schedule callers now use ScheduleAsync/ScheduleAtFixedRateAsync/
ScheduleWithFixedDelayAsync with caller-owned tokens when cancellation is needed.
The four concrete Java overloads, inner Backend/ScheduledFutureTask decorators,
IScheduledTask result interface and CLR JdkFutureTask adapter are removed.
NativeScheduledWork owns one TCS result and the invocation claim. The pool adapter
carries queue ownership/deadline/sequence metadata and implements IScheduledWork,
not IFuture. Task identity is separate from queue membership. The pool owns IDs;
metadata cannot change its sequence arbitrarily after admission. Existing native
one-shot claim/cooperative cancellation, fixed-rate/fixed-delay, context and async
unwrapping policies remain, rather than wrapping another private Java future.

Raw execute now enqueues a private RawWork without any asynchronous result owner.
It claims and releases its callback once. Clear/immediate shutdown discard its
callback; invocation failure is logged and the worker remains available. A caller
that needs an observed result uses SubmitAsync/ScheduleAsync. NonNotifyRunnable
is unnecessary because raw execution creates no Promise decoration. The original
finite-listener test still inspects the empty queue 10000 times.
The unused top-level NonNotifyRunnable.cs duplicate is also removed; a complete
source/test search finds no consumer. Its nested Java helper comments are retained
in the pinned archive below.

Throwing factory admission rolls back the affected reservation before reporting
failure; it cannot silently run after a later successful submission. Null factory
results remain accepted pending work. With immutable constructor configuration,
a stateful factory can recover on later admission while the pool is open; owned
cancellation or immediate shutdown can settle workerless work after closure.
Native schedule rejection faults even when the raw rejection
handler deliberately discards work. Queue insertion rejects reservations owned by
another pool: their clock, shutdown and cancellation removal refer to their owner.
Manual remove/reinsert uses the actual queue handle, never the result Task.

Cancellation before invocation prevents execution and releases queue membership.
After a one-shot claim, owner cancellation does not overwrite a normal result and
does not inject Thread.Interrupt; the two old JdkFutureTask running-cancellation
probes now use native schedules, retaining the 256-iteration no-interrupt workload.
Immediate pool shutdown's worker interruption is a separate existing backend
policy, exercised independently. Shutdown removes queued work and settles native
cancellation; returned IRunnable handles carry membership, not pending results.
Continued periodic/delayed shutdown policy tests retain their barriers and timing.

## Explicit CLR probe changes

The following Porting-only probes intentionally change source quirks to native
contracts. They are not original Java fixture renames. Other migrated probes keep
their scenarios while using native Task observations and queue metadata.

| Previous probe | Native counterpart |
| --- | --- |
| ShutdownNowReturnsQueuedFuturesAndInterruptsRunningWork | ShutdownNowReturnsQueueWorkAndCancelsItsNativeResult |
| CallableResultsAndFailuresSurviveDecorationForClrValueTypes | NativeResultsAndFailuresPreserveClrValueTypes |
| RunnableDecorationRetainsThePinnedBackendFailureSemantics | NativeScheduledActionPublishesItsActualFailure |
| PeriodicBackendFailureStopsRepetitionWithoutCompletingOuterPromise | NativePeriodicFailureStopsRepetitionAndFaultsTheReservation |
| CancelledOneShotRetainsQueueEntryUntilItsDeadlineOrQueueRemoval | NativeOneShotCancellationRemovesItsQueueEntryImmediately |
| CustomRejectionHandlerReceivesScheduledDecoration | DiscardingRawRejectionHandlerCannotHideNativeScheduleRejection |
| ADequeuedPeriodicTaskCanCancelItsBackendWhileTheOuterPromiseStaysPending | RemovedPeriodicReservationCancelsWhenShutdownPolicyPreventsReentry |
| RemoveOnCancelAffectsRawBackendButNotDecoratedNettyFuture | RawQueueRemovalAndNativeCancellationNeedNoFutureDecorationPolicy |
| NativeQueueCanRemoveAndReinsertScheduledFutureBeforeItExecutes | NativeQueueCanRemoveAndReinsertMembershipWithoutUsingTaskIdentity |
| RunningBulkFutureCanCancelWithoutInterruptingItsCallable | ClaimedNativeScheduleKeepsNormalSuccessAfterOwnerCancellation |
| RunningBulkCancellationDoesNotLeakAnInterruptIntoNextWork | NativeScheduleCancellationDoesNotInjectAnInterruptIntoNextWork |
| FactoryFailureReleasesReservationAndRetainsAcceptedQueueWork | NativeScheduleFactoryFailureFaultsAndRollsBackAdmission |

The ScheduledFuture result type is replaced by standard Task observation plus
owned CancellationToken. Completion listeners and progress use the separate native
executor policies already implemented. Removed source comments are preserved
below from pinned Git objects, not transplanted as documentation for retained APIs.
Subsequent wrapper cleanup removes the unused PromiseTask and Callable glue.
Subsequent native fixture migration removes the plain Future/Promise hierarchy;
see common-native-future-retirement.md. The subsequent queue review removes public
mutators and unused metadata forwarding; see common-unordered-native-queue.md.
The subsequent configuration review retires inherited settings. Immediate shutdown
interruption, private queue costs and all pending
source reviews still prevent common completion.
The subsequent graceful-shutdown review implements quiet/timeout admission;
see common-unordered-graceful-shutdown.md.

## Verification

The initial affected Debug selection passes 199 cases with zero failures/skips
(native-unordered-schedule-contracts-debug.trx). The final selection adds the three
queue ownership cases and passes all 202, zero failures/skips
(native-unordered-schedule-final-contracts-debug.trx).

Full default Debug and Release each discover 1357 cases on Windows/net10.0:
1343 passed / zero failed / 14 unchanged skips. Evidence:
native-unordered-schedule-full-debug.trx and native-unordered-schedule-full-release.trx.
All 759 non-Porting fixture identities and all skipped identities match the
preceding worker-identity checkpoint in both configurations. Across the whole suite,
the only changes are the 12 explicit CLR probe mappings above and three new ownership
cases (native-unordered-schedule-identity-comparison.json). No source exclusion/new
skip was introduced; existing warnings are not claimed resolved.

All 104 verified source/test comment entries have zero missing. ScheduledFuture
is now a CLR replacement, explaining the change from 105 verified entries;
no portable fixture was dropped. All 21 unordered source, two ScheduledFuture and
eight original test comments remain, with no missing implementation paths
(native-unordered-schedule-comment-audit.json). Module completion remains unproven.

The final reference sweep subsequently removed the unused top-level helper
duplicate. Follow-up default Debug/Release solution builds both succeed with zero
errors and 855 existing warnings (native-unordered-schedule-final-build-debug.log
and native-unordered-schedule-final-build-release.log). The full test matrix above
precedes that unreachable-type deletion; reachable execution/test code is unchanged.

## Original pinned comment provenance

All comments are copied verbatim in source order. The archive documents removed
JDK result/wrapper behavior; retained source comments remain beside mapped code.

### UnorderedThreadPoolEventExecutor.java

Upstream: common/src/main/java/io/netty/util/concurrent/UnorderedThreadPoolEventExecutor.java

```java
/*
 * Copyright 2016 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */
```

```java
/**
 * {@link EventExecutor} implementation which makes no guarantees about the ordering of task execution that
 * are submitted because there may be multiple threads executing these tasks.
 * This implementation is most useful for protocols that do not need strict ordering.
 * <p>
 * <strong>Because it provides no ordering, care should be taken when using it!</strong>
 *
 * @deprecated The behavior of this event executor deviates from the typical Netty execution model
 * and can cause subtle issues as a result.
 * Applications that wish to process messages with greater parallelism, should instead do explicit
 * off-loading to their own thread-pools.
 */
```

```java
/**
     * Calls {@link UnorderedThreadPoolEventExecutor#UnorderedThreadPoolEventExecutor(int, ThreadFactory)}
     * using {@link DefaultThreadFactory}.
     */
```

```java
/**
     * See {@link ScheduledThreadPoolExecutor#ScheduledThreadPoolExecutor(int, ThreadFactory)}
     */
```

```java
/**
     * Calls {@link UnorderedThreadPoolEventExecutor#UnorderedThreadPoolEventExecutor(int,
     * ThreadFactory, java.util.concurrent.RejectedExecutionHandler)} using {@link DefaultThreadFactory}.
     */
```

```java
/**
     * See {@link ScheduledThreadPoolExecutor#ScheduledThreadPoolExecutor(int, ThreadFactory, RejectedExecutionHandler)}
     */
```

```java
// TODO: At the moment this just calls shutdown but we may be able to do something more smart here which
```

```java
//       respects the quietPeriod and timeout.
```

```java
// If this RunnableScheduledFutureTask wraps a RunnableScheduledFuture that wraps a Callable we need
```

```java
// to ensure that we return the correct result by calling future.get().
```

```java
//
```

```java
// See https://github.com/netty/netty/issues/11072
```

```java
// unwrap exception.
```

```java
// Its a periodic task so we need to ignore the return value
```

```java
// This is a special wrapper which we will be used in execute(...) to wrap the submitted Runnable. This is needed as
```

```java
// ScheduledThreadPoolExecutor.execute(...) will delegate to submit(...) which will then use decorateTask(...).
```

```java
// The problem with this is that decorateTask(...) needs to ensure we only do our own decoration if we not call
```

```java
// from execute(...) as otherwise we may end up creating an endless loop because DefaultPromise will call
```

```java
// EventExecutor.execute(...) when notify the listeners of the promise.
```

```java
//
```

```java
// See https://github.com/netty/netty/issues/6507
```


### ScheduledFuture.java

Upstream: common/src/main/java/io/netty/util/concurrent/ScheduledFuture.java

```java
/*
 * Copyright 2013 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */
```

```java
/**
 * The result of a scheduled asynchronous operation.
 */
```

The subsequent CLR worker replacement failure boundary is implemented in
[common-unordered-worker-failure.md](common-unordered-worker-failure.md).
