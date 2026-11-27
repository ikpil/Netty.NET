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
using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Codec.Compression;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Local;
using static Netty.NET.Handler.Codec.Http.Tests.HttpHeadersTestUtils;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

#pragma warning disable CS0612, CS0618 // determineWrapper(String) and HttpContentCompressor(int, int, int, int) are @Deprecated without a replacement and ported with [Obsolete]; the source tests call them.
#pragma warning disable xUnit1051 // The source's bind/connect/close/writeAndFlush calls observe no test cancellation.

// CLR: the protected determineWrapper(String)/determineEncoding(String), which Java tests call through package access,
// are reached through the nested TestCompressor (as handler's tests reach a protected member through a forwarder,
// docs/handler-porting.md); ZlibWrapper is a C# enum, so the returned null wrapper is ZlibWrapper?. assertInstanceOf is
// Assert.IsAssignableFrom and AssertJ's isNotInstanceOf is Assert.False(o is IX); ByteBufUtil.hexDump is
// ByteBufUtil.HexDump; CharsetUtil.US_ASCII and StandardCharsets.UTF_8 are Encoding.ASCII/UTF8
// (docs/codec-smtp-porting.md). brotli4j's DirectDecompress (a test-only cross-check, owner decision 2026-11-24) is the
// BCL System.IO.Compression.BrotliDecoder.Decompress, whose OperationStatus.Done means "source was successfully and
// completely decompressed into destination", Java's DecoderJNI.Status.DONE
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotlidecoder.decompress?view=net-10.0); the
// BrotliDecoder struct is written in full because the simple name binds to codec-compression's BrotliDecoder, and the
// BrotliEncoder of HttpContentCompressor is codec-compression's port on the BCL encoder (quality 4, window 22,
// docs/codec-compression-porting.md). The @EnabledIf("isBrotliAvailable") tests always run because Brotli.IsAvailable()
// is always true. testExecutorPreserveOrdering: MultiThreadIoEventLoopGroup/LocalIoHandler/LocalChannel/ServerBootstrap
// /Bootstrap are the Transport ports, sync() is awaiting the Task, ChannelInitializer<LocalChannel> is the named nested
// ServerInitializer/ClientInitializer, the anonymous ChannelOutboundHandlerAdapter/ChannelInboundHandlerAdapter are
// named nested classes, BlockingQueue.poll(1, SECONDS) is BlockingCollection.TryTake with a one-second timeout, and the
// @Deprecated pipeline.addLast(EventExecutorGroup, ChannelHandler...) (not ported, docs/transport-porting.md) is
// addLast(group, null, handler), whose null name is generated as in Java. UUID.randomUUID() is Guid.NewGuid().
// AssembledHttpResponse implements IHttpContent; its narrowed retain/touch return AssembledHttpResponse and the
// IByteBufHolder/IReferenceCounted members are implemented explicitly (see DefaultHttpContent);
// UnsupportedOperationException is NotSupportedException, IllegalStateException InvalidOperationException.
// CompositeByteBuf.addComponent(true, buf) is AddComponent(buf, true), the parameter order of the Buffer port
// (src/Netty.NET.Buffer/CompositeByteBuf.cs).
public class HttpContentCompressorTest
{

    // CLR: exposes the protected determineWrapper/determineEncoding to the tests.
    private sealed class TestCompressor : HttpContentCompressor
    {
        internal TestCompressor() : base()
        {
        }

        internal TestCompressor(params ICompressionOptions[] compressionOptions) : base(compressionOptions)
        {
        }

        internal ZlibWrapper? InvokeDetermineWrapper(string acceptEncoding)
        {
            return DetermineWrapper(acceptEncoding);
        }

        internal string InvokeDetermineEncoding(string acceptEncoding)
        {
            return DetermineEncoding(acceptEncoding);
        }
    }

    [Fact]
    public void TestGetTargetContentEncoding()
    {
        TestCompressor compressor = new TestCompressor();

        string[] tests = {
            // Accept-Encoding -> Content-Encoding
            "", null,
            "*", "gzip",
            "*;q=0.0", null,
            "gzip", "gzip",
            "compress, gzip;q=0.5", "gzip",
            "gzip; q=0.5, identity", "gzip",
            "gzip ; q=0.1", "gzip",
            "gzip; q=0, deflate", "deflate",
            " deflate ; q=0 , *;q=0.5", "gzip",
        };
        for (int i = 0; i < tests.Length; i += 2)
        {
            string acceptEncoding = tests[i];
            string contentEncoding = tests[i + 1];
            ZlibWrapper? targetWrapper = compressor.InvokeDetermineWrapper(acceptEncoding);
            string targetEncoding = null;
            if (targetWrapper != null)
            {
                switch (targetWrapper)
                {
                case ZlibWrapper.GZIP:
                    targetEncoding = "gzip";
                    break;
                case ZlibWrapper.ZLIB:
                    targetEncoding = "deflate";
                    break;
                default:
                    Assert.Fail();
                    break;
                }
            }
            Assert.Equal(contentEncoding, targetEncoding);
        }
    }

