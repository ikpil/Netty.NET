# Native executor lifecycle

Baseline: `e66ce34777f9c4a0c57ac74bb97396ca2f54b43c` in the local `../netty`.
This review covers common executor lifecycle completion and its actual C# callers.
Native submission and progress migrations have since removed their Java facades.
Inherited bulk invocation is removed in common-native-bulk-composition.md.
Legacy scheduling and backend policies still need review;
this checkpoint does not complete common or its executor backends.

## Original evidence

- `common/.../concurrent/EventExecutorGroup.java` exposes a persistent termination
  result and two graceful-shutdown entry points. `AbstractEventExecutor.java` and
  `AbstractEventExecutorGroup.java` provide the default periods and forwarding.
- `SingleThreadEventExecutor.java` completes its signal after shutdown cleanup or
  faults it with the original worker/start failure. Quiet-period acceptance,
  shutdown deadlines, suspension and physical-worker state have independent roles.
- `MultithreadEventExecutorGroup.java` counts every child's completed signal and
  succeeds after all children, including failed children. Its listener does not
  transfer child failures to the group result. Construction failure shuts down
  and waits for children already created.
- `NonStickyEventExecutorGroup.java` and its ordered wrappers delegate lifecycle
  ownership to the underlying group/executor.
- `GlobalEventExecutor.java` and `ImmediateEventExecutor.java` expose persistent
  unsupported-termination failures while their shutdown/terminated flags stay false.
- `AutoScalingEventExecutorChooserFactory.java` cancels its scheduled monitoring
  operation when the first child signal completes. This action does not manipulate
  an event-loop-owned queue directly.
- `UnorderedThreadPoolEventExecutor.java` completes its signal in shutdown and
  shutdownNow, before workers necessarily exit. Its existing TODO explicitly says
  graceful shutdown does not respect quietPeriod/timeout yet.
- `transport/.../channel/ThreadPerChannelEventLoopGroup.java` supplies downstream
  evidence for child completion counting and construction-time listener registration.
  Transport implementations are outside the current C# scope. Future transport
  callbacks that access executor-owned state must explicitly dispatch to that executor.

## Public CLR boundary

```csharp
Task Termination { get; }
Task ShutdownGracefullyAsync();
Task ShutdownGracefullyAsync(TimeSpan quietPeriod, TimeSpan timeout);
```

These are the primary lifecycle APIs on IEventExecutorGroup and implementations.
The old terminationFuture/shutdownGracefully methods and supplementary Task views
are removed. An executor/group owns a private non-generic TaskCompletionSource with
RunContinuationsAsynchronously; no Java Void value or Promise result adapter is
required. Global and immediate executors instead retain one Task.FromException.
Every observation and shutdown request returns the same task for that owner.
Nonsticky wrappers preserve the underlying task identity.

Await propagates the original exception. Task.Exception retains the CLR aggregate
exception container. A waiter can use Termination.WaitAsync(token/timeout) without
acquiring authority to cancel or complete the producer's lifecycle result. Invalid
shutdown arguments continue to throw synchronously where the original backend
validates them. The unordered and unsupported executors retain their different
pinned validation behavior.

An unexpected OperationCanceledException from worker creation or the event-loop
run method faults Termination with that original exception object, even when its
token is canceled. The lifecycle producer has no cooperative cancellation request;
a canceled observer wait does not supply one. Cancellation is not inferred from
the type of a worker failure. This deliberately replaces the old Future adapter's
exception-type-based cancellation classification at this boundary.

Task completion does not imply GlobalEventExecutor listener affinity. Await follows
the consumer's normal CLR context rules. ExecutorCompletion explicitly selects
GlobalEventExecutor for termination callbacks, matching the pinned termination
Promises even after the owned worker stops. Early/late callbacks on real single-thread
and unordered workers pass two native integration cases; see common-native-completion.md.
Executor-owned state is accessed through explicit SubmitAsync dispatch. Completion counting uses Interlocked and
ConfigureAwait(false)/UnsafeOnCompleted so construction-time SynchronizationContext
and ExecutionContext do not own the group's completion. Monitoring cancellation
uses the same context-independent observation and its thread-safe cancellation API.
Failed child exceptions are observed while preserving successful group completion.
Using Task.WhenAll directly here would change the pinned group failure contract.

An async delegate can request its own executor shutdown and await the signal:
suspension releases the physical worker so shutdown can finish. Blocking that
worker with Task.Wait/GetResult is not made safe by a Java Future deadlock detector.
The existing synchronous awaitTermination guard remains a separate backend contract.

## Backend timing and remaining work

For the single-thread executor, completion follows cleanup and terminal-state
publication. The subsequent unordered correction now waits for its queue to drain
and worker/start reservations to be released, matching isTerminated/awaitTermination.
It deliberately replaces the pinned early request signal; see
common-unordered-termination.md for source/consumer evidence and regression results.
Extra custom-factory code and yielded asynchronous delegate bodies are not joined
by the worker-loop lifetime signal. The remaining unordered backend review is open.
Worker identity now belongs to the loop even after factory replacement; factory
prefix/suffix code remains outside that identity. Scope tests explicitly join
those Threads rather than expanding Termination's lifetime contract. See
common-unordered-worker-identity.md.

Native scheduling, progress and submission have since been implemented, and inherited
bulk invocation removed. Shared/ordered Java scheduling facades have also been
removed. Unordered concrete Java scheduler/result wrappers are removed as well;
plain Future fixtures now use native APIs and their hierarchy is removed; see
common-native-future-retirement.md. Final pool configuration/queue/shutdown decisions
remain incomplete. Original Java comments and license blocks
remain beside their corresponding implementations, with CLR documentation added
separately.

## Regression evidence

ExecutorLifecycleContractTest now has 28 cases. Nine new CLR cases cover independent
wait cancellation, observation timeout, asynchronous self-shutdown, non-inline
consumer continuations, dormant constructor contexts, children completed before
registration (including fault/cancellation), concurrent child completion, and
OperationCanceledException failures from both worker startup and execution.
The earlier lifecycle/group cases retain persistent identity, original failure
identity, unsupported executors, all-child counting, construction cleanup, default
periods, forwarding, immutable enumeration and nonsticky wrapper behavior.

The initial affected executor selection passed 123 cases. The new lifecycle
selection passed all 28. Default full Debug and Release each pass 1170 cases,
with zero failures and 14 original/runtime skips (1184 total) on Windows/net10.0.
Builds succeed; compiler/analyzer warnings remain. The ten affected original
source entries retain all 309 comment blocks, and all 111 verified source/test
entries have zero missing required comments. Evidence is retained in the
ignored TestResults directory as lifecycle-targeted-first.trx,
lifecycle-native-contracts-final.trx and lifecycle-final-full-{debug,release}.trx.
