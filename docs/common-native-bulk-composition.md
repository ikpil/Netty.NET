# Native Task composition instead of JDK bulk invocation

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.
Scope: common and tests; other modules are read only.

## Source evidence and decision

The pinned AbstractEventExecutorGroup and NonStickyEventExecutorGroup forward
four JDK bulk overloads. SingleThreadEventExecutor rejects them on its worker to
prevent a synchronous wait for queued work from blocking that same worker.
SingleThreadEventExecutorTest exercises that rejection in four scenarios.
Transport ManualIoEventLoop and its test have the same inherited guard.
BurstCostExecutorsBenchmark supplies unsupported interface stubs. A search across
all pinned Java modules finds no production operation calling invokeAll/invokeAny.
These are inherited JDK API requirements, not a reason to implement another CLR
result/waiting/cancellation hierarchy.

The four bulk overloads, newTaskFor hooks and the CLR AbstractExecutorService
facade are removed. Native SubmitAsync owns each result and invocation claim.
Consumers compose Tasks with BCL APIs, select one group child explicitly when the
whole batch must share its queue, and own CancellationTokenSource when they must
cancel queued or cooperative running work. There is no replacement public bulk
wrapper or private Java Future used by a native batch.

Task.WhenAll preserves input order for successful results, waits for every Task,
and faults if any fails; individual Tasks retain their exact outcomes. This
replaces invokeAll's successful return of a list containing failed Futures.
Task.WhenAny returns the first completion, including a fault/cancellation, rather
than invokeAny's first success. A caller that needs first success explicitly
observes failed candidates and cancels remaining work using its owned token.
All-failure reporting and validation of later candidates are caller policy; the
JDK rule selecting only the last failure and selectively validating unused nulls
is not copied. Task.Exception retains all failures; await unwraps one failure.

WaitAsync timeout/cancellation belongs to a wait. It does not cancel the source
Tasks. An owner cancels its token in finally if timeout must discard unstarted
work. After invocation starts, native cancellation remains cooperative; it does
not inject Thread.Interrupt into a shared worker or manufacture cancellation when
user code returns successfully. WaitAsync uses CLR millisecond timeout conversion:
-2ms is invalid, -1ms is the infinite sentinel, and negative submillisecond ticks
truncate to zero. These differ from the JDK bulk nonpositive-timeout policy.
An already canceled submission token
prevents admission. Task.WhenAll accepts an empty batch; Task.WhenAny requires at
least one candidate. Null arguments follow the native delegate/BCL boundaries.

Async consumers yield rather than synchronously wait on executor-owned queues.
The four Java blocking-guard scenarios therefore become four native queued async
composition cases (all/any, with/without observer timeout). Their names explicitly
identify the native counterpart. They verify the owner invokes work on its loop,
yielding permits the queued child to execute, and subsequent owned access is
explicitly dispatched. The removed Java rejection is recorded as an inherited
blocking-API expectation with no retained CLR method; no claim is made that native
await rejects or that the removed callable must never run. Original comments in
the surrounding source/test stay mapped. Test identities and skip changes are
reported with full verification evidence.

| Removed inherited guard test | Native consumer counterpart |
| --- | --- |
| testInvokeAnyInEventLoop | NativeWhenAnyYieldsInEventLoop |
| testInvokeAnyInEventLoopWithTimeout | NativeWhenAnyWithObserverTimeoutYieldsInEventLoop |
| testInvokeAllInEventLoop | NativeWhenAllYieldsInEventLoop |
| testInvokeAllInEventLoopWithTimeout | NativeWhenAllWithObserverTimeoutYieldsInEventLoop |

The existing TaskWhenAllPortTest already verifies all-failure aggregation, including
both original exception objects, and null/empty/completed inputs. New batch
consumers exercise native executor admission and failure rather than introduce
another composition implementation. FirstSuccessConsumerObservesFailuresAndCancelsQueuedLosers
contains a caller-owned example loop using BCL Tasks; it is not a production API.
The unordered running-cancellation counterparts explicitly use the owned token,
wait for user code to acknowledge it, and do not assert injected thread interruption.
The one-worker nested batch now succeeds after an async yield instead of relying
on a timed synchronous wait to cancel its queued child.

