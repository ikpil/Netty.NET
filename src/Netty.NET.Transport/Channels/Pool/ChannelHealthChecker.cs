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

using System;
using System.Threading.Tasks;

namespace Netty.NET.Transport.Channels.Pool;

// CLR: a single asynchronous operation is represented by Func<IChannel, Task<bool>>.
// Health checks are invoked on the channel's event loop; asynchronous results are dispatched
// back to that loop before channel ownership or user callbacks are changed.
/**
 * Called before a {@link Channel} will be returned via {@link ChannelPool#acquire()} or
 * {@link ChannelPool#acquire(Promise)}.
 */
/**
 * {@link ChannelHealthChecker} implementation that checks if {@link Channel#isActive()} returns {@code true}.
 */
/**
 * Check if the given channel is healthy which means it can be used. The returned {@link Future} is notified once
 * the check is complete. If notified with {@link Boolean#TRUE} it can be used {@link Boolean#FALSE} otherwise.
 *
 * This method will be called by the {@link EventLoop} of the {@link Channel}.
 */
public static class ChannelHealthChecker
{
    public static readonly Func<IChannel, Task<bool>> Active = channel => Task.FromResult(channel.IsActive);
}
