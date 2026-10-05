/*
 * Copyright 2025 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

/**
 * A factory that creates auto-scaling {@link EventExecutorChooser} instances.
 * This chooser implements a dynamic, utilization-based auto-scaling strategy.
 * <p>
 * It enables the {@link io.netty.channel.EventLoopGroup} to automatically scale the number of active
 * {@link io.netty.channel.EventLoop} threads between a minimum and maximum threshold.
 * The scaling decision is based on the average utilization of the active threads, measured over a
 * configurable time window.
 * <p>
 * An {@code EventLoop} can be suspended if its utilization is consistently below the
 * {@code scaleDownThreshold}. Conversely, if the group's average utilization is consistently
 * above the {@code scaleUpThreshold}, a suspended thread will be automatically resumed to handle
 * the increased load.
 * <p>
 * To control the aggressiveness of scaling actions, the {@code maxRampUpStep} and {@code maxRampDownStep}
 * parameters limit the maximum number of threads that can be activated or suspended in a single scaling cycle.
 * Furthermore, to ensure decisions are based on sustained trends rather than transient spikes, the
 * {@code scalingPatienceCycles} defines how many consecutive monitoring windows a condition must be met
 * before a scaling action is triggered.
 */
// CLR adaptation: TimeSpan replaces utilizationWindow/windowUnit. Java inner classes hold explicit owner references; Interlocked/Volatile replace AtomicReference.
public sealed class AutoScalingEventExecutorChooserFactory : IEventExecutorChooserFactory
{

    private static readonly Action NO_OOP_TASK = static () => { };
    private readonly int minChildren;
    private readonly int maxChildren;
    private readonly long utilizationCheckPeriodNanos;
    private readonly double scaleDownThreshold;
    private readonly double scaleUpThreshold;
    private readonly int maxRampUpStep;
    private readonly int maxRampDownStep;
    private readonly int scalingPatienceCycles;

    /**
     * Creates a new factory for a scaling-enabled {@link EventExecutorChooser}.
     *
     * @param minThreads               the minimum number of threads to keep active.
     * @param maxThreads               the maximum number of threads to scale up to.
     * @param utilizationWindow        the period at which to check group utilization.
     * @param windowUnit               the unit for {@code utilizationWindow}.
     * @param scaleDownThreshold       the average utilization below which a thread may be suspended.
     * @param scaleUpThreshold         the average utilization above which a thread may be resumed.
     * @param maxRampUpStep            the maximum number of threads to add in one cycle.
     * @param maxRampDownStep          the maximum number of threads to remove in one cycle.
     * @param scalingPatienceCycles    the number of consecutive cycles a condition must be met before scaling.
     */
    public AutoScalingEventExecutorChooserFactory(int minThreads, int maxThreads, TimeSpan utilizationWindow,
                                                  double scaleDownThreshold,
                                                  double scaleUpThreshold, int maxRampUpStep, int maxRampDownStep,
                                                  int scalingPatienceCycles)
    {
        minChildren = ObjectUtil.CheckPositiveOrZero(minThreads, "minThreads");
        maxChildren = ObjectUtil.CheckPositive(maxThreads, "maxThreads");
        if (minThreads > maxThreads)
        {
            throw new ArgumentException($"minThreads: {minThreads} must not be greater than maxThreads: {maxThreads}");
        }
        utilizationCheckPeriodNanos = AbstractScheduledEventExecutor.ToNanos(ObjectUtil.CheckPositive(utilizationWindow, "utilizationWindow"));
        this.scaleDownThreshold = ObjectUtil.CheckInRange(scaleDownThreshold, 0.0, 1.0, "scaleDownThreshold");
        this.scaleUpThreshold = ObjectUtil.CheckInRange(scaleUpThreshold, 0.0, 1.0, "scaleUpThreshold");
        if (scaleDownThreshold >= scaleUpThreshold)
        {
            throw new ArgumentException(
                    "scaleDownThreshold must be less than scaleUpThreshold: " +
                    scaleDownThreshold + " >= " + scaleUpThreshold);
        }
        this.maxRampUpStep = ObjectUtil.CheckPositive(maxRampUpStep, "maxRampUpStep");
        this.maxRampDownStep = ObjectUtil.CheckPositive(maxRampDownStep, "maxRampDownStep");
        this.scalingPatienceCycles = ObjectUtil.CheckPositiveOrZero(scalingPatienceCycles, "scalingPatienceCycles");
    }

