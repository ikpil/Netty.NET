# Native ordered scheduling caller migration

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.
Scope: common and tests; timeout/acquisition consumers in other modules are read only.

## Evidence and native design

AbstractScheduledEventExecutor.java and ScheduledFutureTask.java own the deadline
heap, sequence ordering, consumed-work cancellation, stable repeat IDs, fixed-rate
versus fixed-delay advancement and loop-confined queue changes. IdleStateHandler
re-arms/cancels reader/writer/all-idle reservations on the channel loop;
FixedChannelPool assigns cancelable acquisition timeouts. These require native
executor scheduling, not merely Task.Delay or a Java result wrapper.

Native ScheduleAsync and periodic APIs now serve every ordered/global/single-thread
caller and fixture. The shared group interface no longer inherits the JDK scheduled
service; group forwarding/default unsupported Java schedule overloads are removed.
NonSticky native scheduling still selects its delegated group; ordered wrappers
still report unsupported scheduling through a faulted Task. The CLR JDK service
facade and the ordered ScheduledTask/Callable/Runnable result adapters are removed.
NativeScheduledWork owns its private TCS and indexed deadline membership directly.
Task identity is not used as heap identity; tests inspect the native queue work
separately when verifying deadlines, stable IDs, cancellation and bounded transfer.

Original scheduling fixture names in AbstractScheduled, Global and SingleThread
remain, with their original comments preserved. The four inherited bulk-guard
cases were separately replaced at the preceding bulk checkpoint, as documented
in common-native-bulk-composition.md. Zero/negative delay checks now verify the actual native queue head,
consume/run its reservation and observe the Task. Periodic argument errors use
CLR ArgumentOutOfRangeException; the original fixed-delay-zero test's negative
operand remains intact. Global busy-queue deadlines retain the 1500ms delay and
repeated enqueue workload. SingleThread cancellation/suspension retains its full
2000-iteration race, thread-start barriers, physical joins and original comments;
the caller owns a token instead of canceling a Future. Native cancellation can
settle the Task before queued removal, so suspend polling and joins still prove
queue/worker cleanup independently.

The CLR scheduling regressions retain queue capacity, reinsertion IDs, ordering,
large integer deadlines and subclass validation/hooks. A test-only manual owner
can defer native canceled-work removal to exercise transfer filtering; this does
not add a public cancel-without-removal API. Native argument validation still
runs before admission, and notification/await does not grant loop affinity.
After successful invocation, canceling the owner's token cannot rewrite success.

This removes the ordered/shared Java facade, not the entire scheduling migration.
At this checkpoint unordered's concrete legacy overloads and result backend were
unfinished. The subsequent unordered migration removes those overloads,
JdkFutureTask/ScheduledFutureTask decorators and the remaining scheduled-result
interface; see common-native-unordered-scheduling-migration.md. Final pool
configuration/queue/shutdown review remains open. Plain Future fixtures now use
native APIs and their old hierarchy is removed; see common-native-future-retirement.md.

## Verification

Affected Debug selection: 179 passed / zero failed / zero skips
(native-ordered-schedule-final-contracts-debug.trx). An initial runnable selection
had 112 passed / one failure: the test-only deferred-removal owner stayed marked
as an external thread while its transfer helper required owner affinity. Restoring
owner state before transfer fixes that fixture; native scheduling behavior was
unchanged. native-ordered-schedule-contracts-debug-v3.trx preserves the failure.
Earlier compile diagnostics exposed removed-helper and IRunnable-versus-metadata
fixture references, repaired before runtime verification.

Default whole-suite Debug and Release each discover 1350 cases on Windows/net10.0:
1336 passed / zero failed / 14 unchanged skips. Evidence:
native-ordered-schedule-full-debug.trx and native-ordered-schedule-full-release.trx.
All 759 non-Porting fixture identities and all skipped identities match the
preceding native-bulk checkpoint in both configurations. No source exclusion or
new skip was introduced; existing warnings are not claimed resolved.

The comment audit records zero missing in all 105 verified source/test entries,
all 28 AbstractScheduledEventExecutor comments and all eight ScheduledFutureTask
comments (native-ordered-schedule-comment-audit.json). The manifest has no missing
implementation paths. Comment preservation is separate from contract verification.
Common remains incomplete. The subsequent worker-identity correction fixes native
callback affinity after thread-factory replacement; see
common-unordered-worker-identity.md. Unordered concrete scheduler and failure/queue
probes have since migrated to native results; see
common-native-unordered-scheduling-migration.md. Next, remove remaining plain
Future fixtures/facades and review final pool configuration/queue/shutdown policy.

## Original comment provenance

The ScheduledFutureTask comments below are copied verbatim, in source order,
from the pinned Git object. Native scheduling keeps its mapped execution/queue
comments and license in NativeScheduledWork; the archive retains all removed
Java result-facade documentation. The CLR JDK scheduled-service comments are
copied from the pre-removal mapped worktree, preserving prior CLR name adaptations.

