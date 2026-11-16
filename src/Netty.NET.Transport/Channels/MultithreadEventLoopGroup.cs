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
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Transport.Channels;

/**
 * Abstract base class for {@link EventLoopGroup} implementations that handles their tasks with multiple threads at
 * the same time.
 */
public abstract class MultithreadEventLoopGroup : MultithreadEventExecutorGroup, IEventLoopGroup
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(MultithreadEventLoopGroup));

    private static readonly int DEFAULT_EVENT_LOOP_THREADS;

    static MultithreadEventLoopGroup()
    {
        DEFAULT_EVENT_LOOP_THREADS = Math.Max(1, SystemPropertyUtil.GetInt(
                "io.netty.eventLoopThreads", NettyRuntime.AvailableProcessors() * 2));

        if (logger.IsDebugEnabled())
        {
            logger.Debug("-Dio.netty.eventLoopThreads: {}", DEFAULT_EVENT_LOOP_THREADS);
        }
    }

    /**
     * @see MultithreadEventExecutorGroup#MultithreadEventExecutorGroup(int, Executor, Object...)
     */
    protected MultithreadEventLoopGroup(int nThreads, Action<Action> executor, params object[] args)
        : base(nThreads == 0 ? DEFAULT_EVENT_LOOP_THREADS : nThreads, executor, args)
    {
    }

    /**
     * @see MultithreadEventExecutorGroup#MultithreadEventExecutorGroup(int, ThreadFactory, Object...)
     */
    protected MultithreadEventLoopGroup(int nThreads, IThreadFactory threadFactory, params object[] args)
        : base(nThreads == 0 ? DEFAULT_EVENT_LOOP_THREADS : nThreads, threadFactory, args)
    {
    }

    /**
     * @see MultithreadEventExecutorGroup#MultithreadEventExecutorGroup(int, Executor,
     * EventExecutorChooserFactory, Object...)
     */
    protected MultithreadEventLoopGroup(int nThreads, Action<Action> executor, IEventExecutorChooserFactory chooserFactory,
                                     params object[] args)
        : base(nThreads == 0 ? DEFAULT_EVENT_LOOP_THREADS : nThreads, executor, chooserFactory, args)
    {
    }

    protected override IThreadFactory NewDefaultThreadFactory()
    {
        return new DefaultThreadFactory(GetType(), ThreadPriority.Highest);
    }

    public override IEventLoop Next()
    {
        return (IEventLoop)base.Next();
    }

    protected abstract override IEventExecutor NewChild(Action<Action> executor, params object[] args);

    public virtual Task RegisterAsync(IChannel channel, CancellationToken cancellationToken = default)
    {
        return Next().RegisterAsync(channel, cancellationToken);
    }
}
