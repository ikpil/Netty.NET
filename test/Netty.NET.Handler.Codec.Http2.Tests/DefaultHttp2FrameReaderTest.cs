/*
 * Copyright 2017 The Netty Project
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
using Moq;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2FrameTypes;

namespace Netty.NET.Handler.Codec.Http2.Tests;

/**
 * Tests for {@link DefaultHttp2FrameReader}.
 */
// CLR: Mockito mocks are Moq loose mocks and verify(mock).m(args) is Verify(..., Times.Once()) with the argument
// values compared by Equals, as Mockito's default argument matching does (Moq Quickstart,
// https://github.com/devlooped/moq/wiki/Quickstart; docs/codec-http-porting.md, Shared CLR adaptations);
// @BeforeEach is the constructor and @AfterEach Dispose(). ctx.alloc() is the Alloc() method.
public class DefaultHttp2FrameReaderTest : IDisposable
{
    private readonly Mock<IHttp2FrameListener> listener = new Mock<IHttp2FrameListener>();

    private readonly Mock<IChannelHandlerContext> ctx = new Mock<IChannelHandlerContext>();

    private DefaultHttp2FrameReader frameReader;

    // Used to generate frame
    private HpackEncoder hpackEncoder;

    public DefaultHttp2FrameReaderTest()
    {
        ctx.Setup(c => c.Alloc()).Returns(UnpooledByteBufAllocator.Default);

        frameReader = new DefaultHttp2FrameReader();
        hpackEncoder = new HpackEncoder();
    }

    public void Dispose()
    {
        frameReader.Close();
    }

    private static StringCharSequence Of(string s)
    {
        return new StringCharSequence(s);
    }

