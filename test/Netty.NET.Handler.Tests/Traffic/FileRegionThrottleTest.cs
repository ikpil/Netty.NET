/*
 * Copyright 2020 The Netty Project
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
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Traffic;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Tests.Traffic;

// CLR: @Disabled("This test is flaky, need more investigation") is [Fact(Skip = ...)] with the same text; the body is
// ported as in Java. The static @BeforeAll beforeClass() runs from the class fixture BeforeAll (created before the
// first test of the class, https://xunit.net/docs/shared-context), and tmp.deleteOnExit() deletes the file when that
// fixture is disposed after the class's tests. @BeforeEach setUp()/@AfterEach tearDown() are the constructor and
// IDisposable.Dispose(), which xUnit calls around each test. SplittableRandom is System.Random, whose Next(255) has the
// same range as nextInt(255); the (byte) cast keeps the bits of the Java byte. PlatformDependent.createTempFile is the
// Common PlatformDependent.CreateTempFile, FileOutputStream a FileStream, RandomAccessFile(tmp, "r").getChannel() a
// read-only FileStream, File.length() FileInfo.Length, CharsetUtil.US_ASCII Encoding.ASCII and Charset.defaultCharset()
// Encoding.Default. The anonymous ChannelInitializers are named nested classes, the bootstrap operations are awaited
// Tasks, CountDownLatch is CountdownEvent, and the client connects to the loopback address in place of the wildcard
// server address as in FlowControlHandlerTest.
public class FileRegionThrottleTest : IClassFixture<FileRegionThrottleTest.BeforeAll>, IDisposable
{
    private static readonly byte[] BYTES = new byte[64 * 1024 * 4];
    private const long WRITE_LIMIT = 64 * 1024;
    private static FileInfo tmp;
    private IEventLoopGroup group;

    private static void BeforeClass()
    {
        Random r = new Random();
        for (int i = 0; i < BYTES.Length; i++)
        {
            BYTES[i] = (byte)r.Next(255);
        }

        tmp = PlatformDependent.CreateTempFile("netty-traffic", ".tmp", null);
        try
        {
            using FileStream @out = new FileStream(tmp.FullName, FileMode.Create, FileAccess.Write);
            @out.Write(BYTES);
            @out.Flush();
        }
        catch (IOException e)
        {
            throw new Exception(e.Message, e);
        }
        // CLR: FileInfo caches the file state, so Length is refreshed after the write.
        tmp.Refresh();
    }

    public sealed class BeforeAll : IDisposable
    {
        public BeforeAll()
        {
            BeforeClass();
        }

        public void Dispose()
        {
            tmp.Delete();
        }
    }

    public FileRegionThrottleTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        group = new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory());
    }

    public void Dispose()
    {
        TearDown();
    }

    private void TearDown()
    {
        _ = group.ShutdownGracefullyAsync();
    }

    [Fact(Skip = "This test is flaky, need more investigation")]
    public async Task TestGlobalWriteThrottle()
    {
        CountdownEvent latch = new CountdownEvent(1);
        GlobalTrafficShapingHandler gtsh = new GlobalTrafficShapingHandler(group, WRITE_LIMIT, 0);
        ServerBootstrap bs = new ServerBootstrap();
        bs.Group(group).Channel<NioServerSocketChannel>().ChildHandler(new ServerInitializer(gtsh));
        IChannel sc = await bs.BindAsync(0, TestContext.Current.CancellationToken);
        IChannel cc = await ClientConnect(sc.LocalAddress, new ReadHandler(latch));

        long start = TrafficCounter.MilliSecondFromNano();
        await cc.WriteAndFlushAsync(Unpooled.CopiedBuffer("send-file\n", Encoding.ASCII),
                TestContext.Current.CancellationToken);
        latch.Wait(TestContext.Current.CancellationToken);
        long timeTaken = TrafficCounter.MilliSecondFromNano() - start;
        Assert.True(timeTaken > 3000, "Data streamed faster than expected");
        await sc.CloseAsync(TestContext.Current.CancellationToken);
        await cc.CloseAsync(TestContext.Current.CancellationToken);
    }

    private Task<IChannel> ClientConnect(EndPoint server, ReadHandler readHandler)
    {
        Bootstrap bc = new Bootstrap();
        bc.Group(group).Channel<NioSocketChannel>().Handler(new ClientInitializer(readHandler));
        IPEndPoint address = (IPEndPoint)server;
        if (address.Address.Equals(IPAddress.Any))
        {
            server = new IPEndPoint(IPAddress.Loopback, address.Port);
        }
        return bc.ConnectAsync(server);
    }

    private sealed class ServerInitializer : ChannelInitializer<ISocketChannel>
    {
        private readonly GlobalTrafficShapingHandler gtsh;

        internal ServerInitializer(GlobalTrafficShapingHandler gtsh)
        {
            this.gtsh = gtsh;
        }

        protected override void InitChannel(ISocketChannel ch)
        {
            ch.Pipeline.AddLast(new LineBasedFrameDecoder(int.MaxValue));
            ch.Pipeline.AddLast(new MessageDecoder());
            ch.Pipeline.AddLast(gtsh);
        }
    }

    private sealed class ClientInitializer : ChannelInitializer<ISocketChannel>
    {
        private readonly ReadHandler readHandler;

        internal ClientInitializer(ReadHandler readHandler)
        {
            this.readHandler = readHandler;
        }

        protected override void InitChannel(ISocketChannel ch)
        {
            ch.Pipeline.AddLast(readHandler);
        }
    }

    private sealed class MessageDecoder : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is ByteBuf)
            {
                ByteBuf buf = (ByteBuf)msg;
                string message = buf.ToString(Encoding.Default);
                buf.Release();
                if (message.Equals("send-file"))
                {
                    FileStream raf = new FileStream(tmp.FullName, FileMode.Open, FileAccess.Read);
                    _ = ctx.Channel.WriteAndFlushAsync(new DefaultFileRegion(raf, 0, tmp.Length));
                }
            }
        }
    }

    private sealed class ReadHandler : ChannelInboundHandlerAdapter
    {
        private long bytesTransferred;
        private readonly CountdownEvent latch;

        internal ReadHandler(CountdownEvent latch)
        {
            this.latch = latch;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is ByteBuf)
            {
                ByteBuf buf = (ByteBuf)msg;
                bytesTransferred += buf.ReadableBytes;
                buf.Release();
                if (bytesTransferred == tmp.Length)
                {
                    latch.Signal();
                }
            }
        }
    }
}
