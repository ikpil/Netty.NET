# Native Task scheduling

Baseline: `e66ce34777f9c4a0c57ac74bb97396ca2f54b43c`, read from
`D:/workspace/netty`. The implementation preserves the existing executor queues
and clocks while separating queue membership from the asynchronous result API.

## Source requirements and consumers

- `common/.../concurrent/AbstractScheduledEventExecutor.java` supplies deadline
  ordering, stable sequence IDs, capacity-limited transfer, virtual submission
  hooks and event-loop-owned queue changes.
- `ScheduledFutureTask.java` defines the one-shot invocation claim, fixed-rate
  deadline advancement, fixed-delay advancement after return and cancellation
  without interrupting the event-loop thread.
- `handler/.../timeout/IdleStateHandler.java` and `WriteTimeoutHandler.java`
  cancel outstanding timeout reservations and re-arm deadlines on an executor.
  The future channel port needs those cancellation and affinity contracts.
- `transport/.../nio/AbstractNioChannel.java` and `transport/.../pool/FixedChannelPool.java`
  use cancellable connect/acquisition deadlines; `handler/.../traffic/TrafficCounter.java`
  requires repeating monitoring. Other modules are evidence, not implemented here.
- `AutoScalingEventExecutorChooserFactory.java` monitors at fixed deadlines and
  cancels when a child terminates. `GlobalEventExecutor.java` maintains an internal
  fixed-delay quiet-period task. Both common consumers now use native work.
- `NonStickyEventExecutorGroup.java` delegates group scheduling directly to its
  underlying group. Its ordered child wrapper inherits unsupported scheduling.

`Task.Delay` by itself supplies neither the executor-owned invocation nor the
shared deadline queue. `Task.Run` would change event-loop execution order and
thread ownership. Those policies remain in the Netty executor.

## CLR result and invocation boundary

`ScheduleAsync` accepts actions, functions, token-aware functions and asynchronous
Task-returning functions, returning `Task` or `Task<T>`. Asynchronous delegates are
unwrapped; a null returned Task faults with `InvalidOperationException`.
Invocation is ordered; an asynchronous operation can suspend while later work
starts. Await continuations use normal .NET context rules. Callers submit new
executor-owned mutations explicitly rather than assuming await restores affinity.

`ScheduleAtFixedRateAsync` and `ScheduleWithFixedDelayAsync` accept synchronous
actions, with optional token awareness. Their returned Task represents the entire
reservation and stays pending between successful invocations. It finishes on
cancellation or failure. These APIs do not define asynchronous periodic delegates.
An `async void` action cannot be tracked through its asynchronous completion.

`NativeScheduledWork<T>` has one private TaskCompletionSource with asynchronous
continuations. It implements deadline membership through `IScheduledWork` and
does not implement `IFuture` or allocate a `PromiseTask`/`JdkFutureTask` result.
The ordered heap now contains only native work with deadline and sequence
metadata. Indexed membership remains available for removal; Task identity is
separate from queue membership identity.
The unordered backend supplies only its queue membership adapter; its existing
worker pool, shutdown settings and signed deadline arithmetic still need their
separate final CLR backend review.
Worker identity after factory replacement is corrected independently of the
pool configuration review; see common-unordered-worker-identity.md.

Cancellation before a one-shot invocation prevents the delegate from running.
Once claimed, cancellation is cooperative: a normal return succeeds; an
OperationCanceledException carrying the requested token cancels; an unrelated
OperationCanceledException faults, retaining its exception identity. A token
does not interrupt a worker. A repeating reservation can be canceled while its
current synchronous callback finishes, preventing future invocations; observing
its canceled Task does not mean that callback has physically returned.
Observer cancellation through Task.WaitAsync cancels only that observation.

Caller ExecutionContext flows unless suppressed. Each invocation runs in a scope,
so callback mutations do not leak into the executor or later repetitions.
Internal global quiet-period and utilization work do not capture a constructor
caller's context. Their control policies are independent of the consumer context.

