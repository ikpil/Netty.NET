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

using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http.Tests;

#pragma warning disable xUnit1031 // Future.get() of the source: the EmbeddedChannel write has completed when WriteAndFlushAsync returns.
#pragma warning disable xUnit1051 // The source's writeAndFlush(...) observes no test cancellation.

// CLR: getBuffers() builds big- and little-endian heap and NIO-wrapped buffers with the @Deprecated
// ByteBuf.order(ByteOrder); the Buffer port has no order() and no swapped buffers (docs/codec-base-porting.md, Byte
// order: "Base64 has only its big-endian branches, because ByteBuf.order() is deprecated and not ported";
// docs/buffer-porting.md), so it returns the two big-endian buffers: Unpooled.Buffer(128) and a wrapped managed
// 128-byte array (ByteBuffer.allocate(128) is a heap NIO buffer) with its writer index reset. The encoder writes
// with ByteBufUtil.writeShortBE/writeMediumBE, which write the same bytes into either byte order in Java, so the
// little-endian buffers add no other assertion. The protected encodeInitialLine(...) that the Java test reaches through
// package access is the internal forwarder InvokeEncodeInitialLine. Future.get() wraps the failure in an
// ExecutionException whose cause is the write's exception; Task.GetAwaiter().GetResult() throws the faulted task's
// exception itself ("The task completed in a Faulted state",
// https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.taskawaiter.getresult?view=net-10.0),
// so e.getCause().getCause() is the thrown EncoderException's InnerException. The @Deprecated
// DefaultHttpRequest(HttpVersion, HttpMethod, String, boolean) and DefaultHttpHeaders(boolean) of
// CustomFullHttpRequest are not ported, so it uses their named replacements, the HttpHeadersFactory constructor and
// DefaultHttpHeadersFactory.headersFactory().withValidation(validateHeaders) (owner decision 2026-11-16). The narrowed
// returns of CustomFullHttpRequest are covariant overrides of DefaultHttpRequest's virtual setters, new public members
// for the IByteBufHolder members, and explicit IByteBufHolder/IReferenceCounted implementations (as in
// DefaultFullHttpRequest). CustomHttpRequest's overrides of the @Deprecated getDecoderResult()/getProtocolVersion()/
// getMethod()/getUri() have no interface member to implement (see IHttpObject, IHttpMessage, IHttpRequest), so
// protocolVersion() and method() return the values those getters returned. CharsetUtil.US_ASCII/StandardCharsets.US_ASCII
// are Encoding.ASCII (docs/codec-smtp-porting.md).
public class HttpRequestEncoderTest
{

    private static ByteBuf[] GetBuffers()
    {
        return new ByteBuf[]{
                Unpooled.Buffer(128),
                Unpooled.WrappedBuffer(new byte[128], 0, 128).ResetWriterIndex()
        };
    }

    [Fact]
    public void TestUriWithoutPath()
    {
        foreach (ByteBuf buffer in GetBuffers())
        {
            HttpRequestEncoder encoder = new HttpRequestEncoder();
            encoder.InvokeEncodeInitialLine(buffer, new DefaultHttpRequest(HttpVersion.HTTP_1_1,
                    HttpMethod.GET, "http://localhost"));
            string req = buffer.ToString(Encoding.ASCII);
            Assert.Equal("GET http://localhost/ HTTP/1.1\r\n", req);
            buffer.Release();
        }
    }

