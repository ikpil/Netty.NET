/*
 * Copyright 2019 The Netty Project
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
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Local;

namespace Netty.NET.Transport.Tests;

public class DefaultEventLoopTest : AbstractSingleThreadEventLoopTest
{
    [Fact(Timeout = 3000)]
    public async Task TestChannelsIteratorNotSupported()
    {
        IEventLoopGroup group = NewEventLoopGroup();
        SingleThreadEventLoop loop = (SingleThreadEventLoop) group.Next();

        try
        {
            IChannel ch = NewChannel();
            await loop.RegisterAsync(ch, TestContext.Current.CancellationToken);

            // CLR: UnsupportedOperationException is NotSupportedException, "thrown when an invoked method is not
            // supported" (https://learn.microsoft.com/en-us/dotnet/api/system.notsupportedexception).
            Assert.Throws<NotSupportedException>(() => loop.RegisteredChannelsIterator());
        }
        finally
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }

    protected override IEventLoopGroup NewEventLoopGroup()
    {
        return new DefaultEventLoopGroup();
    }

    protected override IEventLoopGroup NewAutoScalingEventLoopGroup()
    {
        return new AutoScalingDefaultEventLoopGroup(SCALING_MAX_THREADS, AUTO_SCALING_CHOOSER_FACTORY);
    }

    protected override IChannel NewChannel()
    {
        return new LocalChannel();
    }

    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    protected override Type ServerChannelClass()
    {
        return typeof(LocalServerChannel);
    }

    private sealed class SuspendableDefaultEventLoop : SingleThreadEventLoop
    {
        // CLR: an Executor is an Action<Action> delegate.
        internal SuspendableDefaultEventLoop(IEventLoopGroup parent, Action<Action> executor)
            : base(parent, executor, true, true, DEFAULT_MAX_PENDING_TASKS,
                  RejectedExecutionHandlers.Reject())
        {
        }

        protected override void Run()
        {
            for (;;)
            {
                Action task = TakeTask();
                if (task != null)
                {
                    RunTask(task);
                    UpdateLastExecutionTime();
                }

                // Check if a suspend is requested and we have no more tasks. If so,
                // exit the run() method to allow the suspension to complete.
                if (CanSuspend())
                {
                    break;
                }

                if (ConfirmShutdown())
                {
                    break;
                }
            }
        }
    }

    private sealed class AutoScalingDefaultEventLoopGroup : MultithreadEventLoopGroup
    {
        internal AutoScalingDefaultEventLoopGroup(int nThreads, IEventExecutorChooserFactory chooserFactory)
            : base(nThreads, (Action<Action>) null, chooserFactory)
        {
        }

        protected override IEventExecutor NewChild(Action<Action> executor, params object[] args)
        {
            return new SuspendableDefaultEventLoop(this, executor);
        }
    }
}
