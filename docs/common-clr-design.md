# Netty common: CLR design decisions

Baseline: `e66ce34777f9c4a0c57ac74bb97396ca2f54b43c` in `../netty`.
Implementation scope: common and its tests. Other original modules are read to
establish consumers; their implementations are outside this stage.

The destination is a .NET Netty with usable native APIs and the required Netty
execution/resource contracts. A matching Java class hierarchy, a green translated
test, and a comment count are separate evidence; none establishes that destination.
Previously verified components must also pass this design review.

Task aggregation/transfer now uses CLR framework replacements, documented with
source consumers and all original test-method decisions in
[common-task-composition.md](common-task-composition.md). The four Java-shaped
helper classes have been removed rather than wrapped in another public facade.

## Dependency review

Paths in the evidence column are relative to the pinned original repository.
These decisions do not claim that every method of a listed component is complete.

| Area | Original implementation and consumers | Required behavior and CLR decision | Remaining review |
| --- | --- | --- | --- |
| Completion | `common/.../concurrent/DefaultPromise.java`, `PromiseTask.java`; `transport/.../channel/AbstractChannel.java`, `ChannelOutboundBuffer.java` | Task/TCS owns the result and terminal state. Keep a cancellation boundary for operations that have committed to execution. Published completion must agree with every observer. | Reduce public Future/Promise compatibility surfaces and migrate their actual callers; do not keep blocking/JDK methods merely for tests. |
| Listeners and progress | `DefaultPromise.java`; resolver termination, channel-group close, SSL timeout, chunked-write and outbound-buffer consumers | Task owns the operation result. ExecutorProgress implements IProgress<TransferProgress> with unique detachable report/terminal registrations; ExecutorCompletion supplies ordered detachable Action<Task> registrations with independent notification Tasks. Both use native queue-removal policy and scoped executor context. The progressive hierarchy/factories are removed. See common-progress-subscriptions.md and common-native-completion.md. | Plain Future/Promise producers/callers and backend APIs still need migration. Combiners/notifiers use standard Task composition where callback affinity is unnecessary. |
| Execution | `SingleThreadEventExecutor.java`, `AbstractScheduledEventExecutor.java`; `transport/.../channel/AbstractChannel.java` | Preserve serial invocation, executor-owned state, deadlines and shutdown. Native Task-based submission/scheduling use the executor's queues and one TCS result; lifecycle exposes Task termination. | Legacy scheduling/listener callers and the final executor backend still need migration. See common-executor-lifecycle.md and common-native-scheduling.md. |
| Unordered execution | `UnorderedThreadPoolEventExecutor.java` and its inherited JDK scheduler | Dedicated workers, native Task results and private BCL deadline membership. PendingTaskCount replaces the inherited mutable queue; owner cancellation withdraws direct submissions and schedules. Termination waits for queue/worker/start reservations after quiet/timeout closure. Immutable constructor settings and native worker diagnostics replace inherited configuration/statistics. Concrete StopAsync/StopToken provide cooperative immediate stop with notification drain/failures; see common-unordered-cooperative-stop.md. | Shared/group immediate API remains open; local queue costs are measured in common-unordered-queue-costs.md, with contention/cancellation-heavy workloads still unmeasured. Global ThreadPool settings do not configure a local executor pool. |
| Constant registry | `common/.../ConstantPool.java`; `AttributeKey.java`, `Signal.java` and channel configuration constants | ConcurrentDictionary publishes one reference identity per name; Interlocked allocates IDs. Competing factories and ID gaps match the original. AbstractConstant now seals identity methods and uses a non-generic native uniquifier sequence; pools require reference constants. | Further registry/public API naming decisions are separate from the verified concurrency/reference/generic-static contracts. See common-task-composition.md for the repaired identity regressions. |
| Ordinary queues and maps | `PlatformDependent.java`, executor queues, `Recycler.java`; `buffer/.../PoolChunk.java` | Prefer ConcurrentQueue/Dictionary with explicit capacity and ownership policy where needed. PoolChunk's LongLongHashMap can use Dictionary<long,long> with explicit missing-value and remove/put result handling at its callers. | Recheck each queue's compound operations, reservation publication, overload/backpressure and iteration. Do not infer completion from a collection's thread-safe label. |
| Specialized integer queue | `MpscIntQueue.java`; `buffer/.../AdaptivePoolingAllocator.java` free lists | Fixed capacity, integer empty sentinel, fill/drain and weak reduction have actual allocator consumers. These requirements justify an adapter; generic CLR integers require no boxing specialization. | Compare the current ring with CLR collection alternatives against those operations; performance has not been measured. |
| Indexed priority queue | `DefaultPriorityQueue.java`, scheduled-task removal; `codec-http2/.../WeightedFairQueueByteDistributor.java` priority updates | Retain the indexed reference-node heap for mutable priorities and independent queue membership. Ordinary value/immutable entries use BCL PriorityQueue. Stopped scheduler queues clear references and indices. | Core source/interfaces reviewed; bounded indexed/BCL/tree costs below. Remaining scheduler/runtime review stays open. |
| Thread-local state | `FastThreadLocal.java`, `InternalThreadLocalMap.java`; allocator caches and event-loop workers | Physical-worker caches remain thread-local. AsyncLocal describes logical execution context and is a separate purpose. A sealed CLR Thread may be owned/wrapped where cleanup policy requires it. | Remove unnecessary ThreadGroup/JDK facade surface after caller review; keep cleanup/ownership behavior. |
| Resource lifetime | `AbstractReferenceCounted.java`, `ReferenceCountUtil.java`, `Recycler.java`; `buffer/.../AbstractReferenceCountedByteBuf.java` | GC does not decide when shared pooled/native storage is reusable. Retain/release must deallocate exactly once and never resurrect returned storage. Dispose alone does not define shared ownership. | Native owners, borrowed views and pin leases are implemented; integrate them with future pooled retain/release consumers. Ordinary CLR object cleanup and shared storage ownership must remain distinct. |
| Text and memory views | `AsciiString.java`, `CharsetUtil.java`; buffer/codec callers | AsciiString now uses lossless byte widening, native string/span construction and bounded memory views; MemoryStream constructors were replaced by ReadOnlyMemory. Cached text agrees with mapped bytes. Seven allocation callers use GC.AllocateUninitializedArray directly. See common-ascii-memory.md and common-platform-runtime.md. | Full native sequence API, culture/lexical numeric parsing, regex and charset/BOM review remains. Raw platform addresses and pooled-buffer integration remain separate reviews. |
| Runtime selection | `PlatformDependent.java`, `PlatformDependent0.java`; buffer/transport/TLS/resolver consumers | JDK-version facades and JVM reflective array allocation are removed. CLR consumers use Environment.Version and GC directly. Android detection uses the actual OS; JVM/Graal properties do not select CLR features. See common-platform-runtime.md. | NativeMemory owners/views replace the JVM cleaner hierarchy; managed words and copy/fill use CLR spans. Raw address/object-offset APIs remain in progress. See common-native-memory.md and common-heap-memory.md. |
| Ordinary object GC fallback | Deprecated `ObjectCleaner.java`; no production registration consumer in the pinned tree | ConditionalWeakTable lifetime notification and CLR pool dispatch replace the Java live-set/weak-queue/worker loop. Action registration, diagnostic count and concurrent/context-isolated cleanup are verified. See common-object-cleanup.md. | This runtime replacement does not define pooled/native storage ownership or deterministic resource disposal. |