    [Fact]
    public void TestUriWithoutPath2()
    {
        foreach (ByteBuf buffer in GetBuffers())
        {
            HttpRequestEncoder encoder = new HttpRequestEncoder();
            encoder.InvokeEncodeInitialLine(buffer, new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                    "http://localhost:9999?p1=v1"));
            string req = buffer.ToString(Encoding.ASCII);
            Assert.Equal("GET http://localhost:9999/?p1=v1 HTTP/1.1\r\n", req);
            buffer.Release();
        }
    }

    [Fact]
    public void TestUriWithEmptyPath()
    {
        foreach (ByteBuf buffer in GetBuffers())
        {
            HttpRequestEncoder encoder = new HttpRequestEncoder();
            encoder.InvokeEncodeInitialLine(buffer, new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.GET,
                    "http://localhost:9999/?p1=v1"));
            string req = buffer.ToString(Encoding.ASCII);
            Assert.Equal("GET http://localhost:9999/?p1=v1 HTTP/1.1\r\n", req);
            buffer.Release();
        }
    }

    [Fact]
    public void TestUriWithPath()
    {
        foreach (ByteBuf buffer in GetBuffers())
        {
            HttpRequestEncoder encoder = new HttpRequestEncoder();
            encoder.InvokeEncodeInitialLine(buffer, new DefaultHttpRequest(HttpVersion.HTTP_1_1,
                    HttpMethod.GET, "http://localhost/"));
            string req = buffer.ToString(Encoding.ASCII);
            Assert.Equal("GET http://localhost/ HTTP/1.1\r\n", req);
            buffer.Release();
        }
    }

    [Fact]
    public void TestAbsPath()
    {
        foreach (ByteBuf buffer in GetBuffers())
        {
            HttpRequestEncoder encoder = new HttpRequestEncoder();
            encoder.InvokeEncodeInitialLine(buffer, new DefaultHttpRequest(HttpVersion.HTTP_1_1,
                    HttpMethod.GET, "/"));
            string req = buffer.ToString(Encoding.ASCII);
            Assert.Equal("GET / HTTP/1.1\r\n", req);
            buffer.Release();
        }
    }

    [Fact]
    public void TestEmptyAbsPath()
    {
        foreach (ByteBuf buffer in GetBuffers())
        {
            HttpRequestEncoder encoder = new HttpRequestEncoder();
            encoder.InvokeEncodeInitialLine(buffer, new DefaultHttpRequest(HttpVersion.HTTP_1_1,
                    HttpMethod.GET, ""));
            string req = buffer.ToString(Encoding.ASCII);
            Assert.Equal("GET / HTTP/1.1\r\n", req);
            buffer.Release();
        }
    }

    [Fact]
    public void TestQueryStringPath()
    {
        foreach (ByteBuf buffer in GetBuffers())
        {
            HttpRequestEncoder encoder = new HttpRequestEncoder();
            encoder.InvokeEncodeInitialLine(buffer, new DefaultHttpRequest(HttpVersion.HTTP_1_1,
                    HttpMethod.GET, "/?url=http://example.com"));
            string req = buffer.ToString(Encoding.ASCII);
            Assert.Equal("GET /?url=http://example.com HTTP/1.1\r\n", req);
            buffer.Release();
        }
    }

    [Fact]
    public void TestEmptyReleasedBufferShouldNotWriteEmptyBufferToChannel()
    {
        HttpRequestEncoder encoder = new HttpRequestEncoder();
        EmbeddedChannel channel = new EmbeddedChannel(encoder);
        ByteBuf buf = Unpooled.Buffer();
        buf.Release();
        EncoderException e = Assert.Throws<EncoderException>(
                () => channel.WriteAndFlushAsync(buf).GetAwaiter().GetResult());
        Assert.IsAssignableFrom<IllegalReferenceCountException>(e.InnerException);

        channel.FinishAndReleaseAll();
    }

    [Fact]
    public void TestEmptyBufferShouldPassThrough()
    {
        HttpRequestEncoder encoder = new HttpRequestEncoder();
        EmbeddedChannel channel = new EmbeddedChannel(encoder);
        ByteBuf buffer = Unpooled.Buffer();
        channel.WriteAndFlushAsync(buffer).GetAwaiter().GetResult();
        channel.FinishAndReleaseAll();
        Assert.Equal(0, buffer.RefCnt);
    }

    [Fact]
    public void TestEmptyContentsChunked()
    {
        TestEmptyContents(true, false);
    }

    [Fact]
    public void TestEmptyContentsChunkedWithTrailers()
    {
        TestEmptyContents(true, true);
    }

    [Fact]
    public void TestEmptyContentsNotChunked()
    {
        TestEmptyContents(false, false);
    }

    [Fact]
    public void TestEmptyContentNotsChunkedWithTrailers()
    {
        TestEmptyContents(false, true);
    }

    // this is not using Full types on purpose!!!
    private class CustomFullHttpRequest : DefaultHttpRequest, ILastHttpContent
    {
        private readonly ByteBuf content;
        private readonly HttpHeaders trailingHeader;

        internal CustomFullHttpRequest(HttpVersion httpVersion, HttpMethod method, string uri, ByteBuf content)
            : this(httpVersion, method, uri, content, true)
        {
        }

        internal CustomFullHttpRequest(HttpVersion httpVersion, HttpMethod method, string uri,
                                     ByteBuf content, bool validateHeaders)
            : base(httpVersion, method, uri, DefaultHttpHeadersFactory.HeadersFactory().WithValidation(validateHeaders))
        {
            this.content = CheckNotNull(content, "content");
            trailingHeader = DefaultHttpHeadersFactory.HeadersFactory().WithValidation(validateHeaders).NewHeaders();
        }

        private CustomFullHttpRequest(HttpVersion httpVersion, HttpMethod method, string uri,
                                     ByteBuf content, HttpHeaders headers, HttpHeaders trailingHeader)
            : base(httpVersion, method, uri, headers)
        {
            this.content = CheckNotNull(content, "content");
            this.trailingHeader = CheckNotNull(trailingHeader, "trailingHeader");
        }

        public HttpHeaders TrailingHeaders()
        {
            return trailingHeader;
        }

        public ByteBuf Content => content;

        public int RefCnt => content.RefCnt;

        public CustomFullHttpRequest Retain()
        {
            content.Retain();
            return this;
        }

        public CustomFullHttpRequest Retain(int increment)
        {
            content.Retain(increment);
            return this;
        }

        public CustomFullHttpRequest Touch()
        {
            content.Touch();
            return this;
        }

        public CustomFullHttpRequest Touch(object hint)
        {
            content.Touch(hint);
            return this;
        }

        public bool Release()
        {
            return content.Release();
        }

        public bool Release(int decrement)
        {
            return content.Release(decrement);
        }

        public override CustomFullHttpRequest SetProtocolVersion(HttpVersion version)
        {
            base.SetProtocolVersion(version);
            return this;
        }

        public override CustomFullHttpRequest SetMethod(HttpMethod method)
        {
            base.SetMethod(method);
            return this;
        }

        public override CustomFullHttpRequest SetUri(string uri)
        {
            base.SetUri(uri);
            return this;
        }

        public CustomFullHttpRequest Copy()
        {
            return Replace(Content.Copy());
        }

        public CustomFullHttpRequest Duplicate()
        {
            return Replace(Content.Duplicate());
        }

        public CustomFullHttpRequest RetainedDuplicate()
        {
            return Replace(Content.RetainedDuplicate());
        }

        public CustomFullHttpRequest Replace(ByteBuf content)
        {
            CustomFullHttpRequest request = new CustomFullHttpRequest(ProtocolVersion(), Method(), Uri(), content,
                    Headers().Copy(), TrailingHeaders().Copy());
            request.SetDecoderResult(DecoderResult());
            return request;
        }

        IByteBufHolder IByteBufHolder.Copy() => Copy();
        IByteBufHolder IByteBufHolder.Duplicate() => Duplicate();
        IByteBufHolder IByteBufHolder.RetainedDuplicate() => RetainedDuplicate();
        IByteBufHolder IByteBufHolder.Replace(ByteBuf content) => Replace(content);
        IByteBufHolder IByteBufHolder.Retain() => Retain();
        IByteBufHolder IByteBufHolder.Retain(int increment) => Retain(increment);
        IByteBufHolder IByteBufHolder.Touch() => Touch();
        IByteBufHolder IByteBufHolder.Touch(object hint) => Touch(hint);
        IReferenceCounted IReferenceCounted.Retain() => Retain();
        IReferenceCounted IReferenceCounted.Retain(int increment) => Retain(increment);
        IReferenceCounted IReferenceCounted.Touch() => Touch();
        IReferenceCounted IReferenceCounted.Touch(object hint) => Touch(hint);
    }

    [Fact]
    public void TestCustomMessageEmptyLastContent()
    {
        HttpRequestEncoder encoder = new HttpRequestEncoder();
        EmbeddedChannel channel = new EmbeddedChannel(encoder);
        IHttpRequest customMsg = new CustomFullHttpRequest(HttpVersion.HTTP_1_1,
                HttpMethod.POST, "/", Unpooled.EmptyBuffer);
        Assert.True(channel.WriteOutbound(customMsg));
        // Ensure we only produce ByteBuf instances.
        ByteBuf head = channel.ReadOutbound<ByteBuf>();
        Assert.True(head.Release());
        Assert.Null(channel.ReadOutbound());
        Assert.False(channel.Finish());
    }

    private void TestEmptyContents(bool chunked, bool trailers)
    {
        HttpRequestEncoder encoder = new HttpRequestEncoder();
        EmbeddedChannel channel = new EmbeddedChannel(encoder);
        IHttpRequest request = new DefaultHttpRequest(HttpVersion.HTTP_1_1, HttpMethod.POST, "/");
        if (chunked)
        {
            HttpUtil.SetTransferEncodingChunked(request, true);
        }
        Assert.True(channel.WriteOutbound(request));

        ByteBuf contentBuffer = Unpooled.Buffer();
        Assert.True(channel.WriteOutbound(new DefaultHttpContent(contentBuffer)));

        ByteBuf lastContentBuffer = Unpooled.Buffer();
        ILastHttpContent last = new DefaultLastHttpContent(lastContentBuffer);
        if (trailers)
        {
            last.TrailingHeaders().Set("X-Netty-Test", "true");
        }
        Assert.True(channel.WriteOutbound(last));

        // Ensure we only produce ByteBuf instances.
        ByteBuf head = channel.ReadOutbound<ByteBuf>();
        Assert.True(head.Release());

        ByteBuf content = channel.ReadOutbound<ByteBuf>();
        content.Release();

        ByteBuf lastContent = channel.ReadOutbound<ByteBuf>();
        lastContent.Release();
        Assert.False(channel.Finish());
    }

    /**
     * A test that checks for a NPE that would occur if when processing {@link LastHttpContent#EMPTY_LAST_CONTENT}
     * when a certain initialization order of {@link EmptyHttpHeaders} would occur.
     */
    [Fact]
    public void TestForChunkedRequestNpe()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpRequestEncoder());
        Assert.True(channel.WriteOutbound(new CustomHttpRequest()));
        Assert.True(channel.WriteOutbound(new DefaultHttpContent(Unpooled.CopiedBuffer("test", Encoding.ASCII))));
        Assert.True(channel.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT));
        Assert.True(channel.FinishAndReleaseAll());
    }

    /**
     * This class is required to triggered the desired initialization order of {@link EmptyHttpHeaders}.
     * If {@link DefaultHttpRequest} is used, the {@link HttpHeaders} class will be initialized before {@link HttpUtil}
     * and the test won't trigger the original issue.
     */
    private sealed class CustomHttpRequest : IHttpRequest
    {

        public DecoderResult DecoderResult()
        {
            return global::Netty.NET.Handler.Codec.DecoderResult.SUCCESS;
        }

        public void SetDecoderResult(DecoderResult result)
        {
        }

        public HttpVersion ProtocolVersion()
        {
            return HttpVersion.HTTP_1_1;
        }

        public HttpHeaders Headers()
        {
            DefaultHttpHeaders headers = new DefaultHttpHeaders();
            headers.Add("Transfer-Encoding", "chunked");
            return headers;
        }

        public HttpMethod Method()
        {
            return HttpMethod.POST;
        }

        public IHttpRequest SetMethod(HttpMethod method)
        {
            return this;
        }

        public string Uri()
        {
            return "/";
        }

        public IHttpRequest SetUri(string uri)
        {
            return this;
        }

        public IHttpMessage SetProtocolVersion(HttpVersion version)
        {
            return this;
        }
    }
}
