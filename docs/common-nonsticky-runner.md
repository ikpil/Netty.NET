# Native NonSticky runner admission and shutdown

Baseline: `e66ce34777f9c4a0c57ac74bb97396ca2f54b43c` in `../netty`.

## Evidence and purpose

The nested ordered executor in
`common/src/main/java/io/netty/util/concurrent/NonStickyEventExecutorGroup.java`
serializes FIFO callbacks while allowing different underlying workers to run
successive batches. It excludes already ordered underlying executors. Group
submit delegates to the underlying group; an explicit next() child instead owns
its ordered queue. The pinned tree has no other production use of this wrapper;
its original test exercises 10,000 tasks per producer, four batch sizes, 5,000
two-submission races and affinity restoration after rescheduling rejection.

The original reusable Runnable and atomic NONE/SUBMITTED/RUNNING transitions do
not settle native Tasks when initial runner admission fails or an underlying
queue drops the runner. Four CLR regressions reproduced: a child stuck after
initial rejection, concurrent submissions left pending after that rejection,
silent discard after shutdown, and shutdownNow removal of a runner leaving its
internal submitted Tasks pending. These behaviors are not retained as successful
native admission. The original rescheduling-rejection rule remains: an active
runner continues pending work when a normal batch handoff cannot be admitted.
An exact batch boundary schedules another runner even if the queue is currently
empty. The two existing lifecycle regressions caught an initial omission of that
handoff (nonsticky-native-runner-full-debug.trx); the implementation preserves the
original extra empty runner instead of weakening those assertions.

## Native decision

The ordered child now uses Queue<IRunnable> under a private gate. Queue membership
and runner ownership must change atomically, including whole-batch rejection and
empty-drain races. A ConcurrentQueue alone does not supply those compound changes;
adding a second independently synchronized queue would not remove that gate.
This is a correctness choice, without a measured throughput claim.

Each initial admission or batch handoff receives a distinct RunnerReservation.
Its atomic claim selects start or pre-start invalidation/removal. The reservation
has no Task, TCS, result or Future interface; submitted operations retain their
own sole TCS results. A stale runner can neither execute nor reject a subsequent
reservation. A transient initial rejection detaches that batch and faults every
pending native submission with the original exception, then leaves the child
available for a later admission. Outcomes are settled after releasing the child
gate, and dispatch to the underlying executor also occurs outside that gate.

UnorderedThreadPoolEventExecutor recognizes the internal native work protocol in
execute(), including calls forwarded by another executor. A queue-bound runner
records the actual admitting pool, so immediate-stop checks do not depend on the
visible wrapper's concrete type. Native reservations bypass JdkFutureTask and
PromiseTask. At this checkpoint raw non-native execute and legacy scheduling used their existing
backend. The subsequent native unordered migration removes those result wrappers
and gives raw execution a result-free queue entry; see
common-native-unordered-scheduling-migration.md. Final backend configuration/queue/
shutdown and plain Future fixture review remain open.

- Graceful shutdown drains previously admitted child work, continuing on the
  active worker if the closed pool rejects another batch handoff.
- shutdownNow removal of a queued reservation closes the child and cancels all
  its still-queued native operations. An active child observes its bound pool's
  immediate-stop flag between invocations and cancels remaining work.
- A callback already running may finish and retains its original outcome.
  The current pool still interrupts workers on shutdownNow; removal cancellation
  does not introduce an additional Thread.Interrupt policy. Final worker/backend
  interruption and native cooperative cancellation review remains open.
- An inline underlying executor defers its synchronous handoff claim to an
  iterative loop. A thread-static dispatch marker is scoped and restored; it is
  physical-thread metadata, not AsyncLocal state. Callbacks stay FIFO and the
  batch chain does not recursively grow the stack.
- Executor affinity refers to the currently invoking physical thread. Clearing
  affinity checks the executing reservation, so an old worker cannot clear a
  later worker's publication. inEventLoop(null) is false on CLR.

Original implementation comments remain alongside CLR transition explanations,
including both original done comments and the original empty-queue CAS scenarios.
Those scenarios describe the source race; the native gate implements the
corresponding atomic transition. All 30 comment blocks mapped from the group
source remain present. The original test's 12 comment blocks and workloads remain.

## Verification and remaining scope

The four initial regressions failed before repair. Native tests additionally
exercise queued removal and running graceful/immediate shutdown through direct
and forwarding executors, 10,001 inline callbacks with depth one, and FIFO handoff
between two physical threads. Together these add 11 cases. The affected native
submission, original NonSticky, native scheduling and unordered selection passes
129 cases in nonsticky-native-runner-forwarded.trx. Current full Debug/Release
results are recorded in common-porting.md.
The expanded selection including lifecycle and progress passes 179 cases in
nonsticky-native-runner-final-contracts.trx after restoring the exact-boundary
handoff rule.

Native cancellation/rejection releases queued delegates and context through the
existing submission claim. Legacy queued IFuture work is canceled as a temporary
compatibility path; it does not establish final native listener dispatch or
legacy API equivalence. Public Java-shaped submission/bulk-operation/scheduling
facades, detachable completion observers and final unordered backend decisions
are still incomplete. No whole-module completion is implied by this checkpoint.
