# Native submission caller migration

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.
Scope: common and its tests. Handler/transport consumers are read for evidence;
their modules are not implemented by the consumer scenarios below.

## Source and consumer evidence

AbstractEventExecutor inherits JDK submission, supplies PromiseTask from
newTaskFor and narrows results to Netty Future. AbstractEventExecutorGroup selects
a child for each submission; NonSticky group submission delegates to its
underlying group, while submission to an ordered child uses that child. The pinned
unordered pool decorates immediate submission through its legacy scheduler.

FlushConsolidationHandler.scheduleFlush yields queued work so additional writes
can be batched, and cancelScheduledFlush cancels an unstarted flush before an
explicit flush. LocalChannel.finishPeerRead stores a finish-read Future and checks
its completion before dispatching the peer-read operation. SimpleChannelPool and
FixedChannelPool closeAsync dispatch global close work and expose its completion.
SingleThreadEventExecutor.threadProperties submits a no-op to start its worker
and synchronously waits before publishing owner-thread metadata.

Native SubmitAsync already supplies Task/TCS results, pre-invocation claim,
CancellationToken ownership, executor queue admission and shutdown removal. It
does not wrap PromiseTask or create a second result state. Caller ExecutionContext
flows into the invocation and is scoped; async delegates are fully observed but
their awaits do not acquire executor affinity. ExecutorCompletion supplies
callback affinity when required. Queued work is not replaced by Task.Run.

The three Java submit forms are removed from IExecutorService,
AbstractExecutorService, AbstractEventExecutorGroup, NonStickyEventExecutorGroup
and UnorderedThreadPoolEventExecutor. Functions express a return value; Actions
have ordinary Task completion without a public Java Void/null result. Cancellation
uses an owned token source, and notifications use an explicit registration.
No Runnable/Callable-to-Future adapter is retained to invoke the removed API.
The subsequent bulk review removes inherited invokeAll/invokeAny and newTaskFor;
see common-native-bulk-composition.md. Subsequent ordered/unordered scheduling
migrations remove the compatibility backend. The unused PromiseTask and Callable
glue are now removed; see common-native-submission-wrapper-cleanup.md.
The subsequent native fixture migration also removes DefaultPromise and its
waiting/listener interfaces; see common-native-future-retirement.md.

## Internal bootstrap and differences

threadProperties now submits its real startup barrier through SubmitAsync. Its
synchronous getter still must finish starting the worker before returning.
The private wait retries a CLR blocking wait interrupted by Thread.Interrupt and
restores the consumed interrupt before returning or propagating a later error.
It distinguishes a Task fault whose original cause is ThreadInterruptedException
from interruption of the waiting thread, so producer failure cannot become a
retry loop. This is a private getter requirement, not a public uninterruptible
Task/Future API. Async consumers should await and explicitly marshal loop state.

Native unordered submission reports the actual delegate failure. Pinned legacy
Runnable decoration can report success after backend failure. The CLR regression
which documented that quirk used legacy schedule(..., zero) at this checkpoint,
as did the custom-rejection and accepted-queue-after-factory-failure probes.
Those CLR-only probes now verify corrected native failure, rejection and admission
rollback; the concrete Java scheduler/JDK result backend is removed. See
common-native-unordered-scheduling-migration.md. Native worker-start failure removes its reservation and
faults the Task, as verified by NativeExecutorTaskContractTest. A discard handler
cannot leave a native Task pending after shutdown. The backend/factory/policy
redesign remains incomplete and these Java quirks are not final API decisions.

Rejection and startup failures return faulted native Tasks instead of throwing
synchronously from submit; invalid arguments still reject synchronously. The
original SingleThreadEventExecutor test for work added after shutdown now
classifies native faulted rejections through the returned Task and still verifies
that all accepted work completes and no queued work is abandoned.

Task waiting follows CLR timeout/exception rules. Await/GetAwaiter delivers the
exact original failure; Result/Wait aggregates it. Throw-only delegates can use
C#'s explicit lambda return type to disambiguate synchronous and async overloads,
for example SubmitAsync(string () => throw failure).

## Test translation and validation

The five original unordered tests retain their names and original comments.
The endless-execute case registers its native completion callback before releasing
the original two-party rendezvous, waits for callback completion explicitly, then
performs all 10,000 queue-empty assertions. Failure and value tests use the actual
Task outcome. Fixed-rate testing has since migrated to native scheduling and
caller-owned cancellation, retaining the original callback count/delays and comments;
see common-native-unordered-scheduling-migration.md.

