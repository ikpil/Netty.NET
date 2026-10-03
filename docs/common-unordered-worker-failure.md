# Unordered worker replacement failure on CLR

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.
Scope: common and tests; other modules provide read-only consumer evidence.

## Evidence and required behavior

The pinned UnorderedThreadPoolEventExecutor extends ScheduledThreadPoolExecutor,
accepts a ThreadFactory in its constructors and registers identity around its
worker loop. Its original five common tests require actual callbacks, callable
failure identity, periodic execution and affinity. The pinned EventExecutorGroup
documents a lifecycle signal for actual termination. The previous CLR termination
review repairs the source's early shutdown-success behavior; this repair retains
that actual-drain decision. No Netty consumer requests CLR process termination
because a local executor cannot create its next worker.

A pinned all-module shutdownNow search finds common declarations/forwarding,
one common test of a separate ExecutorService, transport forwarding and an NIO
group test. It does not establish that every dedicated-worker invocation must be
stopped through Thread.Interrupt. The interruption policy is still a separate
review; this unit does not change it.

The pre-repair C# worker catches invocation failure, retires, and calls ensureWorker
from its finally block. Factory creation/start exceptions escape that block. In
isolated CLR processes, a throwing replacement factory and an already-started
replacement Thread each exit with -532462766 and an unhandled exception stack
through workerLoop/ensureWorker. A null replacement leaves accepted work without
a worker; the injected escaped invocation also has no published result. That
process times out with exit 41. The source version is the preceding commit,
08d4021. These are observed CLR failures, not a successful JVM compatibility layer.

## Native failure boundary

Normal user delegates still have their own TaskCompletionSource failure boundary.
The regression injects an internal INativeSubmission which escapes that boundary
to exercise worker replacement independently of ordinary user exceptions.
The queue adapter now forwards such a failure to the submission's existing result
owner before allowing worker retirement. It does not introduce another Task result.

If a replacement starts successfully, waiting work proceeds and the escaped
invocation keeps its original failure. If replacement creation throws, Thread.Start
throws, or the factory returns null, the executor closes admission and records a
backend failure. Queued native submissions, schedules, periodic lifecycles,
NonSticky child work and completion notification reservations receive that same
failure through their existing rejection hooks. Queued raw execute callbacks are
released without invocation; they have no result owner. Null replacement produces
an explicit InvalidOperationException. This is a documented CLR failure policy,
not a claim that JDK null-factory or worker replacement semantics are identical.

Other already-running invocations finish under their existing claim/cancellation
rules. Backend failure does not interrupt their threads or overwrite their results.
Termination becomes faulted with the backend failure only after all worker and
factory reservations have drained. isTerminated/awaitTermination describe that
actual drain even when Termination is faulted. Repeated shutdown returns the same
persistent signal; later admission is rejected. A closed, empty pool does not
create an unnecessary replacement just to fail its otherwise drained lifecycle.

Initial admission creation/start failures remain scoped to that admission: the
original task is removed/faulted and a later submission can retry. Initial null
factory results retain their previously documented pending-work behavior. There
is no background retry loop after replacement failure. Failure to deliver a
diagnostic through a throwing logger is contained at the worker-reporting boundary;
native operation/backend failure remains observable independently of logging.
Custom factory code outside task.run remains outside executor drain/exception
ownership, as documented in the worker-identity record.

## Reproducible verification

tools/Test-UnorderedWorkerFailure.ps1 builds and runs isolated net10.0 executables.
The existing test friend assembly name permits internal fault injection; generated
projects live only under ignored artifacts and are not packaged. Each executable
has bounded waits, reports actual results and returns failure if outcomes differ.
The throw/null/started before evidence is retained under
artifacts/unordered-worker-failure-before. Its generated source/build artifacts
were moved from TestResults after SDK source globbing caused an intermediate
test-project build failure; the script now generates outside the test project.
The failed build log is retained in unordered-worker-failure-contracts-debug.log.
No test Compile Remove exclusion was added.

The final Debug isolated probes all return zero under
artifacts/unordered-worker-failure-final-debug: throw, null, already-started Thread
and a throwing logging provider. The logger case verifies at least two attempted
diagnostic calls while preserving the same backend failure. Six normal-host CLR
cases pass with zero failures/skips (unordered-worker-failure-contracts-debug.trx).
They cover queued result/notification/child rejection, surviving-worker drain and
normal result preservation, successful replacement, and closed/empty retirement.
Two further rows check factory reentrancy: the factory can request immediate
shutdown, discard all queued work, and then return null or throw. The throw row
fails before correction because ensureWorker's finally publishes successful
Termination before its caller records the factory exception
(unordered-worker-failure-reentrant-before.trx: one pass / one fail). The retiring
worker now remains owned until that outcome is known. Reentrant closure returning
null needs no replacement; a thrown factory failure still faults Termination.
All eight rows pass in unordered-worker-failure-final-contracts-debug.trx.
An earlier attempt to compile those rows hits an active Rider test runner's DLL
lock (unordered-worker-failure-reentrant-before.log). The CLI subsequently uses
--artifacts-path artifacts/worker-failure-validation; Rider's process is preserved.
The isolated probe script also builds with its own artifacts path.
All original fixture workloads, identities, waits, assertions and Java comments
remain unchanged. The full configuration matrix and comment/inventory checkpoint
are recorded in common-porting.md after validation.

Final isolated Debug and Release each pass all four modes under
artifacts/unordered-worker-failure-reentrant-final-debug and
artifacts/unordered-worker-failure-reentrant-final-release. Final default full
Debug/Release each discover 1394 cases on Windows/net10.0: 1380 pass / zero fail /
14 unchanged skips (unordered-worker-failure-final-full-debug.trx and
unordered-worker-failure-final-full-release.trx). All 759 non-Porting and skip
identities are unchanged; only the eight CLR rows above are added, and configuration
names/outcomes match (unordered-worker-failure-identity-comparison.json). All 98
verified comment entries have zero missing, including 21 unordered source, eight
original test and ten group-contract comments. The pinned inventory matches all
271 entries and no implementation paths are missing
(unordered-worker-failure-comment-audit.json). git diff --check passes;
existing compiler/analyzer warnings remain.

## Remaining work

At this checkpoint immediate shutdown still used legacy Thread.Interrupt. The subsequent
[cooperative stop decision](common-unordered-cooperative-stop.md) implements StopToken
and StopAsync, includes callback drain/failures, and removes factory suffix interruption.
Shared/group immediate API review remains open.
Private queue costs and all 99 pending source decisions also remain, together with
the other common public API/runtime reviews. This repair does not finish common
or claim that all exceptions in arbitrary factory code are contained.