At this checkpoint JdkFutureTask remained inside the unordered legacy scheduler.
The subsequent native unordered migration removes it and all concrete Java
scheduling/result decorators; see common-native-unordered-scheduling-migration.md.
The subsequent wrapper cleanup removes the unused PromiseTask and Callable glue.
Native fixture migration also removes the plain Future/Promise hierarchy; see
common-native-future-retirement.md. Final pool configuration/queue design
and graceful-shutdown parameter policy remain open. Original JDK facade comments are preserved below as mapped-source
provenance; they describe the removed API, not retained CLR contracts.

## Verification

Affected Debug selection: 186 passed / zero failed / zero skips
(native-bulk-final-contracts-debug.trx). The first 97-case selection had one
fixture failure: the assertion helper rejected a returned async Task after a
negative submillisecond timeout was converted to zero. The fixture now checks an
invalid -2ms timeout synchronously, and two rows separately verify -1 tick/zero
immediate observer timeout without canceling accepted work. No executor behavior
was changed for that fixture repair; native-bulk-contracts-debug.trx retains it.
Full default Debug and Release each discover 1350 cases on Windows/net10.0:
1336 passed / zero failed / 14 unchanged skips. Evidence: native-bulk-full-debug.trx
and native-bulk-full-release.trx. The 759 non-Porting fixture identities differ
only by the four removed guard names and their four mapped native counterparts
above. All other identities and all skipped identities match the preceding
unordered termination checkpoint in both configurations. No source exclusion or
new skipped case was introduced. Existing warnings are not claimed resolved.
Full matrix and historical checkpoints are also recorded in common-porting.md.
All 105 verified source/test comment entries have zero missing; the removed JDK
facade documentation below is archival provenance rather than a retained API.
The five changed Netty sources plus SingleThreadEventExecutorTest retain all 247
original Java comments (native-bulk-comment-audit.json). Common and the remaining
native scheduler/backend/source review are still incomplete. The subsequent ordered
scheduling migration removes the shared scheduling interface and ordered adapters;
see common-native-ordered-scheduling-migration.md. The unordered overloads,
result adapters and raw JDK wrapper have also been removed; see
common-native-unordered-scheduling-migration.md. Plain Future fixtures also use
native APIs now; final pool configuration/queue/shutdown policy remains open.

## Original mapped JDK comment provenance

The following comments are copied verbatim from the worktree before removal.
The facade was a CLR translation of inherited JDK code, not a Netty Java source
file. Existing CLR adaptations of names/exception types are preserved as-is.

### IExecutorService.cs removed bulk documentation

```cs
/**
     * Executes the given tasks, returning a list of Futures holding
     * their status and results when all complete.
     * {@link Future#isDone} is {@code true} for each
     * element of the returned list.
     * Note that a <em>completed</em> task could have
     * terminated either normally or by throwing an exception.
     * The results of this method are undefined if the given
     * collection is modified while this operation is in progress.
     *
     * @param tasks the collection of tasks
     * @param <T> the type of the values returned from the tasks
     * @return a list of Futures representing the tasks, in the same
     *         sequential order as produced by the iterator for the
     *         given task list, each of which has completed
     * @throws ThreadInterruptedException if interrupted while waiting, in
     *         which case unfinished tasks are cancelled
     * @throws NullReferenceException if tasks or any of its elements are {@code null}
     * @throws RejectedExecutionException if any task cannot be
     *         scheduled for execution
     */
```

