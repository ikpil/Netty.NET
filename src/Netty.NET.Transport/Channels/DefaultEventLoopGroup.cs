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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
namespace Netty.NET.Transport.Channels;

/**
 * {@link MultithreadEventLoopGroup} which must be used for the local transport.
 */
public class DefaultEventLoopGroup : MultithreadEventExecutorGroup, IIoEventLoopGroup
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

    /**
     * {@link IoEventLoopGroup} implementation that will handle its tasks with multiple threads.
     * <p>
     * This group supports advanced thread management strategies, such as dynamic auto-scaling,
     * by providing a custom {@link EventExecutorChooserFactory}. To enable utilization-based
     * auto-scaling, pass an instance of
     * {@link io.netty.util.concurrent.AutoScalingEventExecutorChooserFactory}.
     */
    /// <summary>Creates a group whose chooser may suspend and resume idle workers.</summary>
    public DefaultEventLoopGroup(int threads, IThreadFactory threadFactory, IEventExecutorChooserFactory chooserFactory)
        : base(threads == 0 ? Math.Max(1, Environment.ProcessorCount * 2) : threads,
            threadFactory == null ? null : new ThreadPerTaskExecutor(threadFactory).Execute,
            chooserFactory ?? throw new ArgumentNullException(nameof(chooserFactory)), true) { }

    /**
     * Creates a new {@link IoHandler} instance.
     *
     * @param ioExecutor        the {@link ThreadAwareExecutor} for the {@link IoHandler}.
     * @return                  a new {@link IoHandler} instance.
     */
    /// <summary>Creates one completion backend per worker using the supplied factory.</summary>
    public DefaultEventLoopGroup(Func<CompletionIoHandler> ioHandlerFactory, int threads = 0, IThreadFactory threadFactory = null)
        : base(threads == 0 ? Math.Max(1, Environment.ProcessorCount * 2) : threads, threadFactory,
            ioHandlerFactory ?? throw new ArgumentNullException(nameof(ioHandlerFactory))) { }

    protected override IEventExecutor NewChild(Action<Action> executor, params object[] args)
    {
        if (args.Length != 0 && args[0] is Func<CompletionIoHandler> factory)
            return new DefaultEventLoop(this, executor, false,
                factory() ?? throw new InvalidOperationException("The I/O handler factory returned null."));
        return new DefaultEventLoop(this, executor, args.Length != 0);
    }
    public override IEventLoop Next() => (IEventLoop)base.Next();
    public virtual Task RegisterAsync(IChannel channel, CancellationToken cancellationToken = default) => Next().RegisterAsync(channel, cancellationToken);

    public Task<IoRegistration> RegisterIoAsync(IIoHandle handle, CancellationToken cancellationToken = default) =>
        Next() is IIoEventLoop loop ? loop.RegisterIoAsync(handle, cancellationToken)
            : Task.FromException<IoRegistration>(new NotSupportedException("The selected loop does not support I/O handles."));

    // Like the source, inspect the first worker without advancing the selection cursor.
    public bool IsCompatible(Type handleType) => Iterator().FirstOrDefault() is IIoEventLoop loop && loop.IsCompatible(handleType);
    public bool IsIoType(Type handlerType) => Iterator().FirstOrDefault() is IIoEventLoop loop && loop.IsIoType(handlerType);
}
