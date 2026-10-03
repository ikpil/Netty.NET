# Auto-scaling monitoring windows under delayed execution

Baseline: e66ce34777f9c4a0c57ac74bb97396ca2f54b43c in D:/workspace/netty.
Scope: common and its tests; other modules are read-only evidence.

## Evidence and retained contract

AutoScalingEventExecutorChooserFactory.java documents average utilization over a
configured monitoring window, with consecutive windows providing scaling patience.
The pinned monitor runs on GlobalEventExecutor at a fixed rate. It samples and
resets accumulated activity on every positive elapsed interval, including overdue
callbacks that execute consecutively after a delay. Several nearly empty intervals
can therefore count as a sustained idle trend and suspend an active executor.
This is an implementation defect relative to the documented sustained-window
policy, rather than a JVM requirement to reproduce in C#.

An all-module pinned search finds chooser/metric consumers in
EventExecutorChooserFactory.java and MultithreadEventExecutorGroup.java, a public
transport constructor in MultiThreadIoEventLoopGroup.java and its tests, the shared
testsuite AbstractSingleThreadEventLoopTest.java factory (100ms window), and
IoUringIoHandlerConfig.java's thread migration documentation. The original common
fixture uses 50ms monitoring, 35ms activity reports and 10ms sleeps. Preserve the
consumer's fixed-rate cadence, min/max and ramp bounds, channel/suspension guards,
pre-increment patience semantics and native lifecycle ownership.

Five new controlled-clock cases fail before repair: short catch-up callbacks can
suspend an executor, partial callbacks consume the active-time budget too soon,
delayed execution is counted repeatedly, and an accepted timestamp of zero is
mistaken for first execution (autoscaling-window-before.trx: 10 pass / 5 fail).
The signed-clock-range row also verifies a real wraparound boundary after repair.

## Native decision

The private monitor retains the fixed-rate schedule and tracks the next configured
window boundary using its monitored executor's ticker. After an accepted sample,
it advances to the first future boundary in the existing phase. Multiple missed
slots produce one actual elapsed-time sample. Further callbacks before that
boundary neither sample/reset activity nor change metrics, patience or executor
state. Advancing missed slots uses signed-distance and remainder arithmetic;
no loop over missed deadlines or public scheduler option is introduced.

Small normal differences in execution time do not restart the phase. A late sample
may be followed by a slightly shorter actual interval at the next scheduled
boundary; that interval remains eligible. The actual elapsed interval remains the
utilization denominator, including delayed windows. This is one sample per
scheduled window, not a claim that every actual interval is exactly the configured
duration or a new sliding-window estimator. Bounds, fallback activity and upper
clamping remain as in the pinned implementation.

An explicit initialization flag distinguishes a valid zero timestamp from no
sample. Signed subtraction supports clock wraparound. Invalid nonpositive elapsed
intervals retain the original skip/rebase behavior and reset the next boundary to
the rebased clock. Child shutdown still skips monitoring; the same owned token
cancels the native monitoring Task after child termination without capturing caller
ExecutionContext. Public fixed-rate/fixed-delay scheduling behavior is unchanged.

## Rejected timing changes

The first attempted gate required a full configured duration since the last actual
sample. That shifted the effective cadence whenever an ordinary callback was a
little early, failing five original initial-scale-down scenarios
(autoscaling-window-contracts-debug.trx). Changing only the private monitor to
fixed delay restored initial scale-down but failed two original scale-up scenarios
(autoscaling-window-final-contracts-debug.trx).

A temporary trace of the unchanged scale-up workload measured actual fixed-delay
intervals of approximately 62-64ms. A single 35ms report then produced utilization
near 0.55, with occasional doubled reports, preventing consecutive high samples.
The isolated failure and trace are retained in autoscaling-window-timing-trace.trx
and autoscaling-window-timing-investigation.txt. This evidence concerns the rejected
fixed-delay implementation, not the cause of the older real-time failure.
All temporary tracing code is removed from the final implementation.

A sixth controlled case independently exposes phase drift: a 1% late sample must
not move the next scheduled boundary. It fails against the rejected elapsed-only
gate (autoscaling-window-cadence-before.trx) and passes with phase preservation.
No original wait, workload, threshold, assertion or fixture identity is changed.

## Verification and remaining work

The final affected Debug selection passes 97 cases with zero failures/skips
(autoscaling-window-phase-contracts-debug.trx). It includes all seven original
auto-scaling fixtures, all 16 controlled chooser/accounting cases and coupled
Global, scheduling, SingleThread and group lifecycle tests.
The original factory's 43 comments and the original fixture's 17 comments are
preserved beside the mapped implementation; CLR decision comments are additional.
Full default Debug and Release on Windows/net10.0 each discover 1386 cases:
1372 passed / zero failed / 14 unchanged skips (autoscaling-window-full-debug.trx
and autoscaling-window-full-release.trx). All 759 non-Porting names and skip
identities match the preceding commit checkpoint; only the six new CLR clock/phase
rows are added, and all Debug/Release outcomes match
(autoscaling-window-identity-comparison.json). All 98 verified comment entries have
zero missing, with all implementation paths present and all 271 pinned source/test
files inventoried (autoscaling-window-comment-audit.json). git diff --check passes;
existing compiler/analyzer warnings remain. UnaryPromiseNotifier's independent
native replacement decision also preserves both original comments and uses the
ten existing transfer cases in these full runs; see common-task-composition.md.

The retained unordered-native-queue-full-release.trx failure in
testScaleUpDoesNotExceedMaxThreads prompted this review. The deterministic cases
prove the catch-up defect and its repair; they do not prove that it caused that
particular real-time failure. Successful real-time reruns alone do not establish
timing stability. The monitor is still subject to late execution and batched I/O
reports. The subsequent inherited unordered configuration decision is implemented
in common-unordered-native-configuration.md. Further public API/backend review and
the remaining source decisions remain required; this unit does not finish common.
