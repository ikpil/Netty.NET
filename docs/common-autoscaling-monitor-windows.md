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

## Resumed-worker measurement eligibility

At baseline 8d16bf2, an isolated copy with in-memory tracing repeats the unchanged
original testScaleUpDoesNotExceedMaxThreads body. The seventh invocation fails;
iteration-06.csv and autoscaling-load-probe.trx retain it. The 50ms monitor samples
the resumed child at actual intervals 47.0048ms, 47.0246ms and 64.0973ms. Its wake
request/decision occurs at 15,755,180,900ns, its no-op task completes at
15,755,456,700ns, and highLoad is set at 15,817,708,900ns. The third low sample
requests suspension at 15,913,307,600ns. Its first 35.0005ms I/O report arrives
2.9942ms later, at 15,916,301,800ns. The first accepted interval was shorter than
one configured period since activation, but already consumed idle patience.
The counters reach three before the resumed worker can publish that report.

The exact pinned Java utilization/counter decision block was extracted and
executed with those three recorded inputs, producing the same 0.005867/0/0
utilizations and third-sample suspension eligibility. This is a decision replay
with simple input/counter stubs, not a claim that a Java runtime produced the
CLR trace. See autoscaling-resume-java-decision.txt and
artifacts/autoscaling-load-validation/TraceDecisionOracle.java. The original
untraced earlier failures cannot retrospectively be assigned this same cause.

The native membership snapshot now also owns a reference-keyed resumed-at map.
Scale-up copies it, records the monitored clock's activation time, and publishes
it with membership through the existing CAS. Rebuilds retain those times; a later
resume replaces the previous epoch. Already-active initial children keep the
original initial-window behavior. While less than one configured period has
elapsed since resume, the monitor still samples/resets activity and publishes
actual utilization, but resets idle/busy patience instead of counting that sample.
Once eligible, the original thresholds, pre-increment patience, ramp bounds and
channel guards apply. This changes decision eligibility, not measured activity,
fixed-rate phase or the elapsed-time denominator. The map is bounded by the
executor set and copied only during activation; it is never mutated after
publication. Signed differences support zero timestamps and clock wraparound.

Six controlled cases fail against the baseline and pass after the change:
representative short-window/batched-report replay (ordinary time, signed wrap
and a zero resume timestamp), low-utilization tasks still reaching suspension,
partial-window metrics without busy patience, and a second resume after rebuilding
the snapshot. The original seven tests' body, waits, load, thresholds, assertions
and comments remain unchanged. The isolated trace-enabled harness subsequently
passes forty repetitions of the original scenario (autoscaling-resume-fixed-probe.trx).
The harness and trace code live only under ignored artifacts; no production
tracing remains. Summary: autoscaling-resume-trace-summary.json.

This repair covers the demonstrated premature-resuspension path. Forty traced
passes and full-suite passes do not establish timing stability under arbitrary
worker/report delay, and do not prove the cause of an untraced earlier failure.
Future transport integration must retain accurate operation-owned I/O reports.

Final validation: affected Debug 131 passed; full Debug/Release each 1477 passed, zero failed and the same 14 skips (1491 discovered). All 759 non-Porting and prior case identities/outcomes remain; only six new resume contracts are added. All 114 reviewed comment entries have zero missing, including 43 factory and 17 original fixture comments; all 271 inventory paths match. Evidence: autoscaling-resume-full-debug.trx, autoscaling-resume-full-release.trx, autoscaling-resume-identity-comparison.json, autoscaling-resume-comment-audit.json and autoscaling-resume-inventory-summary.json. Windows/net10.0, SDK 10.0.203/runtime 10.0.7; broader native runtime/common work remains open.