    public IEventExecutorChooser NewChooser(IEventExecutor[] executors)
    {
        return new AutoScalingEventExecutorChooser(this, executors);
    }

    /**
     * An immutable snapshot of the chooser's state. All state transitions
     * are managed by atomically swapping this object.
     */
    private sealed class AutoScalingState
    {
        internal readonly int activeChildrenCount;
        internal readonly long nextWakeUpIndex;
        internal readonly IEventExecutor[] activeExecutors;
        internal readonly IEventExecutorChooser activeExecutorsChooser;
        internal readonly IReadOnlyDictionary<IEventExecutor, long> resumedAt;

        internal AutoScalingState(int activeChildrenCount, long nextWakeUpIndex, IEventExecutor[] activeExecutors,
            IReadOnlyDictionary<IEventExecutor, long> resumedAt)
        {
            this.activeChildrenCount = activeChildrenCount;
            this.nextWakeUpIndex = nextWakeUpIndex;
            this.activeExecutors = activeExecutors;
            this.resumedAt = resumedAt;
            activeExecutorsChooser = DefaultEventExecutorChooserFactory.INSTANCE.NewChooser(activeExecutors);
        }
    }

    private sealed class AutoScalingEventExecutorChooser : IObservableEventExecutorChooser
    {
        private readonly AutoScalingEventExecutorChooserFactory factory;
        private readonly IEventExecutor[] executors;
        private readonly IEventExecutorChooser allExecutorsChooser;
        private AutoScalingState state;
        private readonly IReadOnlyList<AutoScalingUtilizationMetric> utilizationMetrics;

        internal AutoScalingEventExecutorChooser(AutoScalingEventExecutorChooserFactory factory, IEventExecutor[] executors)
        {
            this.factory = factory;
            this.executors = executors;
            List<AutoScalingUtilizationMetric> metrics = new List<AutoScalingUtilizationMetric>(executors.Length);
            foreach (IEventExecutor executor in executors)
            {
                metrics.Add(new AutoScalingUtilizationMetric(executor));
            }
            utilizationMetrics = metrics.AsReadOnly();
            allExecutorsChooser = DefaultEventExecutorChooserFactory.INSTANCE.NewChooser(executors);

            AutoScalingState initialState = new AutoScalingState(factory.maxChildren, 0L, executors,
                new Dictionary<IEventExecutor, long>(ReferenceEqualityComparer.Instance));
            state = initialState;

            var monitoringCancellation = new CancellationTokenSource();
            var monitor = new UtilizationMonitor(this);
            Task utilizationMonitoringTask = GlobalEventExecutor.INSTANCE.ScheduleNative<object>(
                    _ => { monitor.Run(); return null; }, TimeSpan.FromTicks(factory.utilizationCheckPeriodNanos / 100),
                    factory.utilizationCheckPeriodNanos, monitoringCancellation.Token, captureContext: false);
            utilizationMonitoringTask.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(() =>
            {
                _ = utilizationMonitoringTask.Exception;
            });

            if (executors.Length > 0)
            {
                Task termination = executors[0].Termination;
                // CLR adaptation: scheduled-task cancellation is thread-safe;
                // no Future listener or caller context owns this lifecycle action.
                termination.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(() =>
                {
                    _ = termination.Exception;
                    monitoringCancellation.Cancel();
                    monitoringCancellation.Dispose();
                });
            }
        }

        /**
         * This method is only responsible for picking from the active executors list.
         * The monitor handles all scaling decisions.
         */

        public IEventExecutor Next()
        {
            // Get a snapshot of the current state.
            AutoScalingState currentState = Volatile.Read(ref state);

            if (currentState.activeExecutors.Length == 0)
            {
                // This is only reachable if minChildren is 0 and the monitor has just suspended the last active thread.
                // To prevent an error and ensure the group can recover, we wake one up and use the
                // chooser that contains all executors as a safe temporary choice.
                TryScaleUpBy(1);
                return allExecutorsChooser.Next();
            }
            return currentState.activeExecutorsChooser.Next();
        }

