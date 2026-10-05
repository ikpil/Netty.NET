# Netty common port

Upstream: `e66ce34777f9c4a0c57ac74bb97396ca2f54b43c` (local `../netty`).
Scope: `common/src/main/java` and `common/src/test/java` at that commit.

## Verification

Default builds include all C# test sources. Eight JVM-only test placeholders were
removed, with file-by-file reasons in the manifest and original comments in
[JVM test exclusion provenance](common-jvm-test-exclusions.md):
JFR recording, virtual-thread checks, JNI ClassLoader loading, and SLF4J/Log4j provider
tests. The default solution now builds in Debug and Release and executes the full
test project. The native scheduling and memory checkpoints now pass both full
configurations, with the remaining reviewed/unreviewed work listed below. Do not interpret
the existence of a C# file, a successful library build, or a batch test result
as completion of the module.

Run `dotnet test Netty.NET.sln` for the full suite in Debug, and add
`-c Release` for Release. Use `--filter` for focused test execution; it does not
exclude source files from compilation. The temporary PortingBatch.props and its
project import were removed after all portable tests built by default. Older
PortingBatch commands below and in the manifest are historical verification
records. Full-suite success does not establish completion of the native API port.

`common-porting-manifest.json` inventories all upstream Java source/test files.
Candidate paths are filename matches, not a claim of equivalent behavior.
Pending entries must be reviewed; justified replacements must state the CLR
behavior and the original contract being preserved.
`clr-replacement` records a standard CLR substitute; `not-applicable` records an
implementation or test that depends on a JVM-only facility. These are explicit
decisions, separate from reviewed C# source/test counts.
Regenerate the inventory with `pwsh tools/Update-CommonPortingManifest.ps1`;
recorded reviews are preserved.

Check original comment coverage with
`pwsh tools/Test-CommonCommentCoverage.ps1 -UpdateManifest`. The audit reads the
pinned Git objects, tokenizes comments separately from string literals, and
compares comment text and multiplicity while ignoring whitespace. Markdown
provenance is tokenized within each Java/C# code fence; prose quotes cannot
swallow archived comments as source strings. Passing this
check does not establish behavioral compatibility or correct comment placement;
both still require review.

Comment sources are read as UTF-8 explicitly, including on Windows PowerShell 5.

## Translation rules

- Port Netty's useful behavior to CLR, using native generic types, collections,
  reflection, memory and threading facilities. Do not reproduce JVM-only machinery
  or a private Java data structure when a CLR facility fulfills its purpose.
- Preserve every upstream license header, documentation comment, and implementation
  comment in code that is actually ported. Keep original comments alongside explicit
  CLR adaptation notes. A documented framework replacement or JVM-only exclusion has
  no mechanical C# counterpart. Preserve its original comments with source
locations in replacement provenance, without inventing classes to host them.
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
  cannot; actual CLR consumers own native lists and final strings. The unused
  map scratch list/builder cache and capacity properties were retired.
- JVM Unsafe availability is false on the CLR. Pointer width, architecture, OS,
  and temporary directory use CLR probes unless a corresponding override exists.
  Remaining native-memory methods are not considered ported by these probes.
- InternalDefaultLogger uses TraceSource; trace/debug share Verbose severity.
  Formatting is culture-independent, booleans use lower case, and byte-array
  values use Java signed-byte text. Exception text uses CLR Exception.ToString.

## Work order

The current objective requires a CLR design review of previously translated
components before continuing mechanical API translation. See
[common-clr-design.md](common-clr-design.md) for original consumers, retained
contracts, native decisions and the remaining migration gates. File-level
verification below does not establish completion of that public API review.

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

The current default suite executes **2157 cases** on Windows/x64/net10.0:
**2143 passed / 0 failed / 14 skipped** in Debug and Release.
Evidence: unordered-stop-snapshot-full-debug.trx and unordered-stop-snapshot-full-release.trx.
All 2154 prior identities/outcomes and 759 original non-Porting results remain
unchanged. Three new CLR rows verify atomic pending membership withdrawal before
native cancellation reentry and mixed raw/submitted/scheduled/periodic/ordered-child
result settlement while workers and asynchronous stop callbacks drain. No identity
remap, exclusion or skip change.

Unordered StopCore clears the BCL heap once before canceling its owned snapshot;
native removal hooks no longer search past other canceled/raw entries. The existing
stop notification reservation prevents premature Termination during cancellation
reentry. Sole Task results, raw callback release, worker/factory ownership, failure
aggregation, caller context and cooperative token policies remain.
See common-clr-design.md#immediate-stop-snapshot-withdrawal for pinned Netty/JDK
behavior and actual original consumers. The boundary regression fails before repair
(four remaining entries) and passes after withdrawal.

Targeted Debug and checked Release each pass 277 cases, zero failed/skipped.
All 271 comment rows have no coverage loss; the changed source retains 21/21 original
comments, original pool fixture and earlier provenance remain. Pinned 205 source/66
test inventory and paths, uppercase declarations and no-new-source/test-warning
identities pass. Identical non-friend native consumers preserve mixed cancellation,
callback reentry/drain/failure identity, independent token ownership and persistent
actual termination before/after.

With 98304 pending entries (65536 cached raw callbacks/32768 native submissions),
synchronous StopAsync median is 4762.5729 -> 4.5746 ms. Five samples at each
of five sizes/version, two small warmups, Release/tiering disabled and a workerless
real pool. Every sample settles results and actually terminates. Setup/admission,
worker drain, asynchronous notification execution and final observation are outside
timing; exact rows/current-thread allocation and limitations are documented in the
design. No general throughput/graceful-periodic/user-callback latency claim.
Ignored evidence: unordered-stop-snapshot-*.trx/JSON in TestResults and
artifacts/unordered-stop-snapshot-validation/{before,after}-{consumer,perf}.log,
stop-cost-evidence.json. Only three canonical records updated, no new MD.

Source statuses stay 58 verified / 50 CLR replacement / 15 not applicable /
66 pending / 16 in progress. Next: review graceful periodic withdrawal costs and
remaining native submission/Runnable test bridges, then other backend/source/native/
platform contracts. The earlier focused Global xUnit completion stall remains
unresolved; current matrices completed. Whole common remains open.

The auto-scaling monitor now coalesces callbacks within configured fixed-rate
window boundaries without sampling/resetting activity or patience repeatedly.
Actual elapsed duration remains the utilization denominator, with a valid zero
timestamp and signed clock wraparound. Five independent clock cases fail before
repair; a sixth catches phase drift in a rejected elapsed-only approach.
Fixed delay was also rejected after a trace exposed 62-64ms intervals aliasing
35ms activity reports and breaking original scale-up scenarios. Final coupled
Debug passes 97 cases, including all seven unchanged original auto-scaling fixtures.
See [monitoring window decisions](common-autoscaling-monitor-windows.md).
The controlled defect does not establish the cause of the retained real-time
failure; no original workload, wait, threshold, assertion or comment is changed.
Final full Debug/Release each pass 1372 cases with zero failures and the same 14
skips. All 759 non-Porting identities and all skip identities remain unchanged;
only six CLR clock/phase cases are added, with matching Debug/Release outcomes
(autoscaling-window-identity-comparison.json). All 98 verified comment entries,
43 factory and 17 original fixture comments have zero missing; inventory matches
the pinned 271 files and all implementation paths exist
(autoscaling-window-comment-audit.json).

The unordered inherited configuration and statistics API is retired after a pinned
all-module search finds no corresponding Netty consumers. Constructor worker limits,
factory and raw rejection delegate are immutable; PendingTaskCount, WorkerCount and
ActiveWorkerCount provide momentary diagnostics. Admission closure retains accepted
one-shots and stops periodic reentry; callers withdraw owned work with tokens.
Fifteen CLR-only methods and two theory row identities are explicitly remapped in
[native configuration decisions](common-unordered-native-configuration.md).
The initial selection fails one new probe that incorrectly assumes cancellation
of a currently running periodic Task at closure; the corrected probe waits for
the invocation to return. Final affected Debug passes all 233 cases. Full Debug
and Release each pass 1372 / fail zero / skip 14, with all 759 original identities
and skip identities unchanged and matching outcomes between configurations
(unordered-native-configuration-identity-comparison.json). All 98 verified comment
entries have zero missing; all 271 pinned files are inventoried and implementation
paths exist (unordered-native-configuration-comment-audit.json). Worker replacement
failure is subsequently repaired in common-unordered-worker-failure.md, and concrete
immediate stop in common-unordered-cooperative-stop.md. Private queue costs and
shared/group immediate API review remain open.

The CLR unordered worker failure boundary now contains replacement creation/start
exceptions instead of terminating the process. Isolated pre-repair throw/start
probes exit -532462766; null replacement leaves accepted work pending. Waiting
native operations and notification/NonSticky reservations now receive the backend
failure, and Termination faults after surviving workers drain. An escaped native
invocation keeps its own failure; successful replacement keeps the pool available.
Factory reentrant closure/throw also failed before repair: retaining the retiring
loop until the replacement outcome prevents premature lifecycle success. Eight CLR
rows and four isolated process modes pass; see
[worker failure decisions](common-unordered-worker-failure.md).
Final full Debug/Release each pass 1380 / fail zero / skip 14. All 759 non-Porting
and skip identities are unchanged; only eight CLR rows are added and both
configurations have matching names/outcomes (unordered-worker-failure-identity-comparison.json).
The 98 verified comment entries have zero missing; 21 unordered source, eight
original test and ten group-contract comments remain, with all 271 pinned files
inventoried and implementation paths present (unordered-worker-failure-comment-audit.json).
Both full runs use --artifacts-path artifacts/worker-failure-validation because
Rider holds the ordinary Debug test DLL; its process is preserved. Windows/net10.0
remains the verified scope, existing compiler/analyzer warnings remain, and no
test source exclusion or new skip is introduced. Concrete immediate stop is subsequently
implemented below. Shared/group immediate API and private queue costs remain open,
along with the 99 pending source decisions.

The concrete unordered executor now exposes StopAsync and StopToken. Running operations
explicitly opt into cancellation; accepted waiting work is withdrawn, and actual workers,
factory starts and asynchronous cancellation notifications drain before Termination.
Callback failures remain observable together with any backend failure. Legacy shutdownNow
forwards to this policy. A separate-process before/after comparison reproduces and removes
an interrupt escaping into custom factory suffix code; the revised Debug and Release
probes exit zero. All four worker-failure process modes still pass in both configurations.
See [cooperative stop decisions](common-unordered-cooperative-stop.md).
The expanded affected Debug selection passes 153 cases. Final whole Debug/Release each
pass 1395 / fail zero / skip 14; all 759 non-Porting and all skip identities are unchanged,
only 15 CLR rows are added, and names/outcomes match between configurations
(unordered-stop-identity-comparison.json). One existing CLR-only probe now asserts
explicit StopToken cancellation in place of injected interruption; its identity and
queue-handle checks remain. No original Java fixture is changed. All 98 verified comment
entries and the 21 unordered, eight original fixture and ten group comments have zero
missing; all 271 pinned inventory entries and implementation paths match
(unordered-stop-comment-audit.json). Both whole runs use --artifacts-path
artifacts/cooperative-stop-validation to preserve the Rider process holding the ordinary
Debug DLL. Existing compiler/analyzer warnings remain; only Windows/net10.0 is verified.
Source decisions remain 42 verified, 29 CLR replacements, 13 exclusions, 99 pending and
22 in progress; all 66 original test files have decisions (56 verified, ten exclusions).
These counts do not measure remaining effort or establish native design completion.
The subsequent private queue cost and shared/group stop reviews are recorded below.
Remaining
collections, strings/encoding, platform and ownership reviews stay within the full goal.

The unordered private membership path now uses net10.0 PriorityQueue.Remove with
ReferenceEqualityComparer.Instance. It preserves stored priorities and the owner gate,
without copying/clearing/reinserting every survivor on cancellation. Independent
candidate checks verify ties, compact signed-clock wrap, hostile value equality,
repeated removal and survivor order. BCL heap and SortedSet tradeoffs are measured:
the tree is faster for large scattered removals; heap capacity reuse avoids node
allocation and retains the existing deadline model. Actual native cancellation among
4096 pending schedules reduces current-thread allocation from 98180.8 to 80 bytes/entry.
Two sequential series retain medians/extrema and timing variation; no scheduler-wide
speedup, additional OS/TFM or contention claim is made. See
[queue cost decisions](common-unordered-queue-costs.md) and common-unordered-queue-costs.csv.
Final affected Debug passes 175 / fails zero / skips zero. Default whole Debug/Release
each pass 1398 / fail zero / skip 14; all 759 non-Porting and 14 skip identities remain,
only three CLR rows are added and both configurations' names/outcomes match
(unordered-queue-remove-identity-comparison.json). All 98 verified comment entries and
the 21 unordered source/eight original fixture/ten group comments have zero missing;
all 271 pinned inventory entries and implementation paths match
(unordered-queue-remove-comment-audit.json). Separate --artifacts-path
artifacts/queue-removal-validation preserves Rider's DLL ownership. Existing warnings
remain. Shared/group immediate API is subsequently implemented below; other queue
consumers, workload contention and the remaining 99 pending/22 in-progress sources stay open.

