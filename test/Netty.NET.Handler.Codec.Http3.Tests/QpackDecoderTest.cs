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
using System.Threading;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Assert = XunitAssertMessages.AssertM;
using static Netty.NET.Handler.Codec.Http3.Tests.Http3TestUtils;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: the @ParameterizedTest methods with @MethodSource("data") are [Theory]s with [MemberData(nameof(Data))] over a
// public static TheoryData<int, int> with the same eight rows (Collection<Object[]> of Integers); the display-name
// pattern has no xUnit counterpart. assumeTrue(condition) is Assert.SkipUnless(condition, ...), which reports the case
// as skipped, as JUnit reports an aborted test ("will skip the test if condition is false",
// https://xunit.net/docs/getting-started/v3/whats-new). Hamcrest's assertThat(actual, is(expected)) is
// Assert.Equal(expected, actual); assertThrows(T.class, ...) is Assert.ThrowsAny<T>. The QPACK types and members are
// internal (package-private in Java) and reached through InternalsVisibleTo;
// QpackDecoderStateSyncStrategy.ackEachInsert() is IQpackDecoderStateSyncStrategy.AckEachInsert() and the static
// imports are qualified (QpackUtil.MAX_UNSIGNED_INT, Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY);
// Math.toIntExact is a checked conversion. The BiConsumer<CharSequence, CharSequence> sink is an Action<ICharSequence,
// ICharSequence> and the Runnable an Action; AtomicInteger is an int updated with Interlocked.Increment and read with
// Volatile.Read (incrementAndGet as a method reference is a lambda). A Java String passed as a CharSequence
// (insertLiteral) is a StringCharSequence (Http3TestUtils.Cs), and QpackHeaderField's CharSequence name and value are
// compared with the expected String as text (Hamcrest's is(String) calls String.equals). readUnsignedByte() is
// ReadByte(), which returns the unsigned CLR byte, compared as a short as in Java.
public class QpackDecoderTest
{
    private const string FOO = "foo";
    private const string BAR = "bar";
    private QpackDecoderDynamicTable table;
    private EmbeddedQuicStreamChannel decoderStream;

    private QpackDecoder decoder;
    private int inserted;
    private int maxEntries;
    private QpackAttributes attributes;

