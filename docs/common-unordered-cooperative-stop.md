# Native cooperative immediate stop

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.
Scope: concrete unordered common executor, its existing forwarding paths and tests.

## Pinned evidence and CLR difference

UnorderedThreadPoolEventExecutor.java inherits ScheduledThreadPoolExecutor and
delegates shutdownNow to it. EventExecutorGroup deprecates the method in favor of
graceful shutdown and documents actual executor termination. A pinned all-module
shutdownNow search finds common declarations/forwarders, a common test of a
different ExecutorService, transport forwarding/NIO group tests and a benchmark
stub. No unordered original fixture or production consumer requires arbitrary
CLR Thread.Interrupt injection or transfer of runnable queue handles to another
pool. Keep accepted-work withdrawal, admission closure and actual drain. The
original five unordered and three NonSticky fixture identities and workloads remain.

Java's interruption flag/interruptible operations are not CLR cancellation tokens.
The previous CLR backend copies the JDK interrupt-clearing loop and interrupts
all owned Threads on immediate shutdown. A bounded separate-process probe against
the preceding worker-failure checkpoint's compiled Debug library shows that a
busy invocation can return normally, the loop drains, and a custom factory's
suffix Thread.Sleep still receives ThreadInterruptedException. That suffix is
outside executor identity/drain ownership. Before evidence is retained in
artifacts/unordered-stop-suffix-before (exit 1; factorySuffixInterrupted=True).
The identical public-API probe against the revised library returns zero and
factorySuffixInterrupted=False under artifacts/unordered-stop-suffix-after.
tools/Test-UnorderedStopSuffix.ps1 reproduces this comparison using explicit DLLs.

## Ownership and public API

StopAsync closes admission immediately, withdraws queued native work and releases
queued raw callbacks. It returns the existing persistent Termination Task, not
another lifecycle result or a Task wrapper over an interrupting Java backend.
The executor requests its own StopToken through net10.0 CancellationTokenSource.
CancelAsync. It removes the old Thread.Interrupt calls and JDK interrupt reset.
The compatibility shutdownNow path uses the same policy and returns canceled
membership handles only for existing forwarding/test callers. Shared/group native
immediate API decisions remain a separate review; no global CLR ThreadPool settings
are changed by this concrete API.

Running operations opt into this signal: submit with StopToken, or explicitly
create a linked token from the operation owner's token and StopToken before
termination. The executor cannot cancel unrelated caller-owned token sources.
An already-claimed one-shot which ignores cancellation can still return success;
an OperationCanceledException with its requested submission token cancels under
the existing native producer boundary. Raw callbacks have no implicit cancellation
token; their owner must provide cooperation. Graceful quiet/timeout closure does
not request StopToken. Stop after an already-drained lifecycle returns the same
result without reopening token ownership or changing a completed result.

```csharp
var pool = new UnorderedThreadPoolEventExecutor(1);
using var owner = new CancellationTokenSource();
using var linked = CancellationTokenSource.CreateLinkedTokenSource(owner.Token, pool.StopToken);
Task operation = pool.SubmitAsync(async token =>
{
    await Task.Delay(Timeout.InfiniteTimeSpan, token);
}, linked.Token);
await pool.StopAsync();
// Observe operation separately: after its first yield, its body is caller-owned.
try { await operation; }
catch (OperationCanceledException) { }
```

Stopping claims a callback-drain reservation before CancelAsync requests the token.
Cancellation callbacks present at that request run asynchronously; arbitrary
registered code is never invoked inline under the pool gate, including factory
reentry. A caller context is not flowed into unsafe callbacks or internal stop
observation; ordinary registrations retain their CLR registration context policy.
The callback reservation is released only after their Task is observed. Termination,
isTerminated and awaitTermination all include that reservation alongside queue,
actual workers and factory starts. Repeated/concurrent stop requests signal once.

Callback failure is retained in Termination.Exception, including every exception
when multiple callbacks fail. If a replacement factory also fails, its original
failure remains the first lifecycle failure and callback failures remain in the
same Task's exception information. Callback failure neither invents successful
drain nor overwrites a claimed operation result. Callbacks must not synchronously
wait for Termination from inside their own cancellation notification; their
notification is part of the drain they would be waiting for.

The cancellation source is disposed after all owned drain reservations release,
before lifecycle publication, so registered callback/native wait-handle resources
do not persist through a completed pool. The cached token flag remains readable
after termination. Creating new linked registrations or using its WaitHandle after
that resource lifetime is unsupported. Yielded asynchronous delegate bodies are
still caller-owned; StopAsync does not await arbitrary async work after worker
invocation returns, and those consumers must observe their own operation Tasks.

## Verification

The installed net10.0 reference XML defines CancellationTokenSource.CancelAsync
and its callback completion Task, linked sources and disposal; runtime cases verify
the cancellation flag, asynchronous notification, actual claim/result policy and
exception identities rather than assuming Thread/Task equivalence.
Initial affected Debug passes 148 / zero failures/skips; expanded affected Debug
passes 153 / zero failures/skips (unordered-stop-contracts-debug.trx and
unordered-stop-final-contracts-debug.trx). Fifteen new CLR rows cover opted-in
native submission, legacy forwarding, one-shot/periodic schedules, independent and
linked owner tokens, uncooperative result/drain, asynchronous callback drain and
reentry, concurrent requests, all callback/backend failures, unsafe context,
custom factory suffix and yielded-body ownership. The existing CLR-only
ShutdownNowReturnsQueueWorkAndCancelsItsNativeResult probe now uses the explicit
StopToken and asserts cooperative cancellation instead of an infinite Sleep and
injected interrupt. Its queue-handle withdrawal checks remain. No original Java
fixture name, workload, wait, assertion or comment is changed by that decision.
Whole Debug and Release each pass 1395 / fail zero / skip 14 (1409 discovered);
unordered-stop-full-debug.trx and unordered-stop-full-release.trx use the default
solution on Windows/net10.0 with isolated artifacts to preserve Rider's DLL lease.
All 759 non-Porting and all 14 skip identities match the worker-failure checkpoint;
only 15 CLR rows are added and Debug/Release names/outcomes match
(unordered-stop-identity-comparison.json). All four worker-failure isolated process
modes pass in both configurations. The suffix probe also passes against revised
Release (artifacts/unordered-stop-suffix-release). All 98 verified comment entries,
21 unordered source, eight original fixture and ten group-contract comments have
zero missing. Inventory matches the 271 pinned files and all implementation paths
exist (unordered-stop-comment-audit.json). Existing compiler/analyzer warnings
remain; no additional OS/TFM or performance validation is claimed.

## Remaining work

The concrete immediate interruption decision is implemented. Private queue costs,
shared/group immediate API review and the remaining common runtime/API and 99
pending source decisions still prevent common completion. Verification does not
prove unmeasured performance or additional OS/target framework support.

The subsequent [queue cost review](common-unordered-queue-costs.md) measures local
deadline membership and native cancellation, then removes whole-heap rebuilding.
Those bounded measurements do not cover scheduler contention or transport throughput.