Shared `IEventExecutorGroup.StopAsync` now requests the native stop policy and
returns persistent Termination. Ordered workers close admission and drain accepted
invocations, including escalation from graceful quiet admission; unordered workers
withdraw waiting work and signal their explicit StopToken. NonSticky groups and
selected children forward the owner's policy. Multithread groups request every
child despite synchronous request failures and retain the pinned all-child
completion count. Global/Immediate return their existing failed lifecycle Task.
See [native stop decisions](common-clr-design.md#native-stop-across-executors-and-groups).
Disabling only native escalation reproduces one admission-closure failure among
two theory rows; the direct-stop row passes, and both pass after restoration.
Initial affected Debug passes 92 cases. Final full Debug/Release each pass 1405 /
fail zero / skip the same 14. All 759 non-Porting identities remain; only seven
CLR rows are added, with matching configuration names/outcomes
(group-stop-identity-comparison.json). All 98 verified comment entries plus the
affected source entries (101 distinct entries) have zero missing, and all 271
pinned inventory entries and implementation paths match
(group-stop-comment-audit.json and group-stop-inventory-summary.json). Both full
runs use --artifacts-path artifacts/group-stop-validation to preserve Rider's DLL
ownership. Existing warnings remain, with Windows/net10.0 as the verified scope;
no original Java fixture/comment, test source exclusion or skip is added/changed.
Source decisions remain 42 verified / 29 CLR replacements / 13 exclusions /
99 pending / 22 in progress; original test files remain 56 verified / ten exclusions.
Ordered queue membership/lifetime and its pinned consumers are subsequently
reviewed below. Remaining strings/encoding, platform and resource ownership review
is open.

The indexed priority queue core/interfaces are now reviewed. HTTP/2 mutable
priorities and independent per-queue indices justify retaining the indexed heap;
ordinary value entries use BCL PriorityQueue. Membership now checks reference
identity, general linear-scan fallback is removed, negative capacity uses the CLR
argument exception, and BCL array resizing owns capacity. Scheduler shutdown clears
references/indices rather than assuming the queue is about to be garbage collected.
The retained real-executor weak-work probe fails before and passes after. Other
before failures establish the explicit CLR identity, admission and exception
decisions; they are not all claimed as original runtime defects. Original eight
queue scenarios remain unchanged, with 1024 independent sorted-reference changes
and dual-owner membership coverage. Affected Debug passes 69 cases. Final full
Debug/Release each pass 1411 / fail zero / skip the same 14, with all 759 non-Porting
identities retained and exactly six CLR rows added; configuration names/outcomes
match (indexed-queue-identity-comparison.json). All 101 verified comment entries
plus ScheduledFutureTask (102 distinct entries) have zero missing, including the
two restored PriorityQueue interface comments. Inventory matches all 271 pinned
Java files and implementation paths (indexed-queue-comment-audit.json and
indexed-queue-inventory-summary.json). Isolated --artifacts-path
artifacts/indexed-queue-validation preserves Rider; existing warnings remain and
only Windows/net10.0 is verified.
Two independent post-suite processes compare indexed/BCL/tree representations,
including ownership and wrap checks before measurement. The indexed representation
avoids scanned removal and tree insertion allocation, while the BCL heap is faster
for reused fill/drain in this bounded workload. Conditions, limits, all extrema and
allocation results are in the existing
[CLR design](common-clr-design.md#ordered-scheduler-and-indexed-queue-ownership)
and common-indexed-queue-costs.csv. No overall scheduler throughput claim is made.
Source decisions: 45 verified / 29 CLR replacements / 13 exclusions / 97 pending /
21 in progress; original tests remain 56 verified / ten exclusions. Two subsequent
framework replacements are recorded below.

ConcurrentSet and ReadOnlyIterator now have explicit CLR replacement decisions.
The former has no pinned consumer outside its deprecated class; use framework
concurrent membership rather than a Java AbstractSet/Serializable facade. The
latter's transport group/pool consumers need traversal without Iterator.remove,
already supplied by CLR IEnumerable/IEnumerator. Same-named HTTP nested iterators
are distinct, and this decision does not claim to port the transport consumers.
The existing common child-enumeration contract supplies the local ownership probe.
See [replacement decisions and all original comments](common-clr-design.md#clr-replacements-for-concurrent-sets-and-read-only-iterators).
This unit changes documentation/manifest only; the full Debug/Release runtime
matrix above remains applicable without repeating unchanged tests. The two
focused comment audits report 3/3 and 1/1 preserved occurrences (one identical
license block is attributed to both originals), and all 271 inventory paths match
(concurrent-set-comment-audit.json, readonly-iterator-comment-audit.json and
collection-replacement-inventory-summary.json). No class, no-op, runtime test or
source exclusion is added. At this checkpoint source decisions were 45 verified / 31 CLR
replacements / 13 exclusions / 95 pending / 21 in progress. The next review was the
Ticker/SystemTicker/MockTicker time-source boundary against CLR timing/provider
facilities and pinned scheduler consumers, then continue remaining collections,
strings/encoding and platform/ownership review.

The monotonic-time review repairs native duration precision/overflow, nonpositive
system sleep consuming pending interrupts, and narrowing of long system waits.
Seven of the ten initial CLR regression rows fail before repair. Shared integer
conversion now saturates durations; raw TimeProvider.System timestamps scale with
integer arithmetic and signed wrap. System TimeSpan/long-millisecond waits preserve
their full duration and remain interruptible across Int32-millisecond chunks. See
[the time-source decision](common-clr-design.md#monotonic-time-and-duration-conversion).
The expanded affected Debug selection passes 151 cases. Whole Debug/Release each
pass 1427 / fail zero / skip the same 14 (1441 discovered); all 759 non-Porting and
skip identities remain, only 16 CLR ticker rows are added, and both configurations
have matching identities/outcomes (ticker-identity-comparison.json). BigInteger
oracles cover timestamp scaling/overflow independently. No original fixture,
test exclusion or skip is changed.

All 104 verified comment entries plus ScheduledFutureTask and DefaultMockTicker
(106 distinct entries) have zero missing. Three altered documentation blocks are
restored verbatim; all 17 ticker source and eight original mock-test comments remain.
All 271 pinned source/test entries and implementation paths match
(ticker-comment-audit.json, ticker-inventory-summary.json). Both full runs use
--artifacts-path artifacts/ticker-validation to preserve Rider's ordinary Debug
DLL. Existing compiler/analyzer warnings remain; Windows/net10.0 is the verified
scope. Source decisions: 48 verified / 31 CLR replacements / 13 exclusions /
91 pending / 22 in progress; original tests: 56 verified / ten exclusions.
Ticker/MockTicker behavior is verified within this scope, while their native design
review remains in progress with the controlled mock. This does not establish
module completion or arbitrary TimeProvider injection. Next: resolve DefaultMockTicker
FIFO/fairness and repeated sleep-phase observation, native atomic/reference-set
ownership and noninterruptible advance lock entry, then finish the shared native
clock-selection decision against the original embedded/manual event-loop consumers.

The subsequent controlled-mock review reproduces stale sleep-phase observation:
128 advances can repeatedly see the same old registration, leaving the final sleep
unfinished. A separate probe compiles the unchanged pinned clock/ObjectUtil Java
sources and passes 20 x 128 phases on Corretto 21.0.11. Contended CLR monitor entry
also reproduces interruption aborting advance, unlike the original noninterruptible
ReentrantLock.lock. The expanded before-run fails those two rows and passes two.
The native fix uses Interlocked, reference-identity HashSet membership and a reusable
linked FIFO tick queue. Existing sleepers acknowledge each tick before new phases,
observers or another advance pass; interrupted registrations release their pending
node in finally. Advance preserves consumed interrupts for the next interruptible
wait. This implements clock wakeup policy, not a general fair CLR mutex or FIFO
application-code execution. See the time-source decision above.

The affected Debug selection passes 155 cases. Final whole Debug/Release each
pass 1431 / fail zero / skip the same 14 (1445 discovered). All 759 non-Porting and
skip identities remain; only four CLR phase/clock rows are added and both
configurations have matching identities/outcomes (mock-ticker-identity-comparison.json).
All 105 verified comment entries plus ScheduledFutureTask (106 distinct entries)
have zero missing, including all four mock source comments; all 271 pinned entries
and implementation paths match (mock-ticker-all-comment-audit.json,
mock-ticker-inventory-summary.json). Validation remains Windows/net10.0, with the
same isolated artifact path and existing compiler/analyzer warnings. No new skip,
source exclusion or feature MD file is introduced. Source decisions: 49 verified /
31 CLR replacements / 13 exclusions / 91 pending / 21 in progress; original tests
remain 56 verified / ten exclusions. DefaultMockTicker's reviewed clock-policy
behavior/native design is verified within this scope. Shared native clock-selection
API review remains next, followed by the outstanding collection, encoding,
platform and ownership decisions. Overall common completion remains open.

Native ordered clock selection now accepts TimeProvider through Ticker.FromTimeProvider,
the protected scheduled/core executor constructors and DefaultEventExecutor(TimeProvider).
System input retains its singleton/epoch; custom input captures native frequency/origin
and converts the signed tick difference before scaling. This preserves fractional
precision and native timestamp wrap. Original embedded/manual transport consumers
establish explicit pump/wakeup and unsupported-sleep contracts, so provider advancement
makes work due and executor dispatch owns callback execution. Provider wall time/timers
are not used. See the native provider selection decision in common-clr-design.md.

Eleven new CLR rows cover that boundary and actual dedicated-worker affinity; the
affected Debug selection passes 166. Whole Debug/Release each pass 1442 / fail zero /
skip the same 14 (1456 discovered). All 759 non-Porting and skip identities remain,
only eleven provider rows are added and configuration names/outcomes match
(provider-clock-identity-comparison.json). All 105 verified comment entries plus
ScheduledFutureTask/SingleThreadEventExecutor (107 distinct entries) have zero missing;
the changed seven original clock/executor sources retain all 175 comments. All 271
inventory entries and implementation paths match (provider-clock-comment-audit.json,
provider-clock-inventory-summary.json). Both full runs use --artifacts-path
artifacts/provider-clock-validation to preserve Rider's ordinary Debug DLL.
Windows/net10.0 is the verified scope; existing compiler/analyzer warnings remain.
Ticker/MockTicker native clock-policy selection reviews are now verified within this
scope. The ordered worker/backend review remains in progress, and fixed-system
unordered provider injection and transport implementations are not claimed.
Source decisions remain 49 verified / 31 CLR replacements / 13 exclusions /
91 pending / 21 in progress; original tests remain 56 verified / ten exclusions.
Next: review primitive supplier interfaces against native Func delegates and actual
transport consumers, then continue remaining collection/encoding/platform decisions.

Primitive supplier contracts now use CLR Func<bool>/Func<int>. C# has no checked
exception signature distinction; a delegate may still throw and consumers retain
their lazy/short-circuit and exception boundaries. The two unused C# interfaces
and two constant helper classes are removed. The three original Java types are
recorded as framework replacements, with all thirteen original comment occurrences
archived in common-clr-design.md. Transport selector/receive consumers were checked;
their implementation is outside this common decision. The similarly named JDK
supplier used by MpscIntQueue is a separate source/queue review. No additional
supplier facade, BCL-only invocation test, source exclusion or skip is introduced.

Following those four source deletions, full Debug/Release each pass 1442 / fail zero /
skip the same 14 (1456 discovered); every test identity/outcome is retained from the
provider checkpoint, with no additions/removals (supplier-identity-comparison.json).
All 105 verified comment entries plus ScheduledFutureTask/SingleThreadEventExecutor
and the three suppliers (110 distinct entries) have zero missing. All 271 original
inventory entries and implementation paths match (supplier-comment-audit.json,
supplier-inventory-summary.json). Both full runs use --artifacts-path
artifacts/supplier-validation to preserve Rider's ordinary Debug DLL. Windows/net10.0
remains the verified scope, and other compiler/analyzer warnings remain. Source
decisions are 49 verified / 34 CLR replacements / 13 exclusions / 88 pending /
21 in progress; original tests remain 56 verified / ten exclusions. Next: migrate
the Java HashingStrategy alias/default helper to native IEqualityComparer<T>,
preserving specialized ASCII comparison/hash and actual header/map consumer contracts.
Overall common completion remains open.

HashingStrategy now maps directly to IEqualityComparer<T>, with
EqualityComparer<T>.Default for default typed consumers. The unused default
helper and Java alias interface are removed; AsciiString's two comparers expose
GetHashCode/Equals directly and retain the pinned specialized ASCII algorithms.
Header/map consumers require ASCII-only folding, mixed sequence hashes and
deliberate sensitive-comparer collisions. This does not replace duplicate/ordered
header storage with a Dictionary. Native collections reject null Dictionary keys
and permit null HashSet elements. Mutable keys must be removed before mutation;
shared AsciiString backing arrays require arrayChanged before reinsertion.
Original HashingStrategy comments and the framework decision are recorded in
common-clr-design.md, with no new feature document or test exclusion.

Affected Debug selection passes all 120 cases, including twelve new collection
contracts and an independent exhaustive ASCII folding reference for all 65,536
Latin-1 pairs. Full Debug/Release each pass 1454 / fail zero / skip the same 14
(1468 discovered). All prior case identities/outcomes, including the 759
non-Porting cases, remain unchanged; only those twelve contracts are added
(comparer-identity-comparison.json). All 112 reviewed comment entries have zero
missing: HashingStrategy preserves five and AsciiString preserves 99. All 271
inventory entries and implementation paths match (comparer-comment-audit.json,
comparer-inventory-summary.json). Runs use --artifacts-path
artifacts/comparer-validation, Windows/net10.0, SDK 10.0.203/runtime 10.0.7.
Source decisions are 49 verified / 35 CLR replacements / 13 exclusions /
87 pending / 21 in progress; original tests remain 56 verified / ten exclusions.
Next: review CharsetUtil and actual encoding consumers against CLR Encoding,
fallback/BOM and encoder/decoder state ownership. Broader AsciiString parsing,
sequence API, runtime policies and the common module remain in progress.

CharsetUtil now maps to native Encoding/EncoderFallback/DecoderFallback. Its
unused Java aliases/factories and the two otherwise unused InternalThreadLocalMap
codec caches are removed; the original encoding test loops still execute six
native configurations with all assertions/iterations/comments retained. The old
two-action overload silently ignored one policy; no replacement facade preserves
that defect. Actual AsciiString constructors already use selected Encoding with
span-based one-shot conversion. Java UTF-16 BOM/endian detection, UTF-8 default
replacement and single-byte unmappable surrogate handling differ from CLR and
are explicit consumer policies, established by pinned Java and independent CLR
oracles (encoding-java-oracle.txt, encoding-clr-oracle.txt). Future buffer/codec
framing and operation-owned streaming implementations are outside this decision.
See common-clr-design.md and common-ascii-memory.md; no new feature MD is added.

Seventeen native constructor cases cover literal byte references across input
forms/ranges, byte order/preamble, custom/strict fallback, failure isolation and
absence of hidden thread-local codec state. Affected Debug passes 211 / fails zero /
skips the three unchanged guarded/disabled thread-local cases (214 discovered).
Full Debug and final Release each pass 1471 / fail zero / skip the same 14
(1485 discovered). All prior identities/outcomes and all 759 non-Porting cases
are retained, with only seventeen new cases (encoding-identity-comparison.json).
The first full Release instead passed 1470 / failed one / skipped 14: unchanged
testScaleUpDoesNotExceedMaxThreads reported scaling down under high load. That
result/log is retained as encoding-full-release-first.trx/.log with
encoding-first-release-summary.json. Without source/threshold/assertion changes,
the coupled auto-scaling/native-encoding Release selection passes 40 and the
same Release build's full rerun passes. As with the earlier retained failure in
common-autoscaling-monitor-windows.md, successful reruns do not establish its
cause or timing stability. This unit does not claim an auto-scaling repair.

All 114 reviewed comment entries have zero missing: CharsetUtil 16, AsciiString
99, InternalThreadLocalMap 20 and original character test 24. All 271 pinned
inventory entries and implementation paths match (encoding-comment-audit.json,
encoding-inventory-summary.json). Validation uses --artifacts-path
artifacts/encoding-validation, Windows/net10.0, SDK 10.0.203/runtime 10.0.7.
Source decisions are 49 verified / 36 CLR replacements / 13 exclusions /
86 pending / 21 in progress; original tests remain 56 verified / ten exclusions.
Next: trace the recurring auto-scaling real-time failure with actual clock/I/O
report evidence, then continue AsciiString integer parsing/radix/overflow review.
Full native common completion remains open.

The resumed-worker review reproduces the unchanged real-time auto-scaling scenario
on its seventh invocation in an isolated tracing harness. A 47.0048ms first
post-resume interval (50ms configured) consumes idle patience; two later empty
samples request suspension 2.9942ms before the first I/O report arrives. Executing
the pinned Java decision block with those recorded inputs yields the same decision.
The chooser now publishes reference-keyed activation times with immutable CAS
membership snapshots. Before one configured period has elapsed since resume,
actual metrics still sample/reset/publish, while idle/busy patience stays reset.
Rebuild preserves activation epochs; another resume replaces the old epoch. Once
eligible, thresholds, pre-increment patience, phase, bounds and channel guards
retain their behavior. No activity is invented and no original fixture is changed.
See common-autoscaling-monitor-windows.md; no new feature MD is introduced.

Six new controlled cases fail baseline 8d16bf2 and pass after repair, covering the
short-window/report pattern, valid zero time, signed clock wrap, genuine low load,
partial-window metric publication and a second activation. The affected Debug
selection passes 131 with no failures/skips; the trace-enabled original scenario
passes forty repetitions. The probe/tracing code remains only in ignored artifacts.
Full Debug/Release each pass 1477 / fail zero / skip the same 14 (1491 discovered).
All prior case identities/outcomes and all 759 non-Porting cases remain unchanged,
with only six new contracts (autoscaling-resume-identity-comparison.json).
All 114 reviewed comment entries have zero missing, including the factory's 43
and original fixture's 17; all 271 pinned inventory paths match
(autoscaling-resume-comment-audit.json, autoscaling-resume-inventory-summary.json).
Additional evidence: autoscaling-resume-baseline.trx,
autoscaling-resume-trace-summary.json, autoscaling-resume-java-decision.txt,
autoscaling-resume-fixed-probe.trx. Validation uses --artifacts-path
artifacts/autoscaling-load-validation/current-build, Windows/net10.0,
SDK 10.0.203/runtime 10.0.7. Forty traced passes do not prove arbitrary timing
stability or establish the cause of earlier untraced failures. Source decisions
remain 49 verified / 36 CLR replacements / 13 exclusions / 86 pending /
21 in progress; original tests remain 56 verified / ten exclusions.
Next: continue AsciiString integer parsing, radix/sign/range and overflow review
against pinned implementations and actual header/value-converter consumers.
The remaining native common/runtime and future transport integration stay open.

### Native AsciiString integer parsing checkpoint

Pinned AsciiString.java:1203-1336, original common fixtures and actual
CharSequenceValueConverter/HttpResponseStatus consumers reviewed. Native
ParseInt16/32/64 and TryParse replace the Java-named numeric methods; the current
CharUtil consumer uses ParseInt64. A single bounded byte-span/generic-math core
retains radix 2..36, minus-only sign, ASCII digit grammar and exact signed limits.
It rejects invalid logical ranges before reading and checks arithmetic bounds
before multiplication/subtraction. CLR argument/format/overflow exception choices,
Java's accidental invalid-range outcomes and the BCL-substitution decision are
recorded once in common-clr-design.md. Integer methods have no original comments;
all 99 original AsciiString comments remain beside their implementations.

Six logical-range regressions fail before repair and pass afterwards. The 72 new
contracts cover all radices and Latin-1 bytes, signed boundaries/overflow, empty
and malformed text, nonzero backing offsets, current-culture independence,
zero-on-failure TryParse and the existing native consumer. Affected Debug selection
passes 206; Release with CheckForOverflowUnderflow=true passes all 72. An isolated
Java harness executes the exact pinned parser methods with only storage/view
dependencies stubbed: 60960 inputs match native numeric results/failure and TryParse
outcomes. The separate invalid-range oracle records deliberate CLR differences.

Full Debug/Release each pass 1549 / fail zero / skip the same 14 (1563 discovered).
All prior 1491 identities/outcomes and all 759 non-Porting cases are retained;
only the 72 native numeric cases are added. Comment missing counts match the
previous audit for all 271 entries; pending/unreviewed comment gaps remain open.
All pinned inventory entries and recorded candidate/implementation paths match.
Evidence: ascii-integer-before.trx, ascii-integer-affected-debug.trx,
ascii-integer-checked-release.trx, ascii-integer-full-debug.trx,
ascii-integer-full-release.trx, ascii-integer-identity-comparison.json,
ascii-integer-comment-audit.json, ascii-integer-inventory-summary.json,
ascii-integer-java-clr-oracle.txt, ascii-integer-java-invalid-ranges.txt.

Allocation regression: zero bytes across 6000 successful warmed Parse/TryParse
calls. An isolated Release comparison to the prior parser (four decimal/hex
inputs, three widths, three rounds of 3M parses, tiered compilation disabled)
also records zero allocation for both implementations. Actual timings/checksums
are retained in ascii-integer-benchmark.txt; this bounded smoke measurement is
not a general performance guarantee. Validation uses --artifacts-path
artifacts/ascii-integer-validation/build, Windows/net10.0, SDK 10.0.203/runtime
10.0.7. Probe/harness sources stay in ignored artifacts; no new feature MD.
Source decisions remain 49 verified / 36 CLR replacements / 13 exclusions /
86 pending / 21 in progress; original tests remain 56 verified / ten exclusions.
Next: review AsciiString floating-point parsing against Java lexical/culture
contracts and actual value-converter consumers. AsciiString and common remain
in progress; numeric correctness does not complete sequence/regex or transport
integration review.

### Native AsciiString floating-point parsing checkpoint

Pinned AsciiString.java:1338-1351, common character/memory fixtures,
CharSequenceValueConverter.java:136-148 and DefaultHeaders float/double scenarios
reviewed. ParseSingle/ParseDouble and matching TryParse APIs replace Java names;
no current C# consumers require adapters. Decimal conversion uses invariant BCL
byte spans after lexical validation. Hex conversion uses bounded leading/guard/
sticky bits with one target-format rounding. Logical ranges share NumericSlice
with integer APIs. Input grammar, CLR exceptions, saturation and intentional
native NaN canonical-bit differences are recorded in common-clr-design.md.
All 99 AsciiString comments remain; the replaced four methods have no comments.

Before repair, nine of ten initial culture/grammar/range cases fail; the lowercase
infinity rejection already passes on this machine. The final 96 cases cover
decimal/hex/suffix/control grammar, culture independence, slices, malformed
input, normal/subnormal/overflow and zero ties, direct Single rounding rather
than an intermediate Double, signs, long mantissas/exponents, native NaN and
zero-on-failure TryParse. Affected Debug selection passes 302; checked Release
passes all 96 new and 72 existing integer cases (168), with no failures/skips.

An isolated harness executes the exact four pinned Java methods, stubbing only
storage/byte-widening dependencies, on Corretto 21.0.11. All 46435 input rows match
Single/Double grammar outcomes and exact non-NaN bits; whole/view slices and
Parse/TryParse agree. This includes randomized decimal/hex literals, a sweep of
binary exponent/rounding boundaries, Latin-1 mutations, IEEE round-trip inputs
and long mantissas/exponents. 204 NaN result bit differences are deliberate native
canonical representations, not numerical mismatches. No arbitrary-input proof
or other runtime/OS compatibility claim is made.

Full Debug/Release each pass 1645 / fail zero / skip the same 14 (1659 discovered).
All prior 1563 identities/outcomes and all 759 non-Porting cases are retained;
only the 96 floating-point cases are added. Comment missing counts match the
previous audit for all 271 entries; existing pending/unreviewed gaps remain open.
All pinned inventory paths match. Evidence: ascii-floating-before.trx,
ascii-floating-affected-debug.trx, ascii-floating-checked-release.trx,
ascii-floating-full-debug.trx, ascii-floating-full-release.trx,
ascii-floating-identity-comparison.json, ascii-floating-comment-audit.json,
ascii-floating-inventory-summary.json, ascii-floating-java-clr-oracle.txt.

Warmed allocation regression: zero bytes over 8000 decimal/hex Parse/TryParse
calls. An isolated Release smoke comparison uses four decimal inputs, three
rounds of 200000 parses and disabled tiered compilation. The old string-copy
path allocates 7200000 bytes per batch; the new decimal/hex paths allocate zero.
Checksums and actual timings remain in ascii-floating-benchmark.txt; this bounded
measurement is not a general throughput guarantee. Validation uses
--artifacts-path artifacts/ascii-floating-validation/build, Windows/net10.0,
SDK 10.0.203/runtime 10.0.7. Harness sources remain in ignored artifacts;
no new feature MD. Source decisions remain 49 verified / 36 CLR replacements /
13 exclusions / 86 pending / 21 in progress; tests remain 56 verified / ten exclusions.
Next: review AsciiString regex matching/splitting against pinned full-match,
trailing-empty and actual consumer requirements, then the native sequence API.
AsciiString and common remain in progress.

### Native regex/framework splitting checkpoint

All-module pinned call/member-reference review finds no AsciiString regex-facade
consumers; actual splits/matches use String/Pattern or unrelated IP-rule methods.
Remove matches(String), incomplete split(String) and unused Regex/LINQ imports.
Native consumers use Regex on lossless logical-view text with explicit anchors,
options and field policy. Native grammar/capture/count/zero-width/Unicode/CR
differences and both original comment blocks are in common-clr-design.md.
AsciiString keeps 97 comments in source plus two archived blocks; all 99 are
preserved. split(char) remains for the pending native sequence/lifetime review.

StringUtilTest's nine inherited JDK split scenarios now use native char/count
String.Split with explicit trailing-delimiter trimming where required. Every
original name, literal input, expected array and assertion remains; all five
fixture comments are preserved. Delete the unused JavaStringTestExtensions.
Executing the original nine Java bodies retains all ten assertions and passes;
the pinned AsciiString delegated methods demonstrate the recorded native engine
differences (ascii-regex-java-decisions.txt). Four of eight baseline full-match
cases fail; thirteen final native consumer cases cover view/caching and policies.
Affected Debug selection passes 379. A final CR-policy assertion was added after
the first full Debug run; final whole Debug/Release both include that assertion.

Full Debug/Release each pass 1658 / fail zero / skip the same 14 (1672 discovered).
All prior 1659 identities/outcomes and all 759 non-Porting cases are retained;
only thirteen native consumer cases are added. All 271 pinned inventory paths
match and missing-comment counts are unchanged from the previous audit.
Evidence: ascii-regex-before.trx, ascii-regex-affected-debug.trx,
ascii-regex-final-full-debug.trx, ascii-regex-full-release.trx,
ascii-regex-identity-comparison.json, ascii-regex-comment-audit.json,
ascii-regex-inventory-summary.json. Ignored artifacts retain the Java harness
and typed call-site review; no new feature MD or performance claim.
Windows/net10.0, SDK 10.0.203/runtime 10.0.7, Java oracle Corretto 21.0.11.
Source decisions remain 49 verified / 36 CLR replacements / 13 exclusions /
86 pending / 21 in progress; original tests remain 56 verified / ten exclusions.
Next: review AsciiString delimiter byte views, sequence/search APIs and remaining
ICharSequence/StringExtensions against native span/memory/string consumers.
AsciiString and common remain in progress.

### Native delimiter ranges/character search checkpoint

Remove unused AsciiString.split(char) after all-module call/member-reference
review. Native byte Split ranges preserve every empty field and let consumers
choose shared Memory slices or detached arrays. StringCharSequence exposes
logical UTF-16 AsSpan/AsMemory without substring allocation. Actual mixed-sequence
search consumers retain their bridge and use bounded native span IndexOf.
Four baseline regressions expose start+backingOffset overflow; all pass after
checking logical bounds. All 99 AsciiString comments remain (93 in source, two
regex and four delimiter comments archived in common-clr-design.md).

Exact pinned methods on Corretto 21.0.11: 20640 corpus rows. All 16528 ordinary
search rows agree; 2048 Java overflow exceptions deliberately become -1. Of
2064 splits, 2057 agree and seven retain trailing empties by native policy.
Two original character-search test bodies pass all 21 unchanged assertions.
Independent byte scans and native consumer cases cover all 256 byte values,
logical offsets, extreme starts, UTF-16 and shared/copy ownership. Warm 20000
native byte/char search+split+memory iterations allocate zero and create no
thread-local map; this bounded smoke is not a general throughput claim.

Affected Debug 409 passes; checked Release 24 passes. Full Debug/Release each
1682 passed / zero failed / same 14 skipped (1696 discovered). All prior 1672
identities/outcomes and 759 non-Porting cases remain; only 24 new cases are added.
All 271 pinned inventory paths and prior missing-comment counts are unchanged.
Evidence: ascii-delimiter-before.trx, ascii-delimiter-affected-debug.trx,
ascii-delimiter-checked-release.trx, ascii-delimiter-full-debug.trx,
ascii-delimiter-full-release.trx, ascii-delimiter-identity-comparison.json,
ascii-delimiter-comment-audit.json, ascii-delimiter-inventory-summary.json,
ascii-delimiter-java-decisions.txt, ascii-delimiter-java-clr-oracle.json,
ascii-delimiter-native-smoke.txt. Java/CLR harnesses/corpus remain in ignored
artifacts. Windows/net10.0 SDK 10.0.203/runtime 10.0.7. No new feature MD.
Source/test decision counts remain unchanged. Next: sequence-pattern search,
slicing and coordinated ICharSequence/StringExtensions native API review.
AsciiString and common remain in progress.

### CLR endian views/access-strategy checkpoint

VarHandleFactory is a framework replacement using BinaryPrimitives/MemoryMarshal,
typed counters and native map locking, with actual pinned buffer/counter/fence
consumers reviewed. Fifteen array/narrowing/alignment methods and ByteAt's JVM
Unsafe branch retire; existing CLR fixture assertions migrate to native APIs.
The three huge typed-array indexes now expect native IndexOutOfRangeException,
retaining the no-truncation purpose; other assertions and identities remain.
Source licenses and removed explanatory comments are in common-clr-design.md.

The exact pinned factory on Corretto 21.0.11 agrees with checked CLR common-memory
consumers on 864 endian/read/payload rows. Six endian/owner/view integration cases
and a deterministic 255 byte-wrap case add seven discovered cases. Targeted Debug
before the wrap addition passes 34. Checked runs first discover 394/pass 393/fail
one on the existing negative map index guard; after that repair, one original random
byte-increment case fails. A controlled new boundary case fails before wrapping.
Final checked Release passes all 395. Fixture migration compile errors were fixed;
no test exclusion, workload reduction or new skip. Initial whole Debug/Release each
pass 1898/skip 14 (1912 cases) before the final checked repairs/boundary addition.

Final whole Debug/Release each discover 1913/pass 1899/fail zero/skip 14, retaining
all 1906 prior outcomes and 759 original non-Porting identities. Comment audit
preserves the original VarHandleFactory license and changed source/test comments;
inventory/path validation and uppercase semantic checks pass. The factory moves
pending to clr-replacement; source/module reviews remain open. Direct io_uring
ring ordering is explicitly outside this byte-view decision. Next: remaining
platform capability/bootstrap and utility stubs against their actual purposes.

Evidence: varhandle-memory-final-full-debug.trx, varhandle-memory-final-full-release.trx,
varhandle-memory-final-targeted-debug.trx, varhandle-memory-checked-release.trx,
varhandle-memory-final-checked-release.trx, varhandle-memory-wrap-before-checked.trx,
varhandle-memory-repaired-checked-release.trx, varhandle-memory-comment-audit.json
and varhandle-memory-identity-and-inventory.json. Java/CLR oracle sources and
semantic naming verification remain in ignored artifacts/varhandle-memory-validation.

### Native version metadata checkpoint

Replace the unused Java Version class with standard assembly metadata already
emitted by the SDK. Pinned source/consumer evidence and native mappings, including
explicit differences for Maven IDs, resource merging and optional provenance, are
in common-clr-design.md. All ten source comments are preserved there; the original
has no common test source. The two reproduced pre-retirement defects are reading
a working-directory spoof and ignoring the selected assembly. A numeric-offset
date hypothesis was not reproduced and is not counted as a defect.

The unchanged pinned Java source runs with a context-loader dependency shim on
Corretto 21.0.11. A native consumer reads actual net10.0 Debug/Release DLLs and
checks identity, informational version, expected full SourceRevisionId, explicit/
contextual scope, missing optional provenance, no working-directory lookup and no
retired facade. No new runtime/parser or permanent BCL fixture is added. Whole
default Debug/Release each discover 1906/pass 1892/fail zero/retain 14 skips, with
all 1906 previous identities/outcomes and 759 non-Porting cases unchanged. The
pinned 271-file inventory/path/comment audit remains valid; Version moves from
pending to clr-replacement. Common/platform completion remains open. Next review:
VarHandleFactory's endian byte views and ordered-publication requirements were
the next review candidate; their subsequent CLR decision is recorded above.

Evidence: version-metadata-full-debug.trx, version-metadata-full-release.trx,
version-metadata-comment-audit.json and version-metadata-identity-and-inventory.json;
unchanged Java oracle, before-corrected.log, Debug/Release CLR consumer logs and
SDK target evidence in ignored artifacts/version-metadata-validation.

### CLR initialization/reflection/exception checkpoint

Pinned ClassInitializerUtil initializes classes before native bootstrap; exact CLR
Types now use RuntimeHelpers.RunClassConstructor, rather than reading metadata or
resolving names against another assembly. ReflectionUtil retains its verified CLR
generic resolver and retires the no-op JVM accessible-object API. Misleading
platform loader aliases, the JVM reflection preference and unchecked-throw helpers
retire after pinned common and downstream consumer review. Direct propagation and
bare throw retain FastThreadLocal and shutdown-startup failure origins. Existing
Graal fixture identity now tests real CLR reflection under three Java flag settings.

Eleven new CLR cases: corrected before-repair Debug fails ten/passes one; current
targeted Debug passes 35. Full default Debug/Release each discover 1906/pass 1892/
fail zero/retain 14 unchanged skips. All 1895 prior identities/outcomes, including
759 original non-Porting cases, remain. Affected checked Release discovers 282/
passes 277/skips five unchanged cases. Original ClassInitializerUtil's five and
ReflectionUtil's seven comments are preserved, including exact replacement
provenance; remaining audit counts do not regress. Source review becomes verified
for those two entries; platform/source-module completion remains open. No new
exclusion or skip. Version resources were the next review candidate at this
checkpoint; the subsequent standard metadata replacement is recorded above.

The unchanged pinned initializer runs with a minimal same-loader dependency shim
on Corretto 21.0.11. It verifies synchronous/once-only/64-concurrent initialization,
cause/no-retry failures, empty/no-initializer types and null rejection. JVM and CLR
failure types differ explicitly. Native integration, trimming/AOT and throughput
are unverified. Details and preserved comments: common-clr-design.md.
Evidence: clr-platform-access-before-debug.trx, clr-platform-access-final-targeted-debug.trx,
clr-platform-access-full-debug.trx, clr-platform-access-full-release.trx,
clr-platform-access-checked-release.trx, clr-platform-access-comment-audit.json,
clr-platform-access-identity-and-inventory.json; ignored oracle sources/logs and
method-casing verification in artifacts/clr-platform-access-validation.

### Native byte comparison/hash checkpoint

Review pinned AsciiString, NetUtil and HPACK/QPACK users; replace JVM dispatch with
bounded CLR SequenceEqual/IndexOfAnyExcept, BCL FixedTimeEquals and one native-order
MemoryMarshal hash kernel. Remove four throwing stubs, six redundant scalar/
strategy helpers and five unused layout fields. Keep 0/1 chaining and nonpositive
comparison/zero empty results. Positive and hash-empty ranges validate before
content access, preventing overflow/early mismatch from hiding invalid storage;
null arrays reject explicitly. Original explanatory comments remain in the
implementation/existing common-clr-design.md; commented placeholder code retires.

New 21 cases execute; eight fail before repair. Related Debug passes 42;
checked Release byte/AsciiString/platform/NetUtil passes 375. Default Debug/Release
each discover 1895 / pass 1881 / fail zero / retain 14 unchanged skips. All prior
1874 case identities/outcomes remain, including 759 original non-Porting cases.
TestHashCodeAscii retains 1000 byte/string comparisons; the redundant JVM strategy
assertion retires. Independent Java tables now test the single public hash kernel.

Exact extracted scalar and Unsafe Java paths match all 17024 inputs: lengths
0..128 and 1024/1025/4096/4097, offsets 0..15, eight byte patterns, equal/first/
middle/last mismatch cases, zero checks and hash values. The host is little-endian
Windows x64; actual big-endian execution is not claimed. Functional result checks
do not prove timing-security or throughput equivalence. The BCL documents the
fixed-time byte contract; integer/character helpers remain pending review.
Warmed allocation check: 400000 valid 65-byte operation calls allocate zero managed
bytes on this host. This is a hot-path allocation check, not a throughput benchmark.

Evidence: byte-range-before-debug.trx, byte-range-targeted-debug.trx,
byte-range-full-debug.trx, byte-range-full-release.trx, byte-range-checked-release.trx,
byte-range-java-clr-oracle.json, byte-range-comment-audit.json and
byte-range-identity-and-inventory.json. Reproduction tools/logs stay in ignored
artifacts/byte-range-validation. No feature MD added. ConstantTimeUtils moves from
pending to in-progress for its byte overload; other source/test status decisions
remain unchanged. Next: platform class-loader/reflection/exception APIs against
actual Java consumers and CLR assembly/loading semantics.

### Bounded native byte access checkpoint

Review original buffer word/copy/fill/allocation users and io_uring publication
users before replacing the raw long/object-offset facade. NativeMemoryAllocator,
Owner/View and Memory/Span preserve ordinary memory purposes without JVM headers.
MemoryMarshal uses host byte order; BinaryPrimitives handles explicit wire order.
Ordered native publication remains a separate pending contract. Preserve removed
comments in the existing common-clr-design.md; no feature MD is added.

One address-wrap regression fails before repair. Checked validation initially
finds two pointer-bit conversion failures; explicit unchecked bit conversions
after bounds validation fix them. Full initial Debug/Release expose the prior
(-1, 10) metadata assertion; the original fixture now checks explicit wrap failure
and valid (-16, 10) unsigned bits. Final full Debug/Release each discover 1874 /
pass 1860 / fail zero / keep the same 14 skips and 1861 prior case identities.
Thirteen new cases cover unaligned host/wire words with sentinels, short-range
failure before writes, all four heap/native copy pairs, empty end slices, borrowed
overlapping aliases and native pointer boundaries. Final checked Release passes
73 native/heap/ASCII-native/original platform cases.

The 128 word inputs and 108 copy/fill inputs match exact extracted Java methods,
including 1MiB threshold boundaries, offsets and source/untouched-byte digests.
Both chunked and modern copy paths run on JDK21; neither real JDK8, big-endian
hardware nor native transport execution is claimed. CLR alias-overlap semantics
are independently checked against Array.Copy and are not claimed JVM equivalence.
Source/test decision counts stay unchanged. Remaining low-level array/hash stubs
and other JVM-shaped APIs stay pending; whole platform classes are not verified.

Evidence: native-access-before-debug.trx, native-access-final-full-debug.trx,
native-access-final-full-release.trx, native-access-final-checked-platform-release.trx,
native-access-java-clr-oracle.json, native-access-comment-audit.json and
native-access-identity-and-inventory.json. Reproduction/extraction logs remain in
ignored artifacts/native-access-validation. Next: remaining PlatformDependent0
array/hash stubs against actual original consumers and existing CLR implementations.

### C# method casing checkpoint

User-directed naming pass: capitalize the first character of all 2464
lowercase C# method/local-function declarations (including two explicit interface
implementations), with callers, method groups, nameof, interfaces and overrides.
Roslyn semantic inspection includes both solution projects and the queue-cost
consumer. Native parameter/field names and Java provenance comments stay intact.
Reflective logger/platform probes, ReferenceCountUtil Touch exclusions and Track0
stack-frame filtering follow the new names; declaration-only renaming is not enough.
Type-qualified expressions resolve name hiding. Identical EmptyPriorityQueue
toArray/ToArray methods coalesce into one implementation. No lowercase method
compatibility aliases remain, and no new feature MD was added.

Full Debug/Release each discover 1861 / pass 1847 / fail zero / retain 14 skips.
All prior cases/outcomes map exactly by capitalizing the method-name segment;
718 displayed case identities change names, with no cases added/removed.
All 759 non-Porting cases remain under that mapping. Targeted reflection/leak/
reference-count/platform Debug passes 85, and the queue-cost consumer builds.
Final semantic declaration scan finds zero lowercase methods/local functions.
All comments in 333 changed C# files compare exactly with the prior commit;
all 271 pinned comment-audit totals/missing counts and source/test decisions remain
unchanged. Existing compiler/analyzer warnings remain. Windows/net10.0 only.

Evidence: method-casing-final-full-debug.trx, method-casing-full-release.trx,
method-casing-final-targeted-debug.trx, method-casing-comment-audit.json and
method-casing-identity-and-source-audit.json. Semantic rename/declaration reports
and build logs remain in ignored artifacts/method-casing-validation. Earlier
checkpoint names are historical; Java comments/examples retain original spelling.
Common remains in progress; next porting unit stays raw native-address operations.

### Native reference-count fields checkpoint

Replace the stale, unused generic ReferenceCountUpdater/AtomicIntegerFieldUpdater
adapter with native static ref-int operations and migrate AbstractReferenceCounted
to them. Real pinned AbstractReferenceCountedByteBuf and AdaptivePoolingAllocator
consumers justify reusable count/reset/accessibility/final-release operations.
Remove managed object-field-offset stubs after reviewing counter, Java NIO Selector
and Java SSLContext private-field consumers. CLR does not share those JVM layouts;
future transport/TLS implementations remain outside this common unit. Raw native
addresses/mixed copies and other platform surfaces remain pending. Design/native
range/error policy and exact original comments are in common-clr-design.md CLR
reference-count fields and JVM field access. Five source entries (RefCnt and the
four deprecated updater/provider classes) become CLR replacements. VarHandleFactory
also provides endian byte-memory views and remains pending.

Nonpositive direct count setters previously exposed negative counts; two of three
new baseline cases fail and all three pass after repair. Sixteen native cases cover
independent composed fields/quiescent reset, invalid changes, full positive Int32
boundaries and unchanged failure state, shared native owner disposal, throwing
deallocation, 1000 retain/final-release races, 40000 contended balanced pairs,
500 competing overflow-boundary races and 1000 payload-publication observations.
No original input/assertion/comment/fixture was changed and no skip was added.
The first checked selection passes 74/75: the unchanged ThreadLocalRandom seed
narrows masked timestamp bits with a checked cast and throws during static init.
Fix that existing adapter conversion with explicit unchecked narrowing (its normal
build already truncates those same bits); all 75 checked cases then pass, including
the original 10000-iteration multi-thread retain/release tests. The intermediate
focused selection omitted that one failing dependency only for diagnosis; it is
not the final validation or a new skip. The broader random-adapter/API review stays
open. Earlier checked failure evidence is retained.

Corretto 21.0.11 executes seven exact pinned Java files (RefCnt, AbstractReferenceCounted,
generic updater, Atomic/VarHandle providers, ReferenceCounted and exception) with
minimal platform-probe/checkPositive harnesses. Both Atomic and VarHandle runtime
paths execute 32016 rows: 10672 inputs through each of three counter/owner paths.
All Java provider and path outcomes agree. Native checked execution matches an
independent integer state model for all 10672 inputs. Java/native match exactly on
10561 inputs, including 10266 ordinary values. Intentional differences: 76 doubled
raw-int boundary rows and 35 retain-overflow error-count rows. CLR uses the existing
full positive Int32 range, fails without mutating the count, and reports the actual
count; it does not reproduce Java's doubling wraparound or misleading zero count.
This is behavioral/language evidence, not universal JVM Unsafe equivalence. Warm
one-million single-thread retain/release pairs allocate zero bytes in the bounded
native oracle. Its observed timing is a smoke measurement only; no cross-runtime,
contention, pooling or storage-throughput equivalence is claimed.

Final default Debug/Release each discover 1861 / pass 1847 / fail zero / retain 14
skips. All prior 1845 identities/outcomes and all 759 non-Porting identities remain.
All 271 pinned inventory paths and implementation paths remain valid. All 35 exact
comments in the five replaced original counter/provider files are retained; missing
counts improve by 29, with every other inventory missing count unchanged. The four
AbstractReferenceCounted comments remain intact. Five pending decisions become CLR
replacements: sources 50 verified / 41 CLR replacements / 13 exclusions / 80 pending /
21 in progress; tests 56 verified / ten exclusions. Common is still in progress.

Evidence: reference-count-field-before-debug.trx, reference-count-field-targeted-debug.trx,
reference-count-field-checked-release.trx (initial failure),
reference-count-field-focused-checked-release.trx (diagnosis only),
reference-count-field-final-checked-release.trx, reference-count-field-full-debug.trx,
reference-count-field-full-release.trx, reference-count-field-identity-comparison.json,
reference-count-field-comment-audit.json, reference-count-field-inventory-summary.json,
reference-count-field-java-clr-oracle.json. Native/JVM harness/corpus/logs remain in
ignored artifacts/reference-count-field-validation, with no new feature MD.
Windows/net10.0, SDK 10.0.203/runtime 10.0.7; no other OS/big-endian host run.
Next: raw native-address allocation/word/ordered-write/mixed-copy stubs against
bounded NativeMemoryAllocator/Owner/View contracts and actual buffer/native I/O
consumers; then remaining mixed-sequence/public API/platform consumer review.

### ASCII transform checkpoint

Actual HTTP/header consumers retain trim/case APIs. Correct generic exclusive-end
trimming, unchanged byte-view identity and null mapping; use native logical span
range searches. Preserve unsigned byte content and existing culture-independent
ASCII conversion with detached changed bytes. SWAR's pattern/upper/lower arithmetic
now explicitly wraps in checked builds; getIndex uses native BitOperations.
The unused CLR-only BitOperators class is removed rather than retaining manual
JDK zero-count implementations; this is a public source API removal in the port.
Design/ownership and intentional upstream bug corrections are recorded in
common-clr-design.md ASCII trim and word conversion. SWARUtil's source behavioral
review is verified; this does not complete the future buffer/native public API.

Before repair: new Debug regression selection fails 8/12; checked selection fails
34/40 including original SWAR/case tests. After repair, checked Release passes 82
(12 new, 20 existing case-conversion, eight original SWAR, 42 original character).
Full default Debug/Release each discover 1845 / pass 1831 / fail zero / keep 14
skips. All prior 1833 identities/outcomes and 759 non-Porting cases are retained.
All 271 pinned inventory paths remain. SWAR missing comments improve from two to
zero (19 original comments); all other missing counts stay unchanged, including
99/99 AsciiString and 9/9 AsciiStringUtil original comments. Original fixtures
retain every name/input/assertion/comment and were not edited.

Corretto 21.0.11 executes exact SWAR/AsciiStringUtil files and four exact AsciiString
methods with minimal array/sequence/native-order provider harnesses. 67584 word
rows agree exactly, including both first-index bit orders; 66856 case rows agree
with Java scalar conversion and an independent unsigned reference. Java's optimized
short-tail branch differs in 6706 rows, including 80 41 -> 80 FF versus native
80 61. 67096 trim rows agree with Java String.trim and the native reference.
Intentional differences: generic trim 66702 rows; signed-byte content 1309 rows
per offset; unchanged offset-three identity 46 more rows. These are recorded
bug/native decisions, not universal original Java equivalence. Native BCL ASCII
invalid-input behavior is also executed. No full JVM Unsafe or HTTP/buffer run is
claimed. Warm 20000 unchanged slice conversion/trim iterations allocate zero.
Three-round word/scalar smoke timings on 32/4096-byte mixed payloads remain in
oracle.log; tiered-JIT/order/input sensitivity prevents a general throughput claim.

Evidence: ascii-transform-before-debug.trx, ascii-transform-before-checked.trx,
ascii-transform-final-checked-release.trx, ascii-transform-final-full-debug.trx,
ascii-transform-final-full-release.trx, ascii-transform-identity-comparison.json,
ascii-transform-comment-audit.json, ascii-transform-inventory-summary.json,
ascii-transform-java-clr-oracle.json. Harnesses/corpora/timings remain in ignored
artifacts/ascii-transform-validation; no new feature MD. Windows/net10.0,
SDK 10.0.203/runtime 10.0.7, native little-endian host (no big-endian host claim).
Source decisions: 50 verified / 36 CLR replacements / 13 exclusions / 85 pending /
21 in progress; tests remain 56 verified / ten exclusions. AsciiString/common remain
in progress. Next: remaining mixed-sequence/public API consumers and platform
operations, starting with raw native-address/object-field-offset stubs and their
actual original consumers before selecting CLR replacements or implementations.

### Native OWS/scaffolding checkpoint

Retire unused CLR-only CharUtil noncomparison helpers after all-module pinned
source and whole-workspace caller review. Keep actual comparison code verbatim.
Two integer assertions use AsciiString.ParseInt64; two delimiter assertions over
three representations use StringUtil.substringAfter with explicit fixture text.
All existing names, inputs, expected results and assertions remain. The public
API removal, actual HTTP header consumers and native decisions are recorded once
in common-clr-design.md Native OWS and scaffolding.

Actual StringUtil OWS trim and CSV scanners use native span operations. One
pre-repair null regression fails with NullReferenceException, then passes with
ArgumentNullException(value). Corretto 21.0.11 executes six exact pinned methods
over 70224 unique inputs: all UTF-16 code units surrounded by OWS, exhaustive
length 0-4 CSV alphabet strings and quoted cases. All 210672 trim/CSV results
agree, as do trim and nonempty CSV reference checks. Empty CSV reference identity
is excluded from comparison because CLR canonicalizes empty strings; original
literal-empty fixture assertions still execute. Null NPE maps to the CLR argument
exception. Harness dependencies are native constants/StringBuilder and a null
check shim; this does not exercise thread-local caches or HTTP module execution.

Debug and checked Release affected selections each pass 239, including all 64
original StringUtil cases and 29 new actual OWS/CSV contract cases. Full default
Debug/Release each discover 1833 / pass 1819 / fail zero / retain 14 skips. All
prior 1804 identities/outcomes and all 759 non-Porting cases remain unchanged.
All 271 inventory paths and missing-comment counts are unchanged; all 67
StringUtil comments remain. Evidence: charutil-ows-before.trx,
charutil-ows-affected-debug.trx, charutil-ows-checked-release.trx,
charutil-ows-full-debug.trx, charutil-ows-full-release.trx,
charutil-ows-identity-comparison.json, charutil-ows-comment-audit.json,
charutil-ows-inventory-summary.json, charutil-ows-java-clr-oracle.json.
Harnesses/corpora remain in ignored artifacts/charutil-ows-validation. No new
feature MD or performance claim. Windows/net10.0 SDK 10.0.203/runtime 10.0.7.
Source/test decision counts stay unchanged; StringUtil/common remain in progress.
Next: review actual AsciiString trim/case-transform consumers and native API
choices, then the remaining mixed-sequence ownership/API and memory/SWAR work.

### Native sequence comparison/hash checkpoint

Fix CLR CharUtil.contentEquals incorrectly ignoring case, culture-dependent
ignore-case equality/regions and incomplete final-sigma handling. Use native
Ordinal/OrdinalIgnoreCase over logical UTF-16 spans; indexed fallback consumes
complete surrogate pairs through BCL comparison without string/byte conversion.
StringCharSequence object equality now stays within its immutable type, preserving
symmetry and hash contracts; explicit mixed content/comparer operations remain.
StringCharSequence and Appendable hashes use native string.GetHashCode over spans;
Appendable exposes a bounded synchronous borrowed AsSpan, without async ownership.
Remove two unused CLR-only string region overloads and the unused general per-char
comparator; archive its original comment in existing common-clr-design.md.
All 99 AsciiString original comments remain (one additional archived comparator).

Keep byte-receiver dispatch and ASCII-only content/region/contains comparison.
Correct the CLR ASCII comparator's accidental Unicode folding; every 65536 byte
pair is checked against independent A-Z folding. Existing explicit header comparer
collection/Unicode/hash contracts remain unchanged. Checked validation exposes
11 existing ASCII hash/collection failures; explicit unchecked hash expressions
fix those. The added original 1000-length hash fixture then exposes one signed
word narrowing failure; explicit unchecked bit reinterpretation fixes it. No
global unchecked range policy, test skip or weakened assertion is added.

Seven pre-repair regressions all fail, then pass. Final affected Debug passes 114;
checked Release passes 66 (all 50 new plus 16 existing). Exact pinned Java region/
comparator methods on Corretto 21.0.11 execute 77760 unique rows: ASCII results
match; native general results match the BCL reference with zero differences.
There are 34 deliberate Java-to-native general differences: nine dotted-I,
nine dotless-I and sixteen supplementary case-pair regions. These follow the
documented native Unicode policy, not an assertion of universal Java equivalence.
Exact pinned Java hash methods on 540 deterministic byte/slice inputs agree with
actual checked CLR byte/text/AsciiString hashes (native little-endian Windows).
Warm 20000 comparison/hash iterations allocate zero for the chosen span/indexed
inputs; no general throughput claim or stable cross-process hash guarantee.

Final full Debug/Release each discover 1804 / pass 1790 / fail zero / retain the
same 14 skips. All prior 1754 identities/outcomes and 759 non-Porting cases remain;
only 50 SequenceComparisonContractTest cases are added. All 271 inventory paths
and missing-comment counts are unchanged. Earlier normal full runs also pass;
final full runs follow the checked-hash repair. Failed runs remain separate:
sequence-comparison-before.trx, sequence-comparison-hash-before-checked.trx and
sequence-comparison-word-before-checked.trx. Passing evidence:
sequence-comparison-affected-debug.trx, sequence-comparison-checked-release.trx,
sequence-comparison-final-full-debug.trx, sequence-comparison-final-full-release.trx,
sequence-comparison-identity-comparison.json, sequence-comparison-comment-audit.json,
sequence-comparison-inventory-summary.json, sequence-comparison-java-clr-oracle.json.
Ignored artifacts/sequence-comparison-validation retains harnesses, corpora and
allocation/old-comparator probe; no feature MD is added. Windows/net10.0 SDK
10.0.203/runtime 10.0.7. Source/test decision counts stay unchanged. Remaining
numeric/split/trim/search scaffolding, memory/SWAR and the coordinated ICharSequence
public API still require review; common remains in progress.

### Native string consumer checkpoint

Remove StringExtensions.cs, a CLR-only Java string facade without an upstream
class, after migrating all compiler-confirmed string calls in default source/test
projects. Native Length/indexers/ranges and char searches replace forwarding
methods; string equality uses explicit OrdinalIgnoreCase. ICharSequence calls
remain for the coordinated mixed byte/char API review. Suffix comparison uses
bounded UTF-16 SequenceEqual without temporary substrings; domain wildcard prefix
uses Ordinal and retains the pinned short-host prefix and raw suffix rules.
IPv4 parser-local native dot search preserves -1 beyond the string; embedded-IPv4
scope lookup clamps the native window. Full-string search limits are unchanged.
All original comments, fixture identities, inputs and assertions remain.

The initial soft-hyphen culture hypothesis did not reproduce (two baseline cases
pass); do not count this as a proven defect repair. Exact pinned Java methods
execute on Corretto 21.0.11: 24573 unique rows, zero outcome differences after
null/range exception mapping (1764 suffix, 160 domain, 22649 IP rows). Inputs
include null, extreme lengths, NUL/surrogates, wildcard prefix/suffix, valid and
invalid IPv4/IPv6, embedded tails, scopes/brackets, original fixture literals and
deterministic malformed strings. Native partial-suffix warm smoke: 20000 calls,
old substring facade allocates 1280000 bytes; actual span consumer allocates zero.
This is allocation evidence for that input, not universal performance evidence.

Affected Debug: 153 passed / zero failed / one existing skip. Checked Release:
40 passed (34 new cases and six existing). Full Debug/Release each discover 1754,
pass 1740, fail zero and keep the same 14 skips. All prior 1720 identities/outcomes
and 759 non-Porting cases remain; only 34 NativeStringConsumerContractTest cases
are added. All 271 pinned inventory paths and missing-comment counts are unchanged.
Evidence: native-string-before.trx, native-string-affected-debug.trx,
native-string-checked-release.trx, native-string-full-debug.trx,
native-string-full-release.trx, native-string-identity-comparison.json,
native-string-comment-audit.json, native-string-inventory-summary.json,
native-string-java-clr-oracle.json. Harnesses/corpus/allocation log remain in
ignored artifacts/native-string-validation; no feature MD is added.
Windows/net10.0 SDK 10.0.203/runtime 10.0.7. Source/test decision counts remain
unchanged; mixed-sequence casing, borrowed views/ownership and the ICharSequence
public API need further review. Common remains in progress.

### Native pattern windows/slicing checkpoint

Remove unused instance contains and four indexOf/lastIndexOf pattern facades after
all-module call/member-reference review; actual protocol calls use String, headers
or collections. Remove test-only StringCharSequence.indexOf(string). Original
forward/reverse fixtures retain names, inputs, expected indexes, all 50 assertions
and comments through fixture-local native byte-window consumers. Existing CLR
string-slice assertions also use native ordinal windows. No public search wrapper
is added. Keep the actual ICharSequence slicing bridge, validate endpoints before
arithmetic, and preserve full/empty identity plus partial copy/shared storage.
Native UTF-16/byte encoding and slice/cache/ownership decisions are explicit in
common-clr-design.md. Eight removed comments are archived there; all 99 original
AsciiString comments remain (85 source + 14 archived).

Four checked baseline regressions fail with OverflowException; endpoint guards
restore native ArgumentOutOfRangeException. Exact pinned Java search/slice methods
and MathUtil predicate executed on Corretto 21.0.11: 1964 unique rows, zero outcome
differences after exception mapping (1080 searches, 100 valid content/identity/
sharing slices, 784 invalid ranges). Original Java fixture bodies pass 50 unchanged
assertions. Independent scans cover every byte; native consumers cover UTF-16,
overlaps, empty needles, windows and copy/view lifetimes. Warm 20000 native pattern
search/memory-slice iterations allocate zero; no general throughput claim.
Affected Debug 436 passes; checked Release 29 passes (24 new + five existing).
Full Debug/Release each pass 1706 / fail zero / same 14 skips (1720 discovered).
All prior 1696 identities/outcomes and 759 non-Porting cases remain; only 24 new
cases are added. All 271 inventory paths and missing-comment counts are unchanged.
Evidence: ascii-sequence-before-checked.trx, ascii-sequence-affected-debug.trx,
ascii-sequence-checked-release.trx, ascii-sequence-full-debug.trx,
ascii-sequence-full-release.trx, ascii-sequence-identity-comparison.json,
ascii-sequence-comment-audit.json, ascii-sequence-inventory-summary.json,
ascii-sequence-java-decisions.txt, ascii-sequence-java-clr-oracle.json,
ascii-sequence-native-smoke.txt. Harnesses/corpus remain in ignored artifacts;
no feature MD added. Windows/net10.0 SDK 10.0.203/runtime 10.0.7.
Source/test decision counts stay unchanged. Next: coordinated ICharSequence and
StringExtensions native API review, including indexing and mixed text comparison.
AsciiString and common remain in progress.

UnaryPromiseNotifier is now recorded as a CLR replacement: the pinned all-module
search has no caller, and the deprecated alias's result transfer is already
covered by the ten native TaskCompletionTransferPortTest cases in both full runs.
No additional FutureListener/Promise facade is introduced. Both original comments
are archived in common-task-composition.md. Remaining pending source reviews: 99.

The unordered public mutable queue/remove API and unused metadata forwarding are
removed. PendingTaskCount preserves the original 10000 empty-queue checks; owned
tokens withdraw native submissions and schedules. Two migrated probes reproduce
canceled submission entries keeping a workerless queue alive, repaired by a single
membership hook without another result owner. Shutdown cancellation iterates a BCL
snapshot because withdrawal can reenter and mutate membership. The expanded affected
Debug selection passes 250 cases, including 256 admission/cancellation/claim races
and weak pool-owner lifetime. All 759 non-Porting identities and 14 skips match the
graceful checkpoint; changes are nine explicitly documented CLR-only mappings plus
two new cases. All 98 verified comment entries have zero missing. See
[native queue ownership](common-unordered-native-queue.md),
unordered-native-queue-identity-comparison.json and unordered-native-queue-comment-audit.json.
The first full Release retains one failure in the existing real-time
AutoScalingEventExecutorChooserFactoryTest.testScaleUpDoesNotExceedMaxThreads
(unordered-native-queue-full-release.trx). Its seven original fixtures and an
unchanged full Release rerun pass. Timing stability remains an explicit follow-up;
the failed run is not erased or counted as a successful validation.

Unordered graceful shutdown now implements the group contract despite the pinned
implementation's ignored-parameter TODO. Quiet waiting accepts new work and
restarts on admission and worker completion; timeout closes admission without
interrupting running work or completing Termination early. Delayed/periodic
shutdown policies still govern drain. The lifecycle timer does not capture ambient
ExecutionContext, including already-suppressed flow and long TimeSpan periods.
Queue insertion after closure and detached periodic requeue after termination
cannot reopen the pool. Nine initial regressions and a separate periodic reentry
regression fail before their repairs; final affected Debug 128 cases pass.
The full matrix adds 13 CLR cases without removing or renaming any fixture;
all 759 non-Porting identities and 14 skips remain unchanged. All 98 verified
comment entries have zero missing, including 21 unordered, ten group-contract
and eight original unordered test comments. See
[graceful shutdown admission and drain](common-unordered-graceful-shutdown.md),
unordered-graceful-identity-comparison.json and unordered-graceful-final-comment-audit.json.

The plain Future/Promise hierarchy and its Java blocking/listener facades are
removed after all runtime consumers and the 20 original fixture scenarios use
native Task/TCS, operation claims and ExecutorCompletion. PendingWrite now owns
an explicit borrowed message node and caller-supplied non-generic TCS; eight new
cases verify message release/transfer and cleanup when results are already settled
or release fails. All 759 non-Porting fixtures and 14 skip identities remain unchanged;
the only probe rename distinguishes native cancellation from a fault exception.
An initial full Debug chain timeout is retained in native-future-retirement-full-debug.trx:
the translation forced ThreadPool hops instead of testing synchronous reentrancy.
Inline-capable chain producers retain 20000 operations and the original two-second
bound, and the final matrix passes. All 70 removed-source comments, seven PendingWrite
and 12 fixture comments are preserved. See
[native completion ownership](common-native-future-retirement.md). Concrete unordered
immediate stop is subsequently implemented in common-unordered-cooperative-stop.md;
shared/group immediate API, private queue costs and 99 pending source decisions remain open.

After native submission and scheduling migration, unused PromiseTask/IRunnableFuture
and six Callable/Queueing glue files are removed. Their actual consumers already
use private native claim runners and TaskCompletionSource; all test identities
remain unchanged. All seven PromiseTask comments are archived with pinned provenance.
See [submission wrapper cleanup](common-native-submission-wrapper-cleanup.md).
The subsequent native fixture migration removes DefaultPromise and the remaining
Future/Promise hierarchy too; final backend decisions remain open. See
[native completion ownership](common-native-future-retirement.md).

At the preceding 1301-case checkpoint, removal of the temporary batch configuration and eight JVM-only test
placeholders, `test-selection-cleanup-full-debug.trx` and
`test-selection-cleanup-full-release.trx` record that earlier suite and exactly the
same discovered test identities (1287 passed / zero failed / 14 skipped).
All remaining C# tests compile by default;
focused execution uses `--filter`. See
[cleanup and original comment provenance](common-jvm-test-exclusions.md).

AsyncMapping now returns a provider-owned Task<T> through MapAsync and accepts
optional cooperative CancellationToken, with native input contravariance. No
caller-supplied IPromise or Java result adapter remains in that interface. Ten
native SNI-shaped consumer cases verify loop invocation/completion, retained
message ownership, read resume, immediate/deferred outcomes and independent
producer/observer cancellation. The handler/TLS modules remain unported. See
[native asynchronous mapping](common-native-async-mapping.md).
The first full Debug run had one existing unordered delayed-shutdown test failure
at its pending-work assertion. A worker barrier now establishes that precondition;
no executor behavior or deadline was changed. The failed run is preserved as
async-mapping-full-debug.trx. Final full Debug/Release pass all applicable cases.

CompleteFuture/SucceededFuture/FailedFuture and executor completed-result
factories are removed. Standard Tasks provide terminal results and
ExecutorCompletion provides the separate callback policy. Six native consumers
verify resolver fast-path callback affinity, distinct DNS reservation tokens
when Tasks share identity, and consumer-owned channel/loop metadata. Four existing
rows now test native completed-wait/callback policies; no original common fixture
is disabled. Affected Debug/Release each 59 passed; final whole matrix each 1303
passed / zero failed / 14 skipped. See
[completed results and original provenance](common-completed-results.md).
Subsequent checkpoints also remove progressive/plain producer factories;
unordered concrete scheduling/result wrappers have also been removed (bulk removal:
common-native-bulk-composition.md; scheduling removal:
common-native-unordered-scheduling-migration.md).

Dynamic progress subscription/removal now uses unique disposable registrations
over producer-owned Tasks. Report membership is snapshotted at admission; late
completion uses ExecutorCompletion with the source Task. The progressive Promise
hierarchy, factories and listener-only plumbing are removed; two existing CLR
progress scenarios and affected immediate/pool probes now use native APIs.
Sixteen dynamic cases include subscription-task lifetimes, callback context and
real direct/NonSticky queue removal; the affected selection passes 159 cases.
Both full configurations pass 1319 cases with the same 14 skips; the 759 test
identities outside the added Porting contract folder match the preceding
completed-result checkpoint, with no upstream fixture disabled by this change.
The initial dynamic implementation's detached-terminal-handle timeout is retained
as dynamic-progress-before.trx and repaired by settling claimed handles on whole
disposal. Original removed comments are archived with locations in
[native progress subscriptions](common-progress-subscriptions.md).

External producers now own TaskCompletionSource with RunContinuationsAsynchronously
and expose only Tasks. All newPromise factories and ImmediatePromise are removed;
no replacement producer wrapper/factory is introduced. Sixteen existing CLR
methods/eighteen cases now exercise native completion, observer cancellation,
timeout validation, executor callbacks and nonblocking async loop submission.
The four original invokeAll/invokeAny-in-loop cases keep their rejection and
non-execution assertions with a TCS harness. Five native provider/pool consumer
cases verify immediate/deferred/throwing outcomes and owned-state updates before
caller completion. Focused Debug: 126 passed / zero failed / zero skipped.
Full default Debug/Release each discover 1338 cases: 1324 passed / zero failed /
14 existing skips. All 759 non-Porting fixture identities and skipped identities
match the preceding progress-subscription checkpoint in each configuration.
All 105 verified source/test comment entries have zero missing; the two newly
removed factory/special-adapter comments are preserved with pinned locations.
At that producer checkpoint DefaultPromise/PromiseTask and inherited legacy
backends still remained. Subsequent submission/scheduling and wrapper cleanup
remove PromiseTask and those backends. Plain DefaultPromise fixtures subsequently
use native APIs and their result hierarchy is removed.
See [producer ownership and original provenance](common-native-producers.md).

Java submit overloads and all C# callers are removed. Native SubmitAsync now
owns startup barriers, values/failures, cancellation and group submissions;
ExecutorCompletion provides explicit notification affinity. Four native consumer
cases verify flush coalescing/cancellation and startup interrupt/failure handling.
One CLR-only PromiseTask description test was removed with its obsolete API.
Focused Debug: 152 passed. Full Debug/Release at this checkpoint each discover
1341 cases: 1327 passed / zero failed / 14 unchanged skips. All 759 original
fixture identities and skipped identities match the preceding producer checkpoint.
All 105 verified comment entries have zero missing; inherited JDK submission
documentation is archived with its mapped-source provenance. See
[native submission migration](common-native-submission-migration.md).

The unordered lifecycle now waits for a drained queue and released worker/start
reservations, replacing the pinned early shutdown-request success. Seven new
regressions reproduce active-worker, factory-reservation, queue-clear and lifecycle
reinsertion problems before repair. Clearing the queue cancels removed native
reservations before completing termination. The affected selection passes 156
cases; full matrix evidence is recorded above. Source comments remain preserved.
Custom thread-factory suffixes and yielded async delegate bodies are outside this
worker-loop lifetime signal. Quiet-period/timeout and final backend policy decisions
remain open. See [unordered termination ownership](common-unordered-termination.md).

Inherited JDK invokeAll/invokeAny and their newTaskFor hooks are removed after
checking every pinned module's production uses. The CLR AbstractExecutorService
base is deleted; native executor ownership supplies submission/lifecycle directly.
Useful batch scenarios now compose native Tasks with standard BCL APIs and owned
cancellation. Task.WhenAny's first completion, caller-owned timeout cancellation
and cooperative running cancellation are explicit differences. The four original
SingleThread JDK blocking-method guard cases have named native async/yield/affinity
counterparts; their removed Java rejection expectations are recorded separately,
not claimed as unchanged assertions. Two added CLR rows verify -1 tick/zero
observer timeout conversion without canceling accepted work. The affected Debug
selection passes 186 cases with zero failures/skips. All 247 comments across the
five changed Netty sources and SingleThread fixture remain preserved; inherited
JDK facade comments are archived. Full-suite and identity evidence are recorded
above. Subsequent unordered scheduler migration removes the concrete result wrappers;
plain Future fixtures now use native APIs; final backend decisions remain open. See
[native bulk composition](common-native-bulk-composition.md).

Native `ScheduleAsync`, `ScheduleAtFixedRateAsync` and `ScheduleWithFixedDelayAsync`
use the existing deadline queues and native Task/TCS results without Future/Promise
result adapters. Global quiet-period and auto-scaling monitoring work are migrated.
Thirty-four new cases verify native cancellation, failure, deadline, context and
lifetime contracts. ShutdownNow cancellation initially failed and was repaired.
Seven unused CLR action/token wrappers are removed. Concrete unordered Java scheduler
APIs have since been removed. Native queue ownership, graceful admission/drain and
immutable configuration are implemented. Replacement-factory failure is repaired in
common-unordered-worker-failure.md and concrete cooperative immediate stop in
common-unordered-cooperative-stop.md. Shared/group immediate API and private queue costs remain open.
See [common-native-scheduling.md](common-native-scheduling.md).

All ordered/global/single-thread scheduling callers and fixtures now use native
Tasks and owned cancellation tokens. The shared JDK scheduled-service interface,
group/default Java scheduling overloads and ordered ScheduledTask/Callable/Runnable
result adapters are removed. Deadline metadata stays separate from Task identity.
The original 1500ms Global busy-queue workload and 2000-iteration SingleThread
cancel/suspend race remain intact. The affected Debug selection passes 179 cases;
full default Debug/Release each pass 1336 / fail 0 / skip 14 (1350 total).
All 759 non-Porting fixture identities and all skipped identities match the
preceding bulk checkpoint in both configurations. All 105 verified source/test
comment entries have zero missing, including all eight ScheduledFutureTask
comments archived from the pinned source. Unordered concrete scheduling has since
migrated; final backend review remains unfinished. See
[ordered scheduling migration](common-native-ordered-scheduling-migration.md).

Unordered native scheduled callbacks now retain worker identity after thread-factory
replacement. Accounting belongs to workerLoop, and constructor/setter retain the
configured factory itself. The pinned constructor-only wrapper allowed replacement
workers to lose inEventLoop identity; the CLR-only probe intentionally changes
that expectation. Four native regression cases initially fail three / pass one;
the repaired affected Debug selection passes 139. Whole Debug/Release each pass
1340 / fail 0 / skip 14 (1354 total), with unchanged 759 non-Porting fixture and
skipped identities. The unordered source/test retain all 21/8 original comments;
all 105 verified comment entries have zero missing. The unused duplicate factory
adapter is removed. Concrete scheduling/result adapters have since been removed;
final backend decisions remain open. See [worker identity correction](common-unordered-worker-identity.md).

All unordered scheduling callers and the original repeated-rate fixture now use
native Tasks and owned tokens. Concrete Java scheduling overloads, IScheduledTask,
inner JDK/Promise decorators and JdkFutureTask are removed. Raw execute has a
single-claim callback queue entry with no result facade. Throwing raw factory
admission rolls back; foreign-pool queue insertion is rejected; clear releases raw
callbacks. Three new ownership cases cover those boundaries. Twelve CLR-only
source-quirk probes have explicitly documented native counterparts; all original
fixture identities and workloads remain. Initial/final affected Debug selections
pass 199/202 cases. Full Debug/Release each pass 1343 / fail 0 / skip 14, 1357 total.
All 759 non-Porting and skip identities match the preceding worker checkpoint;
all other changes are exactly the 12 mapped probe renames and three new cases.
ScheduledFuture is a CLR replacement by Task/owned cancellation plus independent
deadline metadata, so verified source/test entries now number 104. All have zero
missing comments; all 21 unordered, two ScheduledFuture and eight original test
comments remain preserved. Final pool configuration/public queue/shutdown and plain
Future fixtures subsequently migrate to native APIs; final backend decisions remain unfinished. See
[native unordered scheduling migration](common-native-unordered-scheduling-migration.md).

Native progress reporting now accepts a producer-owned Task and implements
IProgress<TransferProgress>. ExecutorProgress orders progress and terminal
callbacks, isolates observer failure/context changes, bounds recursive reporting
and supports explicit pending capacity and independent observation disposal.
Twenty-two cases verify these contracts and reference lifetimes. The original
channel/chunked-write consumers establish final-progress-before-completion and
executor affinity. Fixed callback snapshots do not complete legacy dynamic
listener registration/removal migration. See
[common-native-progress.md](common-native-progress.md).

Native submission now rejects a null Task returned by any asynchronous delegate
family with InvalidOperationException instead of Task.Unwrap's canceled result.
All four regression cases failed with TaskCanceledException before the repair;
the combined native submission/progress/scheduling selection now passes 82 cases.
Ordinary synchronous functions may still legitimately return a null value.

SubmitAsync now accepts IEventExecutorGroup as well as individual executors.
Per-submission child selection and NonSticky's underlying-group delegation are
preserved without Java result adapters. Submitting to an explicit NonSticky child
retains its ordered runner. Six group cases verify all delegate forms, original
selection/admission failure, cancellation before selection, routing and two real
event-loop workers. The broader native/original NonSticky selection passes 98
cases. Remaining Java-shaped submit/invokeAll/invokeAny and listener APIs are
separate caller migration work; this checkpoint does not claim their removal.
The original NonSticky ordering workload now uses native submission and
Task.WhenAll, retaining all producer counts, batch sizes, affinity/concurrency
assertions and original comments. Native listener removal is a required dependency:
AddressResolverGroup and DefaultChannelGroup detach callbacks on owner removal,
while SslHandler cancels scheduled handshake timeout from completion.

Direct unordered native submission now bypasses JdkFutureTask/PromiseTask queue
wrappers. Three initially failing regressions verify shutdown rejection even with
a discard handler, cancellation of shutdownNow-removed native work, and removal
of a reservation when worker creation fails. A narrow internal shutdown hook
finishes the existing submitted TCS rather than creating a second result owner.
The affected submission/scheduling/unordered/NonSticky selection passes 118 cases.
Raw execution and concrete scheduling subsequently migrate to native queue work;
final pool/queue/shutdown policies still need review. This
is not full backend completion.

NonSticky ordered-runner admission/removal now uses BCL queues, atomic membership
claims and native pool reservations. Four initially failing regressions verify
retry after rejection, shared admission failure, silent discard and queued removal.
Eleven added cases also cover direct/forwarded graceful versus immediate shutdown,
bounded inline callbacks and two-thread FIFO handoff. The affected selection
passes 129 cases. All 30 original source comment blocks and 12 test comment blocks
remain; original workloads are retained. See
[common-nonsticky-runner.md](common-nonsticky-runner.md). Detachable completion
observers are now implemented separately and the remaining Future/Promise callers
have native equivalents; see common-native-future-retirement.md. The unordered
backend's worker failure and concrete cooperative stop decisions are subsequently
implemented in common-unordered-worker-failure.md and common-unordered-cooperative-stop.md;
shared/group immediate API, private queue costs and remaining public policy review stay open.

ExecutorCompletion now observes a producer-owned Task with ordered, detachable
Action<Task> registrations. A BCL LinkedList and gate claim notification snapshots;
unique IDisposable handles release pending captured owners without canceling the
source. Reentrant additions follow the claimed batch, callback failures/context
mutations are isolated, and per-registration Tasks describe notification delivery.
Twenty-nine cases verify these contracts, including resolver-style removal and an
actual native timeout cancellation consumer. Native drain reservations settle
rejection and immediate queue removal without Future/Promise result wrappers.
Four progress queue-removal regressions initially timed out, then pass after the
same native removal policy was applied to progress drains. The broader native and
original Promise/scheduler/lifecycle/unordered/NonSticky selection passes 222
cases. See [common-native-completion.md](common-native-completion.md).
Two further lifecycle cases verify real early/late termination callbacks on
GlobalEventExecutor after single-thread and unordered workers stop, matching the
pinned termination Promise affinity rather than inferring it from Task.
Plain Future/Promise callers subsequently migrate to native APIs; final worker policy remains open. Dynamic progress
registration and removal of the progressive public hierarchy are now implemented.

Utilization accounting now coordinates the event-loop writer with the scheduled
monitor's exchange-to-zero through Interlocked.Add. An independent-budget regression
sampled 200242ns from only 200000ns reported before the repair; the fixed I/O and
actual task-batch paths each pass three sampling epochs. Original comments and
auto-scaling workloads/assertions remain. The initial completion-observer Release
suite had the earlier high-load auto-scaling assertion failure (1282 / 1 / 14);
the unchanged isolated Release case passed. The accounting defect is independently
proven; its role in that whole-suite failure is not established. See
[common-utilization-accounting.md](common-utilization-accounting.md).

The initial NonSticky full Debug run reported 1248 passed / 2 failed / 14 skipped
in nonsticky-native-runner-full-debug.trx. Both failures exercised the original
extra runner admission at exact batch exhaustion, including an empty queue.
Restoring that source behavior retained the original assertions; the coupled
selection passed 179 cases and the full Debug/Release final runs both passed
1250 / 0 / 14 in nonsticky-native-runner-full-debug-final.trx and
nonsticky-native-runner-full-release-final.trx. These are historical results,
superseded by the current completion-observer checkpoint.

The first native-unordered-submission full Debug run had one failure in
AutoScalingEventExecutorChooserFactoryTest.testScaleUpDoesNotExceedMaxThreads
(1238 passed / 1 failed / 14 skipped); the simultaneous Release run passed
1239 / 0 / 14. The unchanged autoscaling case passed in an isolated run,
autoscaling-scale-up-investigation.trx. The original workload spins for 35ms,
sleeps for 10ms and is sampled by a 50ms real-time monitor. This evidence does
not identify a production defect or prove that parallel configuration runs caused
the failure. Keep this failed run as evidence; do not disable the assertion or
count it as passing. The full Debug recheck passed 1239 / 0 / 14 in
native-unordered-submission-full-debug-recheck.trx. The timing-sensitive failure's
cause remains unproven and merits review; a successful recheck is not a code fix.

The solution contains only the common library and this test project. Incremental
solution builds also pass in Debug/Release; their zero-warning result reflects
up-to-date compilation, not resolution of existing warnings.

The native lifecycle boundary now exposes one persistent Task per owner through
`Termination` and `ShutdownGracefullyAsync`. All actual common implementations
and C# callers are migrated. Twenty-eight lifecycle/group cases, including nine
new CLR cases, verify wait cancellation, failure classification, context and
completion ownership. All 309 original comments across the ten affected Java
source entries remain preserved. The original unordered shutdown-request timing was
recorded at that checkpoint; the subsequent native termination correction replaces
its early success (common-unordered-termination.md). Final backend and legacy
scheduling API decisions remain pending.
See [common-executor-lifecycle.md](common-executor-lifecycle.md).

The preceding native progress checkpoint passed 1226 cases with 14 skips in both
configurations; native scheduling passed 1204 cases with 14 skips.
The preceding native lifecycle checkpoint passed 1170 cases with 14 skips in both
configurations; its logs remain historical evidence in common-executor-lifecycle.md.
The preceding ordered-multimap checkpoint passed 1161 cases with 14 skips.
Before the ordered-multimap review, the managed-byte checkpoint passed 1132 cases
with 14 skips. Before that review, the native-memory checkpoint passed 1100 cases
with 14 skipped in both configurations. That implementation is now inventoried,
with provider and removed-method comments preserved in common-native-memory.md.
The earlier runtime checkpoint had 1068 passed / 5 failed / 14 skipped; the five
native-memory/platform failures are resolved by real CLR owner/view contracts.
Earlier ObjectCleaner and Task-composition results remain historical evidence.

See [common-test-skips.md](common-test-skips.md) for each category, original
conditions and replacement coverage. The eight ordinary-thread Recycler rows
also execute and pass in RecyclerFastThreadLocalTest with automatic cleanup owners.

Fourteen tests retain upstream disabling or runtime
capability rules (two ordinary-thread removal cases, the CI-only oversized
allocation case, SecurityManager group inheritance on an unsupported runtime,
two JVM type-erasure cases that do not apply to CLR reified generics, and eight
Recycler pooling assumptions on ordinary threads without automatic cleanup).
DefaultPromiseTest (20), native TaskWhenAllPortTest (15), TaskCompletionTransferPortTest (10),
TaskAggregationOwnershipPortTest (6), and ConstantIdentityContractTest (6)
now execute alongside AbstractScheduledEventExecutorTest
(9), ImmediateExecutorTest (2), GlobalEventExecutorTest (6), DefaultThreadFactoryTest
(4 passed/1 skipped), SingleThreadEventExecutorTest (17),
UnorderedThreadPoolEventExecutorTest (5), NonStickyEventExecutorGroupTest (10),
AutoScalingEventExecutorChooserFactoryTest (7), ThreadExecutorMapTest (4),
TypeParameterMatcherTest (7 passed/2 skipped), NettyRuntimeTests (7), MpscIntQueueTest (6),
the CLR-adapted JdkLoggerFactoryTest (1), RecyclerTest (59 passed/8 skipped),
RecyclerFastThreadLocalTest (67), RunInFastThreadLocalThreadExtensionTest (3),
ResourceLeakDetectorTest (3), LeakPresenceDetectorTest (3), ThreadDeathWatcherTest (3),
HashedWheelTimerTest (16), and the utility,
thread-local, address, and logger tests.
The 286 AsciiStringCharacterTest compilation diagnostics are resolved. All 42
pinned character tests and 10 memory tests now execute, including six cached-string
scenarios previously missing from the port. Together with 18 CLR cases, all 70
affected tests pass within both default full runs. Original comments are preserved:
99 source comments across AsciiString/the split comparator, 24 character-test
comments and two memory-test comments, with zero missing.
See [common-ascii-memory.md](common-ascii-memory.md) for native string/span/memory
APIs, lossless byte widening, cache sanitization and executed-Java hash references.

The three address-view scenarios use NativeMemoryView, zero-capacity allocation
uses NativeMemoryOwner and the Java-25 provider test validates deterministic CLR
cleanup. All nine original platform cases execute and pass. The two JVM version
parser cases remain explicitly excluded with scenario/comment provenance in
[common-platform-runtime.md](common-platform-runtime.md).
NativeMemoryContractTest covers 26 ownership, pin, reallocation, borrowing,
quota, alignment, concurrency and GC-fallback cases. Runtime selection has five
cases, including the shared allocator limit. See [common-native-memory.md](common-native-memory.md).

Managed primitive write/copy/fill regressions reproduced 11 stub failures before
repair. Existing array entry points now use bounded CLR memory operations;
AsciiStringUtil directly uses MemoryMarshal for native-order SWAR words/tails.
The eleven dead PlatformDependent0 array stubs and four scalar fallback helpers
are removed. Twelve managed-byte and 20 independent scalar-oracle case tests pass;
the affected Debug selection passes 146 cases. All nine AsciiStringUtil comments
remain beside the corresponding implementation. See [common-heap-memory.md](common-heap-memory.md).

ConcurrentOrderedMultiMap uses standard sorted buckets with atomic compound
operations, native Try/KeyValuePair APIs and snapshot iteration. All 20 original
test-method scenarios are covered by 19 translated cases; five randomized
methods preserve 100 repetitions. Ten native contracts verify concurrency,
ownership claims, reentrant equality, key/default-value boundaries and the
AdaptivePoolingAllocator dirty-chunk fallback. All 29 pass in both full runs.
The source remains in progress: extra public-method purposes, real allocator
integration and performance review remain. Its 138 original comments are preserved
as provenance; no JVM skip-list/updater hierarchy is reproduced.
See [common-ordered-multimap.md](common-ordered-multimap.md).

The first runtime Release run also reproduced a thread-local count failure and
a Promise listener NullReferenceException. ThreadLocalContractTest now resets its
physical-worker map before absolute-count assertions, like the original fixture.
InternalLoggerFactoryTest is exclusive and mocks only its observed category;
unrelated background logger creation falls back to the saved factory, avoiding
null loggers permanently captured during static initialization. The 50-case
Release isolation selection and latest full runs pass these cases. The earlier
auto-scaling timing failure remains open investigation evidence.

ObjectCleaner now uses conditional GC notification and native background pool
dispatch, with an Action API and diagnostic pending count. All three original
tests and eight CLR lifetime/identity/context/concurrency regressions pass in
both default runs. The former weak-queue/helper types have been removed. All 26
source comments are preserved in code or replacement provenance, and all six
test comments remain. See [common-object-cleanup.md](common-object-cleanup.md).

The manifest records 42 verified source files, 22 in progress, 99 pending,
29 CLR replacements and 13 JVM-only decisions (205 source entries). Tests have
56 verified, zero pending and ten not-applicable entries (66 original files).
All 98 verified source/test entries have zero missing required comments.
CompleteFuture, SucceededFuture and FailedFuture now record CLR replacement by
standard Tasks; their 13 original comments and the two removed EventExecutor
factory comments are preserved in common-completed-results.md with zero missing.
The four progressive source entries now record native Task/IProgress replacement;
their 13 original comments and eight removed progressive-plumbing/factory comments
are archived in common-progress-subscriptions.md with zero missing.
The seven removed native provider files also have zero missing provenance comments.
Other touched files remain in progress, including raw address/object-offset APIs,
queue/executor/lifecycle APIs, string/encoding and public API migration. Comment coverage alone is not a completion
count.

The user explicitly authorized CLR-native replacements and omission of Java-only
facilities. LongLongHashMap does not require a standalone CLR port: its current
FastThreadLocalThread consumer uses per-thread scope membership without a shared ID
map. Any necessary long-key mapping can use Dictionary<long,long> with consumer
contracts checked at the use site; Java rehash tests do not apply. RuntimeJvmArgs
and its -XX flag parser have no purpose in a CLR process and are excluded. Their
temporary direct ports were removed. Consumer-specific missing-value, previous-value
and copy-ownership behavior must still be checked where it is needed.

JDK logging is replaced by the existing CLR TraceSource backend. The original
factory creation/name test now asserts InternalDefaultLoggerFactory's native
provider; the upstream abstract interface fixture and CLR formatting/filtering
tests continue to verify shared logging behavior. The seven SLF4J/Log4j adapter
source files and five provider-specific test files have no CLR Java dependency to
wrap and are marked not-applicable. Caller metadata and configuration follow the
native backend; TRACE/DEBUG both map to Verbose. JfrEventSafeTest exercises JDK
Event/RecordingStream and @Enabled defaults, and VirtualThreadCheckTest exercises
Thread.isVirtual/MethodHandle and Java Thread subclassing. Those two tests are
not applicable to CLR Task/ThreadPool and EventSource. The eight JVM-specific
test placeholders, including JNI ClassLoader loading, have been removed; their
original comments and exclusion reasons remain in common-jvm-test-exclusions.md
and the manifest. Native loading and shared logging contracts remain tested.

MpscIntQueue retains its useful bounded primitive ring and atomic batch reservations.
The incomplete generic AtomicArray base and Java field-updater emulation are replaced
by int[], Volatile and Interlocked. Initialize every rounded-capacity slot, including
nonzero empty sentinels, and restore the missing weakPeekReduce operation. Delegates
represent primitive suppliers, consumers and reducers; callback null checks use
ArgumentNullException. Poll waits for an unpublished reserved head using nonblocking
Thread.SpinWait, preserving a pending managed-thread interrupt. Six original cases
and thirteen CLR cases verify FIFO/reuse, validation, weak reduction, publication,
interrupt behavior, and four concurrent producers using offer and fill. All 34 source
comments and the original test comment are preserved. The pinned reduction returns
0 for a zero limit and revisits slots when a full ring's limit exceeds capacity;
both behaviors are recorded and tested. As upstream, fill suppliers must not throw
or return the empty sentinel: invalid supplier output can strand a reservation.

Recycler now follows the pinned shared MPMC, pinned-owner and thread-local guarded
and unguarded modes. Restore owner-local LIFO batches, external FIFO returns, ratio
sampling, capacity normalization, atomic duplicate-recycle guards, detach and unpin.
Guarded handles use ReferenceEquals rather than value equality. Thread-local pooling
requires currentThreadWillCleanupFastThreadLocals, not merely an indexed map. CLR
ThreadState.Stopped distinguishes termination from an unstarted owner. Recyclable
values require reference types, and the ObjectPool factory adapters now enforce that
generic constraint. Stale standalone handle/local-pool/thread-local helpers were
removed; the reviewed state machine is private to Recycler<T>.

ConcurrentQueue with CAS capacity reservations replaces JCTools return queues;
retention is bounded by rounded capacity, with CLR segment allocation instead of
JCTools chunk growth. Owner batches remain separate, as upstream. The debugging
blocking mode uses a lazily grown Queue and uninterruptible monitor with exact
capacity. Private Java MessagePassingQueue operations unused by Recycler are not
recreated. All 134 original Recycler/fast-thread theory rows run: 126 pass and eight
ordinary-thread pooling assumptions skip. Eight additional CLR cases verify identity,
unguarded behavior, batch/external ordering, eight concurrent borrowers, termination,
unpin and cleanup capability. The 149-case recycler/runner selection also passes
with io.netty.recycler.blocking=true. All 47 source, 26 base-test and five fast-test
comments are preserved.

The JUnit invocation extension is an explicit Action helper for xUnit. The three
original normal/repeated/parameterized cases run on a real FastThreadLocalThread;
the original repetition count is one. Inherited fast-thread Recycler cases route
their bodies through that helper. ExceptionDispatchInfo rethrows worker failures
after thread-local cleanup while retaining the original exception and stack. Four
CLR cases verify cleanup before return/rethrow and cleanup-callback failure propagation.
CLR unhandled worker exceptions can terminate the test process, so cleanup exceptions
also reach the caller; an earlier invocation failure retains priority. GC tests use
WeakReference<Thread> and bounded collection polling because CLR Thread is sealed;
all retained-object/unpin/late-recycle assertions remain. ObjectCleaner is now a
CLR runtime replacement described above. The deprecated ResourceLeakException
helper remains pending work.

ResourceLeakDetector uses ConditionalWeakTable conditional values to observe the
tracked resource's lifetime. Retaining a tracker cannot keep its resource alive.
Weak owner registrations unlink on close, suppress finalization when empty, and
rearm when the same resource is tracked again. CLR finalizers only enqueue internal
notifications; reporting and listeners execute on the next tracking caller. This
uses CLR GC/finalization scheduling rather than Java ReferenceQueue timing.
GC.KeepAlive replaces the synchronized reachability fence and never acquires the
resource's monitor. Interlocked/Volatile preserve close, access-record and report
races. Capture managed stacks at creation/access/close time, snapshot hint text
immediately, and use CLR full stack formatting. JIT inlining can remove framework
frames, so remove those frames by identity instead of dropping three caller frames.
Exclusions use declaring-type FullName, including nested CLR types, and normalize
generic definitions so a closed registration matches CLR stack metadata across T.

All three original leak tests pass, including 50 threads and 5,000,000 track/close
pairs. The JVM's 60-second timeout was exceeded in a complete CLR batch; the native
test uses a documented 120-second bound while retaining every iteration, stack and
assertion. Full-batch stress runs took approximately one minute. Hint tests keep their
10-second bounds. Twenty CLR contracts cover retained trackers, live referents,
multiple/rearmed registrations, concurrent close and record/close races, hint
snapshots, report deduplication, reporting-disabled drain, close-stack capture,
sampling and declared-method exclusions. Thirty leak/factory/hint cases pass in
Release and with trackClose=false; 27 applicable cases pass with targetRecords=0.
All 64 detector, five legacy leak, five tracker, three hint and 21 test comments
are preserved. The pinned tracker ToString after a successful close with close
tracking enabled retains the negative-capacity failure; getCloseStackTraceIfAny
is the supported way to inspect the close marker.

ResourceLeakDetectorFactory replaces erased Java reflection with CLR Type and
ConstructorInfo. The old open-generic IsAssignableFrom check rejected all custom
providers; four CLR cases reproduced it. One-parameter generic providers now close
for each requested T, and closed providers work with compatible T. Invariant type
mismatches, constraints, static initialization and constructor failures retain the
default fallback. Modern and deprecated constructors are independent, and the
environment property is captured when the factory is constructed. Eight contracts
pass and all eight original comments are preserved. Generic providers initialize
per closed CLR type when that type is first requested.

LeakPresenceDetector counts resources immediately without waiting for collection.
The nongeneric CLR facade shares the global scope, initializer count and diagnostic
setting across every constructed T. Func and Interlocked replace Supplier and
LongAdder; producer threads must still quiesce before checking a scope. Keep the
counter reset, negative late-release report, no-op access records, forced tracking,
dedicated allocation-prohibition exception and scope selection through the factory.
CLR .cctor detection replaces <clinit>; MethodBase.IsConstructor does not identify
static constructors. The static wrapper checks real stack frames, supports nesting,
restores its count on failure and does not exclude allocations on unrelated threads.
An explicit initializer and field-touching init method preserve the original test
despite CLR BeforeFieldInit timing.

ResourceScope implements IDisposable. Repeated Dispose/close is deliberately
idempotent and cannot reopen the scope; the pinned Java decrement could go negative
and allow allocations after a second close. Closing a leaking scope still changes
its state before throwing, and a late tracker close decrements then throws as in the
source. Creation diagnostics use captured managed stacks and the existing weak
suppressed-exception store, preserving the pinned maximum of seven stacks. All three
original tests and twelve CLR contracts pass in Release, both with default settings
and trackCreationStack=true. The concurrent case retains eight producers and 80,000
track/close pairs. All 16 source comments and the original test comment are preserved.

NativeLibraryLoader is a JVM-only JNI resource loader: Java package shading, helper
.class injection into a target ClassLoader, META-INF/native JAR extraction/duplicate
selection, JNI library identity patching and JVM-exit cleanup. Repository-wide callers
are JNI epoll/kqueue/io_uring, macOS resolver, Quiche and OpenSsl bindings; there is no
CLR consumer of these ClassLoader operations. The loader and its five original tests
are explicitly not-applicable, with the placeholder fixture retained and excluded.
Ordinary native loading uses NativeLibrary.Load through NativeLibraryUtil, replacing
handwritten platform P/Invoke/dlopen branches. Returned handles are caller-owned and
freed through NativeLibrary.Free; absolute mode rejects relative paths. Four CLR
contracts pass in Debug/Release, invoking the real native process-ID export and
checking missing libraries, invalid paths and independent handle release. Native
fixtures ran on Windows net10.0; Unix OS-specific fixtures were not executed here.
All four original helper comments are preserved beside the CLR ownership note.

ThreadDeathWatcher uses native background Thread, ConcurrentQueue, Interlocked
and Volatile while preserving the singleton worker's polling and CAS handoff.
Cancelling a watch matches both thread and task by reference identity, and removes
one duplicate registration. Callback failures do not prevent subsequent notifications;
reentrant watch/unwatch operations retain the original second drain. Suppress CLR
ExecutionContext flow during worker startup so the service does not retain caller
AsyncLocal values, while restoring an already-suppressed caller's state. TimeSpan
waits preserve Java millisecond truncation and zero/unbounded joins; large waits
are chunked into interruptible native Join calls. The factory retains minimum
priority, background status, configurable name prefix and nonsticky group metadata.

ReferenceCountUtil restores deprecated releaseLater overloads, schedules the exact
decrement after caller-thread termination, and restores touch stack exclusions.
Pattern matching preserves non-reference-counted passthrough, forwarded retain/touch
return values, validation order and safe-release exception handling. All three original
watcher tests, ten CLR watcher cases and seven CLR reference-count cases pass in
Release. A fresh process with io.netty.serviceThreadPrefix=porting- passes three
applicable cases. Concurrent registration retains eight producers and 8,000 watches,
half cancelled before owner death. All 32 watcher source, nine watcher test and
14 reference-count utility comments are preserved.

HashedWheelTimer now restores pending-count rollback when start fails and the
post-enqueue shutdown rejection. The original start override and a separate native
thread gate verify termination between startup and enqueue. Public Java dispatch
hooks remain virtual in C#. ConcurrentQueue, Interlocked and Volatile replace the
Java queues, atomic wrappers and field updaters. The native default factory creates
ordinary foreground threads at normal priority and captures the creator's logical
group, without fast-local ownership. Worker/timeout/bucket helpers are internal,
matching the original private classes; worker results are a read-only snapshot.
The unused JDK fixed-pool/callable helper stubs that returned null are removed;
there are no CLR repository consumers. Executors only supplies the native factory
required by this timer; it does not emulate the JDK Executors utility surface.

TimeSpan uses 100 ns ticks and saturates nanosecond conversion instead of wrapping.
Positive submillisecond ticks still clamp to 1 ms. Long native sleeps are chunked
and remain interruptible for stop. Preserve the JVM-specific Windows sleep workaround
comments, while omitting that workaround for CLR Thread.Sleep. IDisposable provides
a using lifetime; finalization after failed construction cannot corrupt instance
counts. Interruption racing with worker termination does not surface a CLR
ThreadStateException. Native stop results are caller-owned HashSets.

All 16 original source cases and 12 CLR contracts pass in Debug/Release. This includes
the cancellation callback method that has only a JUnit Timeout annotation and would
otherwise be omitted from discovery. Preserve 100,000 scheduling operations and the
125-650 ms timing bounds. The original shutdown race and post-stop pending-count
assertion were absent from the previous translation and now run. Additional native
cases cover task/executor failure isolation, callback affinity and identity, default
interface cancellation, partial-constructor finalization, duration saturation,
large waits and interrupted shutdown. The pinned stop behavior retains the pending
count for returned unprocessed tasks and does not invoke their cancellation callbacks
after worker termination; both are tested explicitly. All 63 timer, four Timer,
five TimerTask, seven Timeout and nine original test comments are preserved.

TypeParameterMatcher resolves the constructed CLR BaseType chain without erasing
generic arguments. Native Type.IsInstanceOfType handles actual generic and array
types. Find-cache keys include the requested superclass and parameter name, within
the constructed runtime class, preventing incorrect reuse across different parents.
Seven portable upstream cases and seven CLR contracts pass; JVM-only erased-variable
failures explicitly skip. Enclosing CLR generic arguments and array bindings remain
available. ReflectionUtil's no-op JVM accessibility facade is now retired;
actual CLR reflection operations replace it, while the generic resolver remains.
ThreadExecutorMap's four original wrapping/restoration cases now run, including
the real CLR thread factory. NettyRuntime's seven original configuration/race cases
use a serialized environment-property harness and non-interruptible holder locks.

At the historical Future/Promise adapter checkpoint, completion used TaskCompletionSource as its sole result and
terminal-state owner. Remaining metadata controls cancellation eligibility and
preserves cancellation diagnostics for the Netty observer adapter. State transitions, cancellation/uncancellability, cause
identity, FIFO and reentrant listeners, executor affinity, recursion limits,
blocking detection, timeouts, interruption, progressive notification, typed
listener removal are covered by upstream and
CLR contract tests. Java listener wildcards use identity-preserving CLR adapters;
value-type nulls use default(T), and Java wildcard value widening requires
explicit object/boxing with invariant CLR future types. Those adapters are now removed;
the native replacement and intentional differences are in common-native-future-retirement.md.

Native SubmitAsync accepts Action/Func and CancellationToken and returns Task
without a Java Future/Promise result adapter. Task-returning delegates are unwrapped;
execution-context changes are scoped, and event-loop access after I/O requires
explicit resubmission. Thirty additional CLR cases cover terminal-state observation,
cancellation tokens, native delegate families, cancellation/invocation races,
context flow, early release of captured references and concurrent constant factories.
Before the completion refactor, four of five new state regressions failed.
See common-clr-design.md for the preserved policies and outstanding public API work.

PromiseCombiner, PromiseNotifier, deprecated PromiseAggregator and
PromiseNotificationUtil are now CLR framework replacements and their C# helper
classes have been removed. Native Task.WhenAll and TCS TrySetFromTask preserve
composition/transfer without another Java builder/listener facade. Fault
precedence, exception retention, producer ownership and explicit executor
dispatch differ deliberately from some Java behaviors. All original test-method
decisions and real consumer evidence are recorded in
[common-task-composition.md](common-task-composition.md). The three original test
paths now contain 31 native cases, retaining their original license comments.
Five old CLR combiner/cascade cases are superseded by that native coverage.

AbstractConstant now has sealed identity/description overrides, native identity
hashing and one non-generic Interlocked uniquifier sequence. ConstantPool requires
reference constants. Four of six identity regressions failed before repair; all
six now pass, along with the original registry tests. Native integer comparison
avoids overflow-induced ordering violations. All three AbstractConstant and eight
ConstantPool original comment blocks are preserved.

In the historical legacy Future APIs, Java ExecutionException mapped to AggregateException; sync rethrew the original
exception. Suppressed exceptions use weak exception keys. CLR cannot inspect a
pending interrupt flag, so incomplete interruptible waits observe it with
Sleep(0); uninterruptible waits restore it. Monitor waits round up to millisecond
resolution. The historical CompleteFuture translation consumed an interrupt even
after completion, as its Java await does. That class is now replaced by standard
Tasks: completed native waits do not consume a pending CLR interrupt. The native
tests cover this explicit adaptation; see common-completed-results.md.

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
The historical unordered compatibility scheduling interface inherited Netty Future status/result
contracts. All concrete unordered/shared/ordered scheduling facades and adapters are removed; native
scheduling carries only deadline metadata through IScheduledWork. CLR tests
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

At the historical Java-compatible submission checkpoint, PromiseTask returned Netty IFuture through executor/group
interfaces; native SubmitAsync had its own Task-based work item and shared only
the executor invocation boundary. The old recursive callable generic constraint and QueueingTaskNode
submission path are gone. CLR support implements the inherited JDK invokeAll and
invokeAny contracts, including ordered futures, individual failure retention,
first successful result, timeout/interruption cleanup and cancellation. A completion
queue runs independently of listener dispatch. PromiseTask retains the original
Runnable/Callable distinction, adapter descriptions and completion sentinels. Those Java
submission/bulk interfaces, wrappers and remaining Future/Promise hierarchy are now removed.

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

Termination and graceful shutdown use the primary native Task API:
`Termination` and `ShutdownGracefullyAsync`. Private non-generic TCS owns completion;
Future lifecycle aliases and supplementary Task views are removed. Multithread groups
use context-independent Interlocked completion counting and succeed after every child
signal, including failed children. Native continuations require explicit executor
dispatch for owned state. Twenty-eight lifecycle/group cases cover identity, failure,
wait cancellation/timeout, asynchronous self-shutdown, non-inline continuations,
constructor contexts, construction cleanup and forwarding. Default periods, immutable
views and atomic metric bits remain. See [common-executor-lifecycle.md](common-executor-lifecycle.md).

NonSticky runners now return after rescheduling or emptying their queue and restore
executingThread when rescheduling fails. C# forbids a return inside finally, so a
flag defers those original returns until the block exits. An unbounded ConcurrentQueue
adapter replaces the missing JCTools factory branch; CLR segment allocation differs
from JCTools chunks, and bounded/chunked queue factories remain pending. All ten
original test cases retain their four batch sizes, 10,000 tasks per producer and
5,000 two-submission races. All 30 source and 12 test comments are preserved.

At the earlier Java-compatibility checkpoint UnorderedThreadPoolEventExecutor had a CLR adapter for the inherited JDK scheduled
pool defaults, using dedicated workers and a shared deadline queue. Reviewed Netty
decoration and JDK scheduling/shutdown against local Corretto 21.0.11 src.zip.
All five original tests and 34 CLR contracts pass. Preserve its unusual
termination Task was recorded at this compatibility checkpoint as succeeding
on request. The subsequent native correction waits for drained queue/worker
reservations; see common-unordered-termination.md.
Default shutdown retains delayed one-shot work, drops periodic work, and shutdownNow
returns queued futures without completing them. The pinned Runnable decoration does
not query the backend failure: one-shot outer promises can succeed after backend
failure, and periodic backend failure stops repetition while the outer promise stays
pending. Callable decoration does query the result and unwraps the original failure.
Those source-derived quirks were tested at that checkpoint. Native scheduling now
publishes actual callback failure/cancellation and removes the Java decorators;
see common-native-unordered-scheduling-migration.md. All 21 source and 8 test
comments are preserved; complete inherited JDK API review remains in progress.

At that compatibility checkpoint inherited bulk invocation used a separate JdkFutureTask: running callables remained
cancellable, unlike the decorated Netty PromiseTask. Cancellation waits for interrupt
delivery before a worker can run its next task. Pool configuration now covers core
resize/timeout, keep-alive, native queue removal/reinsertion, shutdown policy changes,
statistics and thread-factory failure/null/reentrant reservations. The inherited
factory setter deliberately keeps the supplied factory unwrapped, as in the pinned
source. A periodic task claimed before a policy change can cancel only its backend,
leaving the outer Netty promise pending; a separate deterministic regression verifies
that behavior rather than asserting cancellation for every race outcome. These
adapters are now removed, running cancellation uses native cooperative claims,
and worker identity is maintained by workerLoop after factory replacement.

The auto-scaling factory now preserves CAS snapshots, pre-increment patience counters,
ramp limits, rotating wake-up selection, minimum/maximum bounds, registered-channel
guards, live immutable metric views and the termination listener. Seven original
tests and eight mock-clock contracts pass. All 43 source and 17 test comments are
preserved. TimeSpan replaces Java duration/TimeUnit; its 100ns granularity means a
saturated initial monitoring delay is rounded down by at most 99ns when scheduled;
the native repeat period retains its exact integer nanoseconds.
Protected-internal metric hooks preserve Java protected package access. The allocated
queue constructor initializes the activity timestamp; updateLastExecutionTime also
refreshes it. The explicit-queue constructor keeps the pinned zero-initialized field.
Seven CLR metrics/property tests cover these contracts.

A very large fixed-rate period reproduced ordinary work blocked behind a future
deadline. Signed-difference comparison, wraparound deadlines and adjustment for an
overdue queue head now follow the JDK clock arithmetic. At that compatibility checkpoint Global awaitInactivity followed
Java's millisecond truncation and zero/unbounded join while chunking large waits.
The current native inactivity checkpoint supersedes the zero/submillisecond convention;
large interruptible waits remain supported.

Next, migrate remaining plain Future/Promise fixtures and waiting/listener
facades to the native Task and notification policies, checking pinned consumers
and preserving original comments. Remaining plain Future/Promise adapters and
final executor backend/worker policies remain; continue collections, strings/encoding and platform
items in dependency order. Default whole tests pass, while module completion remains
open: required source reviews, API migrations and remaining ordered-multimap
public-purpose decisions remain.
ThrowableUtil still cannot replace an already-thrown CLR stack or
capture another managed thread's stack; those limitations remain explicit.

The subsequent CLR worker replacement failure boundary is implemented in
[common-unordered-worker-failure.md](common-unordered-worker-failure.md).
