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
using System.IO;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using static Netty.NET.Transport.Tests.TestsuitePermutation;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // The source's bind/connect/write/close calls and polling sleeps do not observe the test cancellation.
public class SocketFixedLengthEchoTest : AbstractSocketTest
{
    private static readonly Random random = new Random();
    internal static readonly byte[] data = new byte[1048576];

    static SocketFixedLengthEchoTest()
    {
        // CLR: PlatformDependent.splittableRandomNextBytes(new SplittableRandom(random.nextLong()), data) is a seeded
        // Random filling the array, as in SocketEchoSourceTest (https://learn.microsoft.com/en-us/dotnet/api/system.random.nextbytes).
        new Random(random.Next()).NextBytes(data);
    }

    [Fact]
    public async Task TestFixedLengthEcho()
    {
        await Run(TestContext.Current, TestFixedLengthEcho0);
    }

    [Fact]
    public async Task TestFixedLengthEchoNotAutoRead()
    {
        await Run(TestContext.Current, TestFixedLengthEchoNotAutoRead0);
    }

    // CLR: xUnit1024 forbids helper overloads with the test names, hence the 0 suffix on the source's
    // testFixedLengthEcho(sb, cb[, autoRead]) and testFixedLengthEchoNotAutoRead(sb, cb), which are private here
    // because xUnit1013 flags public non-test methods on a test class.
    private async Task TestFixedLengthEcho0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestFixedLengthEcho0(sb, cb, true);
    }

    private async Task TestFixedLengthEchoNotAutoRead0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestFixedLengthEcho0(sb, cb, false);
    }

    private static async Task TestFixedLengthEcho0(ServerBootstrap sb, Bootstrap cb, bool autoRead)
    {
        EchoHandler sh = new EchoHandler(autoRead);
        EchoHandler ch = new EchoHandler(autoRead);

        sb.ChildOption(ChannelOption.AUTO_READ, autoRead);
        // CLR: the anonymous ChannelInitializer bodies are ActionChannelInitializer delegates, as in SocketEchoSourceTest.
        sb.ChildHandler(new ActionChannelInitializer<IChannel>(sch =>
        {
            sch.Pipeline.AddLast("decoder", new FixedLengthFrameDecoder(1024));
            sch.Pipeline.AddAfter("decoder", "handler", sh);
        }));

        cb.Option(ChannelOption.AUTO_READ, autoRead);
        cb.Handler(new ActionChannelInitializer<IChannel>(sch =>
        {
            sch.Pipeline.AddLast("decoder", new FixedLengthFrameDecoder(1024));
            sch.Pipeline.AddAfter("decoder", "handler", ch);
        }));

        IChannel sc = await sb.BindAsync();
        IChannel cc = await cb.ConnectAsync(sc.LocalAddress);
        // CLR: new SplittableRandom(random.nextLong()) is a Random seeded from the shared random; Next(maxValue) keeps
        // nextInt(bound)'s exclusive upper bound.
        Random rng = new Random(random.Next());

        for (int i = 0; i < data.Length;)
        {
            int length = Math.Min(rng.Next(1024 * 3), data.Length - i);
            _ = cc.WriteAndFlushAsync(RandomBufferType(cc.Alloc(), data, i, length));
            i += length;
        }

        // CLR: Thread.sleep(50) is an awaited Task.Delay(50), as in SocketEchoSourceTest.
        while (ch.counter < data.Length)
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

        while (sh.counter < data.Length)
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

        await sh.channel.CloseAsync();
        await ch.channel.CloseAsync();
        await sc.CloseAsync();

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

    private class EchoHandler : SimpleChannelInboundHandler<ByteBuf>
    {
        private readonly bool autoRead;
        internal volatile IChannel channel;
        internal readonly AtomicReference<Exception> exception = new AtomicReference<Exception>();
        internal volatile int counter;

        internal EchoHandler(bool autoRead)
        {
            this.autoRead = autoRead;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            channel = ctx.Channel;

            if (!autoRead)
            {
                ctx.Read();
            }
        }

        protected override void ChannelRead0(IChannelHandlerContext ctx, ByteBuf msg)
        {
            Assert.Equal(1024, msg.ReadableBytes);

            byte[] actual = new byte[msg.ReadableBytes];
            msg.GetBytes(msg.ReaderIndex, actual);

            int lastIdx = counter;

            for (int i = 0; i < actual.Length; i ++)
            {
                Assert.Equal(data[i + lastIdx], actual[i]);
            }

            // Update the counter before calling write(...) as write could in theory trigger another channelRead(...)
            // which then would use the wrong lastIdx.
            counter += actual.Length;

            if (channel.Parent != null)
            {
                _ = channel.WriteAsync(msg.Retain());
            }
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
}
#pragma warning restore xUnit1051
