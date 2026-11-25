/*
* Copyright 2014 The Netty Project
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
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http.Tests;

#pragma warning disable xUnit1031 // Future.get() of the source: the EmbeddedChannel write has completed when WriteAndFlushAsync returns.
#pragma warning disable xUnit1051 // The source's writeAndFlush(...) observes no test cancellation.

// CLR: FileRegion is the Transport IFileRegion: transferTo(WritableByteChannel, long) is TransferTo(Stream, long) and
// the misspelled deprecated transfered() is folded into Transferred() (src/Netty.NET.Transport/Channels/IFileRegion.cs),
// so DummyLongFileRegion has no transfered(); inside Netty.NET.Handler.* the simple name Stream binds to the handler's
// Stream namespace, so the BCL type is written System.IO.Stream (docs/handler-porting.md, Name lookup). Future.get() wraps the write failure in an ExecutionException;
// Task.GetAwaiter().GetResult() throws the faulted task's exception itself ("The task completed in a Faulted state",
// https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.taskawaiter.getresult?view=net-10.0),
// so e.getCause() is the thrown EncoderException and e.getCause().getCause() its InnerException.
// java.lang.OutOfMemoryError is System.OutOfMemoryException, an ordinary catchable Exception thrown by the test, which
// HttpObjectEncoder wraps in EncoderException as Java wraps the Throwable; UnsupportedOperationException is
// NotSupportedException (docs/codec-base-porting.md). channel.config().setAllocator(...) is Config.SetAllocator(...).
// AbstractByteBufAllocator/UnpooledByteBufAllocator are the Buffer ports (newHeapBuffer/newDirectBuffer are
// NewHeapBuffer/NewDirectBuffer, isDirectBufferPooled() the IsDirectBufferPooled property). ThrowingHeaders'
// anonymous Iterator is the named nested ThrowingIterator (the codec-base IIterator). The Java messages of
// assertFalse/assertEquals use XunitAssertMessages.AssertM. CharsetUtil.US_ASCII is Encoding.ASCII.
public class HttpResponseEncoderTest
{
    private const long INTEGER_OVERFLOW = (long) int.MaxValue + 1;
    private static readonly IFileRegion FILE_REGION = new DummyLongFileRegion();

    [Fact]
    public void TestLargeFileRegionChunked()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseEncoder());
        IHttpResponse response = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        response.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
        Assert.True(channel.WriteOutbound(response));

        ByteBuf buffer = channel.ReadOutbound<ByteBuf>();

        Assert.Equal("HTTP/1.1 200 OK\r\n" + HttpHeaderNames.TRANSFER_ENCODING + ": " +
                HttpHeaderValues.CHUNKED + "\r\n\r\n", buffer.ToString(Encoding.ASCII));
        buffer.Release();
        Assert.True(channel.WriteOutbound(FILE_REGION));
        buffer = channel.ReadOutbound<ByteBuf>();
        Assert.Equal("80000000\r\n", buffer.ToString(Encoding.ASCII));
        buffer.Release();

        IFileRegion region = channel.ReadOutbound<IFileRegion>();
        Assert.Same(FILE_REGION, region);
        region.Release();
        buffer = channel.ReadOutbound<ByteBuf>();
        Assert.Equal("\r\n", buffer.ToString(Encoding.ASCII));
        buffer.Release();

        Assert.True(channel.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT));
        buffer = channel.ReadOutbound<ByteBuf>();
        Assert.Equal("0\r\n\r\n", buffer.ToString(Encoding.ASCII));
        buffer.Release();

        Assert.False(channel.Finish());
    }

    private class DummyLongFileRegion : IFileRegion
    {

        public long Position()
        {
            return 0;
        }

        public long Transferred()
        {
            return 0;
        }

        public long Count()
        {
            return INTEGER_OVERFLOW;
        }

        public long TransferTo(System.IO.Stream target, long position)
        {
            throw new NotSupportedException();
        }

        public IFileRegion Touch(object hint)
        {
            return this;
        }

        public IFileRegion Touch()
        {
            return this;
        }

        public IFileRegion Retain()
        {
            return this;
        }

        public IFileRegion Retain(int increment)
        {
            return this;
        }

        public int RefCnt => 1;

        public bool Release()
        {
            return false;
        }

        public bool Release(int decrement)
        {
            return false;
        }

        IReferenceCounted IReferenceCounted.Retain() => Retain();
        IReferenceCounted IReferenceCounted.Retain(int increment) => Retain(increment);
        IReferenceCounted IReferenceCounted.Touch() => Touch();
        IReferenceCounted IReferenceCounted.Touch(object hint) => Touch(hint);
    }

    [Fact]
    public void TestEmptyBufferBypass()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseEncoder());

        // Test writing an empty buffer works when the encoder is at ST_INIT.
        channel.WriteOutbound(Unpooled.EmptyBuffer);
        ByteBuf buffer = channel.ReadOutbound<ByteBuf>();
        Assert.Same(Unpooled.EmptyBuffer, buffer);

        // Leave the ST_INIT state.
        IHttpResponse response = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        Assert.True(channel.WriteOutbound(response));
        buffer = channel.ReadOutbound<ByteBuf>();
        Assert.Equal("HTTP/1.1 200 OK\r\n\r\n", buffer.ToString(Encoding.ASCII));
        buffer.Release();

        // Test writing an empty buffer works when the encoder is not at ST_INIT.
        channel.WriteOutbound(Unpooled.EmptyBuffer);
        buffer = channel.ReadOutbound<ByteBuf>();
        Assert.Same(Unpooled.EmptyBuffer, buffer);

        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestEmptyContentChunked()
    {
        TestEmptyContent(true);
    }

    [Fact]
    public void TestEmptyContentNotChunked()
    {
        TestEmptyContent(false);
    }

    private static void TestEmptyContent(bool chunked)
    {
        string content = "netty rocks";
        ByteBuf contentBuffer = Unpooled.CopiedBuffer(content, Encoding.ASCII);
        int length = contentBuffer.ReadableBytes;

        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseEncoder());
        IHttpResponse response = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
        if (!chunked)
        {
            HttpUtil.SetContentLength(response, length);
        }
        Assert.True(channel.WriteOutbound(response));
        Assert.True(channel.WriteOutbound(new DefaultHttpContent(Unpooled.EmptyBuffer)));
        Assert.True(channel.WriteOutbound(new DefaultLastHttpContent(contentBuffer)));

        ByteBuf buffer = channel.ReadOutbound<ByteBuf>();
        if (!chunked)
        {
            Assert.Equal("HTTP/1.1 200 OK\r\ncontent-length: " + length + "\r\n\r\n",
                    buffer.ToString(Encoding.ASCII));
        }
        else
        {
            Assert.Equal("HTTP/1.1 200 OK\r\n\r\n", buffer.ToString(Encoding.ASCII));
        }
        buffer.Release();

        // Test writing an empty buffer works when the encoder is not at ST_INIT.
        buffer = channel.ReadOutbound<ByteBuf>();
        Assert.Equal(0, buffer.ReadableBytes);
        buffer.Release();

        buffer = channel.ReadOutbound<ByteBuf>();
        Assert.Equal(length, buffer.ReadableBytes);
        buffer.Release();

        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestStatusNoContent()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseEncoder());
        AssertEmptyResponse(channel, HttpResponseStatus.NO_CONTENT, null, false);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestStatusNoContentContentLength()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseEncoder());
        AssertEmptyResponse(channel, HttpResponseStatus.NO_CONTENT, HttpHeaderNames.CONTENT_LENGTH, true);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestStatusNoContentTransferEncoding()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseEncoder());
        AssertEmptyResponse(channel, HttpResponseStatus.NO_CONTENT, HttpHeaderNames.TRANSFER_ENCODING, true);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestStatusNotModified()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseEncoder());
        AssertEmptyResponse(channel, HttpResponseStatus.NOT_MODIFIED, null, false);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestStatusNotModifiedContentLength()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseEncoder());
        AssertEmptyResponse(channel, HttpResponseStatus.NOT_MODIFIED, HttpHeaderNames.CONTENT_LENGTH, false);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestStatusNotModifiedTransferEncoding()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseEncoder());
        AssertEmptyResponse(channel, HttpResponseStatus.NOT_MODIFIED, HttpHeaderNames.TRANSFER_ENCODING, false);
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestStatusInformational()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseEncoder());
        for (int code = 100; code < 200; code++)
        {
            HttpResponseStatus status = HttpResponseStatus.ValueOf(code);
            AssertEmptyResponse(channel, status, null, false);
        }
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestStatusInformationalContentLength()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseEncoder());
        for (int code = 100; code < 200; code++)
        {
            HttpResponseStatus status = HttpResponseStatus.ValueOf(code);
            AssertEmptyResponse(channel, status, HttpHeaderNames.CONTENT_LENGTH, code != 101);
        }
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestStatusInformationalTransferEncoding()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseEncoder());
        for (int code = 100; code < 200; code++)
        {
            HttpResponseStatus status = HttpResponseStatus.ValueOf(code);
            AssertEmptyResponse(channel, status, HttpHeaderNames.TRANSFER_ENCODING, code != 101);
        }
        Assert.False(channel.Finish());
    }

    private static void AssertEmptyResponse(EmbeddedChannel channel, HttpResponseStatus status,
                                            ICharSequence headerName, bool headerStripped)
    {
        IHttpResponse response = new DefaultHttpResponse(HttpVersion.HTTP_1_1, status);
        if (HttpHeaderNames.CONTENT_LENGTH.ContentEquals(headerName))
        {
            response.Headers().Set(headerName, "0");
        }
        else if (HttpHeaderNames.TRANSFER_ENCODING.ContentEquals(headerName))
        {
            response.Headers().Set(headerName, HttpHeaderValues.CHUNKED);
        }

        Assert.True(channel.WriteOutbound(response));
        Assert.True(channel.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT));

        ByteBuf buffer = channel.ReadOutbound<ByteBuf>();
        StringBuilder responseText = new StringBuilder();
        responseText.Append(HttpVersion.HTTP_1_1).Append(' ').Append(status.ToString()).Append("\r\n");
        if (!headerStripped && headerName != null)
        {
            responseText.Append(headerName).Append(": ");

            if (HttpHeaderNames.CONTENT_LENGTH.ContentEquals(headerName))
            {
                responseText.Append('0');
            }
            else
            {
                responseText.Append(HttpHeaderValues.CHUNKED);
            }
            responseText.Append("\r\n");
        }
        responseText.Append("\r\n");

        Assert.Equal(responseText.ToString(), buffer.ToString(Encoding.ASCII));

        buffer.Release();

        buffer = channel.ReadOutbound<ByteBuf>();
        buffer.Release();
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

    private void TestEmptyContents(bool chunked, bool trailers)
    {
        HttpResponseEncoder encoder = new HttpResponseEncoder();
        EmbeddedChannel channel = new EmbeddedChannel(encoder);
        IHttpResponse request = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK);
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

    [Fact]
    public void TestStatusResetContentTransferContentLength()
    {
        TestStatusResetContentTransferContentLength0(HttpHeaderNames.CONTENT_LENGTH, Unpooled.Buffer().WriteLong(8));
    }

    [Fact]
    public void TestStatusResetContentTransferEncoding()
    {
        TestStatusResetContentTransferContentLength0(HttpHeaderNames.TRANSFER_ENCODING, Unpooled.Buffer().WriteLong(8));
    }

    private static void TestStatusResetContentTransferContentLength0(ICharSequence headerName, ByteBuf content)
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseEncoder());

        IHttpResponse response = new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.RESET_CONTENT);
        if (HttpHeaderNames.CONTENT_LENGTH.ContentEqualsIgnoreCase(headerName))
        {
            response.Headers().Set(HttpHeaderNames.CONTENT_LENGTH, content.ReadableBytes);
        }
        else
        {
            response.Headers().Set(HttpHeaderNames.TRANSFER_ENCODING, HttpHeaderValues.CHUNKED);
        }

        Assert.True(channel.WriteOutbound(response));
        Assert.True(channel.WriteOutbound(new DefaultHttpContent(content)));
        Assert.True(channel.WriteOutbound(ILastHttpContent.EMPTY_LAST_CONTENT));

        StringBuilder responseText = new StringBuilder();
        responseText.Append(HttpVersion.HTTP_1_1).Append(' ')
                .Append(HttpResponseStatus.RESET_CONTENT).Append("\r\n");
        responseText.Append(HttpHeaderNames.CONTENT_LENGTH).Append(": 0\r\n");
        responseText.Append("\r\n");

        StringBuilder written = new StringBuilder();
        for (;;)
        {
            ByteBuf buffer = channel.ReadOutbound<ByteBuf>();
            if (buffer == null)
            {
                break;
            }
            written.Append(buffer.ToString(Encoding.ASCII));
            buffer.Release();
        }

        Assert.Equal(responseText.ToString(), written.ToString());
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestInitHttpMessageHeaderSanitizationFailureDoesNotCorruptState()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseEncoder());
        DefaultHttpResponse invalidResponse = new DefaultHttpResponse(
                HttpVersion.HTTP_1_1, HttpResponseStatus.NO_CONTENT, new ReadOnlyHttpHeaders(false));

        EncoderException e = Assert.Throws<EncoderException>(() => channel.WriteOutbound(invalidResponse));
        Assert.IsAssignableFrom<NotSupportedException>(e.InnerException);

        AssertEncoderStillUsable(channel);
        Assert.False(channel.Finish());
    }

    private static void AssertEncoderStillUsable(EmbeddedChannel channel)
    {
        Assert.True(channel.WriteOutbound(
                new DefaultFullHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK)));
        ByteBuf buffer = channel.ReadOutbound<ByteBuf>();
        Assert.Equal("HTTP/1.1 200 OK\r\n\r\n", buffer.ToString(Encoding.ASCII));
        buffer.Release();
    }

    [Fact]
    public void TestInitHttpMessageHeaderEncodingFailureReleasesBufferAndPreservesState()
    {
        TrackingFailingAllocator allocator = new TrackingFailingAllocator();
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseEncoder());
        channel.Config.SetAllocator(allocator);

        DefaultHttpResponse response =
                new DefaultHttpResponse(HttpVersion.HTTP_1_1, HttpResponseStatus.OK, new ThrowingHeaders());

        EncoderException e = Assert.Throws<EncoderException>(
                () => channel.WriteAndFlushAsync(response).GetAwaiter().GetResult());
        Assert.IsAssignableFrom<EncoderException>(e);
        Assert.IsAssignableFrom<OutOfMemoryException>(e.InnerException);

        AssertAllTrackedBuffersReleased(allocator);
        AssertEncoderStillUsable(channel);
        channel.FinishAndReleaseAll();
    }

    [Fact]
    public void TestFullHttpMessageHeaderEncodingFailureReleasesBuffer()
    {
        TrackingFailingAllocator allocator = new TrackingFailingAllocator();
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseEncoder());
        channel.Config.SetAllocator(allocator);

        DefaultFullHttpResponse response = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1,
                HttpResponseStatus.OK, Unpooled.EmptyBuffer, new ThrowingHeaders(), new DefaultHttpHeaders());

        EncoderException e = Assert.Throws<EncoderException>(
                () => channel.WriteAndFlushAsync(response).GetAwaiter().GetResult());
        Assert.IsAssignableFrom<EncoderException>(e);
        Assert.IsAssignableFrom<OutOfMemoryException>(e.InnerException);

        AssertAllTrackedBuffersReleased(allocator);
        channel.FinishAndReleaseAll();
    }

    [Fact]
    public void TestChunkedContentLengthAllocationFailureDoesNotDoubleReleaseHeaderBuffer()
    {
        TrackingFailingAllocator allocator = new TrackingFailingAllocator(3);
        EmbeddedChannel channel = new EmbeddedChannel(new HttpResponseEncoder());
        channel.Config.SetAllocator(allocator);

        DefaultFullHttpResponse response = new DefaultFullHttpResponse(HttpVersion.HTTP_1_1,
                HttpResponseStatus.OK, Unpooled.CopiedBuffer("1", Encoding.ASCII));
        HttpUtil.SetTransferEncodingChunked(response, true);

        EncoderException e = Assert.Throws<EncoderException>(() => channel.WriteOutbound(response));
        Assert.IsAssignableFrom<OutOfMemoryException>(e.InnerException);

        for (;;)
        {
            ByteBuf buf = channel.ReadOutbound<ByteBuf>();
            if (buf == null)
            {
                break;
            }
            buf.Release();
        }

        AssertAllTrackedBuffersReleased(allocator);
        channel.FinishAndReleaseAll();
    }

    private static void AssertAllTrackedBuffersReleased(TrackingFailingAllocator allocator)
    {
        Assert.False(allocator.allocated.Count == 0, "expected at least one buffer to be allocated");
        foreach (ByteBuf buf in allocator.allocated)
        {
            Assert.Equal(0, buf.RefCnt, "expected tracked buffer to be released: " + buf);
        }
    }

    /**
     * A {@link ByteBufAllocator} that throws an {@link OutOfMemoryError} when asked to allocate a buffer with a
     * given {@code initialCapacity}, and otherwise delegates to an unpooled allocator while tracking every
     * successfully allocated buffer for leak verification.
     */
    private sealed class TrackingFailingAllocator : AbstractByteBufAllocator
    {
        private const int NEVER_FAIL = -1;

        private readonly IByteBufAllocator @delegate = new UnpooledByteBufAllocator(false);
        internal readonly List<ByteBuf> allocated = new List<ByteBuf>();
        private readonly int failOnInitialCapacity;

        internal TrackingFailingAllocator() : this(NEVER_FAIL)
        {
        }

        internal TrackingFailingAllocator(int failOnInitialCapacity) : base(false)
        {
            this.failOnInitialCapacity = failOnInitialCapacity;
        }

        private void FailIfTargeted(int initialCapacity)
        {
            if (failOnInitialCapacity != NEVER_FAIL && initialCapacity == failOnInitialCapacity)
            {
                throw new OutOfMemoryException("simulated allocation failure for capacity " + initialCapacity);
            }
        }

        protected override ByteBuf NewHeapBuffer(int initialCapacity, int maxCapacity)
        {
            FailIfTargeted(initialCapacity);
            ByteBuf buf = @delegate.HeapBuffer(initialCapacity, maxCapacity);
            allocated.Add(buf);
            return buf;
        }

        protected override ByteBuf NewDirectBuffer(int initialCapacity, int maxCapacity)
        {
            FailIfTargeted(initialCapacity);
            ByteBuf buf = @delegate.DirectBuffer(initialCapacity, maxCapacity);
            allocated.Add(buf);
            return buf;
        }

        public override bool IsDirectBufferPooled => @delegate.IsDirectBufferPooled;
    }

    private sealed class ThrowingHeaders : DefaultHttpHeaders
    {
        public override IIterator<IEntry<ICharSequence, ICharSequence>> IteratorCharSequence()
        {
            return new ThrowingIterator();
        }

        private sealed class ThrowingIterator : IIterator<IEntry<ICharSequence, ICharSequence>>
        {
            public bool HasNext()
            {
                return true;
            }

            public IEntry<ICharSequence, ICharSequence> Next()
            {
                throw new OutOfMemoryException("simulated header encoding failure");
            }

            public void Remove()
            {
                throw new NotSupportedException();
            }

            public IEntry<ICharSequence, ICharSequence> Current => null;

            object IEnumerator.Current => null;

            public bool MoveNext()
            {
                Next();
                return true;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
            }
        }
    }
}