Affected CLR tests now use native result identity, executor callbacks, pending
Task state, cancellation tokens, value-type outcomes, group child selection,
worker lifecycle and queue statistics. The direct NonSticky submission probe
verifies a native queue reservation instead of inspecting PromiseTask's type.
One CLR-only PromiseTask description/sentinel test is removed: Java wrapper
ToString text has no native Task API requirement. It is not an original upstream
test or a disabled discovered case; native lifecycle/retention is verified by
the existing submission contracts.

Four NativeSubmissionConsumerContractTest cases verify yielding/coalescing a
flush, explicit pre-start cancellation, interrupt-preserving startup metadata
and exact interrupted-producer failure propagation. The first focused run's one
failure occurred in shutdown cleanup because its factory threw on every startup,
including shutdown's attempt. The fixture now fails the first creation only and
allows cleanup to start its worker; no production behavior changed for that
repair. Evidence is retained in native-submit-final-contracts-debug.trx/log.
Final focused and full-suite results are recorded in common-porting.md.

Final focused Debug: 152 passed / zero failed / zero skips
(native-submit-final-contracts-debug-after.trx). Full default Debug and Release
each discover 1341 cases: 1327 passed / zero failed / 14 unchanged skips
(native-submit-full-debug.trx and native-submit-full-release.trx). All 759
original fixture identities and all skipped identities match the preceding
producer checkpoint in both configurations. All 105 verified source/test comment
entries have zero missing. These are Windows/net10.0 results, not module completion.

## Original comment provenance

Removed Netty submit overrides have no original method comments. Their remaining
source comments stay in the corresponding mapped files. The inherited JDK
facade's three submission documentation comments and three AbstractExecutorService
throws comments below are preserved verbatim from the existing CLR translation
at repository HEAD, where JDK names had already been adapted to CLR names.
They are inherited-JDK reference documentation, not declarations of a retained
Java submission API. The removed CLR-only unordered adaptation comment is also
retained here as historical implementation provenance.

### IExecutorService.cs inherited submission comments

Previous mapped source: src/Netty.NET.Common/Concurrent/IExecutorService.cs at repository HEAD

```cs
/**
     * Submits a value-returning task for execution and returns a
     * Future representing the pending results of the task. The
     * Future's {@code get} method will return the task's result upon
     * successful completion.
     *
     * <p>
     * If you would like to immediately block waiting
     * for a task, you can use constructions of the form
     * {@code result = exec.submit(aCallable).get();}
     *
     * <p>Note: The {@link Executors} class includes a set of methods
     * that can convert some other common closure-like objects,
     * for example, {@link java.security.PrivilegedAction} to
     * {@link Callable} form so they can be submitted.
     *
     * @param task the task to submit
     * @param <T> the type of the task's result
     * @return a Future representing pending completion of the task
     * @throws RejectedExecutionException if the task cannot be
     *         scheduled for execution
     * @throws NullReferenceException if the task is null
     */
```

```cs
/**
     * Submits a IRunnable task for execution and returns a Future
     * representing that task. The Future's {@code get} method will
     * return the given result upon successful completion.
     *
     * @param task the task to submit
     * @param result the result to return
     * @param <T> the type of the result
     * @return a Future representing pending completion of the task
     * @throws RejectedExecutionException if the task cannot be
     *         scheduled for execution
     * @throws NullReferenceException if the task is null
     */
```

```cs
/**
     * Submits a IRunnable task for execution and returns a Future
     * representing that task. The Future's {@code get} method will
     * return {@code null} upon <em>successful</em> completion.
     *
     * @param task the task to submit
     * @return a Future representing pending completion of the task
     * @throws RejectedExecutionException if the task cannot be
     *         scheduled for execution
     * @throws NullReferenceException if the task is null
     */
```

### AbstractExecutorService.cs inherited submission comments

Previous mapped source: src/Netty.NET.Common/Concurrent/AbstractExecutorService.cs at repository HEAD

```cs
/**
     * @throws RejectedExecutionException {@inheritDoc}
     * @throws NullReferenceException       {@inheritDoc}
     */
```

```cs
/**
     * @throws RejectedExecutionException {@inheritDoc}
     * @throws NullReferenceException       {@inheritDoc}
     */
```

```cs
/**
     * @throws RejectedExecutionException {@inheritDoc}
     * @throws NullReferenceException       {@inheritDoc}
     */
```

### Unordered CLR adaptation comment

```cs
// JDK submit(runnable, result) adapts to a Callable, so decoration must retain its result.
```