    public static TheoryData<int, int> Data()
    {
        int capacity = 128; // maxEntries = 128/32 = 4, maxIndex = 2*4 = 8
        return new TheoryData<int, int>
        {
            { capacity, 0 },
            { capacity, 1 },
            { capacity, 5 },
            { capacity, 8 },
            { capacity, 16 },
            { capacity, 25 },
            { capacity, 64 },
            { capacity, 89 }
        };
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void RequiredInsertCountAsInserted(int capacity, int insertionCount)
    {
        Setup(capacity);

        InsertLiterals(insertionCount);
        EncodeDecodeVerifyRequiredInsertCount(inserted);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void RequiredInsertCountLessThanInserted(int capacity, int insertionCount)
    {
        Setup(capacity);
        Assert.SkipUnless(insertionCount > 0, "insertionCount > 0");

        InsertLiterals(insertionCount);
        EncodeDecodeVerifyRequiredInsertCount(insertionCount - 1);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void RequiredInsertCountBehindMax(int capacity, int insertionCount)
    {
        Setup(capacity);
        Assert.SkipUnless(insertionCount > maxEntries, "insertionCount > maxEntries");

        InsertLiterals(insertionCount);
        EncodeDecodeVerifyRequiredInsertCount(insertionCount - maxEntries + 1);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void GetWithRelativeIndex(int capacity, int insertionCount)
    {
        Setup(capacity);
        Assert.SkipUnless(insertionCount > 3, "insertionCount > 3");

        InsertLiterals(insertionCount);
        int requiredInsertCount = EncodeDecodeRequiredInsertCount(insertionCount);
        int @base = EncodeDecodeDeltaBase(requiredInsertCount, false, 1);
        int relativeIndex = 1;
        QpackHeaderField entry = table.GetEntryRelativeEncodedField(@base - relativeIndex);
        VerifyField(entry, insertionCount - 2);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void GetWithPostBaseRelativeIndex(int capacity, int insertionCount)
    {
        Setup(capacity);
        Assert.SkipUnless(insertionCount > 2, "insertionCount > 2");

        InsertLiterals(insertionCount);
        int requiredInsertCount = EncodeDecodeRequiredInsertCount(insertionCount - 1);
        int @base = EncodeDecodeDeltaBase(requiredInsertCount, true, 0);
        int relativeIndex = 1;
        QpackHeaderField entry = table.GetEntryRelativeEncodedField(@base - relativeIndex);
        VerifyField(entry, insertionCount - 1);
    }

    [Fact]
    public void ZeroMaxBlockedStreamsThrowsOnBlockedStream()
    {
        Setup(128);
        ByteBuf @in = EncodeBlockingFrame(1);

        try
        {
            Assert.ThrowsAny<QpackException>(
                    () => decoder.Decode(attributes, 0L, @in, @in.ReadableBytes, (n, v) =>
                    {
                    }, () =>
                    {
                    }));
        }
        finally
        {
            @in.Release();
        }
    }

    [Fact]
    public void MaxBlockedStreamsLimitEnforced()
    {
        int maxBlockedStreams = 3;
        Setup(128, maxBlockedStreams);
        Action<ICharSequence, ICharSequence> sink = (n, v) =>
        {
        };

        for (long streamId = 0; streamId < maxBlockedStreams; streamId++)
        {
            ByteBuf @in = EncodeBlockingFrame(1);

            try
            {
                Assert.False(decoder.Decode(attributes, streamId, @in, @in.ReadableBytes, sink, () =>
                {
                }),
                        "Stream " + streamId + " should be blocked");
            }
            finally
            {
                @in.Release();
            }
        }

        ByteBuf overflow = EncodeBlockingFrame(1);

        try
        {
            Assert.ThrowsAny<QpackException>(
                    () => decoder.Decode(attributes, 99L, overflow, overflow.ReadableBytes, sink, () =>
                    {
                    }));
        }
        finally
        {
            overflow.Release();
        }
    }

    [Fact]
    public void BlockedStreamsCountReleasedAfterUnblocking()
    {
        int maxBlockedStreams = 2;
        Setup(128, maxBlockedStreams);
        Action<ICharSequence, ICharSequence> sink = (n, v) =>
        {
        };

        for (long streamId = 0; streamId < maxBlockedStreams; streamId++)
        {
            ByteBuf @in = EncodeBlockingFrame(1);

            try
            {
                Assert.False(decoder.Decode(attributes, streamId, @in, @in.ReadableBytes, sink, () =>
                {
                }),
                        "Stream " + streamId + " should be blocked");
            }
            finally
            {
                @in.Release();
            }
        }

        InsertLiterals(1);

        for (long streamId = 100; streamId < 100 + maxBlockedStreams; streamId++)
        {
            ByteBuf @in = EncodeBlockingFrame(2);

            try
            {
                Assert.False(decoder.Decode(attributes, streamId, @in, @in.ReadableBytes, sink, () =>
                {
                }),
                        "Stream " + streamId + " should be blocked after counter was released");
            }
            finally
            {
                @in.Release();
            }
        }
    }

    [Fact]
    public void BlockedStreamsCountReleasedAfterStreamAbandoned()
    {
        int maxBlockedStreams = 2;
        Setup(128, maxBlockedStreams);

        int abandoned = 0;
        Action<ICharSequence, ICharSequence> sink = (n, v) =>
        {
        };

        for (long streamId = 0; streamId < maxBlockedStreams; streamId++)
        {
            ByteBuf @in = EncodeBlockingFrame(1);

            try
            {
                Assert.False(decoder.Decode(attributes, streamId, @in, @in.ReadableBytes, sink,
                        () => Interlocked.Increment(ref abandoned)), "Stream " + streamId + " should be blocked");
            }
            finally
            {
                @in.Release();
            }
        }

        for (long streamId = 0; streamId < maxBlockedStreams; streamId++)
        {
            decoder.StreamAbandoned(decoderStream, streamId);
            ByteBuf cancel = decoderStream.ReadOutbound<ByteBuf>();

            try
            {
                Assert.Equal((short) (0b0100_0000 | streamId), (short) cancel.ReadByte());
            }
            finally
            {
                cancel.Release();
            }
        }

        int resumed = 0;

        for (long streamId = 100; streamId < 100 + maxBlockedStreams; streamId++)
        {
            ByteBuf @in = EncodeBlockingFrame(1);

            try
            {
                Assert.False(decoder.Decode(attributes, streamId, @in, @in.ReadableBytes, sink,
                        () => Interlocked.Increment(ref resumed)), "Stream " + streamId + " should be blocked");
            }
            finally
            {
                @in.Release();
            }
        }

        InsertLiterals(1);
        Assert.Equal(0, Volatile.Read(ref abandoned));
        Assert.Equal(maxBlockedStreams, Volatile.Read(ref resumed));
    }

    /**
     * Encodes a 2-byte QPACK header block prefix (Required Insert Count + Base) that will cause
     * a stream to block until the dynamic table reaches {@code requiredInsertCount}.
     */
    private ByteBuf EncodeBlockingFrame(int requiredInsertCount)
    {
        ByteBuf buf = Unpooled.Buffer(2);
        QpackUtil.EncodePrefixedInteger(buf, (byte) 0b0, 8, requiredInsertCount % (2L * maxEntries) + 1);
        QpackUtil.EncodePrefixedInteger(buf, (byte) 0b0, 7, 0);
        return buf;
    }

    private void Setup(long capacity)
    {
        Setup(capacity, 0);
    }

    private void Setup(long capacity, int maxBlockedStreams)
    {
        long maxTableCapacity = QpackUtil.MAX_UNSIGNED_INT;
        inserted = 0;
        this.maxEntries = checked((int) QpackUtil.MaxEntries(maxTableCapacity));
        DefaultHttp3SettingsFrame settingsFrame = new DefaultHttp3SettingsFrame();
        settingsFrame.Settings().Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY.Id(),
                maxTableCapacity);
        table = new QpackDecoderDynamicTable();
        EmbeddedQuicChannel parent = new EmbeddedQuicChannel(true);
        attributes = new QpackAttributes(parent, false);
        decoderStream = new EmbeddedQuicStreamChannel();
        attributes.DecoderStream(decoderStream);
        decoder = new QpackDecoder(maxTableCapacity, maxBlockedStreams, table,
                IQpackDecoderStateSyncStrategy.AckEachInsert());
        decoder.SetDynamicTableCapacity(capacity);
    }

    private void EncodeDecodeVerifyRequiredInsertCount(int count)
    {
        int ric = EncodeDecodeRequiredInsertCount(count);
        Assert.Equal(count, ric);
    }

    private int EncodeDecodeDeltaBase(int requiredInsertCount, bool postBase, int deltaBase)
    {
        ByteBuf buf = Unpooled.Buffer();
        QpackUtil.EncodePrefixedInteger(buf, (byte) (postBase ? 0b0 : 0b1000_0000), 8, deltaBase);

        try
        {
            return decoder.DecodeBase(buf, requiredInsertCount);
        }
        finally
        {
            buf.Release();
        }
    }

    private int EncodeDecodeRequiredInsertCount(int count)
    {
        ByteBuf buf = Unpooled.Buffer();
        QpackUtil.EncodePrefixedInteger(buf, (byte) 0b0, 8, count == 0 ? 0 : count % (2L * maxEntries) + 1);

        try
        {
            return decoder.DecodeRequiredInsertCount(attributes, buf);
        }
        finally
        {
            buf.Release();
        }
    }

    private void InsertLiterals(int count)
    {
        for (int i = 1; i <= count; i++)
        {
            inserted++;
            decoder.InsertLiteral(decoderStream, Cs(FOO + i), Cs(BAR + i));
        }

        Assert.Equal(count > 0, decoderStream.FinishAndReleaseAll());
    }

    private void VerifyField(QpackHeaderField field, int fieldIndexWhenInserted)
    {
        Assert.Equal(FOO + fieldIndexWhenInserted, field.name.ToString());
        Assert.Equal(BAR + fieldIndexWhenInserted, field.value.ToString());
    }
}
