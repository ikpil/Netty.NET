/*
 * Copyright 2024 The Netty Project
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
using System.Collections.Generic;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;

namespace Netty.NET.Transport.Channels;

/**
 * {@link IoEventLoopGroup} implementation that will handle its tasks with multiple threads.
 * <p>
 * This group supports advanced thread management strategies, such as dynamic auto-scaling,
 * by providing a custom {@link EventExecutorChooserFactory}. To enable utilization-based
 * auto-scaling, pass an instance of
 * {@link io.netty.util.concurrent.AutoScalingEventExecutorChooserFactory}.
 */
public class MultiThreadIoEventLoopGroup : MultithreadEventLoopGroup, IIoEventLoopGroup
{
    /**
     * Creates a new instance of the {@link MultiThreadIoEventLoopGroup} using the default number
     * of threads and default {@link ThreadFactory}.
     */
    public MultiThreadIoEventLoopGroup(IIoHandlerFactory ioHandlerFactory) : this(0, ioHandlerFactory)
    {
    }

    /**
     /**
     * Creates a new instance of the {@link MultiThreadIoEventLoopGroup} using the default {@link ThreadFactory}.
     *
     * @param nThreads          the number of threads and so {@link EventLoop}s that are created.
     * @param ioHandlerFactory  the {@link IoHandlerFactory} that will be used to create {@link IoHandler} for handling
     *                          IO.
     */
    public MultiThreadIoEventLoopGroup(int nThreads, IIoHandlerFactory ioHandlerFactory)
        : this(nThreads, (Action<Action>)null, ioHandlerFactory)
    {
    }

    /**
     * Create a new instance using the default number of thread.
     *
     * @param threadFactory     the {@link ThreadFactory} that is used.
     * @param ioHandlerFactory  the {@link IoHandlerFactory} that will be used to create {@link IoHandler} for handling
     *                          IO.
     */
    public MultiThreadIoEventLoopGroup(IThreadFactory threadFactory, IIoHandlerFactory ioHandlerFactory)
        : this(0, threadFactory, ioHandlerFactory)
    {
    }

    /**
     * Creates a new instance of the {@link MultiThreadIoEventLoopGroup} using the default number
     * of threads.
     *
     * @param executor          the {@link Executor} that is used.
     * @param ioHandlerFactory  the {@link IoHandlerFactory} that will be used to create {@link IoHandler} for handling
     *                          IO.
     */
    public MultiThreadIoEventLoopGroup(Action<Action> executor,
                                       IIoHandlerFactory ioHandlerFactory)
        : base(0, executor, ioHandlerFactory)
    {
    }

    /**
     * Creates a new instance of the {@link MultiThreadIoEventLoopGroup}.
     *
     * @param nThreads          the number of threads and so {@link EventLoop}s that are created.
     * @param executor          the {@link Executor} that is used.
     * @param ioHandlerFactory  the {@link IoHandlerFactory} that will be used to create {@link IoHandler} for handling
     *                          IO.
     */
    public MultiThreadIoEventLoopGroup(int nThreads, Action<Action> executor,
                                       IIoHandlerFactory ioHandlerFactory)
        : base(nThreads, executor, ioHandlerFactory)
    {
    }

    /**
     * Creates a new instance of the {@link MultiThreadIoEventLoopGroup}.
     *
     * @param nThreads          the number of threads and so {@link EventLoop}s that are created.
     * @param threadFactory     the {@link ThreadFactory} that is used.
     * @param ioHandlerFactory  the {@link IoHandlerFactory} that will be used to create {@link IoHandler} for handling
     *                          IO.
     */
    public MultiThreadIoEventLoopGroup(int nThreads, IThreadFactory threadFactory,
                                       IIoHandlerFactory ioHandlerFactory)
        : base(nThreads, threadFactory, ioHandlerFactory)
    {
    }

