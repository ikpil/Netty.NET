/*
 * Copyright 2012 The Netty Project
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
namespace Netty.NET.Transport.Channels;

public class DefaultEventLoop : SingleThreadEventLoop
{
    private readonly ConcurrentDictionary<IChannel, byte> _channels = new(ReferenceEqualityComparer.Instance);
    public DefaultEventLoop() : this(null, new ThreadPerTaskExecutor(new DefaultThreadFactory(typeof(DefaultEventLoop))).Execute) { }
    public DefaultEventLoop(IEventExecutorGroup parent, Action<Action> executor) : base(parent, executor, true) { }
    internal void ChannelRegistered(IChannel channel) => _channels.TryAdd(channel, 0);
    internal void ChannelUnregistered(IChannel channel) => _channels.TryRemove(channel, out _);
    public override int RegisteredChannels() => _channels.Count;
    public override IEnumerable<IChannel> RegisteredChannelsIterator()
    {
        if (!InEventLoop()) throw new InvalidOperationException("Registered channels must be inspected on their event loop.");
        return Array.AsReadOnly(_channels.Keys.ToArray());
    }
    protected override void Run()
    {
        for (; ; )
        {
            var task = TakeTask();
            if (task != null)
            { RunTask(task); UpdateLastExecutionTime(); }
            if (ConfirmShutdown())
                break;
        }
    }
}