## Implemented Task completion boundary

Task/TCS now owns the outcome directly. DefaultPromise, AbstractFuture, their
waiting/listener interfaces and erased listener storage are removed; see
common-native-future-retirement.md. Private native operation claims protect
commitment/cancellation boundaries, and ExecutorCompletion owns ordered detachable
notifications separately from result completion. PendingWrite uses a caller-owned
non-generic TCS and explicit pooled message/producer transfer.

TrySetCanceled(token) publishes cancellation. SetException(OperationCanceledException)
publishes that exact fault, without turning it into a canceled Task. A canceled
Task retains its token and has no synthetic cause object. The remaining native
submission policies distinguish queued cancellation from cooperative requests after
claiming execution. No public setUncancellable/getNow/blocking Future facade remains.

At the earlier adapter checkpoint a concurrent reader could observe the Netty result
as done before its supplementary Task completed; three regressions exposed that
split state and another exposed lost cancellation-token identity. Native consumer
competition tests now compare actual result/notification Task identity and original
value/error/token across 2000 iterations per outcome, without a second result view.

## Native executor submission

`EventExecutorExtensions.SubmitAsync` accepts Action/Func, including token-aware
and Task-returning delegates, and exposes Task/Task<T>. It does not construct a
Java Future/Promise for the submitted operation. An internal IRunnable is only
the boundary to the existing executor backend.

