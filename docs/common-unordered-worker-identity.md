# Unordered worker identity across factory replacement

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.
Scope: common and tests; other modules are read only.

## Source evidence and decision

UnorderedThreadPoolEventExecutor.java registers workers in its eventLoopThreads
set through an AccountingThreadFactory installed only by its constructors.
Its inherited ScheduledThreadPoolExecutor.setThreadFactory replaces that wrapper
without reinstalling accounting. The pinned implementation therefore reports
false from inEventLoop on workers created by a subsequently installed factory.
The prior CLR probe reproduced this quirk. It is not a useful native scheduling
contract: a callback executing on an executor worker must be recognizable as
executor-owned regardless of which factory created its Thread.

A pinned all-module setThreadFactory search finds the two constructor installs
and AbstractMicrobenchmark's inherited factory mapping. No Netty production
consumer requires replacement workers to lose identity. EventExecutor.inEventLoop
and the original unordered worker test require recognition during executor work;
DefaultPromise's deadlock/listener dispatch, native completion/progress dispatch
and scheduled callbacks use that recognition. Other modules are not implemented
by this change. The absence of an inherited setter call is not alone the basis
for exclusion; the worker-affinity requirement establishes the correction.

CLR Thread is sealed. A thread registry, not a Thread subclass, carries worker
ownership. Registration and finally removal now belong to workerLoop itself.
Both original and replacement factories start the same loop, including workers
started while an existing worker is still active. The configured factory is
stored directly and getThreadFactory returns that same object. Constructor and
setter null validation remain. Factory prefix/suffix code outside task.run does
not acquire event-loop identity, and terminated threads are removed from the
registry. A concurrent dictionary permits external affinity queries during pool
transitions without introducing another lock ordering with the pool gate.

The nested accounting wrapper and unused top-level AccountingThreadFactory.cs
duplicate are removed. The latter had a private constructor and no consumers;
it was an orphaned translation of the Java nested helper, not an independent
Netty API. No original Java comment was removed: that nested Java implementation
contains no comments, and the surrounding source documentation stays mapped.

The CLR-only ReplacingFactoryUsesTheInheritedSetterWithoutReapplyingAccounting
probe becomes ReplacingFactoryPreservesNativeWorkerIdentity. Its false-affinity
assertion intentionally becomes true. This records a source bug correction,
not a claim that the inherited JDK setter behavior was preserved. Original Java
fixture names and scenarios are unaffected.

## Verification and remaining work

Four new CLR cases check configured factory identity, original/replacement
factory callback scope, and simultaneous old/new worker recognition. Their
before run has three failures and one pass (unordered-worker-identity-before.trx):
the original factory is hidden by the wrapper and replacement scheduled callbacks
lose affinity. The repaired affected Debug selection passes all 139 cases with
zero failures/skips (unordered-worker-identity-final-contracts-debug.trx), including
native scheduling, completion/progress, original unordered fixtures, pool probes
and termination contracts.

Scope tests explicitly join the factory Thread and wait for its suffix marker.
They do not claim that Termination joins custom factory suffix code or an async
delegate body after it yields. Unordered concrete legacy scheduling/results,
raw execute's JDK wrapper,
remaining configuration decisions and graceful-shutdown parameter policy are
were still incomplete at this checkpoint. The subsequent unordered native migration
removes the concrete Java scheduler/result and raw JDK wrappers; see
common-native-unordered-scheduling-migration.md. Final configuration/queue/shutdown
and plain Future fixture review remain open. This correction does not complete common.

Whole default Debug and Release each discover 1354 cases on Windows/net10.0:
1340 passed / zero failed / 14 unchanged skips. Evidence:
unordered-worker-identity-full-debug.trx and unordered-worker-identity-full-release.trx.
All 759 non-Porting fixture identities and all skipped identities match the
preceding ordered-scheduler checkpoint in both configurations. Within Porting,
only the one explicitly corrected factory probe is renamed and four new cases
are added. No source exclusion/new skip is introduced; existing warnings are not
claimed resolved. All 21 unordered source and eight original test comments remain;
all 105 verified entries have zero missing, and all manifest implementation paths
exist (unordered-worker-identity-comment-audit.json).

At this checkpoint the next work was to replace concrete unordered schedule callers and queue/configuration
probes with native Task results and owned cancellation. The fake-success Runnable
and pending periodic outer-promise cases must instead verify real native failure
and cancellation. Queue tests must use membership identity separately from the
Task result. Remove obsolete result adapters only after their consumers migrate,
including the raw execute backend and direct JdkFutureTask probes; do not keep
inherited APIs solely for fixture syntax. Review native factory failure admission,
shutdown policy and cancellation cleanup independently of the Java decorators.