        /**
         * Tries to increase the active thread count by waking up suspended executors.
         * This method is thread-safe and updates the state atomically.
         *
         * @param amount    The desired number of threads to add to the active count.
         */
        private void TryScaleUpBy(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            for (; ; )
            {
                AutoScalingState oldState = Volatile.Read(ref state);
                if (oldState.activeChildrenCount >= factory.maxChildren)
                {
                    return;
                }

                int canAdd = Math.Min(amount, factory.maxChildren - oldState.activeChildrenCount);
                List<IEventExecutor> wokenUp = new List<IEventExecutor>(canAdd);
                long startIndex = oldState.nextWakeUpIndex;

                for (int i = 0; i < executors.Length; i++)
                {
                    IEventExecutor child = executors[(int)Math.Abs((startIndex + i) % executors.Length)];

                    if (wokenUp.Count >= canAdd)
                    {
                        break; // We have woken up all the threads we reserved.
                    }
                    if (child is SingleThreadEventExecutor)
                    {
                        SingleThreadEventExecutor stee = (SingleThreadEventExecutor)child;
                        if (stee.IsSuspended())
                        {
                            stee.Execute(NO_OOP_TASK);
                            wokenUp.Add(stee);
                        }
                    }
                }

                if (wokenUp.Count == 0)
                {
                    return;
                }

                // Create the new state.
                List<IEventExecutor> newActiveList = new List<IEventExecutor>(oldState.activeExecutors.Length + wokenUp.Count);
                newActiveList.AddRange(oldState.activeExecutors);
                newActiveList.AddRange(wokenUp);

                // CLR: publish activation time with the same immutable membership snapshot.
                // A resumed worker must have a complete configured window before its
                // utilization can count toward sustained idle/busy patience.
                var resumedAt = new Dictionary<IEventExecutor, long>(oldState.resumedAt, ReferenceEqualityComparer.Instance);
                long resumeTime = executors[0].Ticker().NanoTime();
                foreach (IEventExecutor child in wokenUp) resumedAt[child] = resumeTime;

                AutoScalingState newState = new AutoScalingState(
                        oldState.activeChildrenCount + wokenUp.Count,
                        startIndex + wokenUp.Count,
                        newActiveList.ToArray(), resumedAt);

                if (ReferenceEquals(Interlocked.CompareExchange(ref state, newState, oldState), oldState))
                {
                    return;
                }
                // CAS failed, another thread changed the state. Loop again to retry.
            }
        }

        public int ActiveExecutorCount()
        {
            return Volatile.Read(ref state).activeChildrenCount;
        }

        public IReadOnlyList<AutoScalingUtilizationMetric> ExecutorUtilizations()
        {
            return utilizationMetrics;
        }

        private sealed class UtilizationMonitor
        {
            private readonly AutoScalingEventExecutorChooser chooser;
            private readonly List<SingleThreadEventExecutor> consistentlyIdleChildren;
            private readonly List<SingleThreadEventExecutor> consistentlyBusyChildren;
            private long lastCheckTimeNanos;
            private bool hasCheckTime;
            private long nextCheckTimeNanos;

            internal UtilizationMonitor(AutoScalingEventExecutorChooser chooser)
            {
                this.chooser = chooser;
                consistentlyIdleChildren = new List<SingleThreadEventExecutor>(chooser.factory.maxChildren);
                consistentlyBusyChildren = new List<SingleThreadEventExecutor>(chooser.factory.maxChildren);
                if (chooser.executors.Length > 0)
                    nextCheckTimeNanos = unchecked(chooser.executors[0].Ticker().NanoTime() +
                        chooser.factory.utilizationCheckPeriodNanos);
            }

