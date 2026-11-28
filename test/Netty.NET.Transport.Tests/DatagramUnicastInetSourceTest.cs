/*
 * Copyright 2021 The Netty Project
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
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // The source's sync() calls and its channel teardown take no cancellation.
public class DatagramUnicastInetSourceTest : DatagramUnicastSourceTest
{
    [Fact]
    public async Task TestBindWithPortOnly()
    {
        await Run(TestContext.Current, (bootstrap, bootstrap2) => TestBindWithPortOnly0(bootstrap2));
    }

    // CLR: xUnit1024 forbids overloading the TestBindWithPortOnly test name.
    private static async Task TestBindWithPortOnly0(Bootstrap cb)
    {
        IChannel channel = null;

        try
        {
            cb.Handler(new EmptyChannelHandlerAdapter());
            channel = await cb.BindAsync(0);
        }
        finally
        {
            await CloseChannel(channel);
        }
    }

    protected override bool IsConnected(IChannel channel)
    {
        return ((IDatagramChannel) channel).IsConnected;
    }

    protected override Task<IChannel> SetupClientChannel(Bootstrap cb, byte[] bytes, CountDownLatch latch,
                                         StrongBox<Exception> errorRef)
    {
        cb.Handler(new ClientHandler(bytes, latch, errorRef));

        return cb.BindAsync(NewSocketAddress());
    }

    protected override Task<IChannel> SetupServerChannel(Bootstrap sb, byte[] bytes, EndPoint sender,
                                         CountDownLatch latch, StrongBox<Exception> errorRef,
                                         bool echo)
    {
        sb.Handler(new ActionChannelInitializer<IChannel>(ch =>
        {
            ch.Pipeline.AddLast(new ServerHandler(bytes, sender, latch, errorRef, echo));
        }));

        return sb.BindAsync(NewSocketAddress());
    }

    protected override bool SupportDisconnect()
    {
        return true;
    }

    protected override Task Write(IChannel cc, ByteBuf buf, EndPoint remote, WrapType wrapType)
    {
        switch (wrapType)
        {
            case WrapType.DUP:
                return cc.WriteAsync(new DatagramPacket(buf.RetainedDuplicate(), (IPEndPoint) remote));
            case WrapType.SLICE:
                return cc.WriteAsync(new DatagramPacket(buf.RetainedSlice(), (IPEndPoint) remote));
            case WrapType.READ_ONLY:
                return cc.WriteAsync(new DatagramPacket(buf.Retain().AsReadOnly(), (IPEndPoint) remote));
            case WrapType.NONE:
                return cc.WriteAsync(new DatagramPacket(buf.Retain(), (IPEndPoint) remote));
            default:
                // CLR: java.lang.Error is UnreachableException, as in DatagramUnicastSourceTest.Write.
                throw new UnreachableException("Unexpected wrap type: " + wrapType);
        }
    }

    // CLR: C# has no anonymous classes, so the source's anonymous handlers are the private classes below; the
    // ChannelHandlerAdapter subclass with an empty body is EmptyChannelHandlerAdapter.
    private sealed class EmptyChannelHandlerAdapter : ChannelHandlerAdapter
    {
    }

    private sealed class ClientHandler(byte[] bytes, CountDownLatch latch, StrongBox<Exception> errorRef)
            : SimpleChannelInboundHandler<DatagramPacket>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, DatagramPacket msg)
        {
            try
            {
                ByteBuf buf = msg.Content;
                Assert.Equal(bytes.Length, buf.ReadableBytes);

                for (int i = 0; i < bytes.Length; i++)
                {
                    Assert.Equal(bytes[i], buf.GetByte(buf.ReaderIndex + i));
                }

                IPEndPoint localAddress = (IPEndPoint) ctx.Channel.LocalAddress;
                if (IsAnyLocalAddress(localAddress.Address))
                {
                    Assert.Equal(localAddress.Port, msg.Recipient.Port);
                }
                else
                {
                    // Test that the channel's localAddress is equal to the message's recipient
                    Assert.Equal(localAddress, msg.Recipient);
                }
            }
            finally
            {
                latch.CountDown();
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            Interlocked.CompareExchange(ref errorRef.Value, cause, null);
        }
    }

    private sealed class ServerHandler(byte[] bytes, EndPoint sender, CountDownLatch latch,
            StrongBox<Exception> errorRef, bool echo) : SimpleChannelInboundHandler<DatagramPacket>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, DatagramPacket msg)
        {
            try
            {
                if (sender == null)
                {
                    Assert.NotNull(msg.Sender);
                }
                else
                {
                    IPEndPoint senderAddress = (IPEndPoint) sender;
                    if (IsAnyLocalAddress(senderAddress.Address))
                    {
                        Assert.Equal(senderAddress.Port, msg.Sender.Port);
                    }
                    else
                    {
                        Assert.Equal(sender, msg.Sender);
                    }
                }

                ByteBuf buf = msg.Content;
                Assert.Equal(bytes.Length, buf.ReadableBytes);

                for (int i = 0; i < bytes.Length; i++)
                {
                    Assert.Equal(bytes[i], buf.GetByte(buf.ReaderIndex + i));
                }

                // Test that the channel's localAddress is equal to the message's recipient
                Assert.Equal(ctx.Channel.LocalAddress, msg.Recipient);

                if (echo)
                {
                    _ = ctx.WriteAndFlushAsync(new DatagramPacket(buf.RetainedDuplicate(), msg.Sender));
                }
            }
            finally
            {
                latch.CountDown();
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            Interlocked.CompareExchange(ref errorRef.Value, cause, null);
        }
    }
}
#pragma warning restore xUnit1051