The same overloads now accept IEventExecutorGroup. AbstractEventExecutorGroup's
pinned submit methods select a child per submission; NonStickyEventExecutorGroup's
submit methods instead delegate to the underlying group. Native group submission
preserves that distinction. Explicitly submitting to a child returned by
NonSticky.next() still uses that child's ordered runner. Pre-canceled or invalid
submissions do not select a child; selection/admission failures fault the returned
Task with their original exception. No Future/Promise result adapter is created.

Direct submission to UnorderedThreadPoolEventExecutor now uses a queue reservation
without JdkFutureTask or PromiseTask. A narrow internal INativeSubmission exposes
only pre-start cancellation to queue removal; the submitted TCS still owns the
single result. Native admission rejects shutdown even if a legacy discard handler
would silently drop work, worker-start failure removes its queued reservation,
and shutdownNow cancels native submissions removed from the queue. Three targeted
cases failed before repair and pass afterwards. The broader submission/scheduling,
unordered and NonSticky selection passes 118 cases.

NonSticky ordered children now use BCL queues and per-admission native runner
reservations. Initial rejection faults pending native work and permits retry;
graceful shutdown drains admitted work while immediate shutdown cancels queued
work. Unordered execute recognizes native reservations through forwarding
executors, and each runner is bound to its actual queue owner. Eleven new cases
and the original workloads verify those contracts, thread handoff and bounded
inline dispatch. See common-nonsticky-runner.md.

This does not finish the unordered backend review. Submission and scheduling use
native results, while raw execute owns no result facade. Java scheduling overloads,
JDK/Promise decorators and cancellation-maintenance APIs are removed; see
common-native-unordered-scheduling-migration.md. The public mutable queue is removed
and PendingTaskCount supplies diagnostics; see common-unordered-native-queue.md.
Immutable constructor configuration and native worker diagnostics replace inherited
JDK settings; see common-unordered-native-configuration.md. Concrete immediate stop
uses explicit cooperative cancellation; see common-unordered-cooperative-stop.md.
Shared/group immediate API remains open. Local queue costs are measured in
common-unordered-queue-costs.md; contention/cancellation-heavy workloads remain
unmeasured. Worker replacement failure is implemented in
common-unordered-worker-failure.md. Graceful quiet/timeout admission
and actual drain are implemented; see common-unordered-graceful-shutdown.md. Detachable
native completion observers are implemented in ExecutorCompletion. Plain Future/Promise
fixtures now use those native APIs and the waiting/listener hierarchy is removed;
see common-native-future-retirement.md.
Shared/ordered scheduling facades and result adapters are removed; see
common-native-ordered-scheduling-migration.md. Dynamic native progress
registration is implemented; see common-progress-subscriptions.md.

Unordered worker registration now belongs to workerLoop instead of a factory
wrapper, so replacing the factory preserves native scheduled callback identity.
The prior false-affinity probe is explicitly replaced by the native contract;
source quirk evidence and regressions are in common-unordered-worker-identity.md.

The event-loop utilization writer now uses Interlocked.Add against the monitor's
Interlocked.Exchange. An independent known-budget test reproduced duplicate
accounting before the repair; I/O and actual task-batch reporting preserve the
budget afterwards. This fixes that counter race, while the cause of the existing
timing-sensitive high-load auto-scaling assertion remains unproven. See
common-utilization-accounting.md.

Six additional group cases exercise every delegate family, deterministic child
selection, pre-cancellation/argument checks, selector and queue failures,
NonSticky group-versus-child behavior, and a real two-worker group consumer.
Combined submission, progress, scheduling and original NonSticky tests pass
98 cases. Group submissions order initial invocation on their selected child;
they do not serialize an entire asynchronous operation across await suspension.
Keep a selected IEventExecutor when later work must access the same executor-owned
state: another submission to the group can select a different child.
The original NonSticky ordering workload now submits native delegates and joins
their Tasks with Task.WhenAll. It retains 10,000 tasks per producer, all four
batch sizes, the original concurrency/affinity assertions and every source
comment. This workload does not exercise JVM interruption semantics.

