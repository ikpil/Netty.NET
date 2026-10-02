# Native executor completion observation

Baseline: `e66ce34777f9c4a0c57ac74bb97396ca2f54b43c` in `D:/workspace/netty`.
Scope: common and its tests; resolver, transport and handler are evidence for future consumers.

## Original contracts and consumers

`DefaultPromise.java` add/remove/notify methods and `DefaultFutureListeners.java`
establish registration order, identity-based removal, serialized notification,
claimed snapshot semantics, reentrant additions after the current snapshot,
executor invocation, bounded cross-promise recursion and callback failure isolation.
`DefaultPromiseTest.java` covers early/late listener order and chained completion.

`resolver/.../AddressResolverGroup.java` stores one termination listener per executor,
removes it when the group closes, and closes the resolver on termination.
`transport/.../channel/group/DefaultChannelGroup.java` removes its close listener
when a channel is removed. These consumers require early release of captured owners
without canceling a producer-owned operation. `handler/.../ssl/SslHandler.java`
registers completion callbacks that cancel the handshake/close-notify scheduled
timeouts. Invocation must occur on the executor that owns that state.

Termination is a specific exception to choosing the operation's worker: the pinned
SingleThreadEventExecutor, MultithreadEventExecutorGroup and unordered pool all
bind their termination Promise to GlobalEventExecutor. That executor remains
available after the observed worker stops. Native Task carries no implicit affinity;
termination cleanup uses an explicit GlobalEventExecutor observation. A channel
or handshake callback instead chooses its live owning executor. No fallback silently
changes a callback's selected execution location when that executor rejects work.

Plain Task completion suffices for result consumers and thread-safe composition.
For example, MultithreadEventExecutorGroup's termination counter and the automatic
scaler's monitor cancellation remain normal Task continuations; they do not need
a detachable ordered callback registry. Independent ContinueWith calls do not
establish the additional order, removal and lifetime policies for the above consumers.

## CLR decision

`ExecutorCompletion` observes an existing Task on an explicitly selected
IEventExecutor. It never completes, cancels or mirrors that operation. A BCL
LinkedList stores pending registrations; a private gate atomically admits, removes
and claims snapshots. It serializes callbacks even on a parallel executor.

`Register(Action<Task>)` returns a unique `CompletionRegistration` with IDisposable
removal and a `NotificationCompleted` Task. A repeated delegate creates distinct
registrations, so each handle removes exactly its own admission. This adapts Java
listener identity to a normal CLR registration handle without delegate equality
ambiguities or a GenericFutureListener/Future hierarchy.

Disposal before snapshot claim removes the callback, cancels its notification Task
and promptly releases its captured owner. Disposal after claim is nonblocking and
does not revoke that snapshot's invocation right, as in DefaultPromise. Reentrant
registrations follow every callback in the current claimed snapshot. Callback
exceptions are logged; each multicast invocation entry and subsequent registration
still runs, and notification completion succeeds independently of callback failures.
Callbacks are synchronous Actions, as required by the original notification policy.
An async-void delegate cannot have its post-await completion, exceptions or affinity
observed by this registration and is unsupported. Use normal Task composition or
Task-returning executor submission for asynchronous consumers.

The source Task can complete before any callback executes. Awaiting that Task does
not await notification dispatch. Await NotificationCompleted when callback delivery
is required. Registrations after completion are supported; on the executor, callback
invocation can occur synchronously before Register returns. An external producer's
TaskCompletionSource continuation policy determines when terminal observation starts;
there is no extra producer completion hook pretending to precede Task publication.

Constructor, registering caller and producer ExecutionContext are not captured.
Each callback receives the executor thread's current context in an isolated scope,
including when flow is suppressed. A process-wide CLR thread-static depth counter
bounds direct inline chains between observation instances to eight; dispatch at that
boundary uses the selected executor, whose execute operation must handle reentrancy.
Same-instance reentrant additions are drained iteratively. This keeps executor affinity;
it does not route callbacks through an unrelated CLR ThreadPool trampoline.

