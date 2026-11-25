/*
 * Copyright 2013 The Netty Project
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
using System.IO;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Spdy;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, whose polling loop has no timeout in the source.
#pragma warning disable xUnit1051 // The source's bind/connect/write/close calls and polling sleeps do not observe the test cancellation.
public class SocketSpdyEchoTest : AbstractSocketTest
{

    // CLR: the unseeded java.util.Random is an unseeded System.Random, as in codec-http's SpdyFrameDecoderTest: only
    // random 32-bit values matter, so nextInt() is NextInt() below, the low 32 bits of NextInt64() (all int values, as
    // Java's). Only the test thread draws from it (createFrames), and the harness classes run one at a time.
    private static readonly Random random = new Random();
    internal const int ignoredBytes = 20;

    // CLR: random.nextInt(): all 32-bit values.
    private static int NextInt()
    {
        return unchecked((int) random.NextInt64());
    }

    private static ByteBuf CreateFrames(int version)
    {
        ByteBuf frames = Unpooled.Buffer(1174);

        // SPDY UNKNOWN Frame
        frames.WriteByte(0x80);
        frames.WriteByte(version);
        frames.WriteShort(0xFFFF);
        frames.WriteByte(0xFF);
        frames.WriteMedium(4);
        frames.WriteInt(NextInt());

        // SPDY NOOP Frame
        frames.WriteByte(0x80);
        frames.WriteByte(version);
        frames.WriteShort(5);
        frames.WriteInt(0);

        // SPDY Data Frame
        frames.WriteInt(NextInt() & 0x7FFFFFFF | 0x01);
        frames.WriteByte(0x01);
        frames.WriteMedium(1024);
        for (int i = 0; i < 256; i ++)
        {
            frames.WriteInt(NextInt());
        }

        // SPDY SYN_STREAM Frame
        frames.WriteByte(0x80);
        frames.WriteByte(version);
        frames.WriteShort(1);
        frames.WriteByte(0x03);
        frames.WriteMedium(10);
        frames.WriteInt(NextInt() & 0x7FFFFFFF | 0x01);
        frames.WriteInt(NextInt() & 0x7FFFFFFF);
        frames.WriteShort(0x8000);
        if (version < 3)
        {
            frames.WriteShort(0);
        }

        // SPDY SYN_REPLY Frame
        frames.WriteByte(0x80);
        frames.WriteByte(version);
        frames.WriteShort(2);
        frames.WriteByte(0x01);
        frames.WriteMedium(4);
        frames.WriteInt(NextInt() & 0x7FFFFFFF | 0x01);
        if (version < 3)
        {
            frames.WriteInt(0);
        }

        // SPDY RST_STREAM Frame
        frames.WriteByte(0x80);
        frames.WriteByte(version);
        frames.WriteShort(3);
        frames.WriteInt(8);
        frames.WriteInt(NextInt() & 0x7FFFFFFF | 0x01);
        frames.WriteInt(NextInt() | 0x01);

        // SPDY SETTINGS Frame
        frames.WriteByte(0x80);
        frames.WriteByte(version);
        frames.WriteShort(4);
        frames.WriteByte(0x01);
        frames.WriteMedium(12);
        frames.WriteInt(1);
        frames.WriteByte(0x03);
        frames.WriteMedium(NextInt());
        frames.WriteInt(NextInt());

        // SPDY PING Frame
        frames.WriteByte(0x80);
        frames.WriteByte(version);
        frames.WriteShort(6);
        frames.WriteInt(4);
        frames.WriteInt(NextInt());

        // SPDY GOAWAY Frame
        frames.WriteByte(0x80);
        frames.WriteByte(version);
        frames.WriteShort(7);
        frames.WriteInt(8);
        frames.WriteInt(NextInt() & 0x7FFFFFFF);
        frames.WriteInt(NextInt() | 0x01);

        // SPDY HEADERS Frame
        frames.WriteByte(0x80);
        frames.WriteByte(version);
        frames.WriteShort(8);
        frames.WriteByte(0x01);
        frames.WriteMedium(4);
        frames.WriteInt(NextInt() & 0x7FFFFFFF | 0x01);

        // SPDY WINDOW_UPDATE Frame
        frames.WriteByte(0x80);
        frames.WriteByte(version);
        frames.WriteShort(9);
        frames.WriteInt(8);
        frames.WriteInt(NextInt() & 0x7FFFFFFF | 0x01);
        frames.WriteInt(NextInt() & 0x7FFFFFFF | 0x01);

        return frames;
    }

    [Fact(Timeout = 15000)]
    public async Task TestSpdyEcho()
    {
        await Run(TestContext.Current, TestSpdyEcho0);
    }

    // CLR: xUnit1024 forbids helper overloads with the test names, hence the 0 suffix on the source's
    // testSpdyEcho(sb, cb[, version, autoRead]) and testSpdyEchoNotAutoRead(sb, cb), which are private here because
    // xUnit1013 flags public non-test methods on a test class.
    private async Task TestSpdyEcho0(ServerBootstrap sb, Bootstrap cb)
    {
        logger.Info("Testing against SPDY v3.1");
        await TestSpdyEcho0(sb, cb, SpdyVersion.SPDY_3_1, true);
    }

    [Fact(Timeout = 15000)]
    public async Task TestSpdyEchoNotAutoRead()
    {
        await Run(TestContext.Current, TestSpdyEchoNotAutoRead0);
    }

    private async Task TestSpdyEchoNotAutoRead0(ServerBootstrap sb, Bootstrap cb)
    {
        logger.Info("Testing against SPDY v3.1");
        await TestSpdyEcho0(sb, cb, SpdyVersion.SPDY_3_1, false);
    }

    private static async Task TestSpdyEcho0(
            ServerBootstrap sb, Bootstrap cb, SpdyVersion version, bool autoRead)
    {

        ByteBuf frames;
        // CLR: SpdyVersion is a sealed class with static readonly instances (the Java enum, see SpdyVersion.cs), and a
        // case label's constant pattern takes only "any constant expression" (an enum value, a const field, ...;
        // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/patterns), so case SPDY_3_1 is a
        // var pattern with a when case guard on the instance. IllegalArgumentException is ArgumentException.
        switch (version)
        {
        case var v when v == SpdyVersion.SPDY_3_1:
            frames = CreateFrames(3);
            break;
        default:
            throw new ArgumentException("unknown version");
        }

        sb.ChildOption(ChannelOption.AUTO_READ, autoRead);
        cb.Option(ChannelOption.AUTO_READ, autoRead);

        SpdyEchoTestServerHandler sh = new SpdyEchoTestServerHandler(autoRead);
        SpdyEchoTestClientHandler ch = new SpdyEchoTestClientHandler(frames.Copy(), autoRead);

        // CLR: the anonymous ChannelInitializer<SocketChannel> body is an ActionChannelInitializer delegate, as in
        // SocketEchoSourceTest.
        sb.ChildHandler(new ActionChannelInitializer<IChannel>(channel =>
        {
            channel.Pipeline.AddLast(
                    new SpdyFrameCodec(version),
                    sh);
        }));

        cb.Handler(ch);

        IChannel sc = await sb.BindAsync();

        IChannel cc = await cb.ConnectAsync(sc.LocalAddress);
        _ = cc.WriteAndFlushAsync(frames);

        // CLR: Thread.sleep(50) is an awaited Task.Delay(50), as in SocketEchoSourceTest.
        while (ch.counter < frames.WriterIndex - ignoredBytes)
        {
            if (sh.exception.Get() != null)
            {
                break;
            }
            if (ch.exception.Get() != null)
            {
                break;
            }

            await Task.Delay(50);
        }

        if (sh.exception.Get() != null && !(sh.exception.Get() is IOException))
        {
            throw sh.exception.Get();
        }
        if (ch.exception.Get() != null && !(ch.exception.Get() is IOException))
        {
            throw ch.exception.Get();
        }
        if (sh.exception.Get() != null)
        {
            throw sh.exception.Get();
        }
        if (ch.exception.Get() != null)
        {
            throw ch.exception.Get();
        }
    }

    private class SpdyEchoTestServerHandler : ChannelInboundHandlerAdapter
    {
        private readonly bool autoRead;
        internal readonly AtomicReference<Exception> exception = new AtomicReference<Exception>();

        internal SpdyEchoTestServerHandler(bool autoRead)
        {
            this.autoRead = autoRead;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            if (!autoRead)
            {
                ctx.Read();
            }
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            _ = ctx.WriteAsync(msg);
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            try
            {
                ctx.Flush();
            }
            finally
            {
                if (!autoRead)
                {
                    ctx.Read();
                }
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (exception.CompareAndSet(null, cause))
            {
                _ = ctx.CloseAsync();
            }
        }
    }

    private class SpdyEchoTestClientHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        private readonly bool autoRead;
        internal readonly AtomicReference<Exception> exception = new AtomicReference<Exception>();
        internal readonly ByteBuf frames;
        internal volatile int counter;

        internal SpdyEchoTestClientHandler(ByteBuf frames, bool autoRead)
        {
            this.frames = frames;
            this.autoRead = autoRead;
        }
        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            if (!autoRead)
            {
                ctx.Read();
            }
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf @in)
        {
            byte[] actual = new byte[@in.ReadableBytes];
            @in.ReadBytes(actual);

            int lastIdx = counter;
            for (int i = 0; i < actual.Length; i ++)
            {
                Assert.Equal(frames.GetByte(ignoredBytes + i + lastIdx), actual[i]);
            }

            counter += actual.Length;
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            if (exception.CompareAndSet(null, cause))
            {
                _ = ctx.CloseAsync();
            }
        }

        public override void ChannelReadComplete(IChannelHandlerContext ctx)
        {
            if (!autoRead)
            {
                ctx.Read();
            }
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