The native completion-observer boundary supports removal, not just a fixed
completion callback. AddressResolverGroup removes termination listeners when its
resolver group closes; DefaultChannelGroup removes a close listener when a channel
is removed. SslHandler's handshake completion cancels its scheduled timeout.
These pinned consumers require detachable callbacks without canceling the source
Task, prompt release of captured owners, executor invocation and registration
order. DefaultPromise removes pending listeners by identity; its already-claimed
notification snapshot can still run, and reentrant registrations follow that
snapshot. Independent ContinueWith calls do not by themselves establish all of
these policies. ExecutorCompletion now implements these policies with unique
disposable registration handles, BCL snapshot ownership and native queue reservations.
Its 29 contract cases include resolver-style removal and actual scheduler timeout
cancellation. Existing public Future/Promise methods are still present; do not infer
their migration from this native policy. See common-native-completion.md.

- Cancellation before invocation claims the queued work and releases the
  delegate/captured context, even while its empty work item still awaits dequeue.
- After invocation starts, cancellation is cooperative. A synchronous function
  returning normally succeeds. Throwing OperationCanceledException cancels only
  when its token matches the requested submission token; other exceptions fault.
- A Task-returning delegate is unwrapped. Its Task determines the eventual
  result/failure/cancellation after the delegate has started.
  Returning a null Task faults with InvalidOperationException in every async
  delegate family; it must not appear as cancellation. Four regression cases
  reproduced TaskCanceledException before the correction.
- Rejection faults the returned Task; invalid arguments throw synchronously.
- A single invocation claim arbitrates execution, queued cancellation and
  rejection. It is not a second result/terminal-state store.
- Cancellation registration uses UnsafeRegister and explicit unregistering.
  The cancellation callback does not access an incompletely published registration
  handle. An event loop never waits for a losing cancellation callback.
- Caller ExecutionContext is captured unless flow is suppressed. ExecutionContext.Run
  scopes invocation changes; AsyncLocal values cannot leak from one invocation
  into the worker/caller. Physical FastThreadLocal storage is unaffected.
- Delegate invocation is ordered on an ordered executor. Async suspension does
  not hold the executor or promise whole-operation serialization. Awaiting the
  returned Task does not confer event-loop affinity.

An async delegate has normal .NET await-context behavior. After external I/O,
executor-owned state is accessed by explicitly submitting work again:

```csharp
Task<int> operation = executor.SubmitAsync(async cancellationToken =>
{
    // Initial synchronous invocation is on the executor.
    byte[] response = await FetchAsync(cancellationToken).ConfigureAwait(false);
    return await executor.SubmitAsync(() => UpdateExecutorOwnedState(response));
}, cancellationToken);
int result = await operation;
```

The real DefaultEventExecutor consumer test proves that the executor processes
another job during the I/O wait, the suspended continuation can leave the loop,
and the explicit submission returns state access to the loop. It also verifies
graceful shutdown in its cleanup.

## Review and verification gates

The current target is net10.0. Verification is on Windows; this does not establish
other OS support. No performance improvement is claimed for these changes.

The native submission tests exercise all delegate families, original failure
identity, requested/mismatched tokens, queued-cancellation/invocation races,
context flow/suppression, async operation completion and early release of user
references. The constant registry tests force simultaneous factories and check
the published identity, creation-only rejection and concurrent ID allocation.

DefaultPromise's 33 original comment blocks and ConstantPool's 8 original comment
blocks remain in their source files. Original tests remain enabled in the default
suite. The temporary batch import was removed after all portable tests built by
default. The native additions supplement their behavior checks.

Initial completion/submission checkpoint (superseded by the current Task
composition checkpoint in common-porting.md):

- `dotnet test Netty.NET.sln -p:PortingBatch=true --no-restore`: Debug,
  956 passed / 14 skipped / 0 failed (970 total).
- The same command with `-c Release`: 956 passed / 14 skipped / 0 failed.
- `dotnet build Netty.NET.sln --no-restore`: at that checkpoint failed with 286
  diagnostics in AsciiStringCharacterTest. The later AsciiString checkpoint
  restores compilation; later native/heap memory checkpoints pass default full tests.

Detailed results are in the ignored TestResults directory, including
`clr-foundation-debug.trx`, `clr-foundation-release.trx`, and the before/after
completion-regression logs. Build/test analyzer warnings remain.

