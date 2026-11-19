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
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.String;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, whose promise waits have no timeout in the source.
#pragma warning disable xUnit1051 // The source's bind/connect/write/close calls and promise waits do not observe the test cancellation.
public class SocketStringEchoTest : AbstractSocketTest
{
    // CLR: System.Random(3) is seeded like java.util.Random(3) but yields another sequence, so the generated strings
    // differ while keeping their shape. java.util.Random is thread-safe and System.Random is not
    // (https://learn.microsoft.com/en-us/dotnet/api/system.random), so the event-loop draws of nextBoolean() lock it.
    internal static readonly Random random = new Random(3);
    internal static readonly string[] data = new string[1024];

    static SocketStringEchoTest()
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < data.Length; i ++)
        {
            sb.Length = 0;
            int eLen = random.Next(512);
            int j = 1;
            while (sb.Length < eLen)
            {
                // CLR: String.format("%03X/%x.", i, j++) is the X3/x hexadecimal format with the default locale.
                sb.Append(string.Format(CultureInfo.CurrentCulture, "{0:X3}/{1:x}.", i, j++));
            }
            if (sb.Length > eLen)
            {
                sb.Length = eLen;
            }
            data[i] = sb.ToString();
        }
    }

    private static bool NextBoolean()
    {
        lock (random)
        {
            return random.Next(2) == 0;
        }
    }

    [Fact(Timeout = 60000)]
    public async Task TestStringEcho()
    {
        await Run(TestContext.Current, TestStringEcho0);
    }

    // CLR: xUnit1024 forbids helper overloads with the test names, hence the 0 suffix on the source's
    // testStringEcho(sb, cb[, autoRead]) and testStringEchoNotAutoRead(sb, cb), which are private here because
    // xUnit1013 flags public non-test methods on a test class.
    private async Task TestStringEcho0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestStringEcho0(sb, cb, true);
    }

    [Fact(Timeout = 60000)]
    public async Task TestStringEchoNotAutoRead()
    {
        await Run(TestContext.Current, TestStringEchoNotAutoRead0);
    }

    private async Task TestStringEchoNotAutoRead0(ServerBootstrap sb, Bootstrap cb)
    {
        await TestStringEcho0(sb, cb, false);
    }

    private static async Task TestStringEcho0(ServerBootstrap sb, Bootstrap cb, bool autoRead)
    {
        sb.ChildOption(ChannelOption.AUTO_READ, autoRead);
        cb.Option(ChannelOption.AUTO_READ, autoRead);

        // CLR: an ImmediateEventExecutor Promise<Void> is a TaskCompletionSource whose Task replaces sync().
        TaskCompletionSource serverDonePromise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource clientDonePromise = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StringEchoHandler sh = new StringEchoHandler(autoRead, serverDonePromise);
        StringEchoHandler ch = new StringEchoHandler(autoRead, clientDonePromise);

        // CLR: the anonymous ChannelInitializer bodies are ActionChannelInitializer delegates, as in SocketEchoSourceTest;
        // CharsetUtil.ISO_8859_1 is Encoding.Latin1.
        sb.ChildHandler(new ActionChannelInitializer<IChannel>(sch =>
        {
            sch.Pipeline.AddLast("framer", new DelimiterBasedFrameDecoder(512, Delimiters.LineDelimiter()));
            sch.Pipeline.AddLast("decoder", new StringDecoder(Encoding.Latin1));
            sch.Pipeline.AddBefore("decoder", "encoder", new StringEncoder(Encoding.Latin1));
            sch.Pipeline.AddAfter("decoder", "handler", sh);
        }));

        cb.Handler(new ActionChannelInitializer<IChannel>(sch =>
        {
            sch.Pipeline.AddLast("framer", new DelimiterBasedFrameDecoder(512, Delimiters.LineDelimiter()));
            sch.Pipeline.AddLast("decoder", new StringDecoder(Encoding.Latin1));
            sch.Pipeline.AddBefore("decoder", "encoder", new StringEncoder(Encoding.Latin1));
            sch.Pipeline.AddAfter("decoder", "handler", ch);
        }));

        IChannel sc = await sb.BindAsync();
        IChannel cc = await cb.ConnectAsync(sc.LocalAddress);
        foreach (string element in data)
        {
            string delimiter = NextBoolean() ? "\r\n" : "\n";
            _ = cc.WriteAndFlushAsync(element + delimiter);
        }

        await ch.donePromise.Task;
        await sh.donePromise.Task;
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

    internal class StringEchoHandler : SimpleChannelInboundHandler<string>
    {
        private readonly bool autoRead;
        internal readonly TaskCompletionSource donePromise;
        private int dataIndex;
        internal volatile IChannel channel;
        internal readonly AtomicReference<Exception> exception = new AtomicReference<Exception>();

        internal StringEchoHandler(bool autoRead, TaskCompletionSource donePromise)
        {
            this.autoRead = autoRead;
            this.donePromise = donePromise;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            channel = ctx.Channel;
            if (!autoRead)
            {
                ctx.Read();
            }
        }

        // CLR: IllegalStateException is InvalidOperationException; tryFailure/setSuccess are TrySetException/TrySetResult.
        protected override void ChannelRead0(IChannelHandlerContext ctx, string msg)
        {
            if (!data[dataIndex].Equals(msg))
            {
                donePromise.TrySetException(new InvalidOperationException(
                        "index: " + dataIndex.ToString(CultureInfo.InvariantCulture) + " didn't match!"));
                _ = ctx.CloseAsync();
                return;
            }

            if (channel.Parent != null)
            {
                string delimiter = NextBoolean() ? "\r\n" : "\n";
                _ = channel.WriteAsync(msg + delimiter);
            }

            if (++dataIndex >= data.Length)
            {
                donePromise.TrySetResult();
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
                donePromise.TrySetException(new InvalidOperationException("exceptionCaught: " + ctx.Channel, cause));
                _ = ctx.CloseAsync();
            }
        }

        public override void ChannelInactive(IChannelHandlerContext ctx)
        {
            donePromise.TrySetException(new InvalidOperationException("channelInactive: " + ctx.Channel));
        }
    }
}
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
