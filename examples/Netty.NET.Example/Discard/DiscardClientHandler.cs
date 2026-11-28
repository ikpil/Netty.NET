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
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Example.Discard;

/**
 * Handles a client-side channel.
 */
// CLR: channelRead0 is protected as the abstract SimpleChannelInboundHandler.ChannelRead0 it overrides ("An override
// declaration can't change the accessibility of the virtual method",
// https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override). cause.printStackTrace() writes
// Exception.ToString() (the type, message and stack trace) to Console.Error.
public class DiscardClientHandler : SimpleChannelInboundHandler<object>
{

    private ByteBuf content;
    private IChannelHandlerContext ctx;

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        this.ctx = ctx;

        // Initialize the message.
        content = ctx.Alloc().DirectBuffer(DiscardClient.SIZE).WriteZero(DiscardClient.SIZE);

        // Send the initial messages.
        GenerateTraffic();
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        content.Release();
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, object msg)
    {
        // Server is supposed to send nothing, but if it sends something, discard it.
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        // Close the connection when an exception is raised.
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }

    // CLR: package-private is internal; the field is unused, as in the source.
#pragma warning disable CS0649 // Never assigned, as in the source.
    internal long counter;
#pragma warning restore CS0649

    private void GenerateTraffic()
    {
        // Flush the outbound buffer to the socket.
        // Once flushed, generate the same amount of traffic again.
        // CLR: addListener(trafficGenerator) is a continuation of the write Task on the context's event loop
        // (EventExecutorTaskScheduler, the timing of DefaultPromise.notifyListeners()). Java notifies a listener of a
        // promise that is already done at once, but only MAX_LISTENER_STACK_DEPTH (8) levels deep before it hands the
        // notification to the executor; the scheduler has no such bound, so a write Task that already completed runs
        // the listener through ctx.Executor.Execute and the stack does not grow with every write.
        Task future = ctx.WriteAndFlushAsync(content.RetainedDuplicate());
        if (future.IsCompleted)
        {
            ctx.Executor.Execute(() => trafficGenerator(future));
        }
        else
        {
            _ = future.ContinueWith(trafficGenerator, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                new EventExecutorTaskScheduler(() => ctx.Executor));
        }
    }

    // CLR: a C# field initializer cannot reference the instance (CS0236), so the listener lambda is assigned in the
    // constructor. future.isSuccess() is IsCompletedSuccessfully, future.cause() the Task's exception (a
    // TaskCanceledException for a canceled Task, as cause() is a CancellationException there), and future.channel()
    // the channel of the context that wrote.
    private readonly Action<Task> trafficGenerator;

    public DiscardClientHandler()
    {
        trafficGenerator = future =>
        {
            if (future.IsCompletedSuccessfully)
            {
                GenerateTraffic();
            }
            else
            {
                Console.Error.WriteLine(future.Exception?.InnerException ?? new TaskCanceledException(future));
                _ = ctx.Channel.CloseAsync();
            }
        };
    }
}