            public void Run()
            {
                if (chooser.executors.Length == 0 || chooser.executors[0].IsShuttingDown())
                {
                    // The group is shutting down, so no scaling decisions should be made.
                    // The lifecycle listener on the terminationFuture will handle the final cancellation.
                    return;
                }

                // Calculate the actual elapsed time since the last run.
                long now = chooser.executors[0].Ticker().NanoTime();
                long totalTime;

                if (!hasCheckTime)
                {
                    // On the first run, use the configured period as a baseline to avoid skipping the cycle.
                    totalTime = chooser.factory.utilizationCheckPeriodNanos;
                }
                else
                {
                    // On subsequent runs, calculate the actual elapsed time.
                    totalTime = unchecked(now - lastCheckTimeNanos);
                }

                // CLR adaptation: sample once per scheduled measurement window.
                // Catch-up callbacks before the next boundary must not reset
                // activity or count as consecutive idle cycles. Keep the original
                // fixed-rate phase; rebasing every boundary to a late invocation
                // would slow monitoring and alias periodic activity reports.
                if (hasCheckTime && totalTime > 0 && unchecked(now - nextCheckTimeNanos) < 0)
                    return;

                // Always update the timestamp for the next cycle.
                // The original comment applies to accepted windows and invalid
                // clock intervals; skipped catch-up callbacks do not start a cycle.
                lastCheckTimeNanos = now;
                hasCheckTime = true;

                if (totalTime <= 0)
                {
                    // Skip this cycle if the clock has issues or the interval is invalid.
                    nextCheckTimeNanos = unchecked(now + chooser.factory.utilizationCheckPeriodNanos);
                    return;
                }

                long elapsedDeadline = unchecked(now - nextCheckTimeNanos);
                if (elapsedDeadline >= 0)
                {
                    // A delayed sample consumes the elapsed window once. Advance
                    // to the first future boundary without iterating missed slots.
                    // Signed-distance arithmetic also supports clock wraparound.
                    long period = chooser.factory.utilizationCheckPeriodNanos;
                    nextCheckTimeNanos = unchecked(now + (period - elapsedDeadline % period));
                }

                consistentlyIdleChildren.Clear();
                consistentlyBusyChildren.Clear();

                AutoScalingState currentState = Volatile.Read(ref chooser.state);

                for (int i = 0; i < chooser.executors.Length; i++)
                {
                    IEventExecutor child = chooser.executors[i];
                    if (!(child is SingleThreadEventExecutor))
                    {
                        continue;
                    }

                    SingleThreadEventExecutor eventExecutor = (SingleThreadEventExecutor)child;

                    double utilization = 0.0;
                    if (!eventExecutor.IsSuspended())
                    {
                        long activeTime = eventExecutor.GetAndResetAccumulatedActiveTimeNanos();

                        if (activeTime == 0)
                        {
                            long lastActivity = eventExecutor.GetLastActivityTimeNanos();
                            long idleTime = now - lastActivity;

                            // If the event loop has been idle for less time than our utilization window,
                            // it means it was active for the remainder of that window.
                            if (idleTime < totalTime)
                            {
                                activeTime = totalTime - idleTime;
                            }
                            // If idleTime >= totalTime, it was idle for the whole window, so activeTime remains 0.
                        }

                        utilization = Math.Min(1.0, (double)activeTime / totalTime);

                        // Metrics still consume/publish actual activity in a partial resume
                        // window. Only the sustained-load decision waits for a full window;
                        // no activity is invented and subsequent patience semantics are unchanged.
                        bool completeResumeWindow = !currentState.resumedAt.TryGetValue(eventExecutor, out long resumeTime) ||
                            unchecked(now - resumeTime) >= chooser.factory.utilizationCheckPeriodNanos;
                        if (!completeResumeWindow)
                        {
                            eventExecutor.ResetIdleCycles();
                            eventExecutor.ResetBusyCycles();
                        }
                        else if (utilization < chooser.factory.scaleDownThreshold)
                        {
                            // Utilization is low, increment idle counter and reset busy counter.
                            int idleCycles = eventExecutor.GetAndIncrementIdleCycles();
                            eventExecutor.ResetBusyCycles();
                            if (idleCycles >= chooser.factory.scalingPatienceCycles &&
                                eventExecutor.GetNumOfRegisteredChannels() <= 0)
                            {
                                consistentlyIdleChildren.Add(eventExecutor);
                            }
                        }
                        else if (utilization > chooser.factory.scaleUpThreshold)
                        {
                            // Utilization is high, increment busy counter and reset idle counter.
                            int busyCycles = eventExecutor.GetAndIncrementBusyCycles();
                            eventExecutor.ResetIdleCycles();
                            if (busyCycles >= chooser.factory.scalingPatienceCycles)
                            {
                                consistentlyBusyChildren.Add(eventExecutor);
                            }
                        }
                        else
                        {
                            // Utilization is in the normal range, reset counters.
                            eventExecutor.ResetIdleCycles();
                            eventExecutor.ResetBusyCycles();
                        }
                    }

                    chooser.utilizationMetrics[i].SetUtilization(utilization);
                }

                int currentActive = currentState.activeChildrenCount;

                // Make scaling decisions based on stable states.
                if (consistentlyBusyChildren.Count != 0 && currentActive < chooser.factory.maxChildren)
                {
                    // Scale Up, we have children that have been busy for multiple cycles.
                    int threadsToAdd = Math.Min(consistentlyBusyChildren.Count, chooser.factory.maxRampUpStep);
                    threadsToAdd = Math.Min(threadsToAdd, chooser.factory.maxChildren - currentActive);
                    if (threadsToAdd > 0)
                    {
                        // Reset the counters of the children that justified this decision so that they need
                        // another full patience period of sustained load before triggering a further scale-up.
                        foreach (SingleThreadEventExecutor busyChild in consistentlyBusyChildren)
                        {
                            busyChild.ResetBusyCycles();
                            busyChild.ResetIdleCycles();
                        }
                        chooser.TryScaleUpBy(threadsToAdd);
                        // State change is handled by tryScaleUpBy, no need for rebuild here.
                        return; // Exit to avoid conflicting scale down logic in the same cycle.
                    }
                }

                bool changed = false; // Flag to track if we need to rebuild the active executors list.
                if (consistentlyIdleChildren.Count != 0 && currentActive > chooser.factory.minChildren)
                {
                    // Scale down, we have children that have been idle for multiple cycles.

                    int threadsToRemove = Math.Min(consistentlyIdleChildren.Count, chooser.factory.maxRampDownStep);
                    threadsToRemove = Math.Min(threadsToRemove, currentActive - chooser.factory.minChildren);

                    for (int i = 0; i < threadsToRemove; i++)
                    {
                        SingleThreadEventExecutor childToSuspend = consistentlyIdleChildren[i];
                        if (childToSuspend.TrySuspend())
                        {
                            // Reset cycles upon suspension so it doesn't get immediately re-suspended on wake-up.
                            childToSuspend.ResetBusyCycles();
                            childToSuspend.ResetIdleCycles();
                            changed = true;
                        }
                    }
                }

                // If a scale-down occurred, or if the actual state differs from our view, rebuild.
                if (changed || currentActive != currentState.activeExecutors.Length)
                {
                    RebuildActiveExecutors();
                }
            }

            /**
             * Atomically updates the state by creating a new snapshot with the current set of active executors.
             */
            private void RebuildActiveExecutors()
            {
                for (; ; )
                {
                    AutoScalingState oldState = Volatile.Read(ref chooser.state);
                    List<IEventExecutor> active = new List<IEventExecutor>(oldState.activeChildrenCount);
                    foreach (IEventExecutor executor in chooser.executors)
                    {
                        if (!executor.IsSuspended())
                        {
                            active.Add(executor);
                        }
                    }
                    IEventExecutor[] newActiveExecutors = active.ToArray();

                    // If the number of active executors in our scan differs from the count in the state,
                    // another thread likely changed it. We use the count from our fresh scan.
                    // The nextWakeUpIndex is preserved from the old state as this rebuild is not a scale-up action.
                    AutoScalingState newState = new AutoScalingState(
                            newActiveExecutors.Length, oldState.nextWakeUpIndex, newActiveExecutors, oldState.resumedAt);

                    if (ReferenceEquals(Interlocked.CompareExchange(ref chooser.state, newState, oldState), oldState))
                    {
                        break;
                    }
                }
            }
        }
    }
}
