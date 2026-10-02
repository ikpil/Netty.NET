# Producer-owned Task results

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.
Implementation scope: common and its tests. Other modules supply consumer
evidence; resolver, transport and channel pools are not implemented here.

## Evidence and API decision

EventExecutor.newPromise supplies a DefaultPromise whose listeners run on the
selected executor. ImmediateEventExecutor's special Promise bypasses blocking
deadlock checks because every thread is considered in its event loop.
SimpleNameResolver creates a Promise before calling the provider, supports inline
or deferred completion and catches provider exceptions. SingleThreadIoEventLoop
dispatches registration to its loop and publishes the handle after registering
it and incrementing its registration count. AbstractChannel calls
setUncancellable before operations such as bind/connect/register. FixedChannelPool
creates internal promises to run AcquireListener and release callbacks on the
pool executor; release admits queued acquisition before notifying its caller.

These requirements concern result ownership, invocation/cancellation boundaries,
callback execution and the order of owned state mutations. They do not require
an executor factory to own every asynchronous result. Native external producers
create TaskCompletionSource<T> with RunContinuationsAsynchronously and expose
only Task<T>. Awaiters can cancel their wait without mutating that producer.
ExecutorCompletion supplies affinity/ordered synchronous callbacks where needed;
otherwise ordinary await or Task composition suffices. Native SubmitAsync supplies
the execution claim for committed executor work, including pre-start cancellation.
TaskCompletionSource by itself does not implement setUncancellable or serialize a
consumer's registration, pool state or other side effects.

newPromise is removed from IEventExecutor, AbstractEventExecutor,
ImmediateEventExecutor and UnorderedThreadPoolEventExecutor. ImmediatePromise is
removed. No replacement TaskCompletionSource factory/facade is introduced.
At this producer checkpoint DefaultPromise, PromiseTask, Future/Promise interfaces
and inherited submission/scheduling backends remained during caller migration.
Subsequent native submission/scheduling and wrapper cleanup remove PromiseTask
and those backends. The native fixture migration removes DefaultPromise and its
waiting/listener interfaces too; see common-native-future-retirement.md. Final
executor configuration/queue/shutdown and other module design reviews remain.

```csharp
// The provider owns completion; the consumer sees only result.Task.
var result = new TaskCompletionSource<Resource>(
    TaskCreationOptions.RunContinuationsAsynchronously);
using var completion = new ExecutorCompletion(executor, result.Task);
using var registration = completion.Register(task => UpdateOwnedState(task));
StartProvider(result); // Provider-side code; never give result to consumers.
Resource resource = await result.Task;
await registration.NotificationCompleted;
```

Completing a Task and finishing executor callbacks are separate events. Awaiting
the Task does not wait for callback side effects. State needed by the caller at
completion must be mutated before the producer completes the caller's Task, as
the pinned pool release consumer does. Await continuation placement is governed
by normal CLR await context, not the executor used by ExecutorCompletion. Marshal
executor-owned state explicitly after an await; do not block the event loop on a
pending Task. The native asynchronous submission scenario suspends on a producer
Task, lets the loop finish it, and explicitly dispatches its subsequent loop work.

## Translated contract decisions

The existing PromiseContractTest retains 24 discovered cases; sixteen methods
(eighteen cases) which used newPromise now use native Tasks and subscriptions.
Previously migrated progress/completed-result scenarios remain native.

