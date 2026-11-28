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
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Factorial;

/**
 * Handler for a client-side channel.  This handler maintains stateful
 * information which is specific to a certain channel using member variables.
 * Therefore, an instance of this handler can cover only one channel.  You have
 * to create a new handler instance whenever you create a new channel and insert
 * this handler to avoid a race condition.
 */
// CLR: BlockingQueue/LinkedBlockingQueue is BlockingCollection (a ConcurrentQueue by default), take() is Take()
// and offer() is TryAdd(). Take() waits in a blocking wait, which Thread.Interrupt() ends with
// ThreadInterruptedException, the counterpart of InterruptedException, so getFactorial() keeps its retry loop and
// re-interrupts the thread. Writes return a Task (ChannelFuture); a listener is a synchronous continuation on the
// channel's event loop (Common's EventExecutorTaskScheduler), as a Java listener runs there, and future.channel()
// is ctx.Channel because a Task has no channel. The lambda numberSender reads the instance's ctx, so it is
// assigned in the constructor (a C# field initializer cannot reference the instance).
public class FactorialClientHandler : SimpleChannelInboundHandler<BigInteger>
{
    private IChannelHandlerContext ctx;
    private int receivedMessages;
    private int next = 1;
    internal readonly BlockingCollection<BigInteger> answer = new BlockingCollection<BigInteger>();

    public FactorialClientHandler()
    {
        numberSender = future =>
        {
            if (future.IsCompletedSuccessfully)
            {
                SendNumbers();
            }
            else
            {
                Console.Error.WriteLine(future.Exception?.InnerException ?? new TaskCanceledException(future));
                _ = ctx.Channel.CloseAsync();
            }
        };
    }

    public BigInteger GetFactorial()
    {
        bool interrupted = false;

        try
        {
            for (;;)
            {
                try
                {
                    return answer.Take();
                }
                catch (ThreadInterruptedException)
                {
                    interrupted = true;
                }
            }
        }
        finally
        {
            if (interrupted)
            {
                Thread.CurrentThread.Interrupt();
            }
        }
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        this.ctx = ctx;
        SendNumbers();
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, BigInteger msg)
    {
        receivedMessages ++;

        if (receivedMessages == FactorialClient.COUNT)
        {
            // Offer the answer after closing the connection.
            IChannel channel = ctx.Channel;
            _ = channel.CloseAsync().ContinueWith(future =>
            {
                bool offered = answer.TryAdd(msg);
                Debug.Assert(offered);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                new EventExecutorTaskScheduler(() => channel.EventLoop));
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }

    private void SendNumbers()
    {
        // Do not send more than 4096 numbers.
        Task future = null;

        for (int i = 0; i < 4096 && next <= FactorialClient.COUNT; i++)
        {
            future = ctx.WriteAsync(next);
            next++;
        }

        if (next <= FactorialClient.COUNT)
        {
            Debug.Assert(future != null);
            _ = future.ContinueWith(numberSender, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                new EventExecutorTaskScheduler(() => ctx.Executor));
        }

        ctx.Flush();
    }

    private readonly Action<Task> numberSender;
}