    [Fact]
    public void ReadHeaderFrame()
    {
        int streamId = 1;

        ByteBuf input = Unpooled.Buffer();

        try
        {
            IHttp2Headers headers = new DefaultHttp2Headers()
                    .Authority(Of("foo"))
                    .Method(Of("get"))
                    .Path(Of("/"))
                    .Scheme(Of("https"));
            Http2Flags flags = new Http2Flags().EndOfHeaders(true).EndOfStream(true);
            WriteHeaderFrame(input, streamId, headers, flags);
            frameReader.ReadFrame(ctx.Object, input, listener.Object);

            listener.Verify(l => l.OnHeadersRead(ctx.Object, 1, headers, 0, true), Times.Once());
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void ReadHeaderFrameAndContinuationFrame()
    {
        int streamId = 1;

        ByteBuf input = Unpooled.Buffer();

        try
        {
            IHttp2Headers headers = new DefaultHttp2Headers()
                    .Authority(Of("foo"))
                    .Method(Of("get"))
                    .Path(Of("/"))
                    .Scheme(Of("https"));
            WriteHeaderFrame(input, streamId, headers,
                    new Http2Flags().EndOfHeaders(false).EndOfStream(true));
            WriteContinuationFrame(input, streamId, new DefaultHttp2Headers().Add(Of("foo"), Of("bar")),
                    new Http2Flags().EndOfHeaders(true));

            frameReader.ReadFrame(ctx.Object, input, listener.Object);

            IHttp2Headers expected = headers.Add(Of("foo"), Of("bar"));
            listener.Verify(l => l.OnHeadersRead(ctx.Object, 1, expected, 0, true), Times.Once());
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void ReadHeaderFrameAndContinuationFrameExceedMax()
    {
        frameReader = new DefaultHttp2FrameReader(new DefaultHttp2HeadersDecoder(true), 2);
        int streamId = 1;

        ByteBuf input = Unpooled.Buffer();

        try
        {
            IHttp2Headers headers = new DefaultHttp2Headers()
                    .Authority(Of("foo"))
                    .Method(Of("get"))
                    .Path(Of("/"))
                    .Scheme(Of("https"));
            WriteHeaderFrame(input, streamId, headers,
                    new Http2Flags().EndOfHeaders(false).EndOfStream(true));
            WriteContinuationFrame(input, streamId, new DefaultHttp2Headers().Add(Of("foo"), Of("bar")),
                    new Http2Flags().EndOfHeaders(false));
            WriteContinuationFrame(input, streamId, new DefaultHttp2Headers().Add(Of("foo2"), Of("bar2")),
                    new Http2Flags().EndOfHeaders(false));

            Http2Exception ex = Assert.ThrowsAny<Http2Exception>(
                    () => frameReader.ReadFrame(ctx.Object, input, listener.Object));
            Assert.Equal(Http2Error.ENHANCE_YOUR_CALM, ex.Error());
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void ReadHeaderFrameAndContinuationFrameDontExceedMax()
    {
        frameReader = new DefaultHttp2FrameReader(new DefaultHttp2HeadersDecoder(true), 2);
        int streamId = 1;

        ByteBuf input = Unpooled.Buffer();

        try
        {
            IHttp2Headers headers = new DefaultHttp2Headers()
                    .Authority(Of("foo"))
                    .Method(Of("get"))
                    .Path(Of("/"))
                    .Scheme(Of("https"));
            WriteHeaderFrame(input, streamId, headers,
                    new Http2Flags().EndOfHeaders(false).EndOfStream(true));
            WriteContinuationFrame(input, streamId, new DefaultHttp2Headers().Add(Of("foo"), Of("bar")),
                    new Http2Flags().EndOfHeaders(false));
            frameReader.ReadFrame(ctx.Object, input, listener.Object);
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void ReadUnknownFrame()
    {
        ByteBuf input = Unpooled.Buffer();
        ByteBuf payload = Unpooled.Buffer();

        try
        {
            payload.WriteByte(1);

            WriteFrameHeader(input, payload.ReadableBytes, (byte) 0xff, new Http2Flags(), 0);
            input.WriteBytes(payload);
            frameReader.ReadFrame(ctx.Object, input, listener.Object);

            listener.Verify(l => l.OnUnknownFrame(
                    ctx.Object, (byte) 0xff, 0, new Http2Flags(), payload.Slice(0, 1)), Times.Once());
        }
        finally
        {
            payload.Release();
            input.Release();
        }
    }

    [Fact]
    public void FailedWhenUnknownFrameInMiddleOfHeaderBlock()
    {
        int streamId = 1;

        ByteBuf input = Unpooled.Buffer();

        try
        {
            IHttp2Headers headers = new DefaultHttp2Headers()
                    .Authority(Of("foo"))
                    .Method(Of("get"))
                    .Path(Of("/"))
                    .Scheme(Of("https"));
            Http2Flags flags = new Http2Flags().EndOfHeaders(false).EndOfStream(true);
            WriteHeaderFrame(input, streamId, headers, flags);
            WriteFrameHeader(input, 0, (byte) 0xff, new Http2Flags(), streamId);

            Http2Exception ex = Assert.ThrowsAny<Http2Exception>(
                    () => frameReader.ReadFrame(ctx.Object, input, listener.Object));
            Assert.False(ex is Http2Exception.StreamException);
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void FailedWhenContinuationFrameStreamIdMismatch()
    {
        ByteBuf input = Unpooled.Buffer();

        try
        {
            IHttp2Headers headers = new DefaultHttp2Headers()
                    .Authority(Of("foo"))
                    .Method(Of("get"))
                    .Path(Of("/"))
                    .Scheme(Of("https"));
            WriteHeaderFrame(input, 1, headers,
                             new Http2Flags().EndOfHeaders(false).EndOfStream(true));
            WriteContinuationFrame(input, 3, new DefaultHttp2Headers().Add(Of("foo"), Of("bar")),
                    new Http2Flags().EndOfHeaders(true));

            Http2Exception ex = Assert.ThrowsAny<Http2Exception>(
                    () => frameReader.ReadFrame(ctx.Object, input, listener.Object));
            Assert.False(ex is Http2Exception.StreamException);
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void FailedWhenContinuationFrameNotFollowHeaderFrame()
    {
        ByteBuf input = Unpooled.Buffer();

        try
        {
            WriteContinuationFrame(input, 1, new DefaultHttp2Headers().Add(Of("foo"), Of("bar")),
                                   new Http2Flags().EndOfHeaders(true));
            Http2Exception ex = Assert.ThrowsAny<Http2Exception>(
                    () => frameReader.ReadFrame(ctx.Object, input, listener.Object));
            Assert.False(ex is Http2Exception.StreamException);
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void FailedWhenHeaderFrameDependsOnItself()
    {
        ByteBuf input = Unpooled.Buffer();

        try
        {
            IHttp2Headers headers = new DefaultHttp2Headers()
                    .Authority(Of("foo"))
                    .Method(Of("get"))
                    .Path(Of("/"))
                    .Scheme(Of("https"));
            WriteHeaderFramePriorityPresent(
                    input, 1, headers,
                    new Http2Flags().EndOfHeaders(true).EndOfStream(true).PriorityPresent(true),
                    1, 10);
            Http2Exception ex = Assert.ThrowsAny<Http2Exception>(
                    () => frameReader.ReadFrame(ctx.Object, input, listener.Object));
            Assert.False(ex is Http2Exception.StreamException);
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void FailedHeadersValidationThrowsConnectionError()
    {
        ByteBuf input = Unpooled.Buffer();

        try
        {
            // Because we have padding we need at least 1 byte in the payload to specify the padding length.
            WriteFrameHeader(input, 0, HEADERS, new Http2Flags().PaddingPresent(true), 1);
            Http2Exception ex = Assert.ThrowsAny<Http2Exception>(
                    () => frameReader.ReadFrame(ctx.Object, input, listener.Object));
            Assert.False(ex is Http2Exception.StreamException);
            Assert.Equal(Http2Error.FRAME_SIZE_ERROR, ex.Error());
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void ReadHeaderAndData()
    {
        ByteBuf input = Unpooled.Buffer();
        ByteBuf dataPayload = Unpooled.Buffer();

        try
        {
            IHttp2Headers headers = new DefaultHttp2Headers()
                    .Authority(Of("foo"))
                    .Method(Of("get"))
                    .Path(Of("/"))
                    .Scheme(Of("https"));
            dataPayload.WriteByte(1);
            WriteHeaderFrameWithData(input, 1, headers, dataPayload);

            frameReader.ReadFrame(ctx.Object, input, listener.Object);

            listener.Verify(l => l.OnHeadersRead(ctx.Object, 1, headers, 0, false), Times.Once());
            listener.Verify(l => l.OnDataRead(ctx.Object, 1, dataPayload.Slice(0, 1), 0, true), Times.Once());
        }
        finally
        {
            input.Release();
            dataPayload.Release();
        }
    }

    [Fact]
    public void FailedWhenDataFrameNotAssociateWithStream()
    {
        ByteBuf input = Unpooled.Buffer();
        ByteBuf payload = Unpooled.Buffer();

        try
        {
            payload.WriteByte(1);

            WriteFrameHeader(input, payload.ReadableBytes, DATA, new Http2Flags().EndOfStream(true), 0);
            input.WriteBytes(payload);
            Assert.ThrowsAny<Http2Exception>(() => frameReader.ReadFrame(ctx.Object, input, listener.Object));
        }
        finally
        {
            payload.Release();
            input.Release();
        }
    }

    [Fact]
    public void FailedWhenPushPromiseFrameNotAssociateWithStream()
    {
        ByteBuf input = Unpooled.Buffer();

        try
        {
            WriteFrameHeader(input, INT_FIELD_LENGTH, PUSH_PROMISE, new Http2Flags().EndOfHeaders(true), 0);
            input.WriteInt(2);
            Http2Exception ex = Assert.ThrowsAny<Http2Exception>(
                    () => frameReader.ReadFrame(ctx.Object, input, listener.Object));
            Assert.False(ex is Http2Exception.StreamException);
            Assert.Equal(Http2Error.PROTOCOL_ERROR, ex.Error());
            listener.Verify(l => l.OnPushPromiseRead(It.IsAny<IChannelHandlerContext>(), It.IsAny<int>(),
                    It.IsAny<int>(), It.IsAny<IHttp2Headers>(), It.IsAny<int>()), Times.Never());
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void ReadPriorityFrame()
    {
        ByteBuf input = Unpooled.Buffer();

        try
        {
            WritePriorityFrame(input, 1, 0, 10);
            frameReader.ReadFrame(ctx.Object, input, listener.Object);
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void FailedWhenPriorityFrameDependsOnItself()
    {
        ByteBuf input = Unpooled.Buffer();

        try
        {
            WritePriorityFrame(input, 1, 1, 10);
            Assert.ThrowsAny<Http2Exception>(() => frameReader.ReadFrame(ctx.Object, input, listener.Object));
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void FailedWhenConnectionWindowUpdateFrameWithZeroDelta()
    {
        ByteBuf input = Unpooled.Buffer();

        try
        {
            WriteFrameHeader(input, 4, WINDOW_UPDATE, new Http2Flags(), 0);
            input.WriteInt(0);
            Http2Exception ex = Assert.ThrowsAny<Http2Exception>(
                    () => frameReader.ReadFrame(ctx.Object, input, listener.Object));
            Assert.False(ex is Http2Exception.StreamException);
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void FailedWhenStreamWindowUpdateFrameWithZeroDelta()
    {
        ByteBuf input = Unpooled.Buffer();

        try
        {
            WriteFrameHeader(input, 4, WINDOW_UPDATE, new Http2Flags(), 1);
            input.WriteInt(0);
            Http2Exception ex = Assert.ThrowsAny<Http2Exception>(
                    () => frameReader.ReadFrame(ctx.Object, input, listener.Object));
            Assert.IsAssignableFrom<Http2Exception.StreamException>(ex);
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void ReadSettingsFrame()
    {
        ByteBuf input = Unpooled.Buffer();

        try
        {
            WriteFrameHeader(input, 6, SETTINGS, new Http2Flags(), 0);
            input.WriteShort(SETTINGS_MAX_HEADER_LIST_SIZE);
            input.WriteInt(1024);
            frameReader.ReadFrame(ctx.Object, input, listener.Object);

            listener.Object.OnSettingsRead(ctx.Object, new Http2Settings().MaxHeaderListSize(1024));
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void ReadAckSettingsFrame()
    {
        ByteBuf input = Unpooled.Buffer();

        try
        {
            WriteFrameHeader(input, 0, SETTINGS, new Http2Flags().Ack(true), 0);
            frameReader.ReadFrame(ctx.Object, input, listener.Object);

            listener.Object.OnSettingsAckRead(ctx.Object);
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void FailedWhenSettingsFrameOnNonZeroStream()
    {
        ByteBuf input = Unpooled.Buffer();

        try
        {
            WriteFrameHeader(input, 6, SETTINGS, new Http2Flags(), 1);
            input.WriteShort(SETTINGS_MAX_HEADER_LIST_SIZE);
            input.WriteInt(1024);
            Http2Exception ex = Assert.ThrowsAny<Http2Exception>(
                    () => frameReader.ReadFrame(ctx.Object, input, listener.Object));
            Assert.False(ex is Http2Exception.StreamException);
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void FailedWhenAckSettingsFrameWithPayload()
    {
        ByteBuf input = Unpooled.Buffer();

        try
        {
            WriteFrameHeader(input, 1, SETTINGS, new Http2Flags().Ack(true), 0);
            input.WriteByte(1);
            Http2Exception ex = Assert.ThrowsAny<Http2Exception>(
                    () => frameReader.ReadFrame(ctx.Object, input, listener.Object));
            Assert.False(ex is Http2Exception.StreamException);
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void FailedWhenSettingsFrameWithWrongPayloadLength()
    {
        ByteBuf input = Unpooled.Buffer();

        try
        {
            WriteFrameHeader(input, 8, SETTINGS, new Http2Flags(), 0);
            input.WriteInt(SETTINGS_MAX_HEADER_LIST_SIZE);
            input.WriteInt(1024);
            Http2Exception ex = Assert.ThrowsAny<Http2Exception>(
                    () => frameReader.ReadFrame(ctx.Object, input, listener.Object));
            Assert.False(ex is Http2Exception.StreamException);
        }
        finally
        {
            input.Release();
        }
    }

    [Fact]
    public void VerifyValidRequestAfterMalformedPacketCausesStreamException()
    {
        ByteBuf input = Unpooled.Buffer();
        int priorityStreamId = 3, headerStreamId = 5;

        try
        {
            // Write a malformed priority header causing a stream exception in reader
            WriteFrameHeader(input, 4, PRIORITY, new Http2Flags(), priorityStreamId);
            // Fill buffer with dummy payload to be properly read by reader
            input.WriteByte((byte) 0x80);
            input.WriteByte((byte) 0x00);
            input.WriteByte((byte) 0x00);
            input.WriteByte((byte) 0x7f);
            Assert.ThrowsAny<Http2Exception>(() =>
            {
                try
                {
                    frameReader.ReadFrame(ctx.Object, input, listener.Object);
                }
                catch (Exception e)
                {
                    if (e is Http2Exception && Http2Exception.IsStreamError((Http2Exception) e))
                    {
                        throw;
                    }
                }
            });
            // Verify that after stream exception we accept new stream requests
            IHttp2Headers headers = new DefaultHttp2Headers()
                    .Authority(Of("foo"))
                    .Method(Of("get"))
                    .Path(Of("/"))
                    .Scheme(Of("https"));
            Http2Flags flags = new Http2Flags().EndOfHeaders(true).EndOfStream(true);
            WriteHeaderFrame(input, headerStreamId, headers, flags);
            frameReader.ReadFrame(ctx.Object, input, listener.Object);
            listener.Verify(l => l.OnHeadersRead(ctx.Object, 5, headers, 0, true), Times.Once());
        }
        finally
        {
            input.Release();
        }
    }

    private void WriteHeaderFrame(
            ByteBuf output, int streamId, IHttp2Headers headers,
            Http2Flags flags)
    {
        ByteBuf headerBlock = Unpooled.Buffer();

        try
        {
            hpackEncoder.EncodeHeaders(streamId, headerBlock, headers, IHttp2HeadersEncoder.NEVER_SENSITIVE);
            WriteFrameHeader(output, headerBlock.ReadableBytes, HEADERS, flags, streamId);
            output.WriteBytes(headerBlock, headerBlock.ReadableBytes);
        }
        finally
        {
            headerBlock.Release();
        }
    }

    private void WriteHeaderFrameWithData(
            ByteBuf output, int streamId, IHttp2Headers headers,
            ByteBuf dataPayload)
    {
        ByteBuf headerBlock = Unpooled.Buffer();

        try
        {
            hpackEncoder.EncodeHeaders(streamId, headerBlock, headers, IHttp2HeadersEncoder.NEVER_SENSITIVE);
            WriteFrameHeader(output, headerBlock.ReadableBytes, HEADERS,
                    new Http2Flags().EndOfHeaders(true), streamId);
            output.WriteBytes(headerBlock, headerBlock.ReadableBytes);

            WriteFrameHeader(output, dataPayload.ReadableBytes, DATA, new Http2Flags().EndOfStream(true), streamId);
            output.WriteBytes(dataPayload);
        }
        finally
        {
            headerBlock.Release();
        }
    }

    private void WriteHeaderFramePriorityPresent(
            ByteBuf output, int streamId, IHttp2Headers headers,
            Http2Flags flags, int streamDependency, int weight)
    {
        ByteBuf headerBlock = Unpooled.Buffer();

        try
        {
            headerBlock.WriteInt(streamDependency);
            headerBlock.WriteByte(weight - 1);
            hpackEncoder.EncodeHeaders(streamId, headerBlock, headers, IHttp2HeadersEncoder.NEVER_SENSITIVE);
            WriteFrameHeader(output, headerBlock.ReadableBytes, HEADERS, flags, streamId);
            output.WriteBytes(headerBlock, headerBlock.ReadableBytes);
        }
        finally
        {
            headerBlock.Release();
        }
    }

    private void WriteContinuationFrame(
            ByteBuf output, int streamId, IHttp2Headers headers,
            Http2Flags flags)
    {
        ByteBuf headerBlock = Unpooled.Buffer();

        try
        {
            hpackEncoder.EncodeHeaders(streamId, headerBlock, headers, IHttp2HeadersEncoder.NEVER_SENSITIVE);
            WriteFrameHeader(output, headerBlock.ReadableBytes, CONTINUATION, flags, streamId);
            output.WriteBytes(headerBlock, headerBlock.ReadableBytes);
        }
        finally
        {
            headerBlock.Release();
        }
    }

    private static void WritePriorityFrame(
            ByteBuf output, int streamId, int streamDependency, int weight)
    {
        WriteFrameHeader(output, 5, PRIORITY, new Http2Flags(), streamId);
        output.WriteInt(streamDependency);
        output.WriteByte(weight - 1);
    }
}