| Original CLR scenario | Native meaning and deliberate differences |
| --- | --- |
| Typed Promise listeners and fluent returns | Observers receive the original source Task; a disposed registration is detached. Task identity replaces Promise identity; no fluent Java return API is added. |
| Uncancellable result succeeds/fails | Real native submission ignores a cancellation request after invocation claim unless its delegate cooperates; both result and exact failure survive. The execution boundary is not a mutable flag on a public Task. |
| Stable per-Promise cancellation cause | Explicit producer cancellation retains the cancellation token and immutable canceled state. CLR exception object identity is not invented as a per-result contract. |
| Failure identity, null failure and diagnostics | SetException retains the supplied failure; await/GetAwaiter deliver it directly and Result/Wait aggregate it. Null exceptions reject without consuming the source. Existing diagnostic data stays attached; CLR stack propagation does not add Java suppressed exceptions. |
| Identity removal and duplicates | Unique handles independently remove duplicate delegates. Register(null) rejects instead of adopting Java null-terminated listener arrays. Other pending registrations stay ordered. |
| Callback failure and reentrant addition | ExecutorCompletion isolates callback failure and drains reentrant registration after the claimed snapshot. The operation stays successful. |
| Concurrent completion and registration | The three TCS terminal mutations race for one winner through 1000 iterations; 1000 concurrent registrations are each notified once. Notification completion is awaited independently. |
| Eight blocking waiters | Eight native awaiters resume with the same result; no blocking compatibility facade is required. |
| Timeout and enormous/negative duration | WaitAsync times out only the observer. CLR TimeSpan validation is retained, including rejecting MaxValue/MinValue on pending Tasks and accepting InfiniteTimeSpan. Java's saturating nanosecond wait API is not reproduced. |
| Interruptible incomplete waits | Native synchronous Task waiting on the tested Windows/net10.0 runtime observes Thread.Interrupt without completing the source. This is integration evidence, not the preferred async API. |
| Java uninterruptible waits restoring interrupt | Native timed/untimed observer waits use cooperative CancellationToken and preserve producer ownership. No Java interrupt-flag restoration method is added to Task. Original DefaultPromiseTest still verifies the temporary backend adapter where it is used. |
| Blocking inside the event loop | Native async submission suspends instead of blocking, permits the loop to complete the producer and explicitly marshals subsequent loop-owned work. Task itself has no custom BlockingOperationException contract. |
| Early/late listener affinity | Early and late Task observation invokes callbacks on the selected executor. |

The four original SingleThreadEventExecutor invokeAll/invokeAny-in-loop cases
still assert the pinned rejection and non-execution of their Callable. Their
test harness now owns a non-generic TCS instead of calling newPromise; it returns
the original exception through the native Task. Immediate and unordered progress
probes likewise use native pending results without Java blocking-check methods.
Original test methods, assertions by meaning and original comments remain.

NativeProducerConsumerContractTest adds five consumer cases: resolver-shaped
provider-owned immediate/deferred/throwing outcomes, independent waiter
cancellation, exact exception identity, and a pool-shaped release which admits
queued work before completing the caller result on the selected executor.
A provider's thrown OperationCanceledException is faulted by SetException;
explicit SetCanceled marks cancellation. Native token-aware submitted delegates
have their separately documented cooperative cancellation policy. These are
consumer proofs of the common API choices, not ports of the external modules.

## Verification and remaining work

Focused Debug: native-producer-final-contracts-debug.trx, 126 passed / zero failed /
zero skipped on Windows/net10.0. Full default Debug and Release each discover
1338 cases: 1324 passed / zero failed / 14 existing skips. Evidence:
native-producer-full-debug.trx and native-producer-full-release.trx in the ignored
TestResults directory. All 759 non-Porting fixture identities and skipped
identities match the preceding progress-subscription checkpoint in both
configurations. All 105 verified source/test comment entries have zero missing,
including the two newly archived factory/special-adapter comments below.
No extra original test is disabled.
The factory search finds no newPromise call/declaration in C# source/tests;
historical Java links in retained comments describe the pinned source.
Original source comments removed with the factory/special adapter are archived
below, with locations, rather than attached to an invented API.

Shared/ordered scheduling callers and adapters have since migrated to native
Tasks; see common-native-ordered-scheduling-migration.md. Subsequent plain Future
fixtures use native Task/TCS and ExecutorCompletion and their compatibility
hierarchy is removed; see common-native-future-retirement.md. Unordered concrete scheduling and its
JDK result wrappers are removed; see common-native-unordered-scheduling-migration.md.
A green suite does not establish that final backend or common work is finished.

## Original comment provenance

### EventExecutor.java

Upstream: common/src/main/java/io/netty/util/concurrent/EventExecutor.java

Original line 52:

```java
/**
     * Return a new {@link Promise}.
     */
```

### ImmediateEventExecutor.java

Upstream: common/src/main/java/io/netty/util/concurrent/ImmediateEventExecutor.java

Original line 147:

```java
// No check
```
