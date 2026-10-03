# Unordered deadline queue removal costs

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.
Scope: the concrete unordered executor's private membership implementation.

## Required behavior and CLR decision

The pinned UnorderedThreadPoolEventExecutor delegates deadline membership to JDK
ScheduledThreadPoolExecutor. Its decorated tasks forward getDelay/compareTo to
their underlying scheduled tasks. The five original unordered fixtures exercise
execution, scheduling and 10000 empty-queue checks; three NonSticky fixtures use
this executor. An all-module search also finds a transport pipeline fixture, but
no production constructor call requiring a public mutable queue or a custom heap.
The queue API retirement and cancellation boundary are recorded in
[common-unordered-native-queue.md](common-unordered-native-queue.md).

The CLR executor still needs deadline ordering, admission-sequence tie breaking,
signed-clock distance comparison, reference-identity membership, cancellation
withdrawal and atomic state publication under the existing pool gate. A reservation
has at most one queued membership; periodic work requeues after invocation claims
remove it. The Task result belongs to its native producer, independently of membership.

Previously remove(Work) copied every stored element/priority pair to an array,
searched by reference, cleared the heap and reinserted every survivor. Even a
missing reservation copied the entire queue. This is unnecessary for the project's
net10.0 target. The installed reference XML exposes PriorityQueue.Remove with an
explicit equality comparer. The matching
[.NET 10.0.7 implementation](https://github.com/dotnet/runtime/blob/v10.0.7/src/libraries/System.Collections/src/System/Collections/Generic/PriorityQueue.cs)
scans for the entry, repairs the existing heap and clears the removed reference.

The executor now calls that BCL operation with ReferenceEqualityComparer.Instance.
Existing stored priorities, gate, worker claim, cancellation result and drain
publication remain in place. No Java heap/index map, parallel membership registry,
new public queue handle or extra result owner is introduced. Removal is still
linear in queue size; this change does not promise bounded cancellation latency.

## Comparative probe

Run tools/Measure-UnorderedQueueCosts.ps1 with -LibraryPath pointing to the explicit
Release DLL to compare, and a distinct -EvidenceName. The standalone net10.0 probe
under tools/queue-costs is outside the solution/test compilation. Generated build
outputs, run logs, DLL hashes and result JSON remain in ignored artifacts.

The probe compares the former snapshot/rebuild algorithm, direct BCL removal and
a SortedSet adapter. Before measurement, each independently checks survivors
against offset/ID sorting, compact deadlines spanning signed-clock wrap, equal
deadline ties, repeated removal and a distinct object whose Equals claims every
entry equal. The tree adapter also checks actual reference identity for a found
key. These candidate checks do not test the CLR pool's worker scheduling.

Queue sizes are 64, 1024 and 16384. Fill/drain measures admission and earliest
claim together, including backing growth; reuse-fill-drain first populates/drains
the queue outside the interval, retaining capacity. Each removal interval removes
32 seeded scattered entries, then attempts the same 32 absent identities.
Allocation and elapsed time are divided by 2N or 64 operations respectively.
Setup and reference workload construction are outside the measurement interval.

A separate public-executor interval measures 32 seeded owner-token cancellations
among 64/1024/4096 one-day native schedules. A null-returning constructor factory
keeps them pending without worker timing noise. Admission, CTS creation, result
verification and StopAsync cleanup are outside this interval. Actual cancellation
and remaining counts must be correct before a sample is accepted.

Each shape has two warmups and seven measured samples. Report median, minimum,
maximum and current-thread managed allocation per operation. Tiered compilation
is disabled only for the probe process, then the shell environment is restored.
This prevents JIT tier transitions from favoring later candidates in a short run.
An early exploratory adapter also accidentally captured the old algorithm's LINQ
closure on the direct-removal branch; separating the two methods removed that
measurement-only allocation before the final comparisons. Exploratory logs remain
available but are not used as final evidence.

These are bounded single-thread comparisons on an ordinary development machine,
not scheduler throughput, cross-thread allocation, GC pause, overload latency or
transport performance measurements. The public interval includes native callback
and lifecycle publication cost. SortedSet is a candidate adapter, not a measured
replacement of the full executor; a real replacement would have to retain immutable
priority keys during periodic deadline mutation as well as identity removal.

## Measured result and choice

Two final before/after series run sequentially: one after the full Debug suite and
one after both full configurations finish, with no full test run concurrent with
either probe. Windows 10.0.26300, x64,
AMD Ryzen 7 5800X (8 cores/16 logical processors), SDK 10.0.203 and .NET 10.0.7;
Stopwatch frequency 10000000. This is an ordinary workstation without CPU affinity,
exclusive machine reservation or cross-machine statistical comparison.
All final aggregate rows (medians and extrema) are retained in
[common-unordered-queue-costs.csv](common-unordered-queue-costs.csv).

| Pending schedules | Before median range across two series, us/entry | After median range, us/entry | Before current-thread bytes/entry | After bytes/entry |
| --- | ---: | ---: | ---: | ---: |
| 64 | 1.309-1.403 | 0.166-0.169 | 1412.8 | 80 |
| 1024 | 24.997-26.025 | 0.966-1.056 | 24452.8 | 80 |
| 4096 | 116.116-211.616 | 2.953-3.563 | 98180.8 | 80 |

Elapsed medians vary substantially between series for the allocation-heavy old
4096-entry path; no single speedup factor or isolated-machine result is claimed.

The revised BCL candidate allocates zero current-thread managed bytes in both
removal and capacity-reuse fill/drain intervals. The real public cancellation
path still allocates 80 bytes/entry in this workload; zero-allocation scheduler
claims would be incorrect. Initial fill/drain includes backing-array growth.

At 16384 entries in the after-matrix series, heap reuse fill/drain costs 123.7 ns/op,
versus 150.1 ns/op and 24 bytes/op for the tree (48 bytes per insertion, with
one insert and one earliest claim per entry). Conversely, the tree's scattered
hit/miss removal is much faster: 0.261 us/op versus 23.125 us/op for BCL scanning.
The prior rebuild candidate costs 358.486 us/op and about 393207 bytes/op there.
The measured cancellation-heavy tree advantage remains real; it is not presented
as a failure of SortedSet or evidence that heaps always win.

Keep the existing BCL heap and remove its avoidable snapshot/rebuild. It retains
compact array storage, capacity reuse and the established deadline/periodic
boundaries while materially reducing actual token-cancellation cost. There is no
pinned production workload demonstrating that a cancellation-dominated tree and
additional fixed-key membership design should replace the executor's queue.
Reopen that choice if actual consumers show large cancellation-heavy queues or
gate-contention latency; these measurements do not dismiss that case. No custom
Java priority queue is justified by this unordered scope.

Final evidence: artifacts/unordered-queue-costs-before-isolated and
artifacts/unordered-queue-costs-after-isolated, followed by the -before-validated
and -after-validated directories. The final candidate check uses a distinct
object with the same key as a still-present survivor, including the tree case.
The before DLL is the preceding
cooperative-stop checkpoint's compiled Release library; the after DLL contains
this working-tree removal change. Both evidence files record sourceHead f6d2a7f
as checkout context, not as a claim that the changed DLL is committed at that SHA.
Their SHA256 values are respectively
6418A63682B756A31C4B12920B42C5A9A54F0664723F05AA6CC587F7B020163C and
ECBDCDFA315E77AA44E6142992B6F0029BB26A48146308336B498BB96CE1E126.

## Verification and remaining scope

Two new CLR theory rows cancel scattered workerless submissions/schedules, checking
every original Task identity, surviving membership count and original cancellation
token after each withdrawal. A third holds one actual worker, withdraws mixed queued
submissions/schedules, and verifies survivors' deadline order and successful results.
Existing admission/claim races, periodic work, stop callback reentry, worker factory
failure and original fixtures remain in the coupled selection. Initial affected
Debug passes 174; the final selection including actual survivor invocation passes
175, both with zero failures/skips. No original Java fixture or comment is changed.

The unordered queue's local removal choice is implemented and measured in this
scope. Shared/group immediate API review, other ordered/internal queue consumers,
contention/backpressure decisions, remaining native public API and all pending
source reviews still prevent common completion. The next feature is the shared/group
immediate-stop API review against pinned lifecycle consumers. Final full-suite,
identity and inventory evidence is recorded in common-porting.md. Whole Debug and
Release each discover 1412 / pass 1398 / fail zero / skip 14 on Windows/net10.0
(unordered-queue-remove-full-debug.trx and unordered-queue-remove-full-release.trx).
All 759 non-Porting and 14 skip identities match the cooperative-stop checkpoint;
only the three CLR rows are added, with matching Debug/Release names/outcomes
(unordered-queue-remove-identity-comparison.json). All 98 verified comment entries,
21 unordered source, eight original unordered fixture and ten group-contract
comments have zero missing. All 271 pinned files match the manifest and all
implementation paths exist (unordered-queue-remove-comment-audit.json).
Both full runs use --artifacts-path artifacts/queue-removal-validation to preserve
Rider's ordinary Debug DLL lease. Existing compiler/analyzer warnings remain.