The preceding completion/progress/accounting checkpoint passes the default full Debug
and Release suites: 1287 passed / 0 failed / 14 skipped (1301 total) on Windows/net10.0.
Evidence: native-completion-termination-full-debug.trx and
native-completion-termination-full-release.trx. Initial runs failed an existing
auto-scaling timing assertion and a native observer-cancellation test precondition.
The independently reproduced accounting race is fixed; its role in the former
assertion remains unproven. The latter test now explicitly guarantees a pending
producer. Failed runs and distinctions remain in common-porting.md.
Remaining work
includes public API migration, executor backend/lifecycle decisions, raw
platform/encoding review and the pending source/test entries. Full test success
does not establish reviewed completeness of that work. See common-porting.md.

The subsequent AsyncMapping checkpoint migrates that interface to a provider-owned
Task<T> and optional cooperative CancellationToken, with input contravariance.
Native SNI-shaped consumers verify message ownership and explicit loop dispatch;
handler/TLS implementation remains outside this change. That checkpoint's default
Debug/Release results are 1297 passed / zero failed / 14 skipped (1311 discovered)
on Windows/net10.0. Evidence: async-mapping-full-debug-final.trx and
async-mapping-full-release-final.trx. Original comment coverage is zero missing
across 112 verified source/test entries. See common-native-async-mapping.md.

Completed-result classes and executor success/failure factories are now replaced
by standard Tasks. ExecutorCompletion retains the explicit callback policy;
DNS membership uses unique reservations instead of Task identity, and channel
owner/loop metadata remains in consumers. The current full Debug/Release matrix
on Windows/net10.0 each passes 1303 cases / zero failures / 14 skips (1317 total).
Evidence: completed-result-full-debug.trx and completed-result-full-release.trx.
At the completed-result checkpoint, three source entries move to clr-replacement;
all 109 then-verified entries
have zero missing comments. The three removed classes' 13 comments and the two
removed factory comments are also preserved. See common-completed-results.md.

Subsequent progress and external-producer checkpoints remove all executor
result factories. External producers create TCS and expose Tasks; notification
policy and invocation/cancellation claims remain separate. Native provider/pool
consumer tests and translated CLR scenarios are recorded in
common-native-producers.md. The manifest now has 98 verified source/test entries
and four additional progressive CLR replacements. Unused PromiseTask and Callable
glue are removed; see common-native-submission-wrapper-cleanup.md. DefaultPromise and
the remaining Future/Promise hierarchy are CLR replacements with native fixtures;
see common-native-future-retirement.md. Native queue ownership and graceful
quiet/timeout admission and native constructor configuration are implemented.
Concrete immediate stop uses StopToken/StopAsync and includes callback drain/failures;
see common-unordered-cooperative-stop.md. Shared/group immediate API and private queue
costs were open at that checkpoint; the subsequent local measurement is recorded
below. The concrete legacy scheduler/result backend has been removed.
The deprecated UnaryPromiseNotifier alias is also a standard Task/TCS replacement,
with no pinned caller; see common-task-composition.md for transfer coverage and
original comment provenance. No new result/listener facade is required.

The unordered private removal path now uses net10.0 PriorityQueue.Remove with
reference identity instead of copying/clearing/reinserting the whole heap. Measured
capacity reuse and public cancellation support keeping the BCL heap; SortedSet is
faster for large scattered removal but allocates nodes on reusable insertion.
See common-unordered-queue-costs.md for conditions, extrema and the remaining
contention/cancellation-heavy workload limits. Shared/group immediate APIs are
subsequently implemented below.

## Native stop across executors and groups

`IEventExecutorGroup.StopAsync()` requests the backend's stop policy and returns
its persistent `Termination` Task. Ordered workers close admission, drain accepted
invocations and cancel outstanding schedules; unordered workers withdraw waiting
work and request their explicit cooperative `StopToken`. No thread interruption
is injected. Yielded asynchronous bodies remain caller-owned, and canceling an
observer's `WaitAsync` does not cancel shutdown. NonSticky groups and selected
children forward to the underlying executor. Global/Immediate executors return
their existing failed lifecycle Task because they cannot terminate.

The pinned AbstractEventExecutor.java:85 delegates shutdownNow to shutdown;
SingleThreadEventExecutor.java:923 drains accepted invocations. The producer loop
in transport/NioEventLoopTest.java:204-238 needs admission closure and actual
termination, not withdrawal of every accepted ordered invocation. Native ordered
stop additionally escalates an existing graceful quiet period into admission
closure. This is an explicit CLR API decision: the pinned shutdown0 at line 782
ignores requests after graceful shutdown starts. Legacy entry points retain that
non-escalating policy. Checking the CAS state snapshot prevents a concurrent
graceful request from reopening admission after native stop.