```cs
/**
     * Executes the given tasks, returning a list of Futures holding
     * their status and results
     * when all complete or the timeout expires, whichever happens first.
     * {@link Future#isDone} is {@code true} for each
     * element of the returned list.
     * Upon return, tasks that have not completed are cancelled.
     * Note that a <em>completed</em> task could have
     * terminated either normally or by throwing an exception.
     * The results of this method are undefined if the given
     * collection is modified while this operation is in progress.
     *
     * @param tasks the collection of tasks
     * @param timeout the maximum time to wait
     * @param unit the time unit of the timeout argument
     * @param <T> the type of the values returned from the tasks
     * @return a list of Futures representing the tasks, in the same
     *         sequential order as produced by the iterator for the
     *         given task list. If the operation did not time out,
     *         each task will have completed. If it did time out, some
     *         of these tasks will not have completed.
     * @throws ThreadInterruptedException if interrupted while waiting, in
     *         which case unfinished tasks are cancelled
     * @throws NullReferenceException if tasks, any of its elements, or
     *         unit are {@code null}
     * @throws RejectedExecutionException if any task cannot be scheduled
     *         for execution
     */
```

```cs
/**
     * Executes the given tasks, returning the result
     * of one that has completed successfully (i.e., without throwing
     * an exception), if any do. Upon normal or exceptional return,
     * tasks that have not completed are cancelled.
     * The results of this method are undefined if the given
     * collection is modified while this operation is in progress.
     *
     * @param tasks the collection of tasks
     * @param <T> the type of the values returned from the tasks
     * @return the result returned by one of the tasks
     * @throws ThreadInterruptedException if interrupted while waiting
     * @throws NullReferenceException if tasks or any element task
     *         subject to execution is {@code null}
     * @throws ArgumentException if tasks is empty
     * @throws AggregateException if no task successfully completes
     * @throws RejectedExecutionException if tasks cannot be scheduled
     *         for execution
     */
```

```cs
/**
     * Executes the given tasks, returning the result
     * of one that has completed successfully (i.e., without throwing
     * an exception), if any do before the given timeout elapses.
     * Upon normal or exceptional return, tasks that have not
     * completed are cancelled.
     * The results of this method are undefined if the given
     * collection is modified while this operation is in progress.
     *
     * @param tasks the collection of tasks
     * @param timeout the maximum time to wait
     * @param unit the time unit of the timeout argument
     * @param <T> the type of the values returned from the tasks
     * @return the result returned by one of the tasks
     * @throws ThreadInterruptedException if interrupted while waiting
     * @throws NullReferenceException if tasks, or unit, or any element
     *         task subject to execution is {@code null}
     * @throws TimeoutException if the given timeout elapses before
     *         any task successfully completes
     * @throws AggregateException if no task successfully completes
     * @throws RejectedExecutionException if tasks cannot be scheduled
     *         for execution
     */
```

### AbstractExecutorService.cs removed facade comments

```cs
/**
     * Returns a {@code RunnableFuture} for the given runnable and default
     * value.
     *
     * @param runnable the runnable task being wrapped
     * @param value the default value for the returned future
     * @param <T> the type of the given value
     * @return a {@code RunnableFuture} which, when run, will run the
     * underlying runnable and which, as a {@code Future}, will yield
     * the given value as its result and provide for cancellation of
     * the underlying task
     * @since 1.6
     */
```

```cs
/**
     * Returns a {@code RunnableFuture} for the given callable task.
     *
     * @param callable the callable task being wrapped
     * @param <T> the type of the callable's result
     * @return a {@code RunnableFuture} which, when run, will call the
     * underlying callable and which, as a {@code Future}, will yield
     * the callable's result as its result and provide for
     * cancellation of the underlying task
     * @since 1.6
     */
```

```cs
/**
     * the main mechanics of invokeAny.
     */
```

```cs
// CLR implementation of the inherited JDK ExecutorService contract.
```

```cs
// Netty supplies PromiseTask from newTaskFor; completion is queued directly
```

```cs
// after run, independently of executor-dispatched future listeners.
```

### Previous JdkFutureTask.cs CLR adaptation comment

```cs
// CLR adapter for the JDK FutureTask used by inherited bulk invocation. Unlike
```

The private class now serves only the legacy unordered scheduler; its remaining
interrupt/runner/cancellation semantics are unchanged in this checkpoint.
