# Netty common port

Upstream: `e66ce34777f9c4a0c57ac74bb97396ca2f54b43c` (local `../netty`).
Scope: `common/src/main/java` and `common/src/test/java` at that commit.

## Verification

Default builds include every existing test. Several tests still contain Java
syntax, so the full test project currently fails to compile. Do not interpret
the existence of a C# file, a successful library build, or a batch test result
as completion of the module.

During the port, `dotnet test Netty.NET.sln -p:PortingBatch=true` explicitly
selects the tests in `test/Netty.NET.Common.Tests/PortingBatch.props`, including
additional CLR regression tests. It does not disable any tests in a default
build. Extend that list only as each test is translated and verified. The final
acceptance command is `dotnet test Netty.NET.sln` without this switch.

`common-porting-manifest.json` inventories all upstream Java source/test files.
Candidate paths are filename matches, not a claim of equivalent behavior.
Pending entries must be reviewed; justified replacements must state the CLR
behavior and the original contract being preserved.
Regenerate the inventory with `pwsh tools/Update-CommonPortingManifest.ps1`;
recorded reviews are preserved.

Check original comment coverage with
`pwsh tools/Test-CommonCommentCoverage.ps1 -UpdateManifest`. The audit reads the
pinned Git objects, tokenizes comments separately from string literals, and
compares comment text and multiplicity while ignoring whitespace. Passing this
check does not establish behavioral compatibility or correct comment placement;
both still require review.

## Translation rules

- Preserve every upstream license header, documentation comment, and implementation
  comment in both source and tests. Keep original comments alongside explicit CLR
  adaptation notes; comment preservation is part of each file's completion review.
- Preserve state transitions, identity, ordering, exception conditions, resource
  ownership, and thread affinity. Translate Java test assertions by meaning.
- Translate Java NullPointerException argument checks to ArgumentNullException,
  IllegalArgumentException to ArgumentException, and index errors to
  ArgumentOutOfRangeException. Preserve reference-count error messages.
- Java Integer is a nullable reference type. Attribute tests use boxed integers
  (`AttributeKey<object>`) to preserve null and reference identity. Attributes
  currently accept reference types; default(int) must never represent null.
- Attribute names and IDs share a non-generic registry. Java can unchecked-cast
  the same key to another generic type; CLR cannot. Requesting an existing name
  with a different T fails explicitly with ArgumentException. Names remain
  globally unique and IDs cannot collide across value types.
- Use CLR fully qualified type names for Java Class.getName composed keys.
- Java-style `substring(start, end)` uses an exclusive end, whereas CLR
  `Substring(start, length)` uses a length. Keep the distinction explicit.
- BoundedInputStream permits the detecting byte beyond its positive bound,
  then throws IOException. CLR bulk-read EOF is 0 (Java uses -1); a zero-byte
  read must not keep a test loop alive. Both existing stream entry points use
  one implementation, and disposing the wrapper closes the underlying stream.
- Character-sequence slices use offsets relative to their own logical length.
  Added CLR IReadOnlyList operations reject indexes outside that logical length.
- Use Interlocked/Volatile for reference-count state. Retain overflow must leave
  the count unchanged, final release must deallocate exactly once, failed CAS
  must retry, and a zero count cannot be resurrected.
- The BCL Task/thread-pool APIs may replace Java ExecutorService in test
  harnesses. Keep the upstream iteration count, assertions, and synchronization.
  This does not validate Netty's executor implementation.
- Future/Promise listener behavior requires explicit executor/state handling.
  JVM ReferenceQueue, FastThreadLocalThread, Unsafe, native-image substitutions,
  and JVM logging integrations require documented CLR adaptations rather than
  mechanical syntax translation.
- CLR Thread is sealed. FastThreadLocalThread owns a native thread; thread-static
  ownership selects its indexed map, and ordinary threads can enter the explicit
  runWithFastThreadLocal scope. A ConditionalWeakTable supports the deprecated
  cleanup query without retaining completed threads. ThreadGroup identity uses
  weak metadata for explicit groups and creator inheritance through Netty's CLR
  adapters; ordinary external CLR threads have the default logical group. JVM
  SecurityManager and native group permission checks have no CLR counterpart.
- Java erased generic lists can reuse one cache across element types. CLR List<T>
  cannot; changing the requested type clears and replaces the cached list.
- JVM Unsafe availability is false on the CLR. Pointer width, architecture, OS,
  and temporary directory use CLR probes unless a corresponding override exists.
  Remaining native-memory methods are not considered ported by these probes.
- InternalDefaultLogger uses TraceSource; trace/debug share Verbose severity.
  Formatting is culture-independent, booleans use lower case, and byte-array
  values use Java signed-byte text. Exception text uses CLR Exception.ToString.

## Work order

1. ConstantPool, attributes, and reference-count contracts.
2. Character sequences, utilities, and priority queues.
3. Thread-local lifecycle, queues, and thread factories.
4. Future/Promise state, listeners, and combiners.
5. Executors, scheduling, and shutdown/suspend/resume.
6. Timers, Recycler, Cleaner, and leak detection.

