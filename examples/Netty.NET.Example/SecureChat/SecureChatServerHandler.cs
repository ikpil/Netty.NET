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
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Groups;

namespace Netty.NET.Example.SecureChat;

/**
 * Handles a server-side channel.
 */
// CLR: ChannelGroup is DefaultChannelGroup, which takes no EventExecutor (GlobalEventExecutor.INSTANCE notified
// the group futures, which are Tasks here, docs/transport-porting.md) and also removes a member when its channel
// closes. ctx.pipeline().get(SslHandler.class) is Pipeline.Get<SslHandler>() and
// handshakeFuture().addListener(...) is a continuation of the HandshakeFuture() Task on the channel's executor
// (Common's EventExecutorTaskScheduler, the timing of DefaultPromise listeners); the lambda runs whether the
// handshake succeeded or not, as the listener does. engine().getSession().getCipherSuite() is the CLR-only
// CipherSuite(SslHandler) below. ctx.writeAndFlush/c.writeAndFlush are WriteAndFlushAsync, whose Tasks are
// discarded.
public class SecureChatServerHandler : SimpleChannelInboundHandler<string>
{

    internal static readonly DefaultChannelGroup channels = new DefaultChannelGroup();

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        // Once session is secured, send a greeting and register the channel to the global channel
        // list so the channel received the messages from others.
        _ = ctx.Pipeline.Get<SslHandler>().HandshakeFuture().ContinueWith(future =>
                {
                    _ = ctx.WriteAndFlushAsync(
                            "Welcome to secure chat service!\n");
                    _ = ctx.WriteAndFlushAsync(
                            "Your session is protected by " +
                                    CipherSuite(ctx.Pipeline.Get<SslHandler>()) +
                                    " cipher suite.\n");

                    channels.Add(ctx.Channel);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                new EventExecutorTaskScheduler(() => ctx.Executor));
    }

    // CLR-only: sslHandler.engine().getSession().getCipherSuite(). Netty.NET.Handler opens the internal Engine()
    // to this assembly (InternalsVisibleTo), and its NegotiatedCipherSuite is a TlsCipherSuite whose name is the
    // IANA name that the JSSE getCipherSuite() returns.
    private static string CipherSuite(SslHandler sslHandler)
    {
        return sslHandler.Engine().NegotiatedCipherSuite.ToString();
    }

    protected override void ChannelRead0(IChannelHandlerContext ctx, string msg)
    {
        // Send the received message to all channels but the current one.
        foreach (IChannel c in channels)
        {
            if (c != ctx.Channel)
            {
                _ = c.WriteAndFlushAsync("[" + ctx.Channel.RemoteAddress + "] " + msg + '\n');
            }
            else
            {
                _ = c.WriteAndFlushAsync("[you] " + msg + '\n');
            }
        }

        // Close the connection if the client has sent 'bye'.
        if ("bye".Equals(msg.ToLowerInvariant()))
        {
            _ = ctx.CloseAsync();
        }
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        Console.Error.WriteLine(cause);
        _ = ctx.CloseAsync();
    }
}