The pending source continuation and queued drain retain only a weak observation.
An observation, registration or retained notification Task keeps pending callbacks
alive. On removal/invocation, a registration releases its callback and observation;
retaining its terminal notification Task no longer retains the source result.
An explicitly retained ExecutorCompletion retains its source for later registrations;
Dispose releases the source/executor and closes future registration. Closing an
observation cancels pending callbacks while a claimed snapshot may finish.
Unlike listeners stored inside the original Promise, this separate observation has
an explicitly retained lifetime. Keeping only the operation Task is not a callback
delivery guarantee. Resolver/channel-group ports must store the observation or
registration for their membership lifetime and dispose it on removal; scoped
handshake consumers retain it through notification completion. This CLR lifetime
decision is covered by the abandoned-observation and retained-notification tests.

Queue drain reservations implement the existing internal native admission/removal
protocol, with no second result owner or Java Future queue wrapper. Admission
rejection faults pending notification Tasks and closes the observation. Immediate
shutdown removal cancels them; source completion remains untouched. This also works
through a selected NonSticky child and cannot silently discard a native notification
when the pool's legacy rejection handler discards raw work.

The same native queue-removal policy now applies to ExecutorProgress. Four regression
cases initially timed out: pending progress or pending terminal notification, each on
a direct pool or an ordered NonSticky child. Their NotificationsCompleted Tasks now
cancel when immediate shutdown removes the drain. A running drain retains its claim.

## Consumer examples and verification

```csharp
using var terminationObservation = new ExecutorCompletion(GlobalEventExecutor.INSTANCE, executor.Termination);
using var terminationRegistration = terminationObservation.Register(_ => resolver.Dispose());
// Removing this registration releases resolver without requesting executor shutdown.
terminationRegistration.Dispose();
```

```csharp
using var timeoutCancellation = new CancellationTokenSource();
Task timeoutTask = executor.ScheduleAsync(OnHandshakeTimeout, TimeSpan.FromSeconds(10), timeoutCancellation.Token);
using var observation = new ExecutorCompletion(executor, handshakeTask);
using var registration = observation.Register(_ => timeoutCancellation.Cancel());
await registration.NotificationCompleted;
```

ExecutorCompletionContractTest has 29 cases for source success/fault/cancellation
identity, early/late and duplicate registrations, removal/claim and completion races,
multicast failure isolation, same-instance and cross-instance recursion, scoped
executor context, native queue rejection/removal, unordered FIFO serialization and
reference lifetimes. It includes an actual native scheduler timeout-cancellation
consumer and resolver-style captured-owner removal. ExecutorProgressContractTest has
26 cases including the four repaired native queue removals. The coupled native and
original Promise, scheduler, lifecycle, unordered and NonSticky selection passes
222 cases in Debug. Logs: native-completion-final-contracts.trx,
native-completion-initial.trx and native-progress-removal-before.trx in TestResults.
Full default Debug/Release results are recorded in common-porting.md.
The final native-completion-termination full runs each pass 1287 cases with zero
failures and the same 14 existing skips (1301 total), on Windows/net10.0.

Two additional ExecutorLifecycleContractTest cases use actual single-thread and
unordered workers: early and late termination callbacks execute on GlobalEventExecutor,
retain the original Task identity and still run after the owned worker has stopped.
Both pass in native-completion-termination-contracts.trx. This is the pinned
termination Promise's execution location, explicitly selected by the native caller.

Original GenericFutureListener documentation and the corresponding DefaultPromise
notification comments are kept next to the native implementation with CLR notes.
Existing complete originals remain in their mapped legacy files. This checkpoint
does not remove those files or claim every DefaultPromise method has migrated.
The public legacy Future/Promise hierarchy and final backend worker/interruption
decisions remain separate migration work. Submission/scheduling callers and
dynamic progress registrations have since migrated to native policies; the concrete
Java scheduler/result and raw JDK wrappers are removed. See
common-native-unordered-scheduling-migration.md and common-progress-subscriptions.md.
The native observer is for required
execution/removal policy, not another result facade. No performance improvement
is claimed without allocation/throughput measurements.