    [Fact]
    public void TestDetermineEncoding()
    {
        TestCompressor compressor = new TestCompressor((ICompressionOptions[]) null);

        string[] tests = {
                // Accept-Encoding -> Content-Encoding
                "", null,
                ",", null,
                "identity", null,
                "unknown", null,
                "*", "br",
                "br", "br",
                "br ; q=0.1", "br",
                "unknown, br", "br",
                "br, gzip", "br",
                "gzip, br", "br",
                "identity, br", "br",
                "gzip", "gzip",
                "gzip ; q=0.1", "gzip",
        };
        for (int i = 0; i < tests.Length; i += 2)
        {
            string acceptEncoding = tests[i];
            string expectedEncoding = tests[i + 1];
            string targetEncoding = compressor.InvokeDetermineEncoding(acceptEncoding);
            Assert.Equal(expectedEncoding, targetEncoding);
        }
    }

    [Fact]
    public void TestSplitContent()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        ch.WriteInbound(NewRequest());

        ch.WriteOutbound(new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK));
        ch.WriteOutbound(new DefaultHttpContent(Unpooled.CopiedBuffer("Hell", Encoding.ASCII)));
        ch.WriteOutbound(new DefaultHttpContent(Unpooled.CopiedBuffer("o, w", Encoding.ASCII)));
        ch.WriteOutbound(new DefaultLastHttpContent(Unpooled.CopiedBuffer("orld", Encoding.ASCII)));

        AssertEncodedResponse(ch);

        IHttpContent chunk;
        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("1f8b0800000000000000f248cdc901000000ffff", ByteBufUtil.HexDump(chunk.Content));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("cad7512807000000ffff", ByteBufUtil.HexDump(chunk.Content));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("ca2fca4901000000ffff", ByteBufUtil.HexDump(chunk.Content));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("0300c2a99ae70c000000", ByteBufUtil.HexDump(chunk.Content));
        Assert.IsAssignableFrom<IHttpContent>(chunk);
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.False(chunk.Content.IsReadable());
        Assert.IsAssignableFrom<ILastHttpContent>(chunk);
        chunk.Release();

        Assert.Null(ch.ReadOutbound());
    }

    [Fact]
    public void TestChunkedContent()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        ch.WriteInbound(NewRequest());

        IHttpResponse res = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        res.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
        ch.WriteOutbound(res);

        AssertEncodedResponse(ch);

        ch.WriteOutbound(new DefaultHttpContent(Unpooled.CopiedBuffer("Hell", Encoding.ASCII)));
        ch.WriteOutbound(new DefaultHttpContent(Unpooled.CopiedBuffer("o, w", Encoding.ASCII)));
        ch.WriteOutbound(new DefaultLastHttpContent(Unpooled.CopiedBuffer("orld", Encoding.ASCII)));

        IHttpContent chunk;
        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("1f8b0800000000000000f248cdc901000000ffff", ByteBufUtil.HexDump(chunk.Content));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("cad7512807000000ffff", ByteBufUtil.HexDump(chunk.Content));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("ca2fca4901000000ffff", ByteBufUtil.HexDump(chunk.Content));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("0300c2a99ae70c000000", ByteBufUtil.HexDump(chunk.Content));
        Assert.IsAssignableFrom<IHttpContent>(chunk);
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.False(chunk.Content.IsReadable());
        Assert.IsAssignableFrom<ILastHttpContent>(chunk);
        chunk.Release();

        Assert.Null(ch.ReadOutbound());
    }

    [Fact]
    public void TestChunkedContentWithAssembledResponse()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        ch.WriteInbound(NewRequest());

        IHttpResponse res = new AssembledHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                Unpooled.CopiedBuffer("Hell", Encoding.ASCII));
        res.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
        ch.WriteOutbound(res);

        AssertAssembledEncodedResponse(ch);

        ch.WriteOutbound(new DefaultHttpContent(Unpooled.CopiedBuffer("o, w", Encoding.ASCII)));
        ch.WriteOutbound(new DefaultLastHttpContent(Unpooled.CopiedBuffer("orld", Encoding.ASCII)));

        IHttpContent chunk;
        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("1f8b0800000000000000f248cdc901000000ffff", ByteBufUtil.HexDump(chunk.Content));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("cad7512807000000ffff", ByteBufUtil.HexDump(chunk.Content));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("ca2fca4901000000ffff", ByteBufUtil.HexDump(chunk.Content));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("0300c2a99ae70c000000", ByteBufUtil.HexDump(chunk.Content));
        Assert.IsAssignableFrom<IHttpContent>(chunk);
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.False(chunk.Content.IsReadable());
        Assert.IsAssignableFrom<ILastHttpContent>(chunk);
        chunk.Release();

        Assert.Null(ch.ReadOutbound());
    }

    [Fact]
    public void TestChunkedContentWithAssembledResponseIdentityEncoding()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        ch.WriteInbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/"));

        IHttpResponse res = new AssembledHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                Unpooled.CopiedBuffer("Hell", Encoding.ASCII));
        res.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
        ch.WriteOutbound(res);

        ch.WriteOutbound(new DefaultHttpContent(Unpooled.CopiedBuffer("o, w", Encoding.ASCII)));
        ch.WriteOutbound(new DefaultLastHttpContent(Unpooled.CopiedBuffer("orld", Encoding.ASCII)));

        IHttpContent chunk;
        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("Hell", chunk.Content.ToString(Encoding.UTF8));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("o, w", chunk.Content.ToString(Encoding.UTF8));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("orld", chunk.Content.ToString(Encoding.UTF8));
        Assert.IsAssignableFrom<ILastHttpContent>(chunk);
        chunk.Release();

        Assert.Null(ch.ReadOutbound());
    }

    [Fact]
    public void TestContentWithAssembledResponseIdentityEncodingHttp10()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        ch.WriteInbound(new DefaultFullHttpRequest(HttpVersion.HTTP_1_0, HttpMethod.GET, "/"));

        IHttpResponse res = new AssembledHttpResponse(HttpVersion.HTTP_1_0, HttpResponseStatus.OK,
                Unpooled.CopiedBuffer("Hell", Encoding.ASCII));
        ch.WriteOutbound(res);

        ch.WriteOutbound(new DefaultHttpContent(Unpooled.CopiedBuffer("o, w", Encoding.ASCII)));
        ch.WriteOutbound(new DefaultLastHttpContent(Unpooled.CopiedBuffer("orld", Encoding.ASCII)));

        IHttpContent chunk;
        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("Hell", chunk.Content.ToString(Encoding.UTF8));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("o, w", chunk.Content.ToString(Encoding.UTF8));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("orld", chunk.Content.ToString(Encoding.UTF8));
        Assert.IsAssignableFrom<ILastHttpContent>(chunk);
        chunk.Release();

        Assert.Null(ch.ReadOutbound());
    }

    [Fact]
    public void TestChunkedContentWithTrailingHeader()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        ch.WriteInbound(NewRequest());

        IHttpResponse res = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        res.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
        ch.WriteOutbound(res);

        AssertEncodedResponse(ch);

        ch.WriteOutbound(new DefaultHttpContent(Unpooled.CopiedBuffer("Hell", Encoding.ASCII)));
        ch.WriteOutbound(new DefaultHttpContent(Unpooled.CopiedBuffer("o, w", Encoding.ASCII)));
        ILastHttpContent content = new DefaultLastHttpContent(Unpooled.CopiedBuffer("orld", Encoding.ASCII));
        content.TrailingHeaders().Set(Of("X-Test"), Of("Netty"));
        ch.WriteOutbound(content);

        IHttpContent chunk;
        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("1f8b0800000000000000f248cdc901000000ffff", ByteBufUtil.HexDump(chunk.Content));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("cad7512807000000ffff", ByteBufUtil.HexDump(chunk.Content));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("ca2fca4901000000ffff", ByteBufUtil.HexDump(chunk.Content));
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("0300c2a99ae70c000000", ByteBufUtil.HexDump(chunk.Content));
        Assert.IsAssignableFrom<IHttpContent>(chunk);
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.False(chunk.Content.IsReadable());
        Assert.IsAssignableFrom<ILastHttpContent>(chunk);
        Assert.Equal("Netty", ((ILastHttpContent) chunk).TrailingHeaders().Get(Of("X-Test")));
        Assert.Equal(DecoderResult.SUCCESS, chunk.DecoderResult());
        chunk.Release();

        Assert.Null(ch.ReadOutbound());
    }

    [Fact]
    public void TestFullContentWithContentLength()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        ch.WriteInbound(NewRequest());

        IFullHttpResponse fullRes = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                Unpooled.CopiedBuffer("Hello, World", Encoding.ASCII));
        fullRes.Headers().Set(HttpHeaderNames.CONTENT_LENGTH, fullRes.Content.ReadableBytes);
        ch.WriteOutbound(fullRes);

        IHttpResponse res = ch.ReadOutbound<IHttpResponse>();
        Assert.IsAssignableFrom<IHttpResponse>(res);

        Assert.Null(res.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));
        Assert.Equal("gzip", res.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));

        long contentLengthHeaderValue = HttpUtil.GetContentLength(res);
        long observedLength = 0;

        IHttpContent c = ch.ReadOutbound<IHttpContent>();
        observedLength += c.Content.ReadableBytes;
        Assert.Equal("1f8b0800000000000000f248cdc9c9d75108cf2fca4901000000ffff", ByteBufUtil.HexDump(c.Content));
        c.Release();

        c = ch.ReadOutbound<IHttpContent>();
        observedLength += c.Content.ReadableBytes;
        Assert.Equal("0300c6865b260c000000", ByteBufUtil.HexDump(c.Content));
        c.Release();

        ILastHttpContent last = ch.ReadOutbound<ILastHttpContent>();
        Assert.Equal(0, last.Content.ReadableBytes);
        last.Release();

        Assert.Null(ch.ReadOutbound());
        Assert.Equal(contentLengthHeaderValue, observedLength);
    }

    [Fact]
    public void TestFullContent()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        ch.WriteInbound(NewRequest());

        IFullHttpResponse res = new DefaultFullHttpResponse(
            HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
            Unpooled.CopiedBuffer("Hello, World", Encoding.ASCII));
        ch.WriteOutbound(res);

        AssertEncodedResponse(ch);
        IHttpContent c = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("1f8b0800000000000000f248cdc9c9d75108cf2fca4901000000ffff", ByteBufUtil.HexDump(c.Content));
        c.Release();

        c = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("0300c6865b260c000000", ByteBufUtil.HexDump(c.Content));
        c.Release();

        ILastHttpContent last = ch.ReadOutbound<ILastHttpContent>();
        Assert.Equal(0, last.Content.ReadableBytes);
        last.Release();

        Assert.Null(ch.ReadOutbound());
    }

    private sealed class PassThroughOutboundHandler : ChannelOutboundHandlerAdapter
    {
        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            return base.WriteAsync(ctx, msg, cancellationToken);
        }
    }

    private sealed class RespondingHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IFullHttpRequest)
            {
                IFullHttpResponse res =
                    new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                        Unpooled.CopiedBuffer("Hello, World", Encoding.ASCII));
                _ = ctx.WriteAndFlushAsync(res);
                ReferenceCountUtil.Release(msg);
                return;
            }
            base.ChannelRead(ctx, msg);
        }
    }

    private sealed class ServerInitializer : ChannelInitializer<LocalChannel>
    {
        private readonly IEventLoopGroup compressorGroup;

        internal ServerInitializer(IEventLoopGroup compressorGroup)
        {
            this.compressorGroup = compressorGroup;
        }

        protected override void InitChannel(LocalChannel ch)
        {
            ch.Pipeline
                .AddLast(new HttpServerCodec())
                .AddLast(new HttpObjectAggregator(1024))
                .AddLast(compressorGroup, null, new HttpContentCompressor())
                .AddLast(new PassThroughOutboundHandler())
                .AddLast(new RespondingHandler());
        }
    }

    private sealed class CollectingHandler : ChannelInboundHandlerAdapter
    {
        private readonly BlockingCollection<IHttpObject> responses;

        internal CollectingHandler(BlockingCollection<IHttpObject> responses)
        {
            this.responses = responses;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            if (msg is IHttpObject)
            {
                responses.Add((IHttpObject) msg);
                return;
            }
            base.ChannelRead(ctx, msg);
        }
    }

    private sealed class ClientInitializer : ChannelInitializer<LocalChannel>
    {
        private readonly BlockingCollection<IHttpObject> responses;

        internal ClientInitializer(BlockingCollection<IHttpObject> responses)
        {
            this.responses = responses;
        }

        protected override void InitChannel(LocalChannel ch)
        {
            ch.Pipeline.AddLast(new HttpClientCodec()).AddLast(new CollectingHandler(responses));
        }
    }

    // CLR: BlockingQueue.poll(1, TimeUnit.SECONDS), null on timeout.
    private static IHttpObject Poll(BlockingCollection<IHttpObject> responses)
    {
        return responses.TryTake(out IHttpObject o, TimeSpan.FromSeconds(1)) ? o : null;
    }

    [Fact]
    public async Task TestExecutorPreserveOrdering()
    {
        IEventLoopGroup compressorGroup = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());
        IEventLoopGroup localGroup = new MultiThreadIoEventLoopGroup(1, LocalIoHandler.NewFactory());
        IChannel server = null;
        IChannel client = null;
        try
        {
            ServerBootstrap bootstrap = new ServerBootstrap()
                .Channel<LocalServerChannel>()
                .Group(localGroup)
                .ChildHandler(new ServerInitializer(compressorGroup));

            LocalAddress address = new LocalAddress(Guid.NewGuid().ToString());
            server = await bootstrap.BindAsync(address);

            BlockingCollection<IHttpObject> responses = new BlockingCollection<IHttpObject>();

            client = await new Bootstrap()
                .Channel<LocalChannel>()
                .RemoteAddress(address)
                .Group(localGroup)
                .Handler(new ClientInitializer(responses)).ConnectAsync();

            await client.WriteAndFlushAsync(NewRequest());

            AssertEncodedResponse((IHttpResponse) Poll(responses));
            IHttpContent c = (IHttpContent) Poll(responses);
            Assert.NotNull(c);
            Assert.Equal("1f8b0800000000000000f248cdc9c9d75108cf2fca4901000000ffff",
                    ByteBufUtil.HexDump(c.Content));
            c.Release();

            c = (IHttpContent) Poll(responses);
            Assert.NotNull(c);
            Assert.Equal("0300c6865b260c000000", ByteBufUtil.HexDump(c.Content));
            c.Release();

            ILastHttpContent last = (ILastHttpContent) Poll(responses);
            Assert.NotNull(last);
            Assert.Equal(0, last.Content.ReadableBytes);
            last.Release();

            Assert.Null(Poll(responses));
        }
        finally
        {
            if (client != null)
            {
                await client.CloseAsync();
            }
            if (server != null)
            {
                await server.CloseAsync();
            }
            _ = compressorGroup.ShutdownGracefullyAsync();
            _ = localGroup.ShutdownGracefullyAsync();
        }
    }

    /**
     * If the length of the content is unknown, {@link HttpContentEncoder} should not skip encoding the content
     * even if the actual length is turned out to be 0.
     */
    [Fact]
    public void TestEmptySplitContent()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        ch.WriteInbound(NewRequest());

        ch.WriteOutbound(new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK));
        AssertEncodedResponse(ch);

        ch.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT);
        IHttpContent chunk = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("1f8b080000000000000003000000000000000000", ByteBufUtil.HexDump(chunk.Content));
        Assert.IsAssignableFrom<IHttpContent>(chunk);
        chunk.Release();

        chunk = ch.ReadOutbound<IHttpContent>();
        Assert.False(chunk.Content.IsReadable());
        Assert.IsAssignableFrom<ILastHttpContent>(chunk);
        chunk.Release();

        Assert.Null(ch.ReadOutbound());
    }

    /**
     * If the length of the content is 0 for sure, {@link HttpContentEncoder} should skip encoding.
     */
    [Fact]
    public void TestEmptyFullContent()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        ch.WriteInbound(NewRequest());

        IFullHttpResponse res = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK, Unpooled.EmptyBuffer);
        ch.WriteOutbound(res);

        object o = ch.ReadOutbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);

        res = (IFullHttpResponse) o;
        Assert.Null(res.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));

        // Content encoding shouldn't be modified.
        Assert.Null(res.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));
        Assert.Equal(0, res.Content.ReadableBytes);
        Assert.Equal("", res.Content.ToString(Encoding.ASCII));
        res.Release();

        Assert.Null(ch.ReadOutbound());
    }

    [Fact]
    public void TestEmptyFullContentWithTrailer()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        ch.WriteInbound(NewRequest());

        IFullHttpResponse res = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK, Unpooled.EmptyBuffer);
        res.TrailingHeaders().Set(Of("X-Test"), Of("Netty"));
        ch.WriteOutbound(res);

        object o = ch.ReadOutbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);

        res = (IFullHttpResponse) o;
        Assert.Null(res.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));

        // Content encoding shouldn't be modified.
        Assert.Null(res.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));
        Assert.Equal(0, res.Content.ReadableBytes);
        Assert.Equal("", res.Content.ToString(Encoding.ASCII));
        Assert.Equal("Netty", res.TrailingHeaders().Get(Of("X-Test")));
        Assert.Equal(DecoderResult.SUCCESS, res.DecoderResult());
        Assert.Null(ch.ReadOutbound());
    }

    [Fact]
    public void Test100Continue()
    {
        IFullHttpRequest request = NewRequest();
        HttpUtil.Set100ContinueExpected(request, true);

        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        ch.WriteInbound(request);

        IFullHttpResponse continueResponse = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.CONTINUE, Unpooled.EmptyBuffer);

        ch.WriteOutbound(continueResponse);

        IFullHttpResponse res = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK, Unpooled.EmptyBuffer);
        res.TrailingHeaders().Set(Of("X-Test"), Of("Netty"));
        ch.WriteOutbound(res);

        object o = ch.ReadOutbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);

        res = (IFullHttpResponse) o;
        Assert.Same(continueResponse, res);
        res.Release();

        o = ch.ReadOutbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);

        res = (IFullHttpResponse) o;
        Assert.Null(res.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));

        // Content encoding shouldn't be modified.
        Assert.Null(res.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));
        Assert.Equal(0, res.Content.ReadableBytes);
        Assert.Equal("", res.Content.ToString(Encoding.ASCII));
        Assert.Equal("Netty", res.TrailingHeaders().Get(Of("X-Test")));
        Assert.Equal(DecoderResult.SUCCESS, res.DecoderResult());
        Assert.Null(ch.ReadOutbound());
    }

    [Fact]
    public void TestMultiple1xxInformationalResponse()
    {
        IFullHttpRequest request = NewRequest();
        HttpUtil.Set100ContinueExpected(request, true);

        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        ch.WriteInbound(request);

        IFullHttpResponse continueResponse = new DefaultFullHttpResponse(
            HttpVersion.HTTP_1_1, HttpResponseStatus.CONTINUE, Unpooled.EmptyBuffer);
        ch.WriteOutbound(continueResponse);

        IFullHttpResponse earlyHintsResponse = new DefaultFullHttpResponse(
            HttpVersion.HTTP_1_1, HttpResponseStatus.EARLY_HINTS, Unpooled.EmptyBuffer);
        earlyHintsResponse.TrailingHeaders().Set(Of("X-Test"), Of("Netty"));
        ch.WriteOutbound(earlyHintsResponse);

        IFullHttpResponse res = new DefaultFullHttpResponse(
            HttpVersion.HTTP_1_1, HttpResponseStatus.OK, Unpooled.EmptyBuffer);
        res.TrailingHeaders().Set(Of("X-Test"), Of("Netty"));
        ch.WriteOutbound(res);

        object o = ch.ReadOutbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);

        res = (IFullHttpResponse) o;
        Assert.Same(continueResponse, res);
        res.Release();

        o = ch.ReadOutbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);

        res = (IFullHttpResponse) o;
        Assert.Same(earlyHintsResponse, res);
        res.Release();

        o = ch.ReadOutbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);

        res = (IFullHttpResponse) o;
        Assert.Null(res.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));

        // Content encoding shouldn't be modified.
        Assert.Null(res.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));
        Assert.Equal(0, res.Content.ReadableBytes);
        Assert.Equal("", res.Content.ToString(Encoding.ASCII));
        Assert.Equal("Netty", res.TrailingHeaders().Get(Of("X-Test")));
        Assert.Equal(DecoderResult.SUCCESS, res.DecoderResult());
        Assert.Null(ch.ReadOutbound());

        Assert.True(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void Test103EarlyHintsResponse()
    {
        IFullHttpRequest request = NewRequest();

        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        ch.WriteInbound(request);

        IFullHttpResponse earlyHintsResponse = new DefaultFullHttpResponse(
            HttpVersion.HTTP_1_1, HttpResponseStatus.EARLY_HINTS, Unpooled.EmptyBuffer);
        earlyHintsResponse.TrailingHeaders().Set(Of("X-Test"), Of("Netty"));
        ch.WriteOutbound(earlyHintsResponse);

        IFullHttpResponse res = new DefaultFullHttpResponse(
            HttpVersion.HTTP_1_1, HttpResponseStatus.OK, Unpooled.EmptyBuffer);
        res.TrailingHeaders().Set(Of("X-Test"), Of("Netty"));
        ch.WriteOutbound(res);

        object o = ch.ReadOutbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);

        res = (IFullHttpResponse) o;
        Assert.Same(earlyHintsResponse, res);
        res.Release();

        o = ch.ReadOutbound();
        Assert.IsAssignableFrom<IFullHttpResponse>(o);

        res = (IFullHttpResponse) o;
        Assert.Null(res.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));

        // Content encoding shouldn't be modified.
        Assert.Null(res.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));
        Assert.Equal(0, res.Content.ReadableBytes);
        Assert.Equal("", res.Content.ToString(Encoding.ASCII));
        Assert.Equal("Netty", res.TrailingHeaders().Get(Of("X-Test")));
        Assert.Equal(DecoderResult.SUCCESS, res.DecoderResult());
        Assert.Null(ch.ReadOutbound());

        Assert.True(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void TestTooManyResponses()
    {
        IFullHttpRequest request = NewRequest();
        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        ch.WriteInbound(request);

        ch.WriteOutbound(new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK, Unpooled.EmptyBuffer));

        EncoderException e = Assert.Throws<EncoderException>(() => ch.WriteOutbound(new DefaultFullHttpResponse(
                    HttpVersion.HTTP_1_1, HttpResponseStatus.OK, Unpooled.EmptyBuffer)));
        Assert.IsAssignableFrom<InvalidOperationException>(e.InnerException);
        Assert.True(ch.Finish());
        for (;;)
        {
            object message = ch.ReadOutbound();
            if (message == null)
            {
                break;
            }
            ReferenceCountUtil.Release(message);
        }
        for (;;)
        {
            object message = ch.ReadInbound();
            if (message == null)
            {
                break;
            }
            ReferenceCountUtil.Release(message);
        }
    }

    [Fact]
    public void TestIdentity()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        Assert.True(ch.WriteInbound(NewRequest()));

        IFullHttpResponse res = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                Unpooled.CopiedBuffer("Hello, World", Encoding.ASCII));
        int len = res.Content.ReadableBytes;
        res.Headers().Set(HttpHeaderNames.CONTENT_LENGTH, len);
        res.Headers().Set(HttpHeaderNames.CONTENT_ENCODING, HttpHeaderValues.IDENTITY);
        Assert.True(ch.WriteOutbound(res));

        IFullHttpResponse response = ch.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(len.ToString(CultureInfo.InvariantCulture), response.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));
        Assert.Equal(HttpHeaderValues.IDENTITY.ToString(), response.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));
        Assert.Equal("Hello, World", response.Content.ToString(Encoding.ASCII));
        response.Release();

        Assert.True(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void TestCustomEncoding()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        Assert.True(ch.WriteInbound(NewRequest()));

        IFullHttpResponse res = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                Unpooled.CopiedBuffer("Hello, World", Encoding.ASCII));
        int len = res.Content.ReadableBytes;
        res.Headers().Set(HttpHeaderNames.CONTENT_LENGTH, len);
        res.Headers().Set(HttpHeaderNames.CONTENT_ENCODING, "ascii");
        Assert.True(ch.WriteOutbound(res));

        IFullHttpResponse response = ch.ReadOutbound<IFullHttpResponse>();
        Assert.Equal(len.ToString(CultureInfo.InvariantCulture), response.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));
        Assert.Equal("ascii", response.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));
        Assert.Equal("Hello, World", response.Content.ToString(Encoding.ASCII));
        response.Release();

        Assert.True(ch.FinishAndReleaseAll());
    }

    internal static bool IsBrotliAvailable()
    {
        return Brotli.IsAvailable();
    }

    // CLR: brotli4j's DirectDecompress.decompress(bytes) with its result status and data (see the class comment).
    private static string DecompressBrotli(ByteBuf contentBuf)
    {
        byte[] compressed = ByteBufUtil.GetBytes(contentBuf);
        byte[] decompressed = new byte[1024];
        using System.IO.Compression.BrotliDecoder decoder = new System.IO.Compression.BrotliDecoder();
        OperationStatus status = decoder.Decompress(compressed, decompressed, out _, out int written);
        Assert.Equal(OperationStatus.Done, status);
        return Encoding.ASCII.GetString(decompressed, 0, written);
    }

    [Fact]
    public void TestBrotliFullHttpResponse()
    {
        HttpContentCompressor compressor = new HttpContentCompressor((ICompressionOptions[]) null);
        EmbeddedChannel ch = new EmbeddedChannel(compressor);
        Assert.True(ch.WriteInbound(NewBrotliRequest()));

        IFullHttpResponse res = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                Unpooled.CopiedBuffer("Hello Hello Hello Hello Hello", Encoding.ASCII));
        int len = res.Content.ReadableBytes;
        res.Headers().Set(HttpHeaderNames.CONTENT_LENGTH, len);
        res.Headers().Set(HttpHeaderNames.CONTENT_TYPE, "text/plain");
        Assert.True(ch.WriteOutbound(res));

        DefaultHttpResponse response = ch.ReadOutbound<DefaultHttpResponse>();
        Assert.Equal(19.ToString(CultureInfo.InvariantCulture), response.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));
        Assert.Equal("text/plain", response.Headers().Get(HttpHeaderNames.CONTENT_TYPE));
        Assert.Equal("br", response.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));

        CompositeByteBuf contentBuf = Unpooled.CompositeBuffer();
        IHttpContent content;
        while ((content = ch.ReadOutbound<IHttpContent>()) != null)
        {
            if (content.Content.IsReadable())
            {
                contentBuf.AddComponent(content.Content, true);
            }
            else
            {
                content.Content.Release();
            }
        }

        Assert.Equal("Hello Hello Hello Hello Hello", DecompressBrotli(contentBuf));

        Assert.True(ch.FinishAndReleaseAll());
        contentBuf.Release();
    }

    [Fact]
    public void TestBrotliChunkedContent()
    {
        HttpContentCompressor compressor = new HttpContentCompressor((ICompressionOptions[]) null);
        EmbeddedChannel ch = new EmbeddedChannel(compressor);
        Assert.True(ch.WriteInbound(NewBrotliRequest()));

        IHttpResponse res = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        res.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
        res.Headers().Set(HttpHeaderNames.CONTENT_TYPE, "text/plain");
        ch.WriteOutbound(res);

        IHttpResponse outboundRes = ch.ReadOutbound<IHttpResponse>();
        Assert.False(outboundRes is IHttpContent);
        Assert.Equal("chunked", outboundRes.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));
        Assert.Null(outboundRes.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));
        Assert.Equal("br", outboundRes.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));
        Assert.Equal("text/plain", outboundRes.Headers().Get(HttpHeaderNames.CONTENT_TYPE));

        ch.WriteOutbound(new DefaultHttpContent(Unpooled.CopiedBuffer("Hell", Encoding.ASCII)));
        ch.WriteOutbound(new DefaultHttpContent(Unpooled.CopiedBuffer("o world. Hello w", Encoding.ASCII)));
        ch.WriteOutbound(new DefaultLastHttpContent(Unpooled.CopiedBuffer("orld.", Encoding.ASCII)));

        CompositeByteBuf contentBuf = Unpooled.CompositeBuffer();
        IHttpContent content;
        while ((content = ch.ReadOutbound<IHttpContent>()) != null)
        {
            if (content.Content.IsReadable())
            {
                contentBuf.AddComponent(content.Content, true);
            }
            else
            {
                content.Content.Release();
            }
        }

        Assert.Equal("Hello world. Hello world.", DecompressBrotli(contentBuf));

        Assert.True(ch.FinishAndReleaseAll());
        contentBuf.Release();
    }

    [Fact]
    public void TestCompressThresholdAllCompress()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        Assert.True(ch.WriteInbound(NewRequest()));

        IFullHttpResponse res1023 = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                Unpooled.WrappedBuffer(new byte[1023]));
        Assert.True(ch.WriteOutbound(res1023));
        DefaultHttpResponse response1023 = ch.ReadOutbound<DefaultHttpResponse>();
        Assert.Equal("gzip", response1023.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));
        ch.ReleaseOutbound();

        Assert.True(ch.WriteInbound(NewRequest()));
        IFullHttpResponse res1024 = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                Unpooled.WrappedBuffer(new byte[1024]));
        Assert.True(ch.WriteOutbound(res1024));
        DefaultHttpResponse response1024 = ch.ReadOutbound<DefaultHttpResponse>();
        Assert.Equal("gzip", response1024.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));
        Assert.True(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void TestCompressThresholdNotCompress()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor(6, 15, 8, 1024));
        Assert.True(ch.WriteInbound(NewRequest()));

        IFullHttpResponse res1023 = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                Unpooled.WrappedBuffer(new byte[1023]));
        Assert.True(ch.WriteOutbound(res1023));
        DefaultHttpResponse response1023 = ch.ReadOutbound<DefaultHttpResponse>();
        Assert.False(response1023.Headers().Contains(HttpHeaderNames.CONTENT_ENCODING));
        ch.ReleaseOutbound();

        Assert.True(ch.WriteInbound(NewRequest()));
        IFullHttpResponse res1024 = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                Unpooled.WrappedBuffer(new byte[1024]));
        Assert.True(ch.WriteOutbound(res1024));
        DefaultHttpResponse response1024 = ch.ReadOutbound<DefaultHttpResponse>();
        Assert.Equal("gzip", response1024.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));
        Assert.True(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void TestMultipleAcceptEncodingHeaders()
    {
        IFullHttpRequest request = NewRequest();
        request.Headers().Set(HttpHeaderNames.ACCEPT_ENCODING, "unknown; q=1.0")
               .Add(HttpHeaderNames.ACCEPT_ENCODING, "gzip; q=0.5")
               .Add(HttpHeaderNames.ACCEPT_ENCODING, "deflate; q=0");

        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());

        Assert.True(ch.WriteInbound(request));

        IFullHttpResponse res = new DefaultFullHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.OK,
                Unpooled.CopiedBuffer("Gzip Win", Encoding.ASCII));
        Assert.True(ch.WriteOutbound(res));

        AssertEncodedResponse(ch);
        IHttpContent c = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("1f8b080000000000000072afca2c5008cfcc03000000ffff", ByteBufUtil.HexDump(c.Content));
        c.Release();

        c = ch.ReadOutbound<IHttpContent>();
        Assert.Equal("03001f2ebf0f08000000", ByteBufUtil.HexDump(c.Content));
        c.Release();

        ILastHttpContent last = ch.ReadOutbound<ILastHttpContent>();
        Assert.Equal(0, last.Content.ReadableBytes);
        last.Release();

        Assert.Null(ch.ReadOutbound());
        Assert.True(ch.FinishAndReleaseAll());
    }

    [Fact]
    public void TestEmpty()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new HttpContentCompressor());
        Assert.True(ch.WriteInbound(NewRequest()));

        DefaultHttpResponse response = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        response.Headers().Add(HttpHeaderNames.CONTENT_LENGTH, 0);
        Assert.True(ch.WriteOutbound(response));
        Assert.True(ch.WriteOutbound(new DefaultHttpContent(Unpooled.EmptyBuffer)));
        Assert.True(ch.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT));

        ch.CheckException();
        ch.FinishAndReleaseAll();
    }

    private static IFullHttpRequest NewRequest()
    {
        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/");
        req.Headers().Set(HttpHeaderNames.ACCEPT_ENCODING, "gzip");
        return req;
    }

    private static IFullHttpRequest NewBrotliRequest()
    {
        IFullHttpRequest req = new DefaultFullHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET, "/");
        req.Headers().Set(HttpHeaderNames.ACCEPT_ENCODING, "br");
        return req;
    }

    private static void AssertEncodedResponse(EmbeddedChannel ch)
    {
        object o = ch.ReadOutbound();
        Assert.IsAssignableFrom<IHttpResponse>(o);

        AssertEncodedResponse((IHttpResponse) o);
    }

    private static void AssertEncodedResponse(IHttpResponse res)
    {
        Assert.False(res is IHttpContent);
        Assert.Equal("chunked", res.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));
        Assert.Null(res.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));
        Assert.Equal("gzip", res.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));
    }

    private static void AssertAssembledEncodedResponse(EmbeddedChannel ch)
    {
        object o = ch.ReadOutbound();
        Assert.IsAssignableFrom<AssembledHttpResponse>(o);

        AssembledHttpResponse res = (AssembledHttpResponse) o;
        try
        {
            Assert.IsAssignableFrom<IHttpContent>(res);
            Assert.Equal("chunked", res.Headers().Get(HttpHeaderNames.TRANSFER_ENCODING));
            Assert.Null(res.Headers().Get(HttpHeaderNames.CONTENT_LENGTH));
            Assert.Equal("gzip", res.Headers().Get(HttpHeaderNames.CONTENT_ENCODING));
        }
        finally
        {
            res.Release();
        }
    }

    internal class AssembledHttpResponse : DefaultHttpResponse, IHttpContent
    {

        private readonly ByteBuf content;

        internal AssembledHttpResponse(HttpVersion version, HttpResponseStatus status, ByteBuf content)
            : base(version, status)
        {
            this.content = content;
        }

        public IHttpContent Copy()
        {
            throw new NotSupportedException();
        }

        public IHttpContent Duplicate()
        {
            throw new NotSupportedException();
        }

        public IHttpContent RetainedDuplicate()
        {
            throw new NotSupportedException();
        }

        public IHttpContent Replace(ByteBuf content)
        {
            throw new NotSupportedException();
        }

        public AssembledHttpResponse Retain()
        {
            content.Retain();
            return this;
        }

        public AssembledHttpResponse Retain(int increment)
        {
            content.Retain(increment);
            return this;
        }

        public ByteBuf Content => content;

        public int RefCnt => content.RefCnt;

        public bool Release()
        {
            return content.Release();
        }

        public bool Release(int decrement)
        {
            return content.Release(decrement);
        }

        public AssembledHttpResponse Touch()
        {
            content.Touch();
            return this;
        }

        public AssembledHttpResponse Touch(object hint)
        {
            content.Touch(hint);
            return this;
        }

        IByteBufHolder IByteBufHolder.Copy()
        {
            return Copy();
        }

        IByteBufHolder IByteBufHolder.Duplicate()
        {
            return Duplicate();
        }

        IByteBufHolder IByteBufHolder.RetainedDuplicate()
        {
            return RetainedDuplicate();
        }

        IByteBufHolder IByteBufHolder.Replace(ByteBuf content)
        {
            return Replace(content);
        }

        IByteBufHolder IByteBufHolder.Retain()
        {
            return Retain();
        }

        IByteBufHolder IByteBufHolder.Retain(int increment)
        {
            return Retain(increment);
        }

        IByteBufHolder IByteBufHolder.Touch()
        {
            return Touch();
        }

        IByteBufHolder IByteBufHolder.Touch(object hint)
        {
            return Touch(hint);
        }

        IReferenceCounted IReferenceCounted.Retain()
        {
            return Retain();
        }

        IReferenceCounted IReferenceCounted.Retain(int increment)
        {
            return Retain(increment);
        }

        IReferenceCounted IReferenceCounted.Touch()
        {
            return Touch();
        }

        IReferenceCounted IReferenceCounted.Touch(object hint)
        {
            return Touch(hint);
        }
    }
}
