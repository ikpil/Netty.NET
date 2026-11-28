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
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Quic;
using Assert = XunitAssertMessages.AssertM;
using static Netty.NET.Handler.Codec.Http3.Tests.Http3TestUtils;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: @AfterEach tearDown() is Dispose() (xUnit creates a test class instance per test and disposes it after the
// test). Hamcrest's assertThat([reason,] actual, is(expected)) is Assert.Equal(expected, actual[, reason]) (is(true)
// and is(false) are Assert.True/Assert.False with the reason), assertThat(e.getCause(), instanceOf(T.class)) is
// Assert.IsAssignableFrom<T>(e.InnerException), and assertThrows(T.class, ...) is Assert.ThrowsAny<T>. The QPACK types
// and members are internal (package-private in Java) and reached through InternalsVisibleTo; the static imports are
// qualified (Http3.SetQpackAttributes, Http3ErrorCode.QPACK_DECODER_STREAM_ERROR,
// Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY, QpackUtil.EncodePrefixedInteger), and
// QpackSensitivityDetector is the interface IQpackSensitivityDetector. Consumer<Http3Headers> is Action<IHttp3Headers>.
// parent.createStream(...).get() is the result of the completed Task of CreateStreamAsync (GetAwaiter().GetResult());
// alloc() is Alloc(); getByte(int) returns the unsigned CLR byte, so & 0xFF keeps the same value; Math.toIntExact is a
// checked conversion. A Java String passed as a CharSequence (new QpackHeaderField("foo", "bar")) is a
// StringCharSequence (Http3TestUtils.Cs). The throws clauses have no counterpart.
public class QpackDecoderHandlerTest : IDisposable
{
    private static readonly QpackHeaderField fooBar = new QpackHeaderField(Cs("foo"), Cs("bar"));
    private readonly QpackEncoderDynamicTable dynamicTable = new QpackEncoderDynamicTable();
    private EmbeddedQuicChannel parent;
    private QpackEncoder encoder;
    private EmbeddedQuicStreamChannel decoderStream;
    private EmbeddedQuicStreamChannel encoderStream;
    private int maxEntries;
    private QpackAttributes attributes;

    public void Dispose()
    {
        Assert.False(encoderStream.Finish());
        Assert.False(decoderStream.Finish());
    }

    [Fact]
    public void SectionAckNoIncrement()
    {
        Setup(128L);
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));

        Http3Exception e = Assert.ThrowsAny<Http3Exception>(() => SendAckForStreamId(decoderStream.StreamId()));
        Assert.IsAssignableFrom<QpackException>(e.InnerException);

        Http3TestUtils.VerifyClose(Http3ErrorCode.QPACK_DECODER_STREAM_ERROR, parent);
        FinishStreams();
    }

    [Fact]
    public void SectionAck()
    {
        Setup(128L);
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));
        VerifyRequiredInsertCount(1);
        SendInsertCountIncrement(1);
        VerifyKnownReceivedCount(1);

        // Refer now to dynamic table
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));
        SendAckForStreamId(decoderStream.StreamId());

        FinishStreams();

        VerifyRequiredInsertCount(1);
        VerifyKnownReceivedCount(1);
    }

    [Fact]
    public void SectionAckUnknownStream()
    {
        Setup(128);

        Http3Exception e = Assert.ThrowsAny<Http3Exception>(() => SendAckForStreamId(1));
        Assert.IsAssignableFrom<QpackException>(e.InnerException);

        Http3TestUtils.VerifyClose(Http3ErrorCode.QPACK_DECODER_STREAM_ERROR, parent);
        FinishStreams();
    }

    [Fact]
    public void SectionAckAlreadyAcked()
    {
        Setup(128);
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));
        SendInsertCountIncrement(1);
        // Refer now to dynamic table
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));
        SendAckForStreamId(decoderStream.StreamId());

        Http3Exception e = Assert.ThrowsAny<Http3Exception>(() => SendAckForStreamId(decoderStream.StreamId()));
        Assert.IsAssignableFrom<QpackException>(e.InnerException);

        Http3TestUtils.VerifyClose(Http3ErrorCode.QPACK_DECODER_STREAM_ERROR, parent);
        FinishStreams();

        VerifyRequiredInsertCount(1);
        VerifyKnownReceivedCount(1);
    }

    [Fact]
    public void SectionAckMultiPending()
    {
        Setup(128L);
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));
        SendInsertCountIncrement(1);
        // Refer now to dynamic table
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));

        SendAckForStreamId(decoderStream.StreamId());
        SendAckForStreamId(decoderStream.StreamId());

        FinishStreams();

        VerifyRequiredInsertCount(1);
        VerifyKnownReceivedCount(1);
    }

    [Fact]
    public void SectionAckMultiPostAck()
    {
        Setup(128L);
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));
        SendInsertCountIncrement(1);
        // Refer now to dynamic table
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));
        SendAckForStreamId(decoderStream.StreamId());

        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));
        SendAckForStreamId(decoderStream.StreamId());

        FinishStreams();

        VerifyRequiredInsertCount(1);
        VerifyKnownReceivedCount(1);
    }

    [Fact]
    public void SectionAckCancelledStream()
    {
        Setup(128L);
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));
        SendInsertCountIncrement(1);
        // Refer now to dynamic table
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));

        SendStreamCancellation(decoderStream.StreamId());

        Http3Exception e = Assert.ThrowsAny<Http3Exception>(() => SendAckForStreamId(decoderStream.StreamId()));
        Assert.IsAssignableFrom<QpackException>(e.InnerException);

        Http3TestUtils.VerifyClose(Http3ErrorCode.QPACK_DECODER_STREAM_ERROR, parent);
        FinishStreams();
    }

    [Fact]
    public void SplitBufferForSectionAck()
    {
        Setup(128);
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));
        VerifyRequiredInsertCount(1);
        SendInsertCountIncrement(1);
        VerifyKnownReceivedCount(1);

        // Refer now to dynamic table
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));
        ByteBuf buf = EncodeSectionAck(decoderStream.StreamId());

        try
        {
            while (buf.IsReadable())
            {
                Assert.False(decoderStream.WriteInbound(buf.ReadBytes(1)));
            }
        }
        finally
        {
            buf.Release();
        }

        FinishStreams();
    }

    [Fact]
    public void SplitBufferForInsertCountIncrement()
    {
        Setup(128);
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));
        VerifyRequiredInsertCount(1);
        ByteBuf buf = EncodeInsertCountIncrement(1);

        try
        {
            while (buf.IsReadable())
            {
                Assert.False(decoderStream.WriteInbound(buf.ReadBytes(1)));
            }
        }
        finally
        {
            buf.Release();
        }

        VerifyKnownReceivedCount(1);
        FinishStreams();
    }

    [Fact]
    public void SplitBufferForStreamCancellation()
    {
        Setup(128);
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));
        VerifyRequiredInsertCount(1);
        ByteBuf buf = EncodeStreamCancellation(decoderStream.StreamId());

        try
        {
            while (buf.IsReadable())
            {
                Assert.False(decoderStream.WriteInbound(buf.ReadBytes(1)));
            }
        }
        finally
        {
            buf.Release();
        }

        FinishStreams();
    }

    [Fact]
    public void StreamCancel()
    {
        Setup(128);
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));
        VerifyRequiredInsertCount(1);
        SendInsertCountIncrement(1);
        VerifyKnownReceivedCount(1);

        // Refer now to dynamic table
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));
        SendStreamCancellation(decoderStream.StreamId());
        VerifyRequiredInsertCount(1);
        VerifyKnownReceivedCount(1);
        FinishStreams();
    }

    [Fact]
    public void StreamCancelUnknownStream()
    {
        Setup(128);
        SendStreamCancellation(decoderStream.StreamId());
        VerifyRequiredInsertCount(0);
        VerifyKnownReceivedCount(0);
        FinishStreams();
    }

    [Fact]
    public void StreamCancelDynamicTableWithMaxCapacity0()
    {
        Setup(0);
        EncodeHeaders(headers => headers.Add(fooBar.name, fooBar.value));
        VerifyRequiredInsertCount(0);
        VerifyKnownReceivedCount(0);
        // Send a stream cancellation for a dynamic table of capacity 0.
        // See https://www.rfc-editor.org/rfc/rfc9204.html#section-2.2.2.2
        SendStreamCancellation(decoderStream.StreamId());
        FinishStreams(false);
    }

    // A malicious/non-compliant decoder that keeps acknowledging dynamic-table insertions (so the encoder keeps
    // reusing the same entry) but never sends the mandatory Section Acknowledgment/Stream Cancellation for each
    // field section must not be able to grow the encoder's per-connection tracking state without bound.
    // See https://www.rfc-editor.org/rfc/rfc9204.html#section-2.2.2.2
    [Fact]
    public void OutstandingSectionTrackingIsBounded()
    {
        Setup(128L);

        // Insert the entry once. The insert itself is encoded as a literal to avoid blocking the decoder before
        // it has seen the insert, so it does not create an outstanding section tracker.
        EncodeHeaders(0, headers => headers.Add(fooBar.name, fooBar.value));
        SendInsertCountIncrement(1);
        Assert.Equal(0, encoder.OutstandingSectionCount());

        // Reference the now-known entry from MAX_OUTSTANDING_SECTIONS distinct streams, without ever sending a
        // Section Acknowledgment or Stream Cancellation for any of them.
        for (long streamId = 1; streamId <= QpackEncoder.MAX_OUTSTANDING_SECTIONS; streamId++)
        {
            EncodeHeaders(streamId, headers => headers.Add(fooBar.name, fooBar.value));
        }

        Assert.Equal(QpackEncoder.MAX_OUTSTANDING_SECTIONS, encoder.OutstandingSectionCount());

        // The limit is reached: further field sections must fall back to literal encoding instead of growing
        // the tracker map any further.
        long overflowStreamId = QpackEncoder.MAX_OUTSTANDING_SECTIONS + 1;
        ByteBuf @out = decoderStream.Alloc().Buffer();

        try
        {
            EncodeHeaders(overflowStreamId, headers => headers.Add(fooBar.name, fooBar.value), @out);
            Assert.Equal(QpackEncoder.MAX_OUTSTANDING_SECTIONS, encoder.OutstandingSectionCount());
            Assert.True(IsLiteralFieldLineWithLiteralName(@out),
                    "Field section beyond the tracking limit must be encoded as a literal.");
        }
        finally
        {
            @out.Release();
        }

        // Acknowledging one outstanding section frees up tracking capacity again, and the dynamic table is used
        // once more for the next field section.
        SendAckForStreamId(1);
        Assert.Equal(QpackEncoder.MAX_OUTSTANDING_SECTIONS - 1, encoder.OutstandingSectionCount());

        long resumedStreamId = overflowStreamId + 1;
        @out = decoderStream.Alloc().Buffer();

        try
        {
            EncodeHeaders(resumedStreamId, headers => headers.Add(fooBar.name, fooBar.value), @out);
            Assert.Equal(QpackEncoder.MAX_OUTSTANDING_SECTIONS, encoder.OutstandingSectionCount());
            Assert.True(IsIndexedFieldLineWithDynamicTable(@out),
                    "Dynamic table must be used again once tracking capacity frees up.");
        }
        finally
        {
            @out.Release();
        }

        // Clean up all remaining outstanding sections so the test itself does not leak state.
        for (long streamId = 2; streamId <= QpackEncoder.MAX_OUTSTANDING_SECTIONS; streamId++)
        {
            SendStreamCancellation(streamId);
        }

        SendStreamCancellation(resumedStreamId);
        Assert.Equal(0, encoder.OutstandingSectionCount());

        FinishStreams();
    }

    [Fact]
    public void StreamCancellationFreesOutstandingSectionTrackingCapacity()
    {
        Setup(128L);

        EncodeHeaders(0, headers => headers.Add(fooBar.name, fooBar.value));
        SendInsertCountIncrement(1);

        for (long streamId = 1; streamId <= QpackEncoder.MAX_OUTSTANDING_SECTIONS; streamId++)
        {
            EncodeHeaders(streamId, headers => headers.Add(fooBar.name, fooBar.value));
        }

        Assert.Equal(QpackEncoder.MAX_OUTSTANDING_SECTIONS, encoder.OutstandingSectionCount());

        // Cancelling a stream with a still-pending field section also frees up tracking capacity.
        SendStreamCancellation(1);
        Assert.Equal(QpackEncoder.MAX_OUTSTANDING_SECTIONS - 1, encoder.OutstandingSectionCount());

        long resumedStreamId = QpackEncoder.MAX_OUTSTANDING_SECTIONS + 1;
        ByteBuf @out = decoderStream.Alloc().Buffer();

        try
        {
            EncodeHeaders(resumedStreamId, headers => headers.Add(fooBar.name, fooBar.value), @out);
            Assert.True(IsIndexedFieldLineWithDynamicTable(@out));
        }
        finally
        {
            @out.Release();
        }

        Assert.Equal(QpackEncoder.MAX_OUTSTANDING_SECTIONS, encoder.OutstandingSectionCount());

        for (long streamId = 2; streamId <= QpackEncoder.MAX_OUTSTANDING_SECTIONS; streamId++)
        {
            SendStreamCancellation(streamId);
        }

        SendStreamCancellation(resumedStreamId);
        Assert.Equal(0, encoder.OutstandingSectionCount());

        FinishStreams();
    }

    [Fact]
    public void InvalidIncrement()
    {
        Setup(128);
        Http3Exception e = Assert.ThrowsAny<Http3Exception>(() => SendInsertCountIncrement(2));
        Assert.IsAssignableFrom<QpackException>(e.InnerException);

        Http3TestUtils.VerifyClose(Http3ErrorCode.QPACK_DECODER_STREAM_ERROR, parent);
        FinishStreams();
    }

    private void SendAckForStreamId(long streamId)
    {
        Assert.False(decoderStream.WriteInbound(EncodeSectionAck(streamId)));
    }

    private ByteBuf EncodeSectionAck(long streamId)
    {
        ByteBuf ack = decoderStream.Alloc().Buffer();
        // https://quicwg.org/base-drafts/draft-ietf-quic-qpack.html#name-section-acknowledgment
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 1 |      Stream ID (7+)       |
        // +---+---------------------------+
        QpackUtil.EncodePrefixedInteger(ack, (byte) 0b1000_0000, 7, streamId);
        return ack;
    }

    private void SendInsertCountIncrement(long increment)
    {
        Assert.False(decoderStream.WriteInbound(EncodeInsertCountIncrement(increment)));
    }

    private ByteBuf EncodeInsertCountIncrement(long increment)
    {
        ByteBuf incr = decoderStream.Alloc().Buffer();
        // https://quicwg.org/base-drafts/draft-ietf-quic-qpack.html#name-insert-count-increment
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 0 |     Increment (6+)    |
        // +---+---+-----------------------+
        QpackUtil.EncodePrefixedInteger(incr, (byte) 0b0000_0000, 6, increment);
        return incr;
    }

    private void SendStreamCancellation(long streamId)
    {
        Assert.False(decoderStream.WriteInbound(EncodeStreamCancellation(streamId)));
    }

    private ByteBuf EncodeStreamCancellation(long streamId)
    {
        ByteBuf incr = decoderStream.Alloc().Buffer();
        // https://quicwg.org/base-drafts/draft-ietf-quic-qpack.html#name-stream-cancellation
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 0 | 1 |     Stream ID (6+)    |
        // +---+---+-----------------------+
        QpackUtil.EncodePrefixedInteger(incr, (byte) 0b0100_0000, 6, streamId);
        return incr;
    }

    private void EncodeHeaders(Action<IHttp3Headers> headersUpdater)
    {
        EncodeHeaders(decoderStream.StreamId(), headersUpdater);
    }

    private void EncodeHeaders(long streamId, Action<IHttp3Headers> headersUpdater, ByteBuf @out)
    {
        IHttp3Headers headers = new DefaultHttp3Headers();
        headersUpdater(headers);
        encoder.EncodeHeaders(attributes, @out, decoderStream.Alloc(), streamId, headers);
    }

    private void EncodeHeaders(long streamId, Action<IHttp3Headers> headersUpdater)
    {
        ByteBuf @out = decoderStream.Alloc().Buffer();

        try
        {
            EncodeHeaders(streamId, headersUpdater, @out);
        }
        finally
        {
            @out.Release();
        }
    }

    private static bool IsLiteralFieldLineWithLiteralName(ByteBuf encoded)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-literal-field-line-with-lit
        //   0   1   2   3   4   5   6   7
        //   +---+---+---+---+---+---+---+---+
        //   | 0 | 0 | 1 | N | H |NameLen(3+)|
        //   +---+---+---+---+---+-----------+
        // Section prefix is 2 bytes (required insert count + delta base) when both fit in a single byte.
        int firstLiteralByte = encoded.GetByte(2) & 0xFF;
        return (firstLiteralByte & 0b1110_0000) == 0b0010_0000;
    }

    private static bool IsIndexedFieldLineWithDynamicTable(ByteBuf encoded)
    {
        // https://www.rfc-editor.org/rfc/rfc9204.html#name-indexed-field-line
        //   0   1   2   3   4   5   6   7
        // +---+---+---+---+---+---+---+---+
        // | 1 | T |      Index (6+)       |
        // +---+---+-----------------------+
        // T = 0 for the dynamic table.
        int firstLiteralByte = encoded.GetByte(2) & 0xFF;
        return (firstLiteralByte & 0b1100_0000) == 0b1000_0000;
    }

    private void Setup(long maxTableCapacity)
    {
        maxEntries = checked((int) QpackUtil.MaxEntries(maxTableCapacity));
        parent = new EmbeddedQuicChannel(true);
        attributes = new QpackAttributes(parent, false);
        Http3.SetQpackAttributes(parent, attributes);
        IHttp3SettingsFrame settingsFrame = new DefaultHttp3SettingsFrame();
        settingsFrame.Settings().Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY.Id(),
                maxTableCapacity);
        QpackDecoder decoder = new QpackDecoder(maxTableCapacity, 0);
        encoderStream = (EmbeddedQuicStreamChannel) parent.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL,
                new QpackEncoderHandler(maxTableCapacity, decoder)).GetAwaiter().GetResult();
        attributes.EncoderStream(encoderStream);
        encoder = new QpackEncoder(dynamicTable, IQpackSensitivityDetector.NEVER_SENSITIVE);
        encoder.ConfigureDynamicTable(attributes, maxTableCapacity, 0);
        decoderStream = (EmbeddedQuicStreamChannel) parent.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL,
                new QpackDecoderHandler(encoder)).GetAwaiter().GetResult();
        attributes.DecoderStream(decoderStream);
    }

    private void FinishStreams()
    {
        FinishStreams(true);
    }

    private void FinishStreams(bool encoderPendingMessage)
    {
        Assert.False(decoderStream.FinishAndReleaseAll(), "Unexpected decoder stream message");
        Assert.Equal(encoderPendingMessage, encoderStream.FinishAndReleaseAll(), "Unexpected encoder stream message");
        Assert.False(parent.FinishAndReleaseAll(), "Unexpected parent stream message");
    }

    private void VerifyRequiredInsertCount(int insertCount)
    {
        Assert.Equal(insertCount == 0 ? 0 : insertCount % maxEntries + 1,
                dynamicTable.EncodedRequiredInsertCount(dynamicTable.InsertCount()),
                "Unexpected dynamic table insert count.");
    }

    private void VerifyKnownReceivedCount(int receivedCount)
    {
        Assert.Equal(receivedCount == 0 ? 0 : receivedCount % maxEntries + 1, dynamicTable.EncodedKnownReceivedCount(),
                "Unexpected dynamic table known received count.");
    }
}
