/*
 * Copyright 2026 The Netty Project
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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Quic;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: @AfterEach tearDown() is Dispose() (xUnit creates a test class instance per test and disposes it after the
// test). Hamcrest's assertThat([reason,] actual, is(expected)) is Assert.Equal(expected, actual[, reason]),
// assertThat(e.getCause(), instanceOf(T.class)) is Assert.IsAssignableFrom<T>(e.InnerException), and
// assertThrows(T.class, ...) is Assert.ThrowsAny<T>; errorCode() is ErrorCode(). The QPACK types and members are
// internal (package-private in Java) and reached through InternalsVisibleTo; the static imports are qualified
// (Http3.SetQpackAttributes, Http3ErrorCode.QPACK_ENCODER_STREAM_ERROR, QpackUtil.EncodePrefixedInteger),
// QpackDecoderStateSyncStrategy is the interface IQpackDecoderStateSyncStrategy and QpackSensitivityDetector
// IQpackSensitivityDetector. parent.createStream(...).get() is the result of the completed Task of CreateStreamAsync
// (GetAwaiter().GetResult()); alloc() is Alloc(), Integer.MAX_VALUE is int.MaxValue, writeByte('a') passes the char as
// the int value, and writeCharSequence(s, CharsetUtil.US_ASCII) is WriteCharSequence(s, Encoding.ASCII). The throws
// clauses have no counterpart.
public class QpackEncoderHandlerTest : IDisposable
{

    private EmbeddedQuicChannel parent;
    private EmbeddedQuicStreamChannel encoderStream;
    private EmbeddedQuicStreamChannel decoderStream;
    private QpackDecoderDynamicTable dynamicTable;

    public void Dispose()
    {
        Assert.False(parent.FinishAndReleaseAll());
    }

    // A peer must never be able to make us buffer more than maxTableCapacity bytes for a single string literal
    // as no such literal could ever be inserted into the dynamic table anyway.
    [Fact]
    public void InsertWithLiteralNameRejectsNameLengthExceedingMaxTableCapacity()
    {
        Setup(64);

        // Declare a name length that is way larger than what could ever fit into the dynamic table, and only
        // trickle a few bytes of the (bogus) payload. Prior to the fix this would be buffered indefinitely by the
        // ByteToMessageDecoder cumulator, potentially growing towards ~2 GiB.
        ByteBuf buf = encoderStream.Alloc().Buffer();
        QpackUtil.EncodePrefixedInteger(buf, (byte) 0b0100_0000, 5, int.MaxValue - 1);
        buf.WriteByte('a');

        Http3Exception e = Assert.ThrowsAny<Http3Exception>(() => encoderStream.WriteInbound(buf));
        Assert.Equal(Http3ErrorCode.QPACK_ENCODER_STREAM_ERROR, e.ErrorCode());
        Assert.IsAssignableFrom<QpackException>(e.InnerException);

        Http3TestUtils.VerifyClose(Http3ErrorCode.QPACK_ENCODER_STREAM_ERROR, parent);
        Assert.Equal(0, dynamicTable.InsertCount());

        FinishStreams(false);
    }

    [Fact]
    public void InsertWithNameReferenceRejectsValueLengthExceedingMaxTableCapacity()
    {
        Setup(64);

        ByteBuf buf = encoderStream.Alloc().Buffer();
        // T = 1 (static table reference), Name Index = 0.
        QpackUtil.EncodePrefixedInteger(buf, (byte) 0b1100_0000, 6, 0);
        // Declare a huge value length but never actually send that many bytes.
        QpackUtil.EncodePrefixedInteger(buf, (byte) 0b0000_0000, 7, int.MaxValue - 1);
        buf.WriteByte('a');

        Http3Exception e = Assert.ThrowsAny<Http3Exception>(() => encoderStream.WriteInbound(buf));
        Assert.Equal(Http3ErrorCode.QPACK_ENCODER_STREAM_ERROR, e.ErrorCode());
        Assert.IsAssignableFrom<QpackException>(e.InnerException);

        Http3TestUtils.VerifyClose(Http3ErrorCode.QPACK_ENCODER_STREAM_ERROR, parent);
        Assert.Equal(0, dynamicTable.InsertCount());

        FinishStreams(false);
    }

    [Fact]
    public void InsertWithLiteralNameWithinMaxTableCapacityIsAccepted()
    {
        Setup(64);

        SetDynamicTableCapacity(64);
        Assert.False(encoderStream.WriteInbound(EncodeInsertWithLiteralName("foo", "bar")));

        Assert.Equal(1, dynamicTable.InsertCount());
        Assert.Equal((long) ("foo".Length + "bar".Length + 32), dynamicTable.Size());

        FinishStreams(true);
    }

    [Fact]
    public void InsertWithLiteralNameRejectsNameLengthOneByteOverMaxTableCapacity()
    {
        Setup(64);

        // Declare a name length that is just one byte larger than maxTableCapacity, and don't send any payload
        // bytes at all: rejection must be based purely on the declared length, before waiting for (or buffering)
        // any of the string data.
        ByteBuf buf = encoderStream.Alloc().Buffer();
        QpackUtil.EncodePrefixedInteger(buf, (byte) 0b0100_0000, 5, 65);

        Http3Exception e = Assert.ThrowsAny<Http3Exception>(() => encoderStream.WriteInbound(buf));
        Assert.Equal(Http3ErrorCode.QPACK_ENCODER_STREAM_ERROR, e.ErrorCode());

        Http3TestUtils.VerifyClose(Http3ErrorCode.QPACK_ENCODER_STREAM_ERROR, parent);
        Assert.Equal(0, dynamicTable.InsertCount());

        FinishStreams(false);
    }

    private void SetDynamicTableCapacity(int capacity)
    {
        ByteBuf buf = encoderStream.Alloc().Buffer();
        QpackUtil.EncodePrefixedInteger(buf, (byte) 0b0010_0000, 5, capacity);
        Assert.False(encoderStream.WriteInbound(buf));
    }

    private ByteBuf EncodeInsertWithLiteralName(string name, string value)
    {
        ByteBuf buf = encoderStream.Alloc().Buffer();
        QpackUtil.EncodePrefixedInteger(buf, (byte) 0b0100_0000, 5, name.Length);
        buf.WriteCharSequence(name, Encoding.ASCII);
        QpackUtil.EncodePrefixedInteger(buf, (byte) 0b0000_0000, 7, value.Length);
        buf.WriteCharSequence(value, Encoding.ASCII);
        return buf;
    }

    private void Setup(long maxTableCapacity)
    {
        parent = new EmbeddedQuicChannel(true);
        QpackAttributes attributes = new QpackAttributes(parent, false);
        Http3.SetQpackAttributes(parent, attributes);

        dynamicTable = new QpackDecoderDynamicTable();
        QpackDecoder decoder = new QpackDecoder(maxTableCapacity, 0, dynamicTable,
                IQpackDecoderStateSyncStrategy.AckEachInsert());

        encoderStream = (EmbeddedQuicStreamChannel) parent.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL,
                new QpackEncoderHandler(maxTableCapacity, decoder)).GetAwaiter().GetResult();
        attributes.EncoderStream(encoderStream);

        decoderStream = (EmbeddedQuicStreamChannel) parent.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL,
                new QpackDecoderHandler(new QpackEncoder(IQpackSensitivityDetector.NEVER_SENSITIVE)))
                .GetAwaiter().GetResult();
        attributes.DecoderStream(decoderStream);
    }

    private void FinishStreams(bool decoderPendingMessage)
    {
        Assert.Equal(decoderPendingMessage, decoderStream.FinishAndReleaseAll(), "Unexpected decoder stream message");
        Assert.False(encoderStream.FinishAndReleaseAll(), "Unexpected encoder stream message");
    }
}
