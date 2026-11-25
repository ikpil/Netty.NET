/*
 * Copyright 2016 The Netty Project
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
using System.Runtime.ExceptionServices;
using System.Threading;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Channel handler that allows to easily access inbound messages.
 */
// CLR: the Consumer interface is IConsumer<in T> (contravariant, so the shared no-op consumer of object serves every
// reference type, as Java's unchecked cast does); synchronized (queue) is lock (queue); PlatformDependent.throwException
// is ExceptionDispatchInfo.Throw and printStackTrace() writes the exception to the error stream; LockSupport.parkNanos
// of 10 ms is Thread.Sleep(10). ctx.write(msg) discards the Task (docs/transport-porting.md, VoidChannelPromise).
public class LastInboundHandler : ChannelDuplexHandler
{
    private readonly List<object> queue = new List<object>();
    private readonly IConsumer<IChannelHandlerContext> channelReadCompleteConsumer;
    private Exception lastException;
    private IChannelHandlerContext ctx;
    private bool channelActive;
    private string writabilityStates = "";

    // TODO(scott): use JDK 8's Consumer
    public interface IConsumer<in T>
    {
        void Accept(T obj);
    }

    private static readonly IConsumer<object> NOOP_CONSUMER = new NoopConsumerImpl();

    // CLR: the anonymous Consumer of NOOP_CONSUMER.
    private sealed class NoopConsumerImpl : IConsumer<object>
    {
        public void Accept(object obj)
        {
        }
    }

    public static IConsumer<T> NoopConsumer<T>() where T : class
    {
        return NOOP_CONSUMER;
    }

    public LastInboundHandler()
        : this(LastInboundHandler.NoopConsumer<IChannelHandlerContext>())
    {
    }

    public LastInboundHandler(IConsumer<IChannelHandlerContext> channelReadCompleteConsumer)
    {
        this.channelReadCompleteConsumer = CheckNotNull(channelReadCompleteConsumer, "channelReadCompleteConsumer");
    }

    public override void HandlerAdded(IChannelHandlerContext ctx)
    {
        base.HandlerAdded(ctx);
        this.ctx = ctx;
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        if (channelActive)
        {
            throw new InvalidOperationException("channelActive may only be fired once.");
        }
        channelActive = true;
        base.ChannelActive(ctx);
    }

    public bool IsChannelActive()
    {
        return channelActive;
    }

    public string WritabilityStates()
    {
        return writabilityStates;
    }

    public override void ChannelInactive(IChannelHandlerContext ctx)
    {
        if (!channelActive)
        {
            throw new InvalidOperationException("channelInactive may only be fired once after channelActive.");
        }
        channelActive = false;
        base.ChannelInactive(ctx);
    }

    public override void ChannelWritabilityChanged(IChannelHandlerContext ctx)
    {
        if ("".Equals(writabilityStates))
        {
            writabilityStates = ctx.Channel.IsWritable ? "true" : "false";
        }
        else
        {
            writabilityStates += "," + (ctx.Channel.IsWritable ? "true" : "false");
        }
        base.ChannelWritabilityChanged(ctx);
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        lock (queue)
        {
            queue.Add(msg);
        }
    }

    public override void ChannelReadComplete(IChannelHandlerContext ctx)
    {
        channelReadCompleteConsumer.Accept(ctx);
    }

    public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
    {
        lock (queue)
        {
            queue.Add(new UserEvent(evt));
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        if (lastException != null)
        {
            Console.Error.WriteLine(cause);
        }
        else
        {
            lastException = cause;
        }
    }

    public void CheckException()
    {
        if (lastException == null)
        {
            return;
        }
        Exception t = lastException;
        lastException = null;
        ExceptionDispatchInfo.Throw(t);
    }

    public T ReadInbound<T>()
    {
        lock (queue)
        {
            for (int i = 0; i < queue.Count; i++)
            {
                object o = queue[i];
                if (!(o is UserEvent))
                {
                    queue.RemoveAt(i);
                    return (T) o;
                }
            }
        }

        return default;
    }

    public T BlockingReadInbound<T>() where T : class
    {
        T msg;
        while ((msg = ReadInbound<T>()) == null)
        {
            Thread.Sleep(10);
        }
        return msg;
    }

    public T ReadUserEvent<T>()
    {
        lock (queue)
        {
            for (int i = 0; i < queue.Count; i++)
            {
                object o = queue[i];
                if (o is UserEvent)
                {
                    queue.RemoveAt(i);
                    return (T) ((UserEvent) o).evt;
                }
            }
        }

        return default;
    }

    /**
     * Useful to test order of events and messages.
     */
    public T ReadInboundMessageOrUserEvent<T>()
    {
        lock (queue)
        {
            if (queue.Count == 0)
            {
                return default;
            }
            object o = queue[0];
            queue.RemoveAt(0);
            if (o is UserEvent)
            {
                return (T) ((UserEvent) o).evt;
            }
            return (T) o;
        }
    }

    public void WriteOutbound(params object[] msgs)
    {
        foreach (object msg in msgs)
        {
            _ = ctx.WriteAsync(msg);
        }
        ctx.Flush();
        EmbeddedChannel ch = (EmbeddedChannel) ctx.Channel;
        ch.RunPendingTasks();
        ch.CheckException();
        CheckException();
    }

    public void FinishAndReleaseAll()
    {
        CheckException();
        object o;
        while ((o = ReadInboundMessageOrUserEvent<object>()) != null)
        {
            ReferenceCountUtil.Release(o);
        }
    }

    public IChannel Channel()
    {
        return ctx.Channel;
    }

    private sealed class UserEvent
    {
        internal readonly object evt;

        internal UserEvent(object evt)
        {
            this.evt = evt;
        }
    }
}