Each batch must cite upstream paths, port the upstream tests, add CLR-specific
regressions where translation introduces risk, and record actual test results.
Optimization follows behavioral verification and measured performance.

## Current checkpoint

The opt-in batch passes 642 tests; four tests retain upstream disabling or runtime
capability rules (two ordinary-thread removal cases, the CI-only oversized
allocation case, and SecurityManager group inheritance on an unsupported runtime).
DefaultPromiseTest (20), PromiseCombinerTest (12), and PromiseNotifierTest (5)
now execute alongside PromiseAggregatorTest (6), AbstractScheduledEventExecutorTest
(9), ImmediateExecutorTest (2), GlobalEventExecutorTest (6), DefaultThreadFactoryTest
(4 passed/1 skipped), SingleThreadEventExecutorTest (17),
UnorderedThreadPoolEventExecutorTest (5), NonStickyEventExecutorGroupTest (10),
AutoScalingEventExecutorChooserFactoryTest (7), and the utility,
thread-local, address, and logger tests.
The full default test project still fails to compile because unported Java
syntax remains; the latest full build reports 1,096 compiler diagnostics.

The manifest records 41 reviewed source files and 37 reviewed upstream test
files. Other touched source files remain in progress, including native memory,
address and queue APIs, thread factories, executor submission/suspension/scaling,
scheduling, and logging adaptations. Comment coverage alone is not a completion
count.

Future/Promise completion now owns a separate Netty state machine and exposes a
read-only CLR Task. State transitions, cancellation/uncancellability, cause
identity, FIFO and reentrant listeners, executor affinity, recursion limits,
blocking detection, timeouts, interruption, progressive notification, typed
listener removal, and combiner/cascade propagation are covered by upstream and
CLR contract tests. Java listener wildcards use identity-preserving CLR adapters;
value-type nulls use default(T), and Java wildcard value widening requires
explicit object/boxing with invariant CLR future types.

Java ExecutionException maps to AggregateException; sync rethrows the original
exception. Suppressed exceptions use weak exception keys. CLR cannot inspect a
pending interrupt flag, so incomplete interruptible waits observe it with
Sleep(0); uninterruptible waits restore it. Monitor waits round up to millisecond
resolution. CompleteFuture's interrupt consumption differs from an already
completed DefaultPromise just as in the source, and is tested explicitly.

The upstream JVM stack-overflow depth probe cannot run on CLR because stack
overflow is fatal. Both chain shapes instead run at 20,000 promises in and out of
the event loop. Preserve the original 100,000 listener-order iterations and
4,096 signaling races; bounded waits expose failures instead of hanging.

A batch listener timeout led to reviewing GlobalEventExecutor's quiet task and
ScheduledFutureTask. Fix static initialization order, expired-task consumption,
promise completion and periodic re-registration. Regression tests cover global
idle stop/restart, one-shot results/failures, mixed generic deadline queues,
repetition, cancellation and CLR cancellation-token lifecycle. Scheduling and
executor source entries remain in progress until their original test suites
and all APIs are ported. A single green regression batch does not verify them.

AbstractScheduledEventExecutor is now reviewed against the pinned implementation:
skip cancelled tasks during transfer, restore tasks when the destination queue is
full, retain IDs across periodic reinsertion, preserve virtual submission/validation
hooks, and saturate TimeSpan-to-nanoseconds conversion using integer arithmetic.
The scheduling interfaces inherit Netty Future status/result contracts. CLR tests
exercise the overflow boundary, queue capacity, periodic ordering and hooks.

DefaultThreadFactory retains explicit groups and inherits the current creator's
group when its configured group is null. Descendant tests preserve the original
task count and directly verify identities; JVM permission checks are unavailable.
The SecurityManager-specific test preserves the upstream unsupported-runtime skip.
GlobalEventExecutorTest runs every original case, including the continuously busy
queue and stackless termination failure. An AsyncLocal test reproduced unintended
caller context inheritance; suppressing ExecutionContext flow during global worker
creation/start fixes it while restoring the caller's context.

SingleThreadEventExecutorTest now executes every original test, retaining 10,000
startup/suspend races and 2,000 scheduled cancellation/suspend races. Restore the
unstarted-to-suspended transition, retry a failed startup CAS, and re-engage or
re-request suspension when cancellation races with confirmation. A failing startup
race reproduced zero thread-start requests where the original requires one.
All 128 implementation comments and 57 test comments are preserved.

Submission now uses PromiseTask and returns Netty IFuture through executor/group
interfaces. The old recursive callable generic constraint and QueueingTaskNode
submission path are gone. CLR support implements the inherited JDK invokeAll and
invokeAny contracts, including ordered futures, individual failure retention,
first successful result, timeout/interruption cleanup and cancellation. A completion
queue runs independently of listener dispatch. PromiseTask retains the original
Runnable/Callable distinction, adapter descriptions and completion sentinels.