    /**
     * Creates a new instance of the {@link MultiThreadIoEventLoopGroup}.
     *
     * @param nThreads          the number of threads and so {@link EventLoop}s that are created.
     * @param executor          the {@link Executor} that is used.
     * @param chooserFactory    the {@link EventExecutorChooserFactory} that is used to choose the
     *                          {@link IoEventLoop} when {@link MultiThreadIoEventLoopGroup#next()} is
     *                          called.
     * @param ioHandlerFactory  the {@link IoHandlerFactory} that will be used to create {@link IoHandler} for handling
     *                          IO.
     */
    public MultiThreadIoEventLoopGroup(int nThreads, Action<Action> executor,
                                       IEventExecutorChooserFactory chooserFactory,
                                       IIoHandlerFactory ioHandlerFactory)
        : base(nThreads, executor, chooserFactory, ioHandlerFactory)
    {
    }

    /**
     * Creates a new instance of the {@link MultiThreadIoEventLoopGroup}.
     *
     * @param nThreads          the number of threads and so {@link EventLoop}s that are created.
     * @param executor          the {@link Executor} that is used.
     * @param ioHandlerFactory  the {@link IoHandlerFactory} that will be used to create {@link IoHandler} for handling
     *                          IO.
     * @param args              extra args that are passed to {@link #newChild(Executor, Object...)} method.
     */
    protected MultiThreadIoEventLoopGroup(int nThreads, Action<Action> executor,
                                          IIoHandlerFactory ioHandlerFactory, params object[] args)
        : base(nThreads, executor, Combine(ioHandlerFactory, args))
    {
    }

    /**
     * Creates a new instance of the {@link MultiThreadIoEventLoopGroup}.
     *
     * @param nThreads          the number of threads and so {@link EventLoop}s that are created.
     * @param threadFactory     the {@link ThreadFactory} that is used.
     * @param ioHandlerFactory  the {@link IoHandlerFactory} that will be used to create {@link IoHandler} for handling
     *                          IO.
     * @param args              extra args that are passed to {@link #newChild(Executor, Object...)} method.
     */
    protected MultiThreadIoEventLoopGroup(int nThreads, IThreadFactory threadFactory,
                                          IIoHandlerFactory ioHandlerFactory, params object[] args)
        : base(nThreads, threadFactory, Combine(ioHandlerFactory, args))
    {
    }

    /**
     * Creates a new instance of the {@link MultiThreadIoEventLoopGroup}.
     *
     * @param nThreads          the number of threads and so {@link EventLoop}s that are created.
     * @param threadFactory     the {@link ThreadFactory} that is used.
     * @param ioHandlerFactory  the {@link IoHandlerFactory} that will be used to create {@link IoHandler} for handling
     *                          IO.
     * @param chooserFactory    the {@link EventExecutorChooserFactory} that is used to choose the
     * @param args              extra args that are passed to {@link #newChild(Executor, Object...)} method.
     */
    // CLR: MultithreadEventExecutorGroup has no (ThreadFactory, EventExecutorChooserFactory) constructor; the
    // factory becomes the ThreadPerTaskExecutor that MultithreadEventExecutorGroup creates for a ThreadFactory.
    protected MultiThreadIoEventLoopGroup(int nThreads, IThreadFactory threadFactory,
                                          IIoHandlerFactory ioHandlerFactory,
                                          IEventExecutorChooserFactory chooserFactory,
                                          params object[] args)
        : base(nThreads, threadFactory == null ? null : new ThreadPerTaskExecutor(threadFactory).Execute, chooserFactory,
            Combine(ioHandlerFactory, args))
    {
    }

