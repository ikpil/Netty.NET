/*
 * Copyright 2015 The Netty Project
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

using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Pool;

namespace Netty.NET.Transport.Tests;

internal sealed class CountingChannelPoolHandler : IChannelPoolHandler
{
    private int _channelCount, _acquiredCount, _releasedCount;
    // CLR: observe the asynchronous cancellation cleanup before asserting the original counters.
    internal readonly TaskCompletionSource SecondRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void ChannelCreated(IChannel channel) => Interlocked.Increment(ref _channelCount);
    public void ChannelReleased(IChannel channel)
    {
        if (Interlocked.Increment(ref _releasedCount) == 2)
            SecondRelease.TrySetResult();
    }
    public void ChannelAcquired(IChannel channel) => Interlocked.Increment(ref _acquiredCount);
    public int ChannelCount => Volatile.Read(ref _channelCount);
    public int AcquiredCount => Volatile.Read(ref _acquiredCount);
    public int ReleasedCount => Volatile.Read(ref _releasedCount);
}
