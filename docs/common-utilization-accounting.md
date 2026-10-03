# CLR utilization-window accounting

Baseline: `e66ce34777f9c4a0c57ac74bb97396ca2f54b43c` in `D:/workspace/netty`.

## Evidence and decision

SingleThreadEventExecutor records task-batch and I/O active time. The event loop
owns reporting, while AutoScalingEventExecutorChooserFactory's separate scheduled
monitor consumes the counter with an atomic exchange-to-zero. The pinned Java
source uses volatile read/add/write for reporting and AtomicLongFieldUpdater
getAndSet for consumption. Its non-atomic reporting note does not make that
compound addition atomic relative to the monitor.

Actual reporting consumers include transport SingleThreadIoEventLoop and the NIO,
epoll and kqueue I/O handlers. The total reported duration must be accounted for
once across consecutive sampling windows. Copying volatile addition into CLR
Volatile.Read/Write preserves a race rather than that useful contract.

The CLR regression uses one reporting thread and one concurrent sampling thread,
with an independently known total of 200,000 one-nanosecond reports. Before the
repair, its sampled total was 200,242: a value already consumed by the monitor
could be written back and counted again. Conversely, unsynchronized read/add/write
can lose an addition at a reset boundary. Evidence: utilization-accounting-before.trx.

Both reportActiveIoTime and the timed runAllTasks batch now use Interlocked.Add.
The monitor keeps Interlocked.Exchange. These operations share one atomic counter;
each addition belongs to one side of a reset boundary. Reporting remains owned by
the event loop; this is not permission to invoke other event-loop state from
arbitrary threads. Last-activity timestamps retain their original publication rules.
All original source comments remain, with nearby CLR concurrency explanations.

The regression now covers I/O reporting and actual task batches using an independent
mock clock. Each path runs three concurrent sampling epochs; all totals equal the
known budget. Both cases pass in utilization-accounting-after.trx. The earlier
native completion/progress, auto-scaling, lifecycle, SingleThreadEventExecutor and
scheduler selection passed 150 cases with the first I/O regression; the second
task-batch row is included in the final default suite.

## Distinct whole-suite failure

Before this repair, native-completion-full-release.trx had one existing failure in
AutoScalingEventExecutorChooserFactoryTest.testScaleUpDoesNotExceedMaxThreads:
1282 passed / 1 failed / 14 skipped, 1297 total. The matching Debug run passed
1283 / 0 / 14. The unchanged Release case passed alone in
native-completion-autoscaling-investigation.trx. Its original workload spins for
35ms, sleeps for 10ms, and is sampled by a 50ms real-time monitor.

The accounting regression proves a concrete counter defect and its repair.
It does not establish that defect as the cause of this timing-sensitive whole-suite
assertion. No original workload or assertion was disabled or weakened. Keep that
failed run as evidence and keep the cause unproven unless a causal reproducer is
obtained. Final default configuration results are in common-porting.md.
No throughput or allocation improvement is claimed without measurements.

The subsequent common-autoscaling-monitor-windows.md review independently proves
and repairs repeated catch-up sampling through configured phase boundaries.
It preserves fixed-rate cadence and the original workloads/waits/assertions;
it does not identify that defect as the cause of these historical failures.

The first accounting full Debug run instead failed the native observer-only
cancellation test (1284 passed / 1 failed / 14 skipped, 1299 total). Its 100ms delay
did not guarantee a pending producer before WaitAsync; a completed Task wins over
an already-canceled observation token. The regression now explicitly occupies the
worker until observer cancellation is checked and then verifies the producer's
unchanged successful result. Evidence: native-completion-accounting-full-debug.trx
and native-completion-accounting-final-contracts.trx (99 coupled cases passed).
That test correction is distinct from the production accounting repair.
