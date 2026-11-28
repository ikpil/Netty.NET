/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using Netty.NET.Handler.Codec.Compression;
using Netty.NET.Handler.Codec.Http;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Tests.Http2TestUtil;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Test for data decompression in the HTTP/2 codec.
 */
// CLR: the @Mock listeners are Moq mocks; doAnswer(...).when(...) is Setup(...).Callback/Returns and verify(...) is
// Verify(..., Times.Once()) (https://github.com/devlooped/moq/wiki/Quickstart); eq(headers) is the expected value itself,
// which Moq compares with Equals. @BeforeAll is the static constructor (xUnit has no static setup method; the
// codec-compression tests do the same, docs/codec-compression-porting.md, Test notes), @BeforeEach is the constructor
// and the two @AfterEach methods (cleanup, then teardown) run from IAsyncLifetime.DisposeAsync; sync() is awaiting the
// Task. The @ValueSource/@MethodSource tests are theories with InlineData/MemberData rows (TheoryData). JUnit passes
// only the first argument of each paddingAndCompression() row to deflateEncodingWriteLargeMessage(int padding) ("when
// an arguments source provides more arguments than the test method needs, those additional arguments are ignored",
// https://docs.junit.org/5.12.2/user-guide/index.html, Argument Count Validation), while xUnit requires one parameter
// per row value, so that theory takes the compression as an unused discard parameter named _ (its lambda therefore
// ignores the Write*Async Tasks with expression statements instead of "_ ="): it runs once per row, as in JUnit. The
// rows keep ZSTD:
// zstd-jni is library-not-ported (docs/codec-compression-porting.md), so CompressorHttp2ConnectionEncoder and
// DelegatingDecompressorFrameListener treat "zstd" as Java does when Zstd.isAvailable() is false (no compressor, no
// decompressor) and those rows send and check the data uncompressed. Brotli.ensureAvailability() always succeeds (the
// BCL Brotli, docs/codec-compression-porting.md). The anonymous Answer/ChannelInitializer/ChannelInboundHandlerAdapter/
// DelegatingDecompressorFrameListener/Http2ConnectionAdapter classes are lambdas and nested classes, Callable<Void> is
// Action, and the anonymous Http2Runnable instances are the nested ActionRunnable. NIO transport: MultiThreadIoEventLoopGroup with
// NioIoHandler, NioServerSocketChannel and NioSocketChannel; new InetSocketAddress(0) is the IPv4 wildcard, as the
// Transport's ServerBootstrap.BindAsync(int) binds it, so the client connects to NetUtil.LOCALHOST4 (as codec-http's
// HttpClientCodecTest). ByteArrayOutputStream is MemoryStream and toString(ISO_8859_1) is Encoding.Latin1.GetString;
// AtomicReference<Throwable> is an Exception field read and written with Volatile; CountDownLatch is CountdownEvent.
// PlatformDependent.allocateUninitializedArray is GC.AllocateUninitializedArray, which "Allocates an array while
// skipping zero-initialization, if possible"
// (https://learn.microsoft.com/en-us/dotnet/api/system.gc.allocateuninitializedarray?view=net-10.0); either content
// satisfies the assertions, which accept a bounded output or a decompression failure. AssertJ's
// assertThat(int).isLessThanOrEqualTo(int) is Assert.True with the same message text, and
// isInstanceOf(...).rootCause().isInstanceOf(...).hasMessageContaining(...) walks InnerException to the root cause.
// String.getBytes() with the default charset is Encoding.UTF8 (the texts are ASCII) and ThreadLocalRandom.current()
// .nextBytes is Random.Shared.NextBytes. AssertionError(msg, cause) is Xunit.Sdk.XunitException(msg, cause).
public class DataCompressionHttp2Test : IAsyncLifetime
{
    private static readonly AsciiString GET = new AsciiString("GET");
    private static readonly AsciiString POST = new AsciiString("POST");
    private static readonly AsciiString PATH = new AsciiString("/some/path");

