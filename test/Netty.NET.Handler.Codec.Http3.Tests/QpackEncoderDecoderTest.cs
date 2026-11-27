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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using Xunit.Sdk;
using Assert = XunitAssertMessages.AssertM;
using static Netty.NET.Handler.Codec.Http3.Tests.Http3TestUtils;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: @AfterEach tearDown() is Dispose() (xUnit creates a test class instance per test and disposes it after the
// test). Hamcrest's assertThat([reason,] actual, is(expected)) is Assert.Equal(expected, actual[, reason]),
// is(notNullValue()) is Assert.NotNull and greaterThanOrEqualTo(0) is Assert.True(actual >= 0, reason);
// assertThrows(T.class, ...) is Assert.ThrowsAny<T>. The project has no Moq (docs/codec-http3-porting.md), so
// mock(QpackDecoderStateSyncStrategy.class) is the private nested class MockQpackDecoderStateSyncStrategy with
// Mockito's default answers (sectionAcknowledged does nothing, entryAdded returns false), and
// when(syncStrategy.entryAdded(anyInt())) .thenAnswer(__ -> stateSyncStrategyAckNextInsert) sets its entryAdded answer
// for any int to a lambda that reads the field at call time. BlockingQueue<Callable<Void>> is a ConcurrentQueue<Action>
// (offer is Enqueue and poll TryDequeue, the non-blocking operations the test uses; Callable<Void> returning null is an
// Action), and the lambdas that offer other.channelRead(ctx, msg) are Actions. ForwardWriteToReadOnOtherHandler's
// write(ctx, msg, promise) is WriteAsync(ctx, msg, cancellationToken): for a ByteBuf it returns the Task of a promise
// that is never completed, as the Java method never completes the promise, and super.write(ctx, msg, promise) is
// base.WriteAsync; ChannelInboundHandler is IChannelInboundHandler. The lambda sensitivity detector of
// sensitiveHeaderIsNotInsertedIntoDynamicTable is the private nested class AuthorizationSensitivityDetector, where
// "authorization".contentEquals(name) is an ordinal comparison with the name's text. The BiConsumer sink passed to
// decode is the method group Http3HeadersSink.Accept and the Runnables are Actions; IllegalStateException is
// InvalidOperationException and AssertionError(msg, cause) is Xunit.Sdk.XunitException. The QPACK types and members are
// internal (package-private in Java) and reached through InternalsVisibleTo; the static imports are qualified
// (UnpooledByteBufAllocator.Default, Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_*, QuicStreamType.UNIDIRECTIONAL), and
// QpackSensitivityDetector is the interface IQpackSensitivityDetector. A Java String passed as a CharSequence is a
// StringCharSequence (Http3TestUtils.Cs); new AsciiString(CharSequence) is the AsciiString(ICharSequence) constructor,
// and decHeaders.get(name) is compared with it through Assert.Equal<ICharSequence> (the text). getByte(int) returns the
// unsigned CLR byte, so & 0xFF and the (int) casts keep the same values. parent.createStream(...).get() is the result
// of the completed Task of CreateStreamAsync (GetAwaiter().GetResult()); Math.toIntExact is a checked conversion. The
// throws clauses have no counterpart.
public class QpackEncoderDecoderTest : IDisposable
{

    private QpackEncoder encoder;
    private QpackDecoder decoder;
    private bool stateSyncStrategyAckNextInsert = true;
    private int headersAdded;
    private int maxEntries;
    private QpackEncoderDynamicTable encDynamicTable;
    private QpackDecoderDynamicTable decDynamicTable;
    private ConcurrentQueue<Action> suspendedEncoderInstructions;
    private IQpackSensitivityDetector sensitivityDetector = IQpackSensitivityDetector.NEVER_SENSITIVE;

    private readonly MockQpackDecoderStateSyncStrategy syncStrategy = new MockQpackDecoderStateSyncStrategy();
    private readonly IHttp3Headers encHeaders = new DefaultHttp3Headers();
    private readonly IHttp3Headers decHeaders = new DefaultHttp3Headers();
    private readonly ByteBuf @out = Unpooled.Buffer();
    private readonly EmbeddedQuicChannel parent = new EmbeddedQuicChannel(true);
    private QpackAttributes attributes;

    public void Dispose()
    {
        @out.Release();
    }

    [Fact]
    public void DynamicIndexed()
    {
        Setup(128, 0);
        headersAdded++;
        TestDynamicTableIndexed(Cs("foo"), Cs("bar"));
    }

    [Fact]
    public void DynamicIndexedWithBlockedStreams()
    {
        Setup(128, 100);
        headersAdded++;
        TestDynamicTableIndexedWithBlockedStreams(Cs("foo"), Cs("bar"));
    }

    [Fact]
    public void DynamicIndexedWithStaticTableNameRef()
    {
        Setup(128, 0);
        headersAdded++;
        TestDynamicTableIndexed(Cs(":authority"), Cs("netty.quic"));
    }

    [Fact]
    public void DynamicIndexedWithStaticTableNameRefWithBlockedStreams()
    {
        Setup(128, 100);
        headersAdded++;
        TestDynamicTableIndexedWithBlockedStreams(Cs(":authority"), Cs("netty.quic"));
    }

    [Fact]
    public void DynamicIndexedWithNameRef()
    {
        Setup(128, 0);

        headersAdded++;
        TestDynamicTableIndexed(Cs("foo"), Cs("bar"));

        ResetState();

        headersAdded++;
        TestDynamicTableIndexed(Cs("foo"), Cs("bar2"));

        ResetState();

        TestDynamicTableIndexed(Cs("foo"), Cs("bar"));
    }

    [Fact]
    public void DynamicIndexedWithNameRefWithBlockedStream()
    {
        Setup(128, 100);

        headersAdded++;
        TestDynamicTableIndexedWithBlockedStreams(Cs("foo"), Cs("bar"));

        ResetState();

        headersAdded++;
        TestDynamicTableIndexedWithBlockedStreams(Cs("foo"), Cs("bar2"));
    }

    [Fact]
    public void IndexWrapAround()
    {
        Setup(128, 0); // maxEntries => 128/32 = 4, full range = 2*4 = 8

        AddEncodeHeader("foo", "bar", 3);
        Encode(@out, encHeaders);
        VerifyRequiredInsertCount(3);
        VerifyKnownReceivedCount(3);
        Decode(@out, decHeaders);
        Assert.Equal(3, decDynamicTable.InsertCount());
        Assert.Equal(3, decHeaders.Size());
        VerifyDecodedHeaders("foo", "bar", 3);

        ResetState();
        AddEncodeHeader("boo", "far", 3);
        Encode(@out, encHeaders);
        VerifyRequiredInsertCount(6);
        VerifyKnownReceivedCount(6);
        Decode(@out, decHeaders);
        Assert.Equal(6, decDynamicTable.InsertCount());
        Assert.Equal(3, decHeaders.Size());
        VerifyDecodedHeaders("boo", "far", 3);

        ResetState();
        AddEncodeHeader("zoo", "gar", 3);
        Encode(@out, encHeaders);
        VerifyRequiredInsertCount(9);
        VerifyKnownReceivedCount(9);

        Decode(@out, decHeaders);
        Assert.Equal(9, decDynamicTable.InsertCount());
        Assert.Equal(3, decHeaders.Size());
        VerifyDecodedHeaders("zoo", "gar", 3);

        // Now reuse the headers for encode to use dynamic table.
        ResetState();
        Assert.True(encDynamicTable.GetEntryIndex(Cs("zoo1"), Cs("gar")) >= 0,
                "Header not found in encoder dynamic table.");
        Assert.True(encDynamicTable.GetEntryIndex(Cs("zoo2"), Cs("gar")) >= 0,
                "Header not found in encoder dynamic table.");
        encHeaders.Add(Cs("zoo1"), Cs("gar"));
        encHeaders.Add(Cs("zoo2"), Cs("gar"));
        Encode(@out, encHeaders);
        VerifyRequiredInsertCount(9); // No new inserts
        VerifyKnownReceivedCount(9); // No new inserts
        Decode(@out, decHeaders);
        Assert.Equal(9, decDynamicTable.InsertCount());
        Assert.Equal(2, decHeaders.Size());
        VerifyDecodedHeader(Cs("zoo1"), Cs("gar"));
        VerifyDecodedHeader(Cs("zoo2"), Cs("gar"));
    }

    [Fact]
    public void IndexWrapAroundWithBlockedStreams()
    {
        Setup(128, 100); // maxEntries => 128/32 = 4, full range = 2*4 = 8

        AddEncodeHeader("foo", "bar", 3);
        Encode(@out, encHeaders);
        VerifyRequiredInsertCount(3);
        VerifyKnownReceivedCount(0);
        Assert.Equal(0, decDynamicTable.InsertCount());

        DrainNextSuspendedEncoderInstruction();
        Decode(@out, decHeaders);
        DrainAllSuspendedEncoderInstructions();
        Assert.Equal(3, decDynamicTable.InsertCount());
        VerifyKnownReceivedCount(3);
        Assert.Equal(3, decHeaders.Size());
        VerifyDecodedHeaders("foo", "bar", 3);

        ResetState();
        AddEncodeHeader("boo", "far", 3);
        Encode(@out, encHeaders);
        VerifyRequiredInsertCount(6);
        VerifyKnownReceivedCount(0); // All acknowledged entries were removed.

        Decode(@out, decHeaders);
        DrainAllSuspendedEncoderInstructions();
        Assert.Equal(6, decDynamicTable.InsertCount());
        VerifyKnownReceivedCount(6);
        Assert.Equal(3, decHeaders.Size());
        VerifyDecodedHeaders("boo", "far", 3);

        ResetState();
        AddEncodeHeader("zoo", "gar", 3);
        Encode(@out, encHeaders);
        VerifyRequiredInsertCount(9);
        VerifyKnownReceivedCount(0); // All acknowledged entries were removed.

        Decode(@out, decHeaders);
        DrainAllSuspendedEncoderInstructions();
        VerifyKnownReceivedCount(9);
        Assert.Equal(9, decDynamicTable.InsertCount());
        Assert.Equal(3, decHeaders.Size());
        VerifyDecodedHeaders("zoo", "gar", 3);

        // Now reuse the headers for encode to use dynamic table.
        ResetState();
        Assert.True(encDynamicTable.GetEntryIndex(Cs("zoo1"), Cs("gar")) >= 0,
                "Header not found in encoder dynamic table.");
        Assert.True(encDynamicTable.GetEntryIndex(Cs("zoo2"), Cs("gar")) >= 0,
                "Header not found in encoder dynamic table.");
        encHeaders.Add(Cs("zoo1"), Cs("gar"));
        encHeaders.Add(Cs("zoo2"), Cs("gar"));
        Encode(@out, encHeaders);
        VerifyRequiredInsertCount(9); // No new inserts
        VerifyKnownReceivedCount(9); // No new inserts
        Decode(@out, decHeaders);
        Assert.Equal(9, decDynamicTable.InsertCount());
        Assert.Equal(2, decHeaders.Size());
        VerifyDecodedHeader(Cs("zoo1"), Cs("gar"));
        VerifyDecodedHeader(Cs("zoo2"), Cs("gar"));
    }

    [Fact]
    public void Duplicate()
    {
        Setup(256, 0, 50);
        // Do not ack any add so entries are not evicted from the table just marked for eviction hence leveraging
        // duplicate path
        stateSyncStrategyAckNextInsert = false;

        AddEncodeHeader("foo", "bar", 5);
        QpackHeaderField oldEntry = new QpackHeaderField(Cs("foo0"), Cs("bar"));
        Assert.NotNull(encHeaders.Get(oldEntry.name, oldEntry.value));

        ByteBuf spareEncode = Unpooled.Buffer();
        try
        {
            Encode(spareEncode, encHeaders);
        }
        finally
        {
            spareEncode.Release();
        }
        VerifyRequiredInsertCount(5);
        VerifyKnownReceivedCount(0);

        int idx = encDynamicTable.GetEntryIndex(oldEntry.name, oldEntry.value);
        Assert.True(idx >= 0);
        Assert.True(encDynamicTable.RequiresDuplication(idx, oldEntry.Size()));

        ResetState();
        stateSyncStrategyAckNextInsert = true;

        encHeaders.Add(oldEntry.name, oldEntry.value);
        Encode(@out, encHeaders); // duplicate but not add to the header block
        VerifyRequiredInsertCount(6);
        Decode(@out, decHeaders);
        VerifyKnownReceivedCount(6);

        Assert.Equal(6, decDynamicTable.InsertCount());
        Assert.Equal(1, decHeaders.Size());
        VerifyDecodedHeader(oldEntry.name, oldEntry.value);

        // Now encode again to refer to the duplicated entry
        ResetState();

        encHeaders.Add(oldEntry.name, oldEntry.value);
        Encode(@out, encHeaders);
        VerifyRequiredInsertCount(6);
        Decode(@out, decHeaders);
        VerifyKnownReceivedCount(6);

        Assert.Equal(6, decDynamicTable.InsertCount());
        Assert.Equal(1, decHeaders.Size());
        VerifyDecodedHeader(oldEntry.name, oldEntry.value);
    }

    [Fact]
    public void DuplicateWithBlockedStreams()
    {
        Setup(256, 100, 50);
        // Do not ack any add so entries are not evicted from the table just marked for eviction hence leveraging
        // duplicate path
        stateSyncStrategyAckNextInsert = false;

        AddEncodeHeader("foo", "bar", 5);
        QpackHeaderField oldEntry = new QpackHeaderField(Cs("foo0"), Cs("bar"));
        Assert.NotNull(encHeaders.Get(oldEntry.name, oldEntry.value));

        ByteBuf spareEncode = Unpooled.Buffer();
        try
        {
            Encode(spareEncode, encHeaders);
        }
        finally
        {
            spareEncode.Release();
        }
        VerifyRequiredInsertCount(5);
        VerifyKnownReceivedCount(0);

        int idx = encDynamicTable.GetEntryIndex(oldEntry.name, oldEntry.value);
        Assert.True(idx >= 0);
        Assert.True(encDynamicTable.RequiresDuplication(idx, oldEntry.Size()));

        ResetState();
        stateSyncStrategyAckNextInsert = true;

        encHeaders.Add(oldEntry.name, oldEntry.value);
        Encode(@out, encHeaders);
        VerifyRequiredInsertCount(6);

        DrainNextSuspendedEncoderInstruction();
        Decode(@out, decHeaders);
        Assert.Equal(0, decDynamicTable.InsertCount());
        VerifyKnownReceivedCount(0);

        DrainAllSuspendedEncoderInstructions();
        Assert.Equal(6, decDynamicTable.InsertCount());
        VerifyKnownReceivedCount(6);
        Assert.Equal(1, decHeaders.Size());
        VerifyDecodedHeader(oldEntry.name, oldEntry.value);
    }

    private void TestDynamicTableIndexed(ICharSequence name, ICharSequence value)
    {
        encHeaders.Add(name, value);
        Encode(@out, encHeaders);
        VerifyRequiredInsertCount(headersAdded);
        VerifyKnownReceivedCount(headersAdded);

        Decode(@out, decHeaders);
        Assert.Equal(headersAdded, decDynamicTable.InsertCount());
        Assert.Equal(1, decHeaders.Size());
        VerifyDecodedHeader(name, value);

        // Encode again to refer to dynamic table
        @out.Clear();
        decHeaders.Clear();

        Encode(@out, encHeaders);
        VerifyRequiredInsertCount(headersAdded);
        VerifyKnownReceivedCount(headersAdded);

        Decode(@out, decHeaders);
        Assert.Equal(headersAdded, decDynamicTable.InsertCount());
        Assert.Equal(1, decHeaders.Size());
        VerifyDecodedHeader(name, value);
    }

    private void TestDynamicTableIndexedWithBlockedStreams(ICharSequence name, ICharSequence value)
    {
        encHeaders.Add(name, value);
        Encode(@out, encHeaders);
        VerifyRequiredInsertCount(headersAdded);

        VerifyKnownReceivedCount(headersAdded - 1);
        Assert.Equal(headersAdded - 1, decDynamicTable.InsertCount());

        DrainNextSuspendedEncoderInstruction();

        Decode(@out, decHeaders);
        DrainAllSuspendedEncoderInstructions();
        Assert.Equal(headersAdded, decDynamicTable.InsertCount());
        VerifyKnownReceivedCount(headersAdded);

        Assert.Equal(1, decHeaders.Size());
        VerifyDecodedHeader(name, value);
    }

    [Fact]
    public void StaticTableOnly()
    {
        Setup(0, 0);

        encHeaders.Add(Cs(":authority"), Cs("netty.quic")); // name only
        encHeaders.Add(Cs(":path"), Cs("/")); // name & value
        encHeaders.Add(Cs(":method"), Cs("GET")); // name & value with few options per name
        encHeaders.Add(Cs(":status"), Cs("417")); // name & multiple values but value is missing
        encHeaders.Add(Cs("x-qpack-draft"), Cs("19"));

        Encode(@out, encHeaders);
        Decode(@out, decHeaders);

        Assert.Equal(5, decHeaders.Size());
        VerifyDecodedHeader(Cs(":authority"), Cs("netty.quic"));
        VerifyDecodedHeader(Cs(":path"), Cs("/"));
        VerifyDecodedHeader(Cs(":method"), Cs("GET"));
        VerifyDecodedHeader(Cs(":status"), Cs("417"));
        VerifyDecodedHeader(Cs("x-qpack-draft"), Cs("19"));
    }

    [Fact]
    public void SectionAcknowledgmentWithoutDynamicTableThrowsQpackException()
    {
        Setup(0, 0);

        // A peer must not send a Section Acknowledgment when the dynamic table is disabled
        // (https://www.rfc-editor.org/rfc/rfc9204.html#section-4.4.1). This must be reported as a
        // QpackException rather than allowed to throw a NullPointerException.
        Assert.ThrowsAny<QpackException>(() => encoder.SectionAcknowledgment(1));
    }

    [Fact]
    public void DecoderThrowsOnInvalidInput()
    {
        Setup(0, 0);

        encHeaders.Add(Cs(":authority"), Cs("netty.quic")); // name only
        Encode(@out, encHeaders);
        // Add empty byte to the end of the buffer. This should trigger an exception in the decoder.
        @out.WriteByte(0);

        Assert.ThrowsAny<QpackException>(() => Decode(@out, decHeaders));
    }

    [Fact]
    public void SensitiveHeaderIsNotInsertedIntoDynamicTable()
    {
        sensitivityDetector = new AuthorizationSensitivityDetector();
        Setup(128, 0);

        encHeaders.Add(Cs("authorization"), Cs("Bearer secret"));
        encHeaders.Add(Cs("foo"), Cs("bar"));
        Encode(@out, encHeaders);

        // Only the non-sensitive sibling should consume a dynamic-table slot.
        VerifyRequiredInsertCount(1);
        Assert.Equal(QpackEncoderDynamicTable.NOT_FOUND,
                encDynamicTable.GetEntryIndex(Cs("authorization"), Cs("Bearer secret")),
                "Sensitive header must not be inserted into the encoder dynamic table.");
        Assert.True(encDynamicTable.GetEntryIndex(Cs("foo"), Cs("bar")) >= 0,
                "Non-sensitive header must still be inserted into the encoder dynamic table.");

        Decode(@out, decHeaders);
        Assert.Equal(1, decDynamicTable.InsertCount());
        Assert.Equal(2, decHeaders.Size());
        VerifyDecodedHeader(Cs("authorization"), Cs("Bearer secret"));
        VerifyDecodedHeader(Cs("foo"), Cs("bar"));
    }

    [Fact]
    public void SensitiveHeaderWithStaticExactMatchUsesIndexedStatic()
    {
        sensitivityDetector = IQpackSensitivityDetector.ALWAYS_SENSITIVE;
        Setup(128, 0);

        encHeaders.Add(Cs(":method"), Cs("GET"));
        Encode(@out, encHeaders);

        VerifyRequiredInsertCount(0);
        Assert.Equal(0, encDynamicTable.InsertCount(),
                "No dynamic-table insert expected for sensitive static exact match.");

        // First literal byte (byte 2) is an indexed-field-line with T=1.
        Assert.Equal(0, (int) @out.GetByte(0), "Section prefix required_insert_count byte must be 0.");
        Assert.Equal(0, (int) @out.GetByte(1), "Section prefix delta_base byte must be 0.");
        int firstLiteralByte = @out.GetByte(2) & 0xFF;
        Assert.Equal(0b1100_0000, firstLiteralByte & 0b1100_0000,
                "Indexed-static-reference literal must start with 0b11 prefix.");

        Decode(@out, decHeaders);
        Assert.Equal(0, decDynamicTable.InsertCount());
        Assert.Equal(1, decHeaders.Size());
        VerifyDecodedHeader(Cs(":method"), Cs("GET"));
    }

    [Fact]
    public void SensitiveHeaderWithStaticNameRefSetsNeverIndexBit()
    {
        sensitivityDetector = IQpackSensitivityDetector.ALWAYS_SENSITIVE;
        Setup(128, 0);

        encHeaders.Add(Cs(":authority"), Cs("netty.quic"));
        Encode(@out, encHeaders);

        VerifyRequiredInsertCount(0);
        Assert.Equal(0, encDynamicTable.InsertCount(), "No dynamic-table insert expected for sensitive header.");

        // Layout 0 1 N T NameIndex(4+); N is bit 5, T is bit 4 (=1 for static).
        int firstLiteralByte = @out.GetByte(2) & 0xFF;
        Assert.Equal(0b0100_0000, firstLiteralByte & 0b1100_0000,
                "High two bits must be 01 for literal-with-name-ref.");
        Assert.Equal(0b0010_0000, firstLiteralByte & 0b0010_0000,
                "Never-Indexed (N) bit must be set for sensitive header.");
        Assert.Equal(0b0001_0000, firstLiteralByte & 0b0001_0000,
                "T bit must be 1 (static table).");

        Decode(@out, decHeaders);
        Assert.Equal(0, decDynamicTable.InsertCount());
        Assert.Equal(1, decHeaders.Size());
        VerifyDecodedHeader(Cs(":authority"), Cs("netty.quic"));
    }

    [Fact]
    public void SensitiveHeaderWithLiteralNameSetsNeverIndexBit()
    {
        sensitivityDetector = IQpackSensitivityDetector.ALWAYS_SENSITIVE;
        Setup(128, 0);

        encHeaders.Add(Cs("x-secret"), Cs("shhh"));
        Encode(@out, encHeaders);

        VerifyRequiredInsertCount(0);
        Assert.Equal(0, encDynamicTable.InsertCount(), "No dynamic-table insert expected for sensitive header.");

        // Layout 0 0 1 N H NameLen(3+); N is bit 4, H is bit 3 (=1, Huffman).
        int firstLiteralByte = @out.GetByte(2) & 0xFF;
        Assert.Equal(0b0010_0000, firstLiteralByte & 0b1110_0000,
                "High three bits must be 001 for literal-with-literal-name.");
        Assert.Equal(0b0001_0000, firstLiteralByte & 0b0001_0000,
                "Never-Indexed (N) bit must be set for sensitive header.");

        Decode(@out, decHeaders);
        Assert.Equal(0, decDynamicTable.InsertCount());
        Assert.Equal(1, decHeaders.Size());
        VerifyDecodedHeader(Cs("x-secret"), Cs("shhh"));
    }

    private void ResetState()
    {
        @out.Clear();
        encHeaders.Clear();
        decHeaders.Clear();
    }

    private void Encode(ByteBuf buf, IHttp3Headers headers)
    {
        encoder.EncodeHeaders(attributes, buf, UnpooledByteBufAllocator.Default, 1, headers);
        Assert.True(parent.IsActive, "Parent channel closed.");
    }

    private void Decode(ByteBuf buf, IHttp3Headers headers)
    {
        decoder.Decode(attributes, 1, buf, buf.ReadableBytes,
                new Http3HeadersSink(headers, 1024, false, false).Accept, () =>
                {
                    try
                    {
                        decoder.Decode(attributes, 1, buf, buf.ReadableBytes,
                                new Http3HeadersSink(headers, 1024, false, false).Accept,
                                () => throw new InvalidOperationException("Decode resumption suspended."));
                    }
                    catch (QpackException e)
                    {
                        throw new XunitException("Decode failed.", e);
                    }
                });
        Assert.True(parent.IsActive, "Parent channel closed.");
    }

    private void VerifyDecodedHeader(ICharSequence name, ICharSequence value)
    {
        Assert.Equal<ICharSequence>(new AsciiString(value), decHeaders.Get(name));
    }

    private void DrainAllSuspendedEncoderInstructions()
    {
        Action next;
        for (next = Poll(suspendedEncoderInstructions); next != null; next = Poll(suspendedEncoderInstructions))
        {
            next();
        }
    }

    private void DrainNextSuspendedEncoderInstruction()
    {
        Action next = Poll(suspendedEncoderInstructions);
        Assert.NotNull(next); // dynamic table size instruction
        next();
    }

    // CLR: BlockingQueue.poll() (see the type comment): the head of the queue, or null when it is empty.
    private static Action Poll(ConcurrentQueue<Action> queue)
    {
        return queue.TryDequeue(out Action next) ? next : null;
    }

    private void Setup(long dynamicTableSize, int maxBlockedStreams)
    {
        Setup(dynamicTableSize, maxBlockedStreams, 10);
    }

    private void Setup(long maxTableCapacity, int maxBlockedStreams, int expectedTableFreePercentage)
    {
        attributes = new QpackAttributes(parent, false);
        Http3.SetQpackAttributes(parent, attributes);
        maxEntries = checked((int) QpackUtil.MaxEntries(maxTableCapacity));
        DefaultHttp3SettingsFrame localSettingsFrame = new DefaultHttp3SettingsFrame();
        localSettingsFrame.Settings().Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY.Id(),
                maxTableCapacity);
        localSettingsFrame.Settings().Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS.Id(),
                (long) maxBlockedStreams);
        if (maxBlockedStreams > 0)
        {
            // section acknowledgment will implicitly ack insert count.
            stateSyncStrategyAckNextInsert = false;
        }
        syncStrategy.entryAddedAnswer = __ => stateSyncStrategyAckNextInsert;
        encDynamicTable = new QpackEncoderDynamicTable(16, expectedTableFreePercentage);
        decDynamicTable = new QpackDecoderDynamicTable();
        decoder = new QpackDecoder(maxTableCapacity, maxBlockedStreams, decDynamicTable, syncStrategy);
        encoder = new QpackEncoder(encDynamicTable, sensitivityDetector);
        if (maxBlockedStreams > 0)
        {
            suspendedEncoderInstructions = new ConcurrentQueue<Action>();
        }
        EmbeddedQuicStreamChannel encoderStream = (EmbeddedQuicStreamChannel) parent.CreateStreamAsync(
                QuicStreamType.UNIDIRECTIONAL,
                new ForwardWriteToReadOnOtherHandler(new QpackEncoderHandler(maxTableCapacity, decoder),
                        suspendedEncoderInstructions)).GetAwaiter().GetResult();
        EmbeddedQuicStreamChannel decoderStream = (EmbeddedQuicStreamChannel) parent.CreateStreamAsync(
                QuicStreamType.UNIDIRECTIONAL,
                new ForwardWriteToReadOnOtherHandler(new QpackDecoderHandler(encoder))).GetAwaiter().GetResult();
        attributes.EncoderStream(encoderStream);
        attributes.DecoderStream(decoderStream);
        encoder.ConfigureDynamicTable(attributes, maxTableCapacity, maxBlockedStreams);
    }

    private void AddEncodeHeader(string namePrefix, string value, int times)
    {
        for (int i = 0; i < times; i++)
        {
            encHeaders.Add(Cs(namePrefix + i), Cs(value));
        }
    }

    private void VerifyDecodedHeaders(string namePrefix, string value, int times)
    {
        for (int i = 0; i < times; i++)
        {
            VerifyDecodedHeader(Cs(namePrefix + i), Cs(value));
        }
    }

    private void VerifyRequiredInsertCount(int insertCount)
    {
        Assert.Equal(insertCount == 0 ? 0 : insertCount % (2 * maxEntries) + 1,
                encDynamicTable.EncodedRequiredInsertCount(encDynamicTable.InsertCount()),
                "Unexpected dynamic table insert count.");
    }

    private void VerifyKnownReceivedCount(int receivedCount)
    {
        Assert.Equal(receivedCount == 0 ? 0 : receivedCount % (2 * maxEntries) + 1,
                encDynamicTable.EncodedKnownReceivedCount(), "Unexpected dynamic table known received count.");
    }

    // CLR: mock(QpackDecoderStateSyncStrategy.class) with its stubbing (see the type comment).
    private sealed class MockQpackDecoderStateSyncStrategy : IQpackDecoderStateSyncStrategy
    {
        internal Func<int, bool> entryAddedAnswer;

        public void SectionAcknowledged(int requiredInsertCount)
        {
        }

        public bool EntryAdded(int insertCount)
        {
            return entryAddedAnswer != null && entryAddedAnswer(insertCount);
        }
    }

    // CLR: the lambda (name, value) -> "authorization".contentEquals(name) of
    // sensitiveHeaderIsNotInsertedIntoDynamicTable.
    private sealed class AuthorizationSensitivityDetector : IQpackSensitivityDetector
    {
        public bool IsSensitive(ICharSequence name, ICharSequence value)
        {
            return string.Equals("authorization", name.ToString(), StringComparison.Ordinal);
        }
    }

    private sealed class ForwardWriteToReadOnOtherHandler : ChannelOutboundHandlerAdapter
    {

        private readonly IChannelInboundHandler other;
        private readonly ConcurrentQueue<Action> suspendQueue;

        internal ForwardWriteToReadOnOtherHandler(IChannelInboundHandler other) : this(other, null)
        {
        }

        internal ForwardWriteToReadOnOtherHandler(IChannelInboundHandler other,
                                                  ConcurrentQueue<Action> suspendQueue)
        {
            this.other = other;
            this.suspendQueue = suspendQueue;
        }

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            if (msg is ByteBuf)
            {
                if (suspendQueue != null)
                {
                    suspendQueue.Enqueue(() =>
                    {
                        other.ChannelRead(ctx, msg);
                    });
                }
                else
                {
                    other.ChannelRead(ctx, msg);
                }
                return new TaskCompletionSource().Task;
            }
            else
            {
                return base.WriteAsync(ctx, msg, cancellationToken);
            }
        }
    }
}