Multithread groups request every child and retain the pinned termination counting
policy (MultithreadEventExecutorGroup.java:114-123): the group completes successfully
after every child signal completes, including failed children. A synchronous stop
request exception is collected independently; later children are still requested,
then an AggregateException reports those request failures. No second lifecycle
Task or Task.WhenAll replaces the original completion signal. Abstract custom
backends default to their shutdown primitive and may override the native policy.
ExecutorLifecycleContractTest covers these distinctions; current results and
remaining backend/API work are in common-porting.md.

## Ordered scheduler and indexed queue ownership

The indexed heap supplies O(log n) removal and mutable-priority repair with no
per-insertion tree node allocation. The pinned HTTP/2 distributor uses
priorityChanged (WeightedFairQueueByteDistributor.java:376) and separate
state-only/pseudo-time queue indices (lines 738-749). A BCL PriorityQueue stores
priority alongside each element; its reference-aware Remove scans membership.
SortedSet offers keyed removal but priority changes require removal/reinsertion
and allocate tree nodes. Retain the indexed heap for these requirements and use
BCL PriorityQueue for ordinary value/immutable entries. The CLR-only integer queue
scenario now uses that BCL type; there is no general linear-scan fallback in
DefaultPriorityQueue. Nodes must be references and implement indexed membership.

CLR membership checks the actual reference at the recorded index. The pinned
DefaultPriorityQueue.java uses node.equals at its contains helper; actual scheduler
and HTTP/2 nodes use reference identity, while CLR value-equal objects/records must
not remove another reservation through a stale index. Comparer order and per-queue
indices still determine priority repair. Equal-priority FIFO requires a sequence
tie breaker, as used by scheduled work; the generic heap adds none. Enumeration
remains live and unordered; toArray is a typed snapshot rather than Java's erased
array/iterator convenience overloads. BCL Array.Resize owns array capacity, capped
at Array.MaxLength; failed allocation cannot desynchronize a parallel capacity
field. Extreme OOM capacity limits are reviewed structurally, not forced in tests.

The pinned scheduler cancels a snapshot then calls clearIgnoringIndexes
(AbstractScheduledEventExecutor.java:170). That method explicitly assumes the
queue is about to be garbage collected and nodes will not be reused
(PriorityQueue.java clearIgnoringIndexes documentation). A stopped CLR executor can
remain reachable, retaining its queue and scheduled work in the backing array.
Shutdown now clears references and indices. A real retained executor with a
weakly observed canceled reservation reproduces retention before and collection
after the change. The terminal-only clearIgnoringIndexes API remains available
for its original specialized purpose; ordinary clear permits node reuse.

The reproducible bounded probe runs with -IndexedQueues in
tools/Measure-UnorderedQueueCosts.ps1, using an explicit Release library path.
Two sequential processes run after full tests, on Windows 10.0.26300 X64/.NET
10.0.7 (16 logical processors), with tiered compilation disabled only in the
probe, two warmups and seven samples per row. Random seed 42 selects 32 scattered
members; removal rows combine 32 hits and 32 repeated misses. Independent checks
cover ties, compact signed-clock wrap and hostile equality/foreign indices.
Reuse fill/drain rows divide by 2N push/pop calls. Setup and post-interval drain
are excluded; allocation is current-thread managed bytes only.

| At 16384 entries, median ranges across two processes | Indexed heap | BCL PriorityQueue | SortedSet |
| --- | --- | --- | --- |
| Mixed removal, ns/call | 35.94-37.50 | 23237.50-23570.31 | 242.19-262.50 |
| Reused fill/drain, ns/call | 139.87-141.56 | 123.17-124.24 | 155.52-155.94 |
| Reused fill/drain, bytes/call | 0 | 0 | 24 (48 per insertion) |

The BCL heap is faster for reused fill/drain in this probe, while indexed removal
avoids the scan and the tree's insertion allocation. This supports retaining the
indexed representation for mutable priorities/removal; it makes no overall
scheduler throughput, contention or cross-thread allocation claim. All sizes,
sample extrema and median allocations are in common-indexed-queue-costs.csv;
raw outputs and library hash are under ignored artifacts/indexed-queue-costs-*.
Current test results, inventory decisions and remaining work are in common-porting.md.