    /**
     * Creates a new instance of the {@link MultiThreadIoEventLoopGroup}.
     *
     * @param nThreads          the number of threads and so {@link EventLoop}s that are created.
     * @param executor          the {@link Executor} that is used.
     * @param ioHandlerFactory  the {@link IoHandlerFactory} that will be used to create {@link IoHandler} for handling
     *                          IO.
     * @param chooserFactory    the {@link EventExecutorChooserFactory} that is used to choose the
     * @param args              extra args that are passed to {@link #newChild(Executor, Object...)} method.
     */
    protected MultiThreadIoEventLoopGroup(int nThreads, Action<Action> executor,
                                          IIoHandlerFactory ioHandlerFactory,
                                          IEventExecutorChooserFactory chooserFactory,
                                          params object[] args)
        : base(nThreads, executor, chooserFactory, Combine(ioHandlerFactory, args))
    {
    }

    // The return type should be IoHandleEventLoop but we choose EventLoop to allow us to introduce the IoHandle
    // concept without breaking API.
    protected override IEventExecutor NewChild(Action<Action> executor, params object[] args)
    {
        IIoHandlerFactory handlerFactory = (IIoHandlerFactory)args[0];
        object[] argsCopy;

        if (args.Length > 1)
        {
            argsCopy = new object[args.Length - 1];
            Array.Copy(args, 1, argsCopy, 0, argsCopy.Length);
        }
        else
        {
            argsCopy = EmptyArrays.EMPTY_OBJECTS;
        }

        return NewChild(executor, handlerFactory, argsCopy);
    }

    /**
     * Creates a new {@link IoEventLoop} to use with the given {@link Executor} and {@link IoHandler}.
     *
     * @param executor              the {@link Executor} that should be used to handle execution of tasks and IO.
     * @param ioHandlerFactory      the {@link IoHandlerFactory} that should be used to obtain {@link IoHandler} to
     *                              handle IO.
     * @param args                  extra arguments that are based by the constructor.
     * @return                      the created {@link IoEventLoop}.
     */
    protected virtual IIoEventLoop NewChild(Action<Action> executor, IIoHandlerFactory ioHandlerFactory,
                                   params object[] args)
    {
        return new SingleThreadIoEventLoop(this, executor, ioHandlerFactory);
    }

    /**
     * Sets the {@link SingleThreadIoEventLoop#setIoRatio(int)} for all the {@link SingleThreadIoEventLoop}s
     * managed by this group.
     */
    // CLR: NotSupportedException replaces UnsupportedOperationException.
    public void SetIoRatio(int ioRatio)
    {
        ObjectUtil.CheckInRange(ioRatio, 1, 100, "ioRatio");

        foreach (IEventExecutor e in Iterator())
        {
            if (!(e is SingleThreadIoEventLoop))
            {
                throw new NotSupportedException("Only support by " +
                    nameof(SingleThreadIoEventLoop) + ": " + e);
            }
        }

        foreach (IEventExecutor e in Iterator())
        {
            ((SingleThreadIoEventLoop)e).SetIoRatio(ioRatio);
        }
    }

    public override IIoEventLoop Next()
    {
        return (IIoEventLoop)base.Next();
    }

    // CLR: the default IoEventLoopGroup methods are not members of an implementing class, so the group exposes
    // register(IoHandle), isCompatible(Class) and isIoType(Class) with the interface's default behaviour.
    public Task<IIoRegistration> RegisterAsync(IIoHandle handle)
    {
        return Next().RegisterAsync(handle);
    }

    public bool IsCompatible(Type handleType)
    {
        using var executors = Iterator().GetEnumerator();
        return executors.MoveNext() && ((IIoEventLoop)executors.Current).IsCompatible(handleType);
    }

    public bool IsIoType(Type handlerType)
    {
        using var executors = Iterator().GetEnumerator();
        return executors.MoveNext() && ((IIoEventLoop)executors.Current).IsIoType(handlerType);
    }

    private static object[] Combine(IIoHandlerFactory handlerFactory, params object[] args)
    {
        List<object> combinedList = new List<object>();
        combinedList.Add(handlerFactory);

        if (args != null)
        {
            combinedList.AddRange(args);
        }

        return combinedList.ToArray();
    }
}