Cancellation registration can fire before its handle is assigned. The callback
publishes completion without touching an unpublished handle; constructor and
publication paths unregister it after assignment. The invocation copies its
delegate under the ownership gate. Cancellation clears shared callback/context
references and removes pending queue membership. Unregister is nonblocking, so
an executor does not wait for a callback competing for its queue lock.

## Deliberate corrections and migration state

The old unordered Runnable decorator can report successful completion after an
inner JDK FutureTask captured an exception. Its periodic outer promise can remain
pending when only the inner task is canceled. Native scheduling publishes the
actual callback failure and canceled reservation instead. The compatibility paths
have since been removed, and their CLR-only source-quirk probes explicitly become
native failure/cancellation tests; see common-native-unordered-scheduling-migration.md.

Unordered shutdownNow returns queue membership work and completes the removed
native reservations' Tasks as canceled. Raw callbacks are released without
allocating a result facade. A worker-start
exception faults and removes an admitted native reservation. Native admission
after shutdown faults even if a configured legacy rejection handler discards work.
These corrections avoid permanently pending or falsely successful native results.

Seven unused CLR `Scheduled*Action*`/`ScheduledAsyncTask` wrappers were removed.
Their sole test consumer now uses ScheduleAsync. They had no pinned Java source
counterparts, and the misleading Async name previously meant only a token wrapper.
The shared JDK scheduling interface, ordered result adapters and group/default
Java scheduling overloads have since been removed. All ordered/global/single-thread
callers and fixtures use native scheduling; see
common-native-ordered-scheduling-migration.md. The unordered executor's concrete
legacy overloads and result backend have also been removed; raw execution owns
no Future result. See common-native-unordered-scheduling-migration.md. Remaining
public queue/configuration/shutdown decisions still prevent common and final backend
completion. Plain Future fixtures now use native APIs; see common-native-future-retirement.md.

Original comments remain beside corresponding retained scheduling implementations;
removed ordered result-facade comments are preserved with pinned source provenance
in common-native-ordered-scheduling-migration.md; removed unordered wrapper and
ScheduledFuture interface comments are in common-native-unordered-scheduling-migration.md.
The native implementation also retains the copied ScheduledFutureTask license
and scheduling comments. The comment audit reads UTF-8 explicitly to avoid
Windows PowerShell 5 treating `// héllo` as missing because of ANSI decoding.

## Validation

`NativeSchedulingContractTest` has 34 cases covering mixed-result FIFO deadlines,
native membership without Future results, fixed-rate/fixed-delay clock origins,
consumed-work cancellation, immediate/huge deadlines, argument rejection,
repeated ExecutionContext isolation, action/periodic failures, token-aware and
asynchronous delegate families, cooperative running cancellation, observer-only
cancellation, unrelated cancellation exceptions, null Tasks, periodic cancellation
during invocation, shutdown policy changes, shutdownNow, worker-start failures,
NonSticky group forwarding and unsupported child scheduling, captured-reference
release, and 256 cancellation/submission races with queue cleanup.

The observer-only cancellation test now explicitly occupies the single worker
while the 100ms producer timeout remains queued. A full Debug run previously
failed because wall-clock delay did not prevent the producer from completing
before WaitAsync observed cancellation. An already-completed Task legitimately
wins that observation. A worker barrier establishes the intended pending-producer
precondition while preserving cancellation and eventual result assertions.
The failure remains in native-completion-accounting-full-debug.trx; the repaired
native completion/progress/scheduler/accounting selection passes 99 cases in
native-completion-accounting-final-contracts.trx.

The shutdownNow regression initially failed while the other 24 initial cases
passed, then passed after the queue-removal correction. Existing affected tests
passed (75 cases), and native plus unordered contracts passed (59 cases) before
the final nine additional native cases. Logs/TRX are under the ignored test
project TestResults directory. Default full Debug/Release each pass 1204 cases,
with zero failures and 14 existing skips (1218 total), on Windows/net10.0.
Evidence: native-scheduling-full-debug.trx and native-scheduling-full-release.trx;
builds pass with zero errors and existing warnings. All 111 verified source/test
entries have zero missing original comments. Module completion remains open.