Relevant CLR specifications:

- [TaskCompletionSource producer/consumer separation](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource-1?view=net-10.0).
- [Cooperative Task cancellation](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/task-cancellation).
- [GetOrAdd factory execution](https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentdictionary-2.getoradd?view=net-10.0).
- [UnsafeRegister context and immediate-callback behavior](https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtoken.unsaferegister?view=net-10.0).
- [Memory ownership, consumers and leases](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/memory-t-usage-guidelines).

The ordered chunk-cache consumer now has native atomic ceiling/conditional
removal and snapshot APIs. Original scenarios and CLR ownership/concurrency
regressions pass. Broader source review remains; see common-ordered-multimap.md.

The subsequent CLR worker replacement failure boundary is implemented in
[common-unordered-worker-failure.md](common-unordered-worker-failure.md).

## CLR replacements for concurrent sets and read-only iterators

ConcurrentSet.java is deprecated in the pinned source in favor of JDK concurrent
map key sets. An all-repository pinned search finds no consumer outside its own
class, and there is no C# class/caller/test to preserve. Use
ConcurrentDictionary<T, byte> for concurrent membership: TryAdd/TryRemove supply
the admission/removal result, ContainsKey/Count/Clear expose membership, and
pair enumeration supplies a concurrent view without a Java AbstractSet wrapper.
Choose the equality comparer for the domain; membership here is set equality,
not the indexed heap's reference ownership. A HashSet snapshot can supply set
algebra when needed. JVM Serializable/serialVersionUID creates no CLR requirement.
No ConcurrentSet facade is added and no unported consumer is claimed implemented.

ReadOnlyIterator.java exists to forward traversal while rejecting Java
Iterator.remove. Its actual consumers are transport ThreadPerChannelEventLoopGroup
(line 147) and AbstractChannelPoolMap (line 115); same-named nested HTTP header
iterators are separate classes. CLR IEnumerable<T>/IEnumerator<T> provide
traversal and disposal without a Remove member. They preserve this restriction
without an adapter and do not make the collection or referenced elements
immutable. Future channel/pool collections still own ordering, consistency,
mutation and resource policy. The existing common group enumeration contract
verifies that child traversal exposes no mutable collection; it does not port
those transport consumers. No ReadOnlyIterator facade is added.

Original comments at the pinned common/src/main/java/io/netty/util/internal/
ConcurrentSet.java and ReadOnlyIterator.java are preserved below. Both originals
share this identical license header; it is attributed to both files.

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

ConcurrentSet.java additionally contains these two documentation blocks. The
constructor actually creates its own map; the source wording is archived as written.

```java
/**
 * @deprecated For removal in Netty 4.2. Please use {@link ConcurrentHashMap#newKeySet()} instead
 */

/**
     * Creates a new instance which wraps the specified {@code map}.
     */
```

## Monotonic time and duration conversion

Pinned Ticker.java:24-84, SystemTicker.java:21-43 and MockTicker.java:25-50
separate elapsed nanoseconds from the raw initial timestamp. Scheduler deadlines,
SingleThreadEventExecutor work metrics and AutoScalingEventExecutorChooserFactory
consume elapsed time; transport I/O handlers pass raw System.nanoTime values to
subtract the same initial offset. EmbeddedEventLoop and ManualIoEventLoop choose
an executor clock. Preserve that boundary and signed long wrap instead of using
UTC, DateTime or a TimeSpan wall-clock value.

SystemTimer reads TimeProvider.System.GetTimestamp/TimestampFrequency. Integer
scaling avoids floating-point precision loss; an integral nanosecond multiplier
is cached for the ordinary clock path. Other ratios use integer division or Int128
intermediate multiplication, wrapping only the final timestamp. Duration conversion
is different: shared TimeUtil saturates TimeSpan's integer 100ns ticks and long
milliseconds to signed nanoseconds, matching the local Corretto 21.0.11
java.base/java/util/concurrent/TimeUnit.java toNanos contract. No double
TotalNanoseconds or intermediate TimeSpan.FromMilliseconds(long) is needed.
SystemTimer's existing public conversion constants remain available, but the
clock no longer uses them for floating-point conversion.

