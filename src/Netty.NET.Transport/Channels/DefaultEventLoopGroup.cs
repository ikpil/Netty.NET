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
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Transport.Channels;

/**
 * {@link MultithreadEventLoopGroup} which must be used for the local transport.
 */
// CLR: the Java class carries a bare @Deprecated annotation without a replacement; it stays ported for the
// local transport and the Java tests that construct it.
public class DefaultEventLoopGroup : MultithreadEventLoopGroup
{
    /**
     * Create a new instance with the default number of threads.
     */
    public DefaultEventLoopGroup() : this(0)
    {
    }

    /**
     * Create a new instance
     *
     * @param nThreads          the number of threads to use
     */
    public DefaultEventLoopGroup(int nThreads) : this(nThreads, (IThreadFactory)null)
    {
    }

    /**
     * Create a new instance with the default number of threads and the given {@link ThreadFactory}.
     *
     * @param threadFactory     the {@link ThreadFactory} or {@code null} to use the default
     */
    public DefaultEventLoopGroup(IThreadFactory threadFactory) : this(0, threadFactory)
    {
    }

    /**
     * Create a new instance
     *
     * @param nThreads          the number of threads to use
     * @param threadFactory     the {@link ThreadFactory} or {@code null} to use the default
     */
    public DefaultEventLoopGroup(int nThreads, IThreadFactory threadFactory) : base(nThreads, threadFactory)
    {
    }

    /**
     * Create a new instance
     *
     * @param nThreads          the number of threads to use
     * @param executor          the Executor to use, or {@code null} if the default should be used.
     */
    public DefaultEventLoopGroup(int nThreads, Action<Action> executor) : base(nThreads, executor)
    {
    }

    protected override IEventExecutor NewChild(Action<Action> executor, params object[] args)
    {
        return new DefaultEventLoop(this, executor);
    }
}