Single-thread activity accounting and atomic idle/busy cycle counters now follow
the original. CLR contract tests use a mock ticker to check work budgets, accumulated
time, I/O reports, streak reset and concurrent increments. Native thread properties
retain last observed priority/daemon values after termination. Stack capture works
on the owner; remote stack and non-destructive interrupt-flag queries throw explicit
NotSupportedException instead of returning invented data.

The CLR blocking-queue adapter preserves non-destructive peek, atomic removal and
FIFO while chunking large waits. Regressions reproduced destructive peek, missing
removal and TimeSpan overflow. CLR Monitor.Enter can throw on an interrupt during
contention, unlike Java non-interruptible lock entry; an uninterruptible monitor
adapter restores the consumed flag. A full batch exposed this difference during
the original cancellation/suspend race. Queue nonblocking methods, Promise locks
and executor processing locks now preserve that flag; timed queue waits still
observe interrupts. The compatibility collection constructor imports an initial
snapshot, and the adapter owns subsequent queue mutations.

Termination and graceful shutdown now expose the persistent Netty Future through
executor/group interfaces. Existing Task convenience methods view that same
completion. Multithread groups aggregate child Future listeners on GlobalEventExecutor,
including failed children, and their iteration cannot expose the mutable backing set.
Restore inherited shutdownNow, Java method overriding, default shutdown periods and
observable chooser metric forwarding. CLR readonly interfaces represent immutable
collection views; nested chooser/metric support types are standalone files. Preserve
the metric's atomic raw-double bits, including NaN payloads. Nineteen lifecycle/group
contracts cover failure identity, listener affinity, construction cleanup and forwarding.

NonSticky runners now return after rescheduling or emptying their queue and restore
executingThread when rescheduling fails. C# forbids a return inside finally, so a
flag defers those original returns until the block exits. An unbounded ConcurrentQueue
adapter replaces the missing JCTools factory branch; CLR segment allocation differs
from JCTools chunks, and bounded/chunked queue factories remain pending. All ten
original test cases retain their four batch sizes, 10,000 tasks per producer and
5,000 two-submission races. All 30 source and 12 test comments are preserved.

UnorderedThreadPoolEventExecutor has a CLR adapter for the inherited JDK scheduled
pool defaults, using dedicated workers and a shared deadline queue. Reviewed Netty
decoration and JDK scheduling/shutdown against local Corretto 21.0.11 src.zip.
All five original tests and 34 CLR contracts pass. Preserve its unusual
termination Future: it succeeds when shutdown is requested, before workers stop.
Default shutdown retains delayed one-shot work, drops periodic work, and shutdownNow
returns queued futures without completing them. The pinned Runnable decoration does
not query the backend failure: one-shot outer promises can succeed after backend
failure, and periodic backend failure stops repetition while the outer promise stays
pending. Callable decoration does query the result and unwraps the original failure.
These source-derived behaviors are tested explicitly. All 21 source and 8 test
comments are preserved; complete inherited JDK API review remains in progress.

Inherited bulk invocation uses a separate JdkFutureTask: running callables remain
cancellable, unlike the decorated Netty PromiseTask. Cancellation waits for interrupt
delivery before a worker can run its next task. Pool configuration now covers core
resize/timeout, keep-alive, native queue removal/reinsertion, shutdown policy changes,
statistics and thread-factory failure/null/reentrant reservations. The inherited
factory setter deliberately keeps the supplied factory unwrapped, as in the pinned
source. A periodic task claimed before a policy change can cancel only its backend,
leaving the outer Netty promise pending; a separate deterministic regression verifies
that behavior rather than asserting cancellation for every race outcome.

The auto-scaling factory now preserves CAS snapshots, pre-increment patience counters,
ramp limits, rotating wake-up selection, minimum/maximum bounds, registered-channel
guards, live immutable metric views and the termination listener. Seven original
tests and eight mock-clock contracts pass. All 43 source and 17 test comments are
preserved. TimeSpan replaces Java duration/TimeUnit; its 100ns granularity means a
saturated monitoring period is rounded down by at most 99ns when scheduled.
Protected-internal metric hooks preserve Java protected package access. The allocated
queue constructor initializes the activity timestamp; updateLastExecutionTime also
refreshes it. The explicit-queue constructor keeps the pinned zero-initialized field.
Seven CLR metrics/property tests cover these contracts.

A very large fixed-rate period reproduced ordinary work blocked behind a future
deadline. Signed-difference comparison, wraparound deadlines and adjustment for an
overdue queue head now follow the JDK clock arithmetic. Global awaitInactivity follows
Java's millisecond truncation and zero/unbounded join while chunking waits beyond the
CLR Int32-millisecond limit; zero/submillisecond and interrupted TimeSpan.MaxValue
regressions pass.

Next, finish the remaining executor/platform API reviews and continue the pending
common source and upstream tests recorded in the manifest. The complete module and
default test build remain unfinished; this is a verified porting checkpoint.
ThrowableUtil still cannot replace an already-thrown CLR stack or
capture another managed thread's stack; those limitations remain explicit.