SystemTicker's positive waits round a submillisecond remainder up and split long
waits into Int32-millisecond Thread.Sleep chunks. Nonpositive waits return without
consuming a pending interrupt, matching TimeUnit.sleep. The native TimeSpan and
millisecond overloads preserve their full duration rather than first saturating
to the nanosecond horizon. Positive waits remain interruptible; timer resolution
and OS scheduling are not nanosecond accuracy guarantees. MockTicker advancement
saturates each converted duration, while accumulation still wraps as the original
AtomicLong does. Original TimeUnit comments are retained verbatim beside CLR notes.

Ticker remains the executor's elapsed-nanosecond and synchronous-wait policy.
The controlled mock supplies nanosecond advancement and blocking sleeper observation
that TimeProvider alone does not specify; inheriting TimeProvider's default timer
behavior would silently introduce real-clock timers into a controlled mock.

DefaultMockTicker implements the useful fair-wakeup policy with an explicit FIFO
tick queue. Each advance notifies registered sleepers in registration order; new
sleep phases, observers and subsequent advances wait for those notifications to
be processed. This prevents awaitSleepingThread from observing the stale old phase
after its deadline. CLR Monitor still supplies no general fair-mutex guarantee,
and application code after sleep returns has no FIFO execution guarantee. A reusable
LinkedListNode per sleeper avoids new tick-node allocation on every advance;
Interlocked owns the native long timestamp, HashSet with ReferenceEqualityComparer
owns thread membership, and interrupted sleepers remove registration and pending
notification nodes in finally. The original fairness comment remains alongside
the precise CLR policy note.

Advance entry and pending-notification waits preserve a consumed interrupt for the
next interruptible wait, as Java ReentrantLock.lock does; sleep and observer waits
remain interruptible. Four CLR regressions cover 128 consecutive sleep phases,
signed clock wrap, interrupted-registration cleanup and contended advance. The
before-run fails the stale phase and contended-advance rows. For the latter, the
test only holds the private gate to force contention and checks public clock/interrupt
outcomes. A separate probe compiled the five unchanged pinned Java sources
(four clock types plus ObjectUtil) with local Corretto 21.0.11; 20 x 128 consecutive
sleep phases pass. That confirms the expected original behavior, without claiming
to run the entire Java suite. The six original C# mock scenarios remain unchanged.

### Native provider selection for ordered scheduling

Ticker.FromTimeProvider accepts a CLR timestamp source. TimeProvider.System selects
the existing singleton and shared epoch. A custom selection captures a stable
positive TimestampFrequency and its native timestamp origin; its elapsed clock
starts at zero. It subtracts native ticks with signed wrap before integer scaling,
avoiding fractional-frequency origin rounding and native timestamp rollover errors.
The initialNanoTime metadata is the scaled captured origin. Raw-clock legacy helpers
must not mix system timestamps with a custom provider's epoch or units.

AbstractScheduledEventExecutor(parent, timeProvider) and the allocated-queue
SingleThreadEventExecutor core constructor select that clock; DefaultEventExecutor
exposes a public TimeProvider constructor. Existing constructors retain the system
singleton. Provider input is immutable for the executor, and the caller owns the
provider. Native Task scheduling, cancellation, deadline/tie ordering, metrics and
callback dispatch still use the existing executor state and queue.

Pinned transport EmbeddedEventLoop.java:43-45/90-92 selects a clock and runs due
work when driven. ManualIoEventLoop.java:145-154 explicitly requires manual wakeup
when the supplied clock advances faster than I/O system time. Preserve that contract:
provider advancement makes deadlines due, and the owner must pump/wake the executor
(e.g. submit ordinary work through SubmitAsync for a dedicated DefaultEventExecutor).
Do not dispatch callbacks from TimeProvider.CreateTimer or read GetUtcNow for deadlines.
A timestamp-only custom adapter rejects synchronous sleep, as the pinned embedded
FreezableTicker does. SystemTicker and DefaultMockTicker retain their explicit waits.

Eleven native provider rows cover system identity/null input, invalid frequencies,
nonzero/fractional-frequency origins, subnanosecond conversion, native tick wrap,
unsupported sleep and due/tie/affinity behavior on manual and actual dedicated
executors. Their provider throws if wall time or provider timers are accessed.
This reviews common ordered clock selection; it does not implement those transport
event loops or add provider injection to the pinned fixed-system unordered backend.
