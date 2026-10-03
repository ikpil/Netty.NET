/*
 * Copyright 2016 The Netty Project
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
using System.Collections.Generic;

namespace Netty.NET.Common.Concurrent;

/**
 * An {@link EventExecutorChooser} that exposes metrics for observation.
 */
public interface IObservableEventExecutorChooser : IEventExecutorChooser
{
    /**
     * Returns the current number of active {@link EventExecutor}s.
     * @return the number of active executors.
     */
    int ActiveExecutorCount();

    /**
     * Returns a list containing the last calculated utilization for each
     * {@link EventExecutor} in the group.
     *
     * @return an umodifiable view of the executor utilizations.
     */
    IReadOnlyList<AutoScalingUtilizationMetric> ExecutorUtilizations();
}
