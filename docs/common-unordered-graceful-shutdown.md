# Unordered graceful shutdown admission and drain

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.
Scope: common and its tests; other modules are read-only evidence.

## Evidence and decision

EventExecutorGroup.java separates isShuttingDown from final shutdown. During the
quiet period, new submissions are guaranteed admission and restart the period.
Timeout limits the wait until shutdown, rather than guaranteeing termination of
arbitrary user code. SingleThreadEventExecutor.java validates nonnegative quiet
period and timeout >= quiet period, records execution activity and drains work.
The pinned unordered implementation instead calls shutdown immediately, ignores
both parameters and completes its promise at request time. Its original TODO is
preserved beside the CLR implementation and in the previous scheduling archive.
The CLR follows the documented group contract, not this incomplete implementation.

Pinned direct unordered consumers are the five common unordered fixtures, three
NonStickyEventExecutorGroup scenarios and DefaultChannelPipelineTest's unordered
executor variant (transport, original line 1549). Other module lifecycle consumers
await executor-group shutdown. This change does not port those other modules or
add ordering guarantees to the parallel pool.

## Native lifecycle policy

The persistent Termination Task remains the sole lifecycle result. A graceful
request immediately makes isShuttingDown true. Admission stays open until there
has been no submission or completed worker invocation for the quiet period and
no invocation or thread-creation reservation is active, or until the first
request's timeout expires. Admission checks and the timer use the same gate and
monotonic nanosecond source. Each accepted raw execution, native submission,
native schedule or queue reinsertion records activity. Completion also restarts
quiet waiting, allowing running work to submit follow-up work until timeout.
This explicitly adapts ordered execution activity to multiple concurrent workers.

The first graceful request owns the periods; repeated requests return the same
Task without shortening quiet waiting or extending the timeout. Negative quiet
period and timeout < quiet period throw ArgumentOutOfRangeException without
starting shutdown. Zero quiet period closes admission immediately and drains;
shutdown() and shutdownNow() override any pending quiet wait. The defaults remain
two seconds quiet and fifteen seconds timeout.

At closure, the existing explicit delayed/periodic shutdown policies apply.
Delayed one-shot work is retained; periodic work is canceled after its current
invocation returns, with queued periodic work canceled immediately. The subsequent
configuration review retires the inherited policy toggles; callers withdraw owned
one-shot reservations with tokens. See common-unordered-native-configuration.md.
Retained delayed work can also outlast timeout.
Timeout rejects new work; it does not inject Thread.Interrupt, overwrite a claimed
result or manufacture successful drain. Running invocations and worker/start
reservations must still return before Termination completes. Asynchronous delegate
bodies after their initial invocation yields remain caller-owned operation Tasks,
as documented in common-unordered-termination.md.

A one-shot System.Threading.Timer controls the lifecycle independently of worker
availability. Task.Run/Task.Delay do not replace the executor workers. Timer creation
suppresses ExecutionContext capture, including when flow was already suppressed;
long TimeSpan values saturate nanoseconds and use bounded millisecond timer chunks.
The timer is disposed at final closure or explicit shutdown. Submissions check the
deadline themselves, so a late timer callback cannot extend admission. A callback
already queued at disposal observes the closed state and cannot rearm the timer.
Closure still requires acquiring the pool's gate; arbitrary blocking factory code
inside that gate is not forcibly interrupted.

The mutable queue cannot insert work after admission closes, even while retained
work keeps Termination pending. Removal still transfers invocation responsibility
to the caller; clear cancels owned queued native results before publishing drain.
This closes a lifecycle hole rather than treating a queue handle as a Task result.
A detached periodic handle also cannot requeue into a terminated pool, even with
the continued periodic policy enabled at this historical checkpoint. A separate before-repair regression reproduces
resurrection (unordered-graceful-reentry-before.trx); reentry now cancels the
reservation instead of reopening a completed lifecycle. The broader inherited
public queue/configuration API review was unfinished at this checkpoint; both
surfaces are subsequently retired as documented in common-porting.md.

## Tests and verification

Nine new native lifecycle cases all fail before implementation
(unordered-graceful-before.trx), then pass. They cover argument validation, active
work and submission during quiet waiting, completion activity, original timeout
ownership, rejection after timeout, no interruption or early success, workerless
closure/policy removal, submission activity and explicit shutdown overrides.
Four additional cases cover closed queue reinsertion, detached periodic reentry
and ambient context
collection with ordinary and suppressed flow. The final affected Debug selection
passes 128 cases with zero failures/skips
(unordered-graceful-final-contracts-v3-debug.trx).

Two CLR-only probes are corrected without changing original Java fixture identities:
ShutdownTaskWaitsForAcceptedWorkAfterTheShutdownRequest now requests zero periods
instead of relying on ignored negative arguments; ContinuedPeriodicPolicyRunsAfterShutdownAndCanBeDisabled
also requests zero periods because it specifically tests the post-shutdown policy.
The original five unordered and three NonSticky fixtures retain their identities,
workloads and comments. An intermediate added queue test mistakenly invoked a
far-future detached deadline before it was due; the corrected test uses a due
one-shot, retaining deadline enforcement rather than weakening implementation.

Full default Debug and Release each discover 1378 cases: 1364 passed, zero failed
and 14 unchanged skips on Windows/net10.0 (unordered-graceful-final-full-debug.trx
and unordered-graceful-final-full-release.trx). All 759 non-Porting fixture
identities and all skip identities match the preceding native-future-retirement
checkpoint. The only additions are the 13 new CLR cases; no fixture is removed or
renamed (unordered-graceful-identity-comparison.json). The final comment audit
records zero missing in all 98 verified entries, 21 unordered source comments,
ten EventExecutorGroup comments and eight original unordered test comments
(unordered-graceful-final-comment-audit.json). All implementation paths exist;
git diff --check passes. Existing compiler/analyzer warnings remain. No common
or whole-backend completion is claimed.
The subsequent native queue review removes public mutable handles and maps the
CLR-only transfer/clear probes to owned cancellation and actual rejection/removal;
see common-unordered-native-queue.md. The above counts/identities are the graceful
checkpoint preceding that migration. Native configuration is subsequently implemented
in common-unordered-native-configuration.md. Remaining work includes immediate
interruption and the remaining source/test inventory reviews.

## Next public pool/queue review

An all-module pinned Java search for inherited core/maximum/keep-alive settings,
core timeout, prestart, delayed/periodic shutdown policy setters/getters and
rejection-handler settings has no consumer. getQueue appears only in
UnorderedThreadPoolEventExecutorTest.java line 69, asserting an empty queue during
10000 callbacks. Constructor thread count/factory/rejection parameters and native
executor semantics require independent decisions; absence of inherited setting
consumers does not exclude those constructors or lifecycle rules. Next, replace
unneeded JDK public configuration/mutable queue surfaces with native configuration
and diagnostics that preserve actual consumers. Preserve the original empty-queue
workload and explicitly map CLR-only probes. Internal BCL deadline ordering,
atomic membership, cancellation removal and worker lifetime remain necessary.
The queue decision is now implemented in common-unordered-native-queue.md;
the configuration decision is subsequently implemented in
common-unordered-native-configuration.md. Immediate interruption and private queue
costs remain open.

The subsequent CLR worker replacement failure boundary is implemented in
[common-unordered-worker-failure.md](common-unordered-worker-failure.md).
