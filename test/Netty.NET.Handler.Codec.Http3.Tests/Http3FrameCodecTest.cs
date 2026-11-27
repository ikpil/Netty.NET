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
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using Xunit.Sdk;
using Assert = XunitAssertMessages.AssertM;
using static Netty.NET.Handler.Codec.Http3.Tests.Http3TestUtils;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: @AfterEach tearDown() is Dispose() (xUnit creates a test class instance per test case and disposes it after the
// case). The @ParameterizedTest methods with @MethodSource("data") and @MethodSource("dataNoFragment") are [Theory]s with
// [MemberData(nameof(Data))] and [MemberData(nameof(DataNoFragment))] over public static TheoryData<bool, int, bool> and
// TheoryData<int, bool> with the same rows in the same order (Collection<Object[]> and Arrays.asList); the display-name
// patterns have no xUnit counterpart. The anonymous ChannelInitializer<QuicStreamChannel> classes of setUp and
// newValidatedRequestStream are the private nested classes CodecChannelInitializer and ValidatedRequestStreamInitializer
// over ChannelInitializer<IQuicStreamChannel>; like the anonymous classes they read the test's decoder, encoder and
// qpackAttributes when the channel is initialized. The NonStandardHttp3SettingsValidator lambda (id, v) -> false is the
// private nested class NoNonStandardSettings, and the GenericFutureListener of whenEncoderStreamAvailable an
// Action<Task<IQuicStreamChannel>> (future.isSuccess() is IsCompletedSuccessfully). ch.pipeline().addLast(executor,
// frameCodec) is AddLast(executor, null, frameCodec): the @Deprecated addLast(EventExecutorGroup, ChannelHandler...),
// whose @deprecated text names addLast(ChannelHandler...), is not ported in Transport (IChannelPipeline.cs), and it adds
// each handler with addLast(group, null, handler). The project has no Moq (docs/codec-http3-porting.md), so
// mock(Http3UnknownFrame.class) and mock(Http3SettingsFrame.class) with their stubbing are the private nested classes
// MockHttp3UnknownFrame and MockHttp3SettingsFrame with Mockito's default answers for the members that are not stubbed
// (null, false, 0); verify(frame, times(1)).release() is Assert.Equal(1, frame.releaseCount) over the calls of
// Release(). Collections.singletonMap(key, 0L).entrySet().iterator() is the enumerator of a one-entry
// Dictionary<long, long>. The handler and QPACK types are internal (package-private in Java) and reached through
// InternalsVisibleTo; Http3FrameTypeValidator.NO_VALIDATION is IHttp3FrameTypeValidator.NO_VALIDATION, the static
// imports of Http3CodecUtils and QpackUtil are qualified, and QpackSensitivityDetector is the interface
// IQpackSensitivityDetector. parent.createStream(...).get() is the result of the completed Task of CreateStreamAsync
// (GetAwaiter().GetResult()). assertThrows(T.class, ...) is Assert.ThrowsAny<T>. JUnit's failed assertions throw an
// Error that catch (Exception e) does not catch, while xUnit's assertion exceptions are Exceptions, so the catch of
// testFrameEncodedAndDecoded is catch (Exception e) when e is not an IAssertionException, and "throw e" is "throw".
// ThreadLocalRandom.current() is Random.Shared (nextBytes is NextBytes, nextInt(bound) Next(bound)). DefaultEventExecutor
// is Common's DefaultEventExecutor: submit(task).syncUninterruptibly() is an await of SubmitAsync, execute is Execute,
// pendingTasks() PendingTasks() and shutdownGracefully(0, 0, MILLISECONDS).syncUninterruptibly() is
// ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero) awaited by GetAwaiter().GetResult(). Semaphore(0) is a
// SemaphoreSlim(0) (release() is Release(), acquireUninterruptibly() Wait()), CountDownLatch a CountdownEvent (await(5,
// SECONDS) is Wait(TimeSpan, CancellationToken)), and CompositeByteBuf.addComponent(true, buffer) is AddComponent(buffer,
// true). The test methods that block on a future (testReadResumptionUsesHandlerExecutor,
// testClosingBlockedStreamReleasesBlockedStream) are async Task methods that await it with the test's cancellation token
// (xUnit1031, xUnit1051). Integer.MAX_VALUE is int.MaxValue; readInbound()/readOutbound() are
// ReadInbound<T>()/ReadOutbound<T>(), refCnt() the RefCnt property, isActive() IsActive, streamId() StreamId(), and
// pipeline().firstContext() the Pipeline.FirstContext property. A Java String passed as a CharSequence is a
// StringCharSequence (Http3TestUtils.Cs). The throws clauses have no counterpart.
public class Http3FrameCodecTest : IDisposable
{
    private const int MAX_HEADER_SIZE = 1024;
    private QpackEncoder encoder;
    private QpackDecoder decoder;
    private EmbeddedQuicStreamChannel encoderStream;
    private EmbeddedQuicStreamChannel decoderStream;
    private EmbeddedQuicChannel parent;
    private QpackEncoderHandler qpackEncoderHandler;
    private QpackDecoderHandler qpackDecoderHandler;
    private QpackAttributes qpackAttributes;
    private long maxTableCapacity;
    private DefaultEventExecutor codecExecutor;
    private SemaphoreSlim unblockCodecExecutor;

    public static TheoryData<bool, int, bool> Data()
    {
        return new TheoryData<bool, int, bool>
        {
                { true, 0, false },
                { true, 0, true },
                { true, 100, false },
                { true, 100, true },
                { false, 0, false },
                { false, 0, true },
                { false, 100, false },
                { false, 100, true }
        };
    }

    public static TheoryData<int, bool> DataNoFragment()
    {
        return new TheoryData<int, bool>
        {
                { 0, false },
                { 0, true },
                { 100, false },
                { 100, true }
        };
    }

    private EmbeddedQuicStreamChannel codecChannel;

    private void SetUp(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams, int.MaxValue);
    }

    private void SetUp(int maxBlockedStreams, bool delayQpackStreams, int maxUnknownFramePayloadLength)
    {
        SetUp(maxBlockedStreams, delayQpackStreams, maxUnknownFramePayloadLength, null);
    }

    private void SetUp(int maxBlockedStreams, bool delayQpackStreams, int maxUnknownFramePayloadLength,
                       IEventExecutorGroup executor)
    {
        parent = new EmbeddedQuicChannel(true);
        qpackAttributes = new QpackAttributes(parent, false);
        Http3.SetQpackAttributes(parent, qpackAttributes);
        IHttp3SettingsFrame http3SettingsFrame = new DefaultHttp3SettingsFrame();
        maxTableCapacity = 1024L;
        http3SettingsFrame.Settings().Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY.Id(),
                maxTableCapacity);
        http3SettingsFrame.Settings().Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS.Id(),
                (long) maxBlockedStreams);
        decoder = new QpackDecoder(maxTableCapacity, maxBlockedStreams);
        decoder.SetDynamicTableCapacity(maxTableCapacity);
        qpackEncoderHandler = new QpackEncoderHandler(maxTableCapacity, decoder);
        encoderStream = (EmbeddedQuicStreamChannel) parent.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL,
                new ChannelOutboundHandlerAdapter()).GetAwaiter().GetResult();
        encoder = new QpackEncoder(IQpackSensitivityDetector.NEVER_SENSITIVE);
        qpackDecoderHandler = new QpackDecoderHandler(encoder);
        decoderStream = (EmbeddedQuicStreamChannel) parent.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL,
                new ChannelOutboundHandlerAdapter()).GetAwaiter().GetResult();
        qpackAttributes.WhenEncoderStreamAvailable(future =>
        {
            if (future.IsCompletedSuccessfully)
            {
                encoder.ConfigureDynamicTable(qpackAttributes, maxTableCapacity, maxBlockedStreams);
            }
        });
        if (!delayQpackStreams)
        {
            SetQpackStreams();
        }
        codecChannel = (EmbeddedQuicStreamChannel) parent.CreateStreamAsync(QuicStreamType.BIDIRECTIONAL,
                new CodecChannelInitializer(this, maxUnknownFramePayloadLength, executor)).GetAwaiter().GetResult();
    }

    // CLR: the anonymous ChannelInitializer of setUp.
    private sealed class CodecChannelInitializer : ChannelInitializer<IQuicStreamChannel>
    {
        private readonly Http3FrameCodecTest test;
        private readonly int maxUnknownFramePayloadLength;
        private readonly IEventExecutorGroup executor;

        internal CodecChannelInitializer(Http3FrameCodecTest test, int maxUnknownFramePayloadLength,
                                         IEventExecutorGroup executor)
        {
            this.test = test;
            this.maxUnknownFramePayloadLength = maxUnknownFramePayloadLength;
            this.executor = executor;
        }

        protected override void InitChannel(IQuicStreamChannel ch)
        {
            Http3RequestStreamEncodeStateValidator encStateValidator =
                    new Http3RequestStreamEncodeStateValidator();
            Http3RequestStreamDecodeStateValidator decStateValidator =
                    new Http3RequestStreamDecodeStateValidator();
            Http3FrameCodec frameCodec = new Http3FrameCodec(IHttp3FrameTypeValidator.NO_VALIDATION,
                    test.decoder, MAX_HEADER_SIZE, maxUnknownFramePayloadLength, test.encoder,
                    encStateValidator, decStateValidator, new NoNonStandardSettings());
            if (executor == null)
            {
                ch.Pipeline.AddLast(frameCodec);
            }
            else
            {
                ch.Pipeline.AddLast(executor, null, frameCodec);
            }
            ch.Pipeline.AddLast(encStateValidator);
            ch.Pipeline.AddLast(decStateValidator);
        }
    }

    // CLR: the NonStandardHttp3SettingsValidator lambda (id, v) -> false of setUp and newValidatedRequestStream.
    private sealed class NoNonStandardSettings : Http3Settings.INonStandardHttp3SettingsValidator
    {
        public bool Validate(long id, long value)
        {
            return false;
        }
    }

    private void SetQpackStreams()
    {
        SetQpackEncoderStream();
        SetQpackDecoderStream();
    }

    private void SetQpackEncoderStream()
    {
        qpackAttributes.EncoderStream(encoderStream);
        object written = encoderStream.ReadOutbound();
        Assert.NotNull(written);
        ReferenceCountUtil.Release(written);
    }

    private void SetQpackDecoderStream()
    {
        qpackAttributes.DecoderStream(decoderStream);
    }

    public void Dispose()
    {
        TearDown();
    }

    private void TearDown()
    {
        if (unblockCodecExecutor != null)
        {
            unblockCodecExecutor.Release();
        }
        Assert.False(codecChannel.Finish());
        Assert.False(decoderStream.Finish());
        Assert.False(encoderStream.Finish());
        Assert.False(parent.Finish());
        if (codecExecutor != null)
        {
            codecExecutor.ShutdownGracefullyAsync(TimeSpan.Zero, TimeSpan.Zero).GetAwaiter().GetResult();
        }
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3CancelPushFrame_63(bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestFrameEncodedAndDecoded(
                fragmented, maxBlockedStreams, delayQpackStreams, new DefaultHttp3CancelPushFrame(63));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3CancelPushFrame_16383(bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestFrameEncodedAndDecoded(
                fragmented, maxBlockedStreams, delayQpackStreams, new DefaultHttp3CancelPushFrame(16383));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3CancelPushFrame_1073741823(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestFrameEncodedAndDecoded(
                fragmented, maxBlockedStreams, delayQpackStreams, new DefaultHttp3CancelPushFrame(1073741823));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3CancelPushFrame_4611686018427387903(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestFrameEncodedAndDecoded(fragmented, maxBlockedStreams, delayQpackStreams,
                new DefaultHttp3CancelPushFrame(4611686018427387903L));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3DataFrame(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        byte[] bytes = new byte[1024];
        Random.Shared.NextBytes(bytes);
        DefaultHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame(new DefaultHttp3Headers());
        AddRequestHeaders(headersFrame.Headers());
        TestFrameEncodedAndDecoded(fragmented, maxBlockedStreams, delayQpackStreams, headersFrame);
        TestFrameEncodedAndDecoded(fragmented, maxBlockedStreams, delayQpackStreams,
                new DefaultHttp3DataFrame(Unpooled.WrappedBuffer(bytes)));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3GoAwayFrame_63(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestFrameEncodedAndDecoded(
                fragmented, maxBlockedStreams, delayQpackStreams, new DefaultHttp3GoAwayFrame(63));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3GoAwayFrame_16383(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestFrameEncodedAndDecoded(
                fragmented, maxBlockedStreams, delayQpackStreams, new DefaultHttp3GoAwayFrame(16383));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3GoAwayFrame_1073741823(bool fragmented,
                                                int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestFrameEncodedAndDecoded(
                fragmented, maxBlockedStreams, delayQpackStreams, new DefaultHttp3GoAwayFrame(1073741823));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3MaxPushIdFrame_63(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestFrameEncodedAndDecoded(
                fragmented, maxBlockedStreams, delayQpackStreams, new DefaultHttp3MaxPushIdFrame(63));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3MaxPushIdFrame_16383(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestFrameEncodedAndDecoded(
                fragmented, maxBlockedStreams, delayQpackStreams, new DefaultHttp3MaxPushIdFrame(16383));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3MaxPushIdFrame_1073741823(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestFrameEncodedAndDecoded(
                fragmented, maxBlockedStreams, delayQpackStreams, new DefaultHttp3MaxPushIdFrame(1073741823));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3SettingsFrame(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        IHttp3SettingsFrame settingsFrame = new DefaultHttp3SettingsFrame();
        settingsFrame.Settings().Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_MAX_TABLE_CAPACITY.Id(), 100L);
        settingsFrame.Settings().Put(Http3SettingIdentifier.HTTP3_SETTINGS_QPACK_BLOCKED_STREAMS.Id(), 1L);
        settingsFrame.Settings().Put(Http3SettingIdentifier.HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE.Id(), 128L);
        settingsFrame.Settings().Put(Http3SettingIdentifier.HTTP3_SETTINGS_ENABLE_CONNECT_PROTOCOL.Id(), 0L);
        settingsFrame.Settings().Put(Http3SettingIdentifier.HTTP3_SETTINGS_H3_DATAGRAM.Id(), 1L);
        // Ensure we can encode and decode all sizes correctly.
        // unknown settings id/key will be ignored
        settingsFrame.Settings().Put(63, 63L);
        settingsFrame.Settings().Put(16383, 16383L);
        settingsFrame.Settings().Put(1073741823, 1073741823L);
        settingsFrame.Settings().Put(4611686018427387903L, 4611686018427387903L);
        TestFrameEncodedAndDecoded(
                fragmented, maxBlockedStreams, delayQpackStreams, settingsFrame);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3HeadersFrame(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        IHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame();
        AddRequestHeaders(headersFrame.Headers());
        TestFrameEncodedAndDecoded(fragmented, maxBlockedStreams, delayQpackStreams, headersFrame);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3HeadersFrameWithTrailers(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        IHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame();
        AddRequestHeaders(headersFrame.Headers());
        TestFrameEncodedAndDecoded(fragmented, maxBlockedStreams, delayQpackStreams, headersFrame);

        DefaultHttp3HeadersFrame trailers = new DefaultHttp3HeadersFrame();
        // add an extra header to block decoding if dynamic table enabled.
        trailers.Headers().Add(Cs("foo"), Cs("bar"));
        TestFrameEncodedAndDecoded(fragmented, maxBlockedStreams, delayQpackStreams, trailers);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3HeadersFrameWithInvalidTrailers(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        IHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame();
        AddRequestHeaders(headersFrame.Headers());
        TestFrameEncodedAndDecoded(fragmented, maxBlockedStreams, delayQpackStreams, headersFrame);

        DefaultHttp3HeadersFrame trailer = new DefaultHttp3HeadersFrame();
        trailer.Headers().Add(Cs(":method"), Cs("GET"));
        Assert.ThrowsAny<Http3HeadersValidationException>(
                () => TestFrameEncodedAndDecoded(fragmented, maxBlockedStreams, delayQpackStreams, trailer));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3PushPromiseFrame(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        IHttp3PushPromiseFrame pushPromiseFrame = new DefaultHttp3PushPromiseFrame(9);
        AddRequestHeaders(pushPromiseFrame.Headers());
        TestFrameEncodedAndDecoded(fragmented, maxBlockedStreams, delayQpackStreams, pushPromiseFrame);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestMultipleHttp3PushPromiseFrame(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        IHttp3PushPromiseFrame pushPromiseFrame = new DefaultHttp3PushPromiseFrame(9);
        AddRequestHeaders(pushPromiseFrame.Headers());
        TestFrameEncodedAndDecoded(fragmented, maxBlockedStreams, delayQpackStreams, pushPromiseFrame);

        IHttp3PushPromiseFrame pushPromiseFrame2 = new DefaultHttp3PushPromiseFrame(10);
        AddRequestHeaders(pushPromiseFrame2.Headers());
        // add an extra header to block decoding if dynamic table enabled.
        pushPromiseFrame2.Headers().Add(Cs("foo"), Cs("bar"));
        TestFrameEncodedAndDecoded(fragmented, maxBlockedStreams, delayQpackStreams, pushPromiseFrame2);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestMultipleHttp3PushPromiseFrameWithInvalidHeaders(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        IHttp3PushPromiseFrame pushPromiseFrame = new DefaultHttp3PushPromiseFrame(9);
        AddRequestHeaders(pushPromiseFrame.Headers());
        TestFrameEncodedAndDecoded(fragmented, maxBlockedStreams, delayQpackStreams, pushPromiseFrame);

        IHttp3PushPromiseFrame pushPromiseFrame2 = new DefaultHttp3PushPromiseFrame(10);
        Assert.ThrowsAny<Http3HeadersValidationException>(
                () => TestFrameEncodedAndDecoded(fragmented, maxBlockedStreams, delayQpackStreams, pushPromiseFrame2));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3UnknownFrame(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestFrameEncodedAndDecoded(fragmented, maxBlockedStreams, delayQpackStreams,
                new DefaultHttp3UnknownFrame(Http3CodecUtils.MIN_RESERVED_FRAME_TYPE, Unpooled.Buffer().WriteLong(8)));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestHttp3UnknownFrameWithInvalidPayloadLength(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams, 127);
        Assert.ThrowsAny<Http3Exception>(() =>
                TestFrameEncodedAndDecoded(fragmented, maxBlockedStreams, delayQpackStreams,
                        new DefaultHttp3UnknownFrame(Http3CodecUtils.MIN_RESERVED_FRAME_TYPE,
                                Unpooled.Buffer().WriteZero(128))));
    }

    // Reserved types that were used in HTTP/2 and should close the connection with an error
    [Theory]
    [MemberData(nameof(Data))]
    public void TestDecodeReservedFrameType0x2(bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestDecodeReservedFrameType(fragmented, 0x2);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestDecodeReservedFrameType0x6(bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestDecodeReservedFrameType(fragmented, 0x6);
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestDecodeReservedFrameType0x8(bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestDecodeReservedFrameType(fragmented, 0x8);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestDecodeReservedFrameType0x9(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestDecodeReservedFrameType(delayQpackStreams, 0x9);
    }

    private void TestDecodeReservedFrameType(bool delayQpackStreams, long type)
    {
        ByteBuf buffer = Unpooled.Buffer();
        Http3CodecUtils.WriteVariableLengthInteger(buffer, type);

        Exception e = Assert.ThrowsAny<Exception>(() =>
        {
            Assert.False(codecChannel.WriteInbound(buffer));
            if (delayQpackStreams)
            {
                SetQpackStreams();
                codecChannel.CheckException();
            }
        });
        AssertException(Http3ErrorCode.H3_FRAME_UNEXPECTED, e);
        VerifyClose(Http3ErrorCode.H3_FRAME_UNEXPECTED, parent);
        Assert.Equal(0, buffer.RefCnt);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestEncodeReservedFrameType0x2(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestEncodeReservedFrameType(delayQpackStreams, 0x2);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestEncodeReservedFrameType0x6(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestEncodeReservedFrameType(delayQpackStreams, 0x6);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestEncodeReservedFrameType0x8(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestEncodeReservedFrameType(delayQpackStreams, 0x8);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestEncodeReservedFrameType0x9(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestEncodeReservedFrameType(delayQpackStreams, 0x9);
    }

    private void TestEncodeReservedFrameType(bool delayQpackStreams, long type)
    {
        MockHttp3UnknownFrame frame = new MockHttp3UnknownFrame(type);
        Exception e = Assert.ThrowsAny<Exception>(() =>
        {
            Assert.False(codecChannel.WriteOutbound(frame));
            if (delayQpackStreams)
            {
                SetQpackStreams();
                codecChannel.CheckException();
            }
        });
        AssertException(Http3ErrorCode.H3_FRAME_UNEXPECTED, e);
        // should have released the frame as well
        Assert.Equal(1, frame.releaseCount);
        VerifyClose(Http3ErrorCode.H3_FRAME_UNEXPECTED, parent);
    }

    // Reserved types that were used in HTTP/2 and should close the connection with an error
    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestDecodeReservedSettingsKey0x2(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestDecodeReservedSettingsKey(delayQpackStreams, 0x2);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestDecodeReservedSettingsKey0x3(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestDecodeReservedSettingsKey(delayQpackStreams, 0x3);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestDecodeReservedSettingsKey0x4(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestDecodeReservedSettingsKey(delayQpackStreams, 0x4);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestDecodeReservedSettingsKey0x5(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestDecodeReservedSettingsKey(delayQpackStreams, 0x5);
    }

    private void TestDecodeReservedSettingsKey(bool delayQpackStreams, long key)
    {
        ByteBuf buffer = Unpooled.Buffer();
        Http3CodecUtils.WriteVariableLengthInteger(buffer, Http3CodecUtils.HTTP3_SETTINGS_FRAME_TYPE);
        Http3CodecUtils.WriteVariableLengthInteger(buffer, 2);
        // Write the key and some random value... Both should be only 1 byte long each.
        Http3CodecUtils.WriteVariableLengthInteger(buffer, key);
        Http3CodecUtils.WriteVariableLengthInteger(buffer, 1);
        TestDecodeInvalidSettings(delayQpackStreams, buffer);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestDecodeSettingsWithSameKey(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        ByteBuf buffer = Unpooled.Buffer();
        Http3CodecUtils.WriteVariableLengthInteger(buffer, Http3CodecUtils.HTTP3_SETTINGS_FRAME_TYPE);
        Http3CodecUtils.WriteVariableLengthInteger(buffer, 4);
        // Write the key and some random value... Both should be only 1 byte long each.
        Http3CodecUtils.WriteVariableLengthInteger(buffer,
                Http3SettingIdentifier.HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE.Id());
        Http3CodecUtils.WriteVariableLengthInteger(buffer, 1);
        Http3CodecUtils.WriteVariableLengthInteger(buffer,
                Http3SettingIdentifier.HTTP3_SETTINGS_MAX_FIELD_SECTION_SIZE.Id());
        Http3CodecUtils.WriteVariableLengthInteger(buffer, 1);

        TestDecodeInvalidSettings(delayQpackStreams, buffer);
    }

    private void TestDecodeInvalidSettings(bool delayQpackStreams, ByteBuf buffer)
    {
        Exception e = Assert.ThrowsAny<Exception>(() =>
        {
            Assert.False(codecChannel.WriteInbound(buffer));
            if (delayQpackStreams)
            {
                SetQpackStreams();
                codecChannel.CheckException();
            }
        });
        AssertException(Http3ErrorCode.H3_SETTINGS_ERROR, e);
        VerifyClose(Http3ErrorCode.H3_SETTINGS_ERROR, parent);
        Assert.Equal(0, buffer.RefCnt);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestEncodeReservedSettingsKey0x2(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestEncodeReservedSettingsKey(delayQpackStreams, 0x2);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestEncodeReservedSettingsKey0x3(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestEncodeReservedSettingsKey(delayQpackStreams, 0x3);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestEncodeReservedSettingsKey0x4(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestEncodeReservedSettingsKey(delayQpackStreams, 0x4);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestEncodeReservedSettingsKey0x5(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestEncodeReservedSettingsKey(delayQpackStreams, 0x5);
    }

    private void TestEncodeReservedSettingsKey(bool delayQpackStreams, long key)
    {
        MockHttp3SettingsFrame frame = new MockHttp3SettingsFrame(key);
        Exception e = Assert.ThrowsAny<Exception>(() =>
        {
            Assert.False(codecChannel.WriteOutbound(frame));
            if (delayQpackStreams)
            {
                SetQpackStreams();
                codecChannel.CheckException();
            }
        });
        AssertException(Http3ErrorCode.H3_SETTINGS_ERROR, e);
        VerifyClose(Http3ErrorCode.H3_SETTINGS_ERROR, parent);
    }

    private static void AddPseudoRequestHeaders(IHttp3Headers headers)
    {
        headers.Add(Cs(":authority"), Cs("netty.quic")); // name only
        headers.Add(Cs(":path"), Cs("/")); // name & value
        headers.Add(Cs(":method"), Cs("GET")); // name & value with few options per name
        headers.Add(Cs(":scheme"), Cs("https"));
    }

    private static void AddRequestHeaders(IHttp3Headers headers)
    {
        AddPseudoRequestHeaders(headers);
        headers.Add(Cs("x-qpack-draft"), Cs("19"));
    }

    private void TestFrameEncodedAndDecoded(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams, IHttp3Frame frame)
    {
        bool isDataFrame = frame is IHttp3DataFrame;
        bool isHeaderFrame = FrameContainsHeaders(frame);

        EncodeFrame(delayQpackStreams, frame, isHeaderFrame);

        if (IsWriteBuffered(delayQpackStreams, isHeaderFrame))
        {
            SetQpackStreams();
            codecChannel.CheckException();
        }
        ByteBuf buffer = codecChannel.ReadOutbound<ByteBuf>();

        if (fragmented)
        {
            try
            {
                do
                {
                    ByteBuf slice = buffer.ReadRetainedSlice(
                            Random.Shared.Next(buffer.ReadableBytes + 1));
                    bool producedData = codecChannel.WriteInbound(slice);
                    if (!isDataFrame)
                    {
                        if (buffer.IsReadable() || (isHeaderFrame && maxBlockedStreams > 0))
                        {
                            Assert.False(producedData);
                        }
                        else
                        {
                            Assert.True(producedData);
                        }
                    }
                } while (buffer.IsReadable());
            }
            catch (Exception e) when (!(e is IAssertionException))
            {
                if (isDataFrame)
                {
                    ReferenceCountUtil.Release(frame);
                }
                throw;
            }
            finally
            {
                buffer.Release();
            }
        }
        else
        {
            if (maxBlockedStreams > 0 && isHeaderFrame)
            {
                Assert.False(codecChannel.WriteInbound(buffer));
            }
            else
            {
                Assert.True(codecChannel.WriteInbound(buffer));
            }
        }

        RelayQPACKEncoderInstructions();

        IHttp3Frame actualFrame;
        if (isDataFrame)
        {
            CompositeByteBuf composite = Unpooled.CompositeBuffer();
            for (;;)
            {
                IHttp3DataFrame dataFrame = codecChannel.ReadInbound<IHttp3DataFrame>();
                if (dataFrame == null)
                {
                    break;
                }
                composite.AddComponent(dataFrame.Content, true);
            }
            actualFrame = new DefaultHttp3DataFrame(composite);
        }
        else
        {
            actualFrame = codecChannel.ReadInbound<IHttp3Frame>();
        }
        Http3TestUtils.AssertFrameEquals(frame, actualFrame);
    }

    private bool IsWriteBuffered(bool delayQpackStreams, bool isHeaderFrame)
    {
        return delayQpackStreams && !qpackAttributes.EncoderStreamAvailable() && isHeaderFrame;
    }

    private void EncodeFrame(bool delayQpackStreams, IHttp3Frame frame, bool isHeaderFrame)
    {
        bool wroteData = codecChannel.WriteOutbound(RetainAndDuplicate(frame));
        Assert.Equal(!IsWriteBuffered(delayQpackStreams, isHeaderFrame), wroteData);
    }

    private static IHttp3Frame RetainAndDuplicate(IHttp3Frame frame)
    {
        if (frame is IByteBufHolder)
        {
            return (IHttp3Frame) ((IByteBufHolder) frame).RetainedDuplicate();
        }
        return frame;
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestMultipleFramesEncodedAndDecodedInOneBufferHeaders(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        IHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame();
        AddRequestHeaders(headersFrame.Headers());
        TestMultipleFramesEncodedAndDecodedInOneBuffer(maxBlockedStreams, delayQpackStreams, headersFrame,
                new DefaultHttp3DataFrame(Unpooled.Buffer().WriteLong(1)));
    }

    [Theory]
    [MemberData(nameof(Data))]
    public void TestMultipleFramesEncodedAndDecodedInOneBufferPushPromise(
            bool fragmented, int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        DefaultHttp3HeadersFrame headersFrame = new DefaultHttp3HeadersFrame(new DefaultHttp3Headers());
        AddPseudoRequestHeaders(headersFrame.Headers());
        TestFrameEncodedAndDecoded(fragmented, maxBlockedStreams, delayQpackStreams, headersFrame);

        IHttp3PushPromiseFrame pushPromiseFrame = new DefaultHttp3PushPromiseFrame(9);
        AddRequestHeaders(pushPromiseFrame.Headers());
        TestMultipleFramesEncodedAndDecodedInOneBuffer(maxBlockedStreams, delayQpackStreams, pushPromiseFrame,
                new DefaultHttp3DataFrame(Unpooled.Buffer().WriteLong(1)));
    }

    private void TestMultipleFramesEncodedAndDecodedInOneBuffer(
            int maxBlockedStreams, bool delayQpackStreams, IHttp3Frame first, IHttp3Frame second)
    {
        bool hasHeaderFrame = FrameContainsHeaders(first) || FrameContainsHeaders(second);
        bool writeBuffered = IsWriteBuffered(delayQpackStreams, hasHeaderFrame);

        Assert.Equal(!writeBuffered, codecChannel.WriteOutbound(RetainAndDuplicate(first)));
        Assert.Equal(!writeBuffered, codecChannel.WriteOutbound(RetainAndDuplicate(second)));

        if (writeBuffered)
        {
            SetQpackStreams();
            codecChannel.CheckException();
        }

        ByteBuf mergedBuffer = Unpooled.Buffer();
        for (;;)
        {
            ByteBuf buffer = codecChannel.ReadOutbound<ByteBuf>();
            if (buffer == null)
            {
                break;
            }
            mergedBuffer.WriteBytes(buffer);
            buffer.Release();
        }

        if (maxBlockedStreams > 0 && hasHeaderFrame)
        {
            Assert.False(codecChannel.WriteInbound(mergedBuffer));
        }
        else
        {
            Assert.True(codecChannel.WriteInbound(mergedBuffer));
        }
        RelayQPACKEncoderInstructions();

        IHttp3Frame readFrame = codecChannel.ReadInbound<IHttp3Frame>();
        Http3TestUtils.AssertFrameEquals(first, readFrame);
        readFrame = codecChannel.ReadInbound<IHttp3Frame>();
        Http3TestUtils.AssertFrameEquals(second, readFrame);

        Assert.False(codecChannel.Finish());
    }

    [Fact]
    public async Task TestReadResumptionUsesHandlerExecutor()
    {
        codecExecutor = new DefaultEventExecutor();
        unblockCodecExecutor = new SemaphoreSlim(0);
        SetUp(1, true, int.MaxValue, codecExecutor);
        await codecExecutor.SubmitAsync(() =>
        {
        }, TestContext.Current.CancellationToken);

        CountdownEvent executorBlocked = new CountdownEvent(1);
        codecExecutor.Execute(() =>
        {
            executorBlocked.Signal();
            unblockCodecExecutor.Wait();
        });
        Assert.True(executorBlocked.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(0, codecExecutor.PendingTasks());

        SetQpackDecoderStream();

        Assert.Equal(1, codecExecutor.PendingTasks());
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestInvalidHttp3MaxPushIdFrame(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestInvalidHttp3Frame0(delayQpackStreams, Http3CodecUtils.HTTP3_MAX_PUSH_ID_FRAME_TYPE,
                Http3CodecUtils.HTTP3_CANCEL_PUSH_FRAME_MAX_LEN + 1, Http3ErrorCode.H3_FRAME_ERROR);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestInvalidHttp3GoAwayFrame(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestInvalidHttp3Frame0(delayQpackStreams, Http3CodecUtils.HTTP3_GO_AWAY_FRAME_TYPE,
                Http3CodecUtils.HTTP3_GO_AWAY_FRAME_MAX_LEN + 1, Http3ErrorCode.H3_FRAME_ERROR);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestInvalidHttp3SettingsFrame(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestInvalidHttp3Frame0(delayQpackStreams, Http3CodecUtils.HTTP3_SETTINGS_FRAME_TYPE,
                Http3CodecUtils.HTTP3_SETTINGS_FRAME_MAX_LEN + 1, Http3ErrorCode.H3_EXCESSIVE_LOAD);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestInvalidHttp3CancelPushFrame(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestInvalidHttp3Frame0(delayQpackStreams, Http3CodecUtils.HTTP3_CANCEL_PUSH_FRAME_TYPE,
                Http3CodecUtils.HTTP3_CANCEL_PUSH_FRAME_MAX_LEN + 1, Http3ErrorCode.H3_FRAME_ERROR);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestInvalidHttp3HeadersFrame(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestInvalidHttp3Frame0(delayQpackStreams, Http3CodecUtils.HTTP3_HEADERS_FRAME_TYPE,
                MAX_HEADER_SIZE + 1, Http3ErrorCode.H3_EXCESSIVE_LOAD);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestInvalidHttp3PushPromiseFrame(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestInvalidHttp3Frame0(delayQpackStreams,  Http3CodecUtils.HTTP3_PUSH_PROMISE_FRAME_TYPE,
                MAX_HEADER_SIZE + 9, Http3ErrorCode.H3_EXCESSIVE_LOAD);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestInvalidHttp3CancelPushFrameIdLength(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestInvalidIdLengthFrame0(delayQpackStreams, Http3CodecUtils.HTTP3_CANCEL_PUSH_FRAME_TYPE);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestInvalidHttp3GoAwayFrameIdLength(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestInvalidIdLengthFrame0(delayQpackStreams, Http3CodecUtils.HTTP3_GO_AWAY_FRAME_TYPE);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestInvalidHttp3MaxPushIdFrameIdLength(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestInvalidIdLengthFrame0(delayQpackStreams, Http3CodecUtils.HTTP3_MAX_PUSH_ID_FRAME_TYPE);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestInvalidHttp3PushPromiseFrameIdLength(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestInvalidIdLengthFrame0(delayQpackStreams, Http3CodecUtils.HTTP3_PUSH_PROMISE_FRAME_TYPE);
    }

    // Declares a frame whose payload length is only 1 byte, but whose sole payload byte (prefix bits 11) encodes
    // a variable length integer that requires 8 bytes. Before the fix, the codec derived the integer's length
    // purely from that byte and read past the declared/available length via readLong(), throwing a raw
    // IndexOutOfBoundsException instead of failing the connection with H3_FRAME_ERROR.
    private void TestInvalidIdLengthFrame0(bool delayQpackStreams, int type)
    {
        ByteBuf buffer = Unpooled.Buffer();
        Http3CodecUtils.WriteVariableLengthInteger(buffer, type);
        Http3CodecUtils.WriteVariableLengthInteger(buffer, 1);
        buffer.WriteByte(0xC0);

        Exception e = Assert.ThrowsAny<Exception>(() =>
        {
            Assert.False(codecChannel.WriteInbound(buffer));
            if (delayQpackStreams)
            {
                SetQpackStreams();
                codecChannel.CheckException();
            }
        });
        AssertException(Http3ErrorCode.H3_FRAME_ERROR, e);
        VerifyClose(Http3ErrorCode.H3_FRAME_ERROR, parent);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestHttp3CancelPushFrameTrailingBytesNotReparsed(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestTrailingBytesNotReparsed0(delayQpackStreams, Http3CodecUtils.HTTP3_CANCEL_PUSH_FRAME_TYPE);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestHttp3GoAwayFrameTrailingBytesNotReparsed(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestTrailingBytesNotReparsed0(delayQpackStreams, Http3CodecUtils.HTTP3_GO_AWAY_FRAME_TYPE);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestHttp3MaxPushIdFrameTrailingBytesNotReparsed(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestTrailingBytesNotReparsed0(delayQpackStreams, Http3CodecUtils.HTTP3_MAX_PUSH_ID_FRAME_TYPE);
    }

    // Declares a frame whose payload length is the maximum of 8 bytes, but whose sole payload byte (prefix bits 00)
    // encodes a 1-byte variable length integer, followed by 7 trailing bytes that (if the codec incorrectly
    // considered the frame fully consumed after reading only 1 byte) would be re-parsed as the type/length/payload
    // of a bogus subsequent frame. The codec must reject the mismatched length with H3_FRAME_ERROR instead of
    // treating the frame as consumed and letting the trailing bytes desynchronize the stream.
    private void TestTrailingBytesNotReparsed0(bool delayQpackStreams, int type)
    {
        ByteBuf buffer = Unpooled.Buffer();
        Http3CodecUtils.WriteVariableLengthInteger(buffer, type);
        Http3CodecUtils.WriteVariableLengthInteger(buffer, 8);
        buffer.WriteByte(0x01);
        // Trailing bytes crafted to look like a HEADERS frame (illegal on the control stream) if the codec were
        // to treat these bytes as the start of the next frame.
        buffer.WriteBytes(new byte[] { Http3CodecUtils.HTTP3_HEADERS_FRAME_TYPE, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 });

        Exception e = Assert.ThrowsAny<Exception>(() =>
        {
            Assert.False(codecChannel.WriteInbound(buffer));
            if (delayQpackStreams)
            {
                SetQpackStreams();
                codecChannel.CheckException();
            }
        });
        AssertException(Http3ErrorCode.H3_FRAME_ERROR, e);
        VerifyClose(Http3ErrorCode.H3_FRAME_ERROR, parent);
        // The connection is in an error state, so no frame should have been produced and the trailing bytes must
        // not have been decoded as a subsequent frame.
        Assert.Null(codecChannel.ReadInbound());
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestInvalidHttp3SettingsFrameKeyLength(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        ByteBuf buffer = Unpooled.Buffer();
        Http3CodecUtils.WriteVariableLengthInteger(buffer, Http3CodecUtils.HTTP3_SETTINGS_FRAME_TYPE);
        // Declare a 2-byte payload (room for a 1-byte key + 1-byte value), but the key's own first byte
        // (prefix bits 11) encodes an 8-byte variable length integer, which does not fit in the declared length.
        Http3CodecUtils.WriteVariableLengthInteger(buffer, 2);
        buffer.WriteByte(0xC0);
        buffer.WriteByte(0x01);

        Exception e = Assert.ThrowsAny<Exception>(() =>
        {
            Assert.False(codecChannel.WriteInbound(buffer));
            if (delayQpackStreams)
            {
                SetQpackStreams();
                codecChannel.CheckException();
            }
        });
        AssertException(Http3ErrorCode.H3_FRAME_ERROR, e);
        VerifyClose(Http3ErrorCode.H3_FRAME_ERROR, parent);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestInvalidHttp3SettingsFrameValueLength(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        ByteBuf buffer = Unpooled.Buffer();
        Http3CodecUtils.WriteVariableLengthInteger(buffer, Http3CodecUtils.HTTP3_SETTINGS_FRAME_TYPE);
        // Declare a 2-byte payload; the 1-byte key consumes the first byte, leaving 1 declared byte for the
        // value, but the value's own first byte (prefix bits 11) encodes an 8-byte variable length integer.
        Http3CodecUtils.WriteVariableLengthInteger(buffer, 2);
        buffer.WriteByte(0x01);
        buffer.WriteByte(0xC0);

        Exception e = Assert.ThrowsAny<Exception>(() =>
        {
            Assert.False(codecChannel.WriteInbound(buffer));
            if (delayQpackStreams)
            {
                SetQpackStreams();
                codecChannel.CheckException();
            }
        });
        AssertException(Http3ErrorCode.H3_FRAME_ERROR, e);
        VerifyClose(Http3ErrorCode.H3_FRAME_ERROR, parent);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestSkipUnknown(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        ByteBuf buffer = Unpooled.Buffer();
        Http3CodecUtils.WriteVariableLengthInteger(buffer, 4611686018427387903L);
        Http3CodecUtils.WriteVariableLengthInteger(buffer, 10);
        buffer.WriteZero(10);

        Assert.False(codecChannel.WriteInbound(buffer));
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestSkipFragmentedUnknown(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        ByteBuf first = Unpooled.Buffer();
        Http3CodecUtils.WriteVariableLengthInteger(first, 0xa);
        Http3CodecUtils.WriteVariableLengthInteger(first, 10);
        first.WriteZero(3);

        Assert.False(codecChannel.WriteInbound(first));

        ByteBuf second = Unpooled.Buffer();
        second.WriteZero(7);

        Assert.False(codecChannel.WriteInbound(second));
        Assert.False(codecChannel.Finish());
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestInvalidHttp3CancelPushFrameZeroLength(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestInvalidHttp3Frame0(delayQpackStreams,
            Unpooled.WrappedBuffer(new byte[] { (byte) Http3CodecUtils.HTTP3_CANCEL_PUSH_FRAME_TYPE, 0 }),
            Http3ErrorCode.H3_FRAME_ERROR);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestInvalidHttp3GoAwayFrameZeroLength(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestInvalidHttp3Frame0(delayQpackStreams,
            Unpooled.WrappedBuffer(new byte[] { (byte) Http3CodecUtils.HTTP3_GO_AWAY_FRAME_TYPE, 0 }),
            Http3ErrorCode.H3_FRAME_ERROR);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestInvalidHttp3MaxPushIdFrameZeroLength(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestInvalidHttp3Frame0(delayQpackStreams,
            Unpooled.WrappedBuffer(new byte[] { (byte) Http3CodecUtils.HTTP3_MAX_PUSH_ID_FRAME_TYPE, 0 }),
            Http3ErrorCode.H3_FRAME_ERROR);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestInvalidHttp3PushPromiseFrameZeroLength(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestInvalidHttp3Frame0(delayQpackStreams,
            Unpooled.WrappedBuffer(new byte[] { (byte) Http3CodecUtils.HTTP3_PUSH_PROMISE_FRAME_TYPE, 0 }),
            Http3ErrorCode.H3_FRAME_ERROR);
    }

    [Theory]
    [MemberData(nameof(DataNoFragment))]
    public void TestInvalidHttp3SettingsFrameKeyWithoutValue(int maxBlockedStreams, bool delayQpackStreams)
    {
        SetUp(maxBlockedStreams, delayQpackStreams);
        TestInvalidHttp3Frame0(delayQpackStreams,
            Unpooled.WrappedBuffer(new byte[] { (byte) Http3CodecUtils.HTTP3_SETTINGS_FRAME_TYPE, 1, 1 }),
            Http3ErrorCode.H3_FRAME_ERROR);
    }

    [Fact]
    public async Task TestClosingBlockedStreamReleasesBlockedStream()
    {
        SetUp(1, false);
        Assert.False(codecChannel.WriteInbound(BlockingHeadersFrame()));
        await codecChannel.CloseAsync(TestContext.Current.CancellationToken);
        ByteBuf expected = Unpooled.Buffer();
        QpackUtil.EncodePrefixedInteger(expected, (byte) 0b0100_0000, 6, codecChannel.StreamId());
        ByteBuf canceled = decoderStream.ReadOutbound<ByteBuf>();
        try
        {
            Assert.Equal(expected, canceled);
        }
        finally
        {
            canceled.Release();
            expected.Release();
        }
    }

    [Fact]
    public void TestFinOnBlockedStreamReleasesBlockedStream()
    {
        SetUp(1, false);
        EmbeddedQuicStreamChannel first = NewValidatedRequestStream();
        EmbeddedQuicStreamChannel second = NewValidatedRequestStream();
        try
        {
            Assert.False(first.WriteInboundWithFin(BlockingHeadersFrame()));
            Assert.True(first.IsActive);
            Assert.False(second.WriteInbound(BlockingHeadersFrame()));
        }
        finally
        {
            first.FinishAndReleaseAll();
            second.FinishAndReleaseAll();
            decoderStream.ReleaseOutbound();
        }
    }

    private static ByteBuf BlockingHeadersFrame()
    {
        ByteBuf buffer = Unpooled.Buffer();
        Http3CodecUtils.WriteVariableLengthInteger(buffer, Http3CodecUtils.HTTP3_HEADERS_FRAME_TYPE);
        Http3CodecUtils.WriteVariableLengthInteger(buffer, 3);
        buffer.WriteByte(0x02);
        buffer.WriteByte(0x00);
        buffer.WriteByte(0x80);
        return buffer;
    }

    private EmbeddedQuicStreamChannel NewValidatedRequestStream()
    {
        return (EmbeddedQuicStreamChannel) parent.CreateStreamAsync(QuicStreamType.BIDIRECTIONAL,
            new ValidatedRequestStreamInitializer(this)).GetAwaiter().GetResult();
    }

    // CLR: the anonymous ChannelInitializer of newValidatedRequestStream().
    private sealed class ValidatedRequestStreamInitializer : ChannelInitializer<IQuicStreamChannel>
    {
        private readonly Http3FrameCodecTest test;

        internal ValidatedRequestStreamInitializer(Http3FrameCodecTest test)
        {
            this.test = test;
        }

        protected override void InitChannel(IQuicStreamChannel ch)
        {
            Http3RequestStreamEncodeStateValidator encStateValidator =
                new Http3RequestStreamEncodeStateValidator();
            Http3RequestStreamDecodeStateValidator decStateValidator =
                new Http3RequestStreamDecodeStateValidator();
            ch.Pipeline.AddLast(new Http3FrameCodec(IHttp3FrameTypeValidator.NO_VALIDATION,
                test.decoder, MAX_HEADER_SIZE, int.MaxValue, test.encoder,
                encStateValidator, decStateValidator, new NoNonStandardSettings()));
            ch.Pipeline.AddLast(encStateValidator);
            ch.Pipeline.AddLast(decStateValidator);
            ch.Pipeline.AddLast(Http3RequestStreamValidationHandler.NewServerValidator(
                test.qpackAttributes, test.decoder, encStateValidator, decStateValidator));
        }
    }

    private void TestInvalidHttp3Frame0(bool delayQpackStreams, int type, int length, Http3ErrorCode code)
    {
        ByteBuf buffer = Unpooled.Buffer();
        Http3CodecUtils.WriteVariableLengthInteger(buffer, type);
        Http3CodecUtils.WriteVariableLengthInteger(buffer, length);
        TestInvalidHttp3Frame0(delayQpackStreams, buffer, code);
    }

    private void TestInvalidHttp3Frame0(bool delayQpackStreams, ByteBuf buffer, Http3ErrorCode code)
    {
        Exception e = Assert.ThrowsAny<Exception>(() =>
        {
            Assert.False(codecChannel.WriteInbound(buffer));
            if (delayQpackStreams)
            {
                SetQpackStreams();
                codecChannel.CheckException();
            }
        });
        AssertException(code, e);
        VerifyClose(code, parent);
    }

    private void RelayQPACKEncoderInstructions()
    {
        object msg;
        while ((msg = encoderStream.ReadOutbound()) != null)
        {
            qpackEncoderHandler.ChannelRead(encoderStream.Pipeline.FirstContext, msg);
        }
        while ((msg = decoderStream.ReadOutbound()) != null)
        {
            qpackDecoderHandler.ChannelRead(decoderStream.Pipeline.FirstContext, msg);
        }
    }

    private static bool FrameContainsHeaders(IHttp3Frame frame)
    {
        return frame is IHttp3HeadersFrame || frame is IHttp3PushPromiseFrame;
    }

    // CLR: mock(Http3UnknownFrame.class) with when(frame.type()).thenReturn(type), when(frame.touch()).thenReturn(frame)
    // and when(frame.touch(any())).thenReturn(frame); the other members keep Mockito's default answers, and Release()
    // counts its calls for verify(frame, times(1)).release() (see the class comment).
    private sealed class MockHttp3UnknownFrame : IHttp3UnknownFrame
    {
        private readonly long type;
        internal int releaseCount;

        internal MockHttp3UnknownFrame(long type)
        {
            this.type = type;
        }

        public long Type()
        {
            return type;
        }

        public long Length()
        {
            return 0;
        }

        public ByteBuf Content => null;

        public int RefCnt => 0;

        public IByteBufHolder Copy()
        {
            return null;
        }

        public IByteBufHolder Duplicate()
        {
            return null;
        }

        public IByteBufHolder RetainedDuplicate()
        {
            return null;
        }

        public IByteBufHolder Replace(ByteBuf content)
        {
            return null;
        }

        public IByteBufHolder Retain()
        {
            return null;
        }

        public IByteBufHolder Retain(int increment)
        {
            return null;
        }

        public IByteBufHolder Touch()
        {
            return this;
        }

        public IByteBufHolder Touch(object hint)
        {
            return this;
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

        public bool Release()
        {
            releaseCount++;
            return false;
        }

        public bool Release(int decrement)
        {
            return false;
        }
    }

    // CLR: mock(Http3SettingsFrame.class) with when(frame.iterator()).thenReturn(Collections.singletonMap(key, 0L)
    // .entrySet().iterator()); the other members keep Mockito's default answers (see the class comment).
    private sealed class MockHttp3SettingsFrame : IHttp3SettingsFrame
    {
        private readonly IEnumerator<KeyValuePair<long, long>> iterator;

        internal MockHttp3SettingsFrame(long key)
        {
            iterator = new Dictionary<long, long> { { key, 0L } }.GetEnumerator();
        }

        public long Type()
        {
            return 0;
        }

        public Http3Settings Settings()
        {
            return null;
        }

        public long? Put(long key, long value)
        {
            return null;
        }

        public IEnumerator<KeyValuePair<long, long>> GetEnumerator()
        {
            return iterator;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
