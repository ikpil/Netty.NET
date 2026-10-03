# Native unordered worker configuration and shutdown policy

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.
Scope: common and tests; other modules provide read-only consumer evidence.

## Source evidence

UnorderedThreadPoolEventExecutor.java inherits ScheduledThreadPoolExecutor. Its own
four constructors take a thread count, ThreadFactory and optional rejection handler;
the original class does not declare mutable core/maximum/keep-alive/prestart or
delayed/periodic shutdown settings. An all-module pinned Java search for these
settings finds no Netty consumer. setThreadFactory occurs in the unordered
constructors only to install AccountingThreadFactory. The other factory-setting
match in microbench AbstractMicrobenchmark.java is on a different executor.
All-module pool statistics searches also find no consumer.

Actual unordered construction is in five common unordered tests, three NonSticky
tests and transport DefaultChannelPipelineTest.java line 1549. Those consumers need
parallel invocation, deadlines, result/cancellation ownership and shutdown. The
original constructor configuration remains useful; it is not excluded merely
because other inherited settings have no consumer. Dedicated local workers remain
necessary; changing global ThreadPool settings would not configure this executor.

## Native API and implementation

The four constructor overloads remain, with the C# argument named workerCount.
Negative values, null factory and null rejection delegate fail before admission.
Worker count, factory and raw rejection delegate are readonly for the pool's
lifetime. The factory may implement its own stateful thread creation; worker
membership belongs to the native loop, independently of factory identity or
prefix/suffix code. Native submissions still fault on rejection even if the
constructor's raw-work handler discards. No mutable options object or replacement
JDK facade is introduced.

Workers start on admission. Positive counts bound concurrent invocations and keep
idle workers until shutdown. Zero retains the constructor's transient single-worker
behavior: an empty queue retires it after the inherited default 10ms idle wait;
a future deadline keeps that worker available until execution or cancellation.
The private loop now waits for that deadline directly instead of waking to retry
an irrelevant empty-queue timeout. Admission and replacement honor the same fixed
limit. Factory/start reservations and rollback still participate in actual drain.

| Surface | Decision |
| --- | --- |
| Core/maximum setters and getters | Remove; constructor owns the fixed worker count. |
| Keep-alive and core-timeout methods | Remove; retain only the zero-count default idle behavior privately. |
| Prestart methods | Remove; admission starts workers. |
| Factory/rejection get/set methods | Remove; retain actual constructor arguments and native loop identity. |
| Delayed/periodic shutdown get/set methods | Remove; retain native default policy and owned cancellation. |
| Largest/completed/total counters | Remove unused inherited statistics and their private accounting. |
| isTerminating | Use admission/lifecycle state and the persistent Termination Task. |
| Current counts | PendingTaskCount, WorkerCount and ActiveWorkerCount are momentary readonly observations. |

WorkerCount includes pool-owned threads still retiring, but excludes a factory
reservation before it returns a Thread. ActiveWorkerCount describes claimed
invocations, not asynchronous bodies after they yield. PendingTaskCount includes
future deadlines and excludes active invocations. Separate reads are not an atomic
snapshot or an admission/capacity promise.

At graceful quiet/timeout closure or shutdown(), accepted one-shot work is retained
and periodic work cannot reenter. A currently executing periodic invocation may
finish before its lifecycle Task becomes canceled; shutdown does not interrupt that
invocation or invent early drain. Queued periodic reservations cancel immediately.
Callers withdraw their own one-shots with CancellationToken, including after
admission closes. shutdownNow() removes owned queued work and retains the separately
unfinished legacy interruption policy. Timeout closes admission rather than
guaranteeing that arbitrary user code or a retained deadline has finished.

## Explicit CLR-only probe decisions

Original Java fixture names, workloads, waits, assertions and comments remain.
The following inherited-setting probes were added for CLR during earlier porting;
their replacement or exclusion is explicit, rather than claimed as identical JDK
API behavior. Removed mutation scenarios have no corresponding Netty consumer.