    private readonly Mock<IHttp2FrameListener> serverListener = new Mock<IHttp2FrameListener>();
    private readonly Mock<IHttp2FrameListener> clientListener = new Mock<IHttp2FrameListener>();

    private IHttp2ConnectionEncoder clientEncoder;
    private ServerBootstrap sb;
    private Bootstrap cb;
    private IChannel serverChannel;
    private IChannel clientChannel;
    private volatile IChannel serverConnectedChannel;
    private CountdownEvent serverLatch;
    private IHttp2Connection serverConnection;
    private IHttp2Connection clientConnection;
    private Http2ConnectionHandler clientHandler;
    private MemoryStream serverOut;
    private int maxServerOutBufferSize;
    private int maxAllocation;
    private Exception serverException;

    static DataCompressionHttp2Test()
    {
        BeforeAllTests();
    }

    private static void BeforeAllTests()
    {
        Brotli.EnsureAvailability();
    }

    public DataCompressionHttp2Test()
    {
        Setup();
    }

    private void Setup()
    {
        maxAllocation = 0;
        serverListener.Setup(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<IHttp2Headers>(), It.IsAny<int>(), It.IsAny<bool>()))
            .Callback((IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int padding, bool endStream) =>
            {
                if (endStream)
                {
                    serverConnection.Stream(streamId).Close();
                }
            });
        serverListener.Setup(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<IHttp2Headers>(), It.IsAny<int>(), It.IsAny<short>(), It.IsAny<bool>(), It.IsAny<int>(),
                It.IsAny<bool>()))
            .Callback((IChannelHandlerContext ctx, int streamId, IHttp2Headers headers, int streamDependency,
                       short weight, bool exclusive, int padding, bool endStream) =>
            {
                if (endStream)
                {
                    serverConnection.Stream(streamId).Close();
                }
            });
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        Cleanup();
        await Teardown();
    }

    private void Cleanup()
    {
        if (serverOut != null)
        {
            serverOut.Dispose();
        }
    }

    private async Task Teardown()
    {
        if (clientChannel != null)
        {
            await clientChannel.CloseAsync();
            clientChannel = null;
        }

        if (serverChannel != null)
        {
            await serverChannel.CloseAsync();
            serverChannel = null;
        }

        IChannel serverConnectedChannel = this.serverConnectedChannel;
        if (serverConnectedChannel != null)
        {
            await serverConnectedChannel.CloseAsync();
            this.serverConnectedChannel = null;
        }

        if (sb != null)
        {
            Task serverGroup = sb.Config.Group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            Task serverChildGroup = sb.Config.ChildGroup.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            await serverGroup;
            await serverChildGroup;
        }

        if (cb != null)
        {
            Task clientGroup = cb.Config.Group.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero);
            await clientGroup;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public async Task JustHeadersNoData(int padding)
    {
        await BootstrapEnv(0);
        IHttp2Headers headers = new DefaultHttp2Headers().Method(GET).Path(PATH)
                .Set(HttpHeaderNames.CONTENT_ENCODING, HttpHeaderValues.GZIP);

        RunInChannel(clientChannel, new ActionRunnable(() =>
        {
            _ = clientEncoder.WriteHeadersAsync(CtxClient(), 3, headers, padding, true, NewPromiseClient());
            clientHandler.Flush(CtxClient());
        }));
        AwaitServer();
        serverListener.Verify(l => l.OnHeadersRead(It.IsAny<IChannelHandlerContext>(), 3, headers, 0,
                DEFAULT_PRIORITY_WEIGHT, false, padding, true), Times.Once());
    }

    public static TheoryData<int, AsciiString> PaddingAndCompression()
    {
        TheoryData<int, AsciiString> builder = new TheoryData<int, AsciiString>();

        foreach (int padding in new int[]{0, 10})
        {
            foreach (AsciiString compression in new AsciiString[]{
                    HttpHeaderValues.GZIP, HttpHeaderValues.BR, HttpHeaderValues.ZSTD, HttpHeaderValues.SNAPPY})
            {
                builder.Add(padding, compression);
            }
        }

        return builder;
    }

    [Theory]
    [MemberData(nameof(PaddingAndCompression))]
    public async Task EncodingSingleEmptyMessage(int padding, AsciiString compressionAlgorithm)
    {
        string text = "";
        await TestEncodingMessage(padding, text, compressionAlgorithm);
    }

    [Theory]
    [MemberData(nameof(PaddingAndCompression))]
    public async Task EncodingSingleMessage(int padding, AsciiString compressionAlgorithm)
    {
        string text = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaabbbbbbbbbbbbbbbbbbbbbbbbbbbbbccccccccccccccccccccccc";
        await TestEncodingMessage(padding, text, compressionAlgorithm);
    }

    [Theory]
    [MemberData(nameof(PaddingAndCompression))]
    public async Task EncodingMultipleMessages(int padding, AsciiString compressionAlgorithm)
    {
        string text1 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaabbbbbbbbbbbbbbbbbbbbbbbbbbbbbccccccccccccccccccccccc";
        string text2 = "dddddddddddddddddddeeeeeeeeeeeeeeeeeeeffffffffffffffffffff";
        ByteBuf data1 = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(text1));
        ByteBuf data2 = Unpooled.CopiedBuffer(Encoding.UTF8.GetBytes(text2));
        await BootstrapEnv(data1.ReadableBytes + data2.ReadableBytes);

        try
        {
            IHttp2Headers headers = new DefaultHttp2Headers().Method(POST).Path(PATH)
                    .Set(HttpHeaderNames.CONTENT_ENCODING, compressionAlgorithm);

            RunInChannel(clientChannel, new ActionRunnable(() =>
            {
                _ = clientEncoder.WriteHeadersAsync(CtxClient(), 3, headers, padding, false, NewPromiseClient());
                _ = clientEncoder.WriteDataAsync(CtxClient(), 3, data1.Retain(), padding, false, NewPromiseClient());
                _ = clientEncoder.WriteDataAsync(CtxClient(), 3, data2.Retain(), padding, true, NewPromiseClient());
                clientHandler.Flush(CtxClient());
            }));
            AwaitServer();
            Assert.Equal(text1 + text2, Encoding.Latin1.GetString(serverOut.ToArray()));
        }
        finally
        {
            data1.Release();
            data2.Release();
        }
    }

    [Theory]
    [MemberData(nameof(PaddingAndCompression))]
    public async Task EncodingTooBigMessage(int padding, AsciiString compressionAlgorithm)
    {
        // Make the compressed message produce half a megabyte of text, then limit the output buffer size to 64 KiB.
        byte[] text = GC.AllocateUninitializedArray<byte>(524288);
        int inputLength = text.Length;
        maxAllocation = inputLength / 8;

        await TestEncodingMessage(padding, text, compressionAlgorithm, () =>
        {
            Assert.True(serverLatch.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            serverOut.Flush();
            Exception cause = Volatile.Read(ref serverException);
            if (cause == null)
            {
                // Compression codec must have mitigations
                Assert.True(maxServerOutBufferSize <= maxAllocation,
                        "[check that the original string of size " + inputLength + ", " +
                                "got compressed and decompressed into max " + maxAllocation + " sized buffers] " +
                                "Expecting actual: " + maxServerOutBufferSize + " to be less than or equal to: " +
                                maxAllocation);
            }
            else
            {
                // Compression codec must reject
                Assert.IsAssignableFrom<Http2Exception.StreamException>(cause);
                Exception rootCause = cause;

                while (rootCause.InnerException != null)
                {
                    rootCause = rootCause.InnerException;
                }

                Assert.IsAssignableFrom<DecompressionException>(rootCause);
                Assert.Contains("maximum size", rootCause.Message);
            }
        });
    }

    private Task TestEncodingMessage(int padding, string text, AsciiString compressionAlgorithmName)
    {
        return TestEncodingMessage(padding, text, compressionAlgorithmName, () =>
        {
            AwaitServer();
            Assert.Equal(text, Encoding.Latin1.GetString(serverOut.ToArray()));
        });
    }

    private Task TestEncodingMessage(int padding, string text, AsciiString compressionAlgorithmName,
                                     Action assertions)
    {
        return TestEncodingMessage(padding, Encoding.Latin1.GetBytes(text), compressionAlgorithmName, assertions);
    }

    private async Task TestEncodingMessage(int padding, byte[] text, AsciiString compressionAlgorithmName,
                                           Action assertions)
    {
        ByteBuf data = Unpooled.CopiedBuffer(text);
        await BootstrapEnv(data.ReadableBytes);

        try
        {
            IHttp2Headers headers = new DefaultHttp2Headers().Method(POST).Path(PATH)
                    .Set(HttpHeaderNames.CONTENT_ENCODING, compressionAlgorithmName);

            RunInChannel(clientChannel, new ActionRunnable(() =>
            {
                _ = clientEncoder.WriteHeadersAsync(CtxClient(), 3, headers, padding, false, NewPromiseClient());
                _ = clientEncoder.WriteDataAsync(CtxClient(), 3, data.Retain(), padding, true, NewPromiseClient());
                clientHandler.Flush(CtxClient());
            }));
            assertions();
        }
        finally
        {
            data.Release();
        }
    }

    [Theory]
    [MemberData(nameof(PaddingAndCompression))]
    public async Task DeflateEncodingWriteLargeMessage(int padding, AsciiString _)
    {
        const int BUFFER_SIZE = 1 << 12;
        byte[] bytes = new byte[BUFFER_SIZE];
        Random.Shared.NextBytes(bytes);
        await BootstrapEnv(BUFFER_SIZE);
        ByteBuf data = Unpooled.WrappedBuffer(bytes);

        try
        {
            IHttp2Headers headers = new DefaultHttp2Headers().Method(POST).Path(PATH)
                    .Set(HttpHeaderNames.CONTENT_ENCODING, HttpHeaderValues.DEFLATE);

            RunInChannel(clientChannel, new ActionRunnable(() =>
            {
                clientEncoder.WriteHeadersAsync(CtxClient(), 3, headers, padding, false, NewPromiseClient());
                clientEncoder.WriteDataAsync(CtxClient(), 3, data.Retain(), padding, true, NewPromiseClient());
                clientHandler.Flush(CtxClient());
            }));
            AwaitServer();
            Assert.Equal(data.ResetReaderIndex().ToString(Encoding.Latin1),
                    Encoding.Latin1.GetString(serverOut.ToArray()));
        }
        finally
        {
            data.Release();
        }
    }

    private async Task BootstrapEnv(int serverOutSize)
    {
        CountdownEvent prefaceWrittenLatch = new CountdownEvent(1);
        serverOut = new MemoryStream(serverOutSize);
        serverLatch = new CountdownEvent(1);
        Volatile.Write(ref serverException, null);
        sb = new ServerBootstrap();
        cb = new Bootstrap();

        // Streams are created before the normal flow for this test, so these connection must be initialized up front.
        serverConnection = new DefaultHttp2Connection(true);
        clientConnection = new DefaultHttp2Connection(false);

        serverConnection.AddListener(new StreamClosedListener(this));

        serverListener.Setup(l => l.OnDataRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                It.IsAny<ByteBuf>(), It.IsAny<int>(), It.IsAny<bool>()))
            .Returns((IChannelHandlerContext ctx, int streamId, ByteBuf buf, int padding, bool endOfStream) =>
            {
                int processedBytes = buf.ReadableBytes + padding;

                maxServerOutBufferSize = Math.Max(maxServerOutBufferSize, buf.ReadableBytes);
                buf.ReadBytes(serverOut, buf.ReadableBytes);

                if (endOfStream)
                {
                    IHttp2Stream stream = serverConnection.Stream(streamId);
                    if (stream != null)
                    {
                        stream.Close();
                    }
                }

                return processedBytes;
            });

        CountdownEvent serverChannelLatch = new CountdownEvent(1);
        sb.Group(new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory()));
        sb.Channel<NioServerSocketChannel>();
        sb.ChildHandler(new ServerInitializer(this, serverChannelLatch));

        cb.Group(new MultiThreadIoEventLoopGroup(NioIoHandler.NewFactory()));
        cb.Channel<NioSocketChannel>();
        cb.Handler(new ClientInitializer(this, prefaceWrittenLatch));

        serverChannel = await sb.BindAsync(new IPEndPoint(IPAddress.Any, 0));
        int port = ((IPEndPoint) serverChannel.LocalAddress).Port;

        clientChannel = await cb.ConnectAsync(new IPEndPoint(NetUtil.LOCALHOST4, port));
        Assert.True(prefaceWrittenLatch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(serverChannelLatch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    // CLR: the anonymous Http2ConnectionAdapter of bootstrapEnv(...).
    private sealed class StreamClosedListener : Http2ConnectionAdapter
    {
        private readonly DataCompressionHttp2Test test;

        internal StreamClosedListener(DataCompressionHttp2Test test)
        {
            this.test = test;
        }

        public override void OnStreamClosed(IHttp2Stream stream)
        {
            CountDown(test.serverLatch);
        }
    }

    // CLR: the anonymous ChannelInitializer<Channel> of the server side of bootstrapEnv(...).
    private sealed class ServerInitializer : ChannelInitializer<IChannel>
    {
        private readonly DataCompressionHttp2Test test;
        private readonly CountdownEvent serverChannelLatch;

        internal ServerInitializer(DataCompressionHttp2Test test, CountdownEvent serverChannelLatch)
        {
            this.test = test;
            this.serverChannelLatch = serverChannelLatch;
        }

        protected override void InitChannel(IChannel ch)
        {
            test.serverConnectedChannel = ch;
            IChannelPipeline p = ch.Pipeline;
            IHttp2FrameWriter frameWriter = new DefaultHttp2FrameWriter();
            test.serverConnection.Remote().FlowController(
                    new DefaultHttp2RemoteFlowController(test.serverConnection));
            test.serverConnection.Local().FlowController(
                    new DefaultHttp2LocalFlowController(test.serverConnection).FrameWriter(frameWriter));
            IHttp2ConnectionEncoder encoder = new CompressorHttp2ConnectionEncoder(
                    new DefaultHttp2ConnectionEncoder(test.serverConnection, frameWriter));
            IHttp2ConnectionDecoder decoder =
                    new DefaultHttp2ConnectionDecoder(test.serverConnection, encoder, new DefaultHttp2FrameReader());
            Http2ConnectionHandler connectionHandler = new Http2ConnectionHandlerBuilder()
                    .FrameListener(new ServerDecompressorFrameListener(test))
                    .Codec(decoder, encoder).Build();
            p.AddLast(connectionHandler);
            serverChannelLatch.Signal();
        }
    }

    // CLR: the anonymous DelegatingDecompressorFrameListener of the server side of bootstrapEnv(...).
    private sealed class ServerDecompressorFrameListener : DelegatingDecompressorFrameListener
    {
        private readonly DataCompressionHttp2Test test;

        internal ServerDecompressorFrameListener(DataCompressionHttp2Test test)
            : base(test.serverConnection, test.serverListener.Object, test.maxAllocation)
        {
            this.test = test;
        }

        public override int OnDataRead(IChannelHandlerContext ctx, int streamId, ByteBuf data,
                                       int padding, bool endOfStream)
        {
            try
            {
                return base.OnDataRead(ctx, streamId, data, padding, endOfStream);
            }
            catch (Http2Exception e)
            {
                Volatile.Write(ref test.serverException, e);
                throw;
            }
        }
    }

    // CLR: the anonymous ChannelInitializer<Channel> of the client side of bootstrapEnv(...) and its anonymous
    // ChannelInboundHandlerAdapter (PrefaceWrittenHandler).
    private sealed class ClientInitializer : ChannelInitializer<IChannel>
    {
        private readonly DataCompressionHttp2Test test;
        private readonly CountdownEvent prefaceWrittenLatch;

        internal ClientInitializer(DataCompressionHttp2Test test, CountdownEvent prefaceWrittenLatch)
        {
            this.test = test;
            this.prefaceWrittenLatch = prefaceWrittenLatch;
        }

        protected override void InitChannel(IChannel ch)
        {
            IChannelPipeline p = ch.Pipeline;
            IHttp2FrameWriter frameWriter = new DefaultHttp2FrameWriter();
            test.clientConnection.Remote().FlowController(
                    new DefaultHttp2RemoteFlowController(test.clientConnection));
            test.clientConnection.Local().FlowController(
                    new DefaultHttp2LocalFlowController(test.clientConnection).FrameWriter(frameWriter));
            test.clientEncoder = new CompressorHttp2ConnectionEncoder(
                    new DefaultHttp2ConnectionEncoder(test.clientConnection, frameWriter));

            IHttp2ConnectionDecoder decoder =
                    new DefaultHttp2ConnectionDecoder(test.clientConnection, test.clientEncoder,
                            new DefaultHttp2FrameReader());
            test.clientHandler = new Http2ConnectionHandlerBuilder()
                    .FrameListener(new DelegatingDecompressorFrameListener(test.clientConnection,
                            test.clientListener.Object, 0))
                    // By default tests don't wait for server to gracefully shutdown streams
                    .GracefulShutdownTimeoutMillis(0)
                    .Codec(decoder, test.clientEncoder).Build();
            p.AddLast(test.clientHandler);
            p.AddLast(new PrefaceWrittenHandler(prefaceWrittenLatch));
        }
    }

    private sealed class PrefaceWrittenHandler : ChannelInboundHandlerAdapter
    {
        private readonly CountdownEvent prefaceWrittenLatch;

        internal PrefaceWrittenHandler(CountdownEvent prefaceWrittenLatch)
        {
            this.prefaceWrittenLatch = prefaceWrittenLatch;
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt == Http2ConnectionPrefaceAndSettingsFrameWrittenEvent.INSTANCE)
            {
                prefaceWrittenLatch.Signal();
                ctx.Pipeline.Remove(this);
            }
        }
    }

    private void AwaitServer()
    {
        Assert.True(serverLatch.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        serverOut.Flush();
        Exception cause = Volatile.Read(ref serverException);
        if (cause != null)
        {
            throw new Xunit.Sdk.XunitException("Server-side decompression error", cause);
        }
    }

    private IChannelHandlerContext CtxClient()
    {
        return clientChannel.Pipeline.FirstContext;
    }

    private TaskCompletionSource NewPromiseClient()
    {
        return NewPromise();
    }

    // CLR: CountDownLatch.countDown(), which does nothing when the count is already zero (CountdownEvent.Signal throws);
    // a Signal() that loses the race to zero against another thread is retried and then stops.
    private static void CountDown(CountdownEvent latch)
    {
        while (latch.CurrentCount > 0)
        {
            try
            {
                latch.Signal();
                return;
            }
            catch (InvalidOperationException)
            {
                // CLR: the count reached zero on another thread.
            }
        }
    }

    // CLR: the anonymous Http2Runnable instances (see the class comment).
    private sealed class ActionRunnable : IHttp2Runnable
    {
        private readonly Action action;

        internal ActionRunnable(Action action)
        {
            this.action = action;
        }

        public void Run()
        {
            action();
        }
    }
}
