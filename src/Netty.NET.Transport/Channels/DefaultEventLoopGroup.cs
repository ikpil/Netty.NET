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
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
namespace Netty.NET.Transport.Channels;

/**
 * {@link MultithreadEventLoopGroup} which must be used for the local transport.
 */
public class DefaultEventLoopGroup : MultithreadEventExecutorGroup, IEventLoopGroup
{
    /**
     * Create a new instance with the default number of threads and the given {@link ThreadFactory}.
     *
     * @param threadFactory     the {@link ThreadFactory} or {@code null} to use the default
     */
    /**
     * Create a new instance
     *
     * @param nThreads          the number of threads to use
     * @param threadFactory     the {@link ThreadFactory} or {@code null} to use the default
     */
    /**
     * Create a new instance
     *
     * @param nThreads          the number of threads to use
     * @param executor          the Executor to use, or {@code null} if the default should be used.
     */
    public DefaultEventLoopGroup(int threads = 0, IThreadFactory threadFactory = null)
        : base(threads == 0 ? Math.Max(1, Environment.ProcessorCount * 2) : threads, threadFactory) { }
    protected override IEventExecutor NewChild(Action<Action> executor, params object[] args) => new DefaultEventLoop(this, executor);
    public override IEventLoop Next() => (IEventLoop)base.Next();
    public Task RegisterAsync(IChannel channel, CancellationToken cancellationToken = default) => Next().RegisterAsync(channel, cancellationToken);
}