| Previous CLR probe | Native replacement |
| --- | --- |
| ContinuedPeriodicPolicyRunsAfterShutdownAndCanBeDisabled | ClosingAdmissionStopsPeriodicReentryAfterItsRunningInvocation |
| OwnerCancellationPreventsPeriodicReentryAfterShutdown | ClosureCancelsPeriodicReservationsBeforeOwnerCancellation |
| DisablingDelayedPolicyDropsFutureWorkButKeepsAlreadyDueWork | OwnerCancellationDropsFutureWorkAndKeepsAcceptedDueWork |
| DelayedPolicyCanChangeAfterShutdownHasAlreadyStarted | OwnerCancellationWithdrawsADelayedReservationAfterAdmissionCloses |
| CoreResizeRetiresIdleWorkersWithoutInterruptingActiveWork | ConstructorWorkerLimitBoundsOverlapWithoutInterruptingActiveWork |
| CoreTimeoutRetiresIdleWorkersAndKeepsOneForDelayedWork | PositiveWorkerCountStartsLazilyAndRetainsIdleWorkersUntilShutdown |
| ZeroKeepAliveDoesNotHoldTheQueueLockWhileDelayedWorkRemains | ZeroWorkerCountKeepsFutureDeadlinesAndAllowsImmediateWork |
| ReplacingFactoryPreservesNativeWorkerIdentity | StatefulConstructorFactoryCreatesSuccessiveNativeWorkers |
| CompletedTaskStatisticsIncludeRawExecuteAndScheduledSubmissions | PendingAndActiveCountsDistinguishRawAndNativeInvocations |
| NullFactoryResultCanRecoverAfterShutdownWhenDelayedWorkRemains | NullFactoryRetainedWorkIsCanceledByImmediateShutdown |
| InvalidConfigurationPreservesTheExistingPoolSettings | ConstructorRejectsInvalidWorkerCountFactoryAndHandler |
| ShutdownPolicyRemovalCompletesWithoutCreatingAWorker | ImmediateShutdownSettlesAWorkerlessDeadlineWithoutCreatingAWorker |
| WorkerlessPoolClosesAfterQuietPeriodAndSettlesRemovedDeadlines | WorkerlessPoolClosesAfterQuietPeriodAndRetainsDeadlinesUntilOwnerCancellation |
| ConfiguredFactoryIdentityIsIndependentOfWorkerAccounting | ConstructorFactoryCreatesThreadsIndependentlyOfWorkerAccounting |
| ReplacementFactoryKeepsOldAndNewWorkersRecognizedTogether | StatefulConstructorFactoryKeepsConcurrentWorkersRecognizedTogether |
| NativeScheduledCallbackHasAffinityOnlyInsideTheWorkerLoop(replaceFactory) | Same affinity scenario using direct/forwarded constructor factories (forwardFactory); two CLR row identities change. |

Rejection probes now supply the discard delegate to the constructor. Other count
probes use the native properties. Factory reentrancy waits for the actual raw
callback's counter instead of a removed total-completion statistic. shutdownNow
handle resurrection checks no longer turn on a removed continued-periodic policy;
the removed handles still cannot run callbacks or revive the pool.

The initial 233-case selection passes 232 and fails the newly mapped running-periodic
probe: it incorrectly assumes immediate Task cancellation at admission closure.
The queued/currently-running distinction above corrects that assumption without
changing production cancellation. Preserve unordered-native-configuration-contracts-debug.trx.
Final affected Debug passes all 233 / zero failures / zero skips in
unordered-native-configuration-final-contracts-debug.trx. The final full suite also
covers subsequent removal of the now-unused private prestart result helper.

## Remaining work

Full default Debug and Release on Windows/net10.0 each discover 1386 cases:
1372 passed / zero failed / 14 unchanged skips (unordered-native-configuration-full-debug.trx
and unordered-native-configuration-full-release.trx). All 759 non-Porting names and
all skip identities match the preceding auto-scaling checkpoint. Only the 15 CLR
method names and two theory row identities listed above change; Debug/Release names
and outcomes match (unordered-native-configuration-identity-comparison.json).
All 98 verified comment entries have zero missing, including 21 unordered source,
eight original unordered test and ten group-contract comments. All implementation
paths exist and the inventory matches all 271 pinned source/test files
(unordered-native-configuration-comment-audit.json). git diff --check passes;
existing compiler/analyzer warnings remain.

The inherited configuration/diagnostics decision is implemented, not deferred by
moving the old methods to an internal interface. All original comments remain in
the mapped implementation or pinned provenance. Full matrix, fixture identity and
comment/inventory evidence are recorded in common-porting.md after validation.
Worker replacement failure is subsequently repaired in common-unordered-worker-failure.md.
Immediate interruption, private queue
costs and the remaining common source/runtime design reviews still need work.
No common or complete unordered-backend claim is made by this unit.