### ScheduledFutureTask.java pinned comments

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
// set once when added to priority queue
```

```java
/* 0 - no repeat, >0 - repeat at fixed rate, <0 - repeat with fixed delay */
```

```java
// Optimization to avoid checking system clock again
```

```java
// after deadline has passed and task has been dequeued
```

```java
// Not yet expired, need to add or remove from queue
```

```java
// check if is done as it may was cancelled
```

```java
/**
     * {@inheritDoc}
     *
     * @param mayInterruptIfRunning this value has no effect in this implementation.
     */
```

### IScheduledExecutorService.cs mapped JDK documentation

```cs
/**
     * Submits a one-shot task that becomes enabled after the given delay.
     *
     * @param command the task to execute
     * @param delay the time from now to delay execution
     * @param unit the time unit of the delay parameter
     * @return a IScheduledTask representing pending completion of
     *         the task and whose {@code get()} method will return
     *         {@code null} upon completion
     * @throws RejectedExecutionException if the task cannot be
     *         scheduled for execution
     * @throws NullReferenceException if command or unit is null
     */
```

```cs
/**
     * Submits a value-returning one-shot task that becomes enabled
     * after the given delay.
     *
     * @param callable the function to execute
     * @param delay the time from now to delay execution
     * @param unit the time unit of the delay parameter
     * @param <V> the type of the callable's result
     * @return a IScheduledTask that can be used to extract result or cancel
     * @throws RejectedExecutionException if the task cannot be
     *         scheduled for execution
     * @throws NullReferenceException if callable or unit is null
     */
```

```cs
/**
     * Submits a periodic action that becomes enabled first after the
     * given initial delay, and subsequently with the given period;
     * that is, executions will commence after
     * {@code initialDelay}, then {@code initialDelay + period}, then
     * {@code initialDelay + 2 * period}, and so on.
     *
     * <p>The sequence of task executions continues indefinitely until
     * one of the following exceptional completions occur:
     * <ul>
     * <li>The task is {@linkplain Future#cancel explicitly cancelled}
     * via the returned future.
     * <li>The executor terminates, also resulting in task cancellation.
     * <li>An execution of the task throws an exception.  In this case
     * calling {@link Future#get() get} on the returned future will throw
     * {@link AggregateException}, holding the exception as its cause.
     * </ul>
     * Subsequent executions are suppressed.  Subsequent calls to
     * {@link Future#isDone isDone()} on the returned future will
     * return {@code true}.
     *
     * <p>If any execution of this task takes longer than its period, then
     * subsequent executions may start late, but will not concurrently
     * execute.
     *
     * @param command the task to execute
     * @param initialDelay the time to delay first execution
     * @param period the period between successive executions
     * @param unit the time unit of the initialDelay and period parameters
     * @return a IScheduledTask representing pending completion of
     *         the series of repeated tasks.  The future's {@link
     *         Future#get() get()} method will never return normally,
     *         and will throw an exception upon task cancellation or
     *         abnormal termination of a task execution.
     * @throws RejectedExecutionException if the task cannot be
     *         scheduled for execution
     * @throws NullReferenceException if command or unit is null
     * @throws ArgumentException if period less than or equal to zero
     */
```

```cs
/**
     * Submits a periodic action that becomes enabled first after the
     * given initial delay, and subsequently with the given delay
     * between the termination of one execution and the commencement of
     * the next.
     *
     * <p>The sequence of task executions continues indefinitely until
     * one of the following exceptional completions occur:
     * <ul>
     * <li>The task is {@linkplain Future#cancel explicitly cancelled}
     * via the returned future.
     * <li>The executor terminates, also resulting in task cancellation.
     * <li>An execution of the task throws an exception.  In this case
     * calling {@link Future#get() get} on the returned future will throw
     * {@link AggregateException}, holding the exception as its cause.
     * </ul>
     * Subsequent executions are suppressed.  Subsequent calls to
     * {@link Future#isDone isDone()} on the returned future will
     * return {@code true}.
     *
     * @param command the task to execute
     * @param initialDelay the time to delay first execution
     * @param delay the delay between the termination of one
     * execution and the commencement of the next
     * @param unit the time unit of the initialDelay and delay parameters
     * @return a IScheduledTask representing pending completion of
     *         the series of repeated tasks.  The future's {@link
     *         Future#get() get()} method will never return normally,
     *         and will throw an exception upon task cancellation or
     *         abnormal termination of a task execution.
     * @throws RejectedExecutionException if the task cannot be
     *         scheduled for execution
     * @throws NullReferenceException if command or unit is null
     * @throws ArgumentException if delay less than or equal to zero
     */
```

### Removed mapped ordered schedule implementation notes

```cs
//ObjectUtil.checkNotNull(unit, "unit");
```

```cs
//ObjectUtil.checkNotNull(unit, "unit");
```

```cs
//ObjectUtil.checkNotNull(unit, "unit");
```

```cs
//ObjectUtil.checkNotNull(unit, "unit");
```
