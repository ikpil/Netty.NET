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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;
using Netty.NET.Handler.Ssl;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: the anonymous AbstractSniHandler and ChannelInboundHandlerAdapter subclasses are named nested classes.
// ImmediateEventExecutor.INSTANCE.newSucceededFuture(value) is Task.FromResult(value), a completed Task<object>
// (docs/common-task-composition.md); RuntimeException is Exception; AtomicBoolean/AtomicReference are fields set with
// Volatile.Write/Interlocked.CompareExchange; String.format("%04x", n) is n.ToString("x4") in the invariant culture;
// e.getCause() is InnerException; assertInstanceOf is Assert.IsAssignableFrom. (byte) 3 and the handshake content type
// are the unsigned System.Byte values 3 and 22 (owner decision).
public class SslClientHelloHandlerTest
{

    // ClientHello carrying SNI hostname "chat4.leancloud.cn", borrowed from SniHandlerTest.
    private const string TLS_CLIENT_HELLO_HEX_PART1 = "16030100";
    private const string TLS_CLIENT_HELLO_HEX_PART2 =
            "c6010000c20303bb0855d66532c05a0ef784f7c384feeafa68b3" +
            "b655ac7288650d5eed4aa3fb52000038c02cc030009fcca9cca8ccaac02b" +
            "c02f009ec024c028006bc023c0270067c00ac0140039c009c0130033009d" +
            "009c003d003c0035002f00ff010000610000001700150000124348415434" +
            "2e4c45414e434c4f55442e434e000b000403000102000a000a0008001d00" +
            "170019001800230000000d0020001e060106020603050105020503040104" +
            "0204030301030203030201020202030016000000170000";

    [Fact]
    public void TestSyncLookupCallbackExceptionFiredOnPipeline()
    {
        ThrowingSniHandler handler = new ThrowingSniHandler();

        ExceptionRecorder exceptionRef = new ExceptionRecorder();
        EmbeddedChannel ch = new EmbeddedChannel(handler, exceptionRef);

        try
        {
            ch.WriteInbound(Unpooled.WrappedBuffer(StringUtil.DecodeHexDump(TLS_CLIENT_HELLO_HEX_PART1)));
            ch.WriteInbound(Unpooled.WrappedBuffer(StringUtil.DecodeHexDump(TLS_CLIENT_HELLO_HEX_PART2)));
        }
        finally
        {
            ch.FinishAndReleaseAll();
        }

        Exception cause = exceptionRef.Get();
        Assert.NotNull(cause);
        Assert.IsAssignableFrom<DecoderException>(cause);
        Assert.False(handler.NullRetryOccurred(), "Expected no select(ctx, null) retry");
    }

    [Fact]
    public void TestLeadingZeroLengthHandshakeRecordFailsClosed()
    {
        // A zero-length TLS handshake record: content type = handshake (0x16), version 3.3, length = 0.
        // It is followed by a real ClientHello, which some TLS implementations would still process.
        AssertZeroLengthHandshakeRecordFailsClosed("1603030000", TLS_CLIENT_HELLO_HEX_PART1,
                TLS_CLIENT_HELLO_HEX_PART2);
    }

    [Fact]
    public void TestInterleavedZeroLengthHandshakeRecordFailsClosed()
    {
        // Fragment the ClientHello handshake message over two records and put a zero-length handshake record
        // in between them.
        string handshake = TLS_CLIENT_HELLO_HEX_PART2.Substring(2);
        string firstFragment = handshake.Substring(0, 20);
        string secondFragment = handshake.Substring(20);
        AssertZeroLengthHandshakeRecordFailsClosed(
                "160301" + (firstFragment.Length / 2).ToString("x4", CultureInfo.InvariantCulture) + firstFragment,
                "1603030000",
                "160301" + (secondFragment.Length / 2).ToString("x4", CultureInfo.InvariantCulture) + secondFragment);
    }

    private static void AssertZeroLengthHandshakeRecordFailsClosed(params string[] chunks)
    {
        RecordingSniHandler handler = new RecordingSniHandler();
        SniEventRecorder eventRef = new SniEventRecorder();

        EmbeddedChannel ch = new EmbeddedChannel(handler, eventRef);

        try
        {
            DecoderException e = Assert.Throws<DecoderException>(() =>
            {
                foreach (string chunk in chunks)
                {
                    ch.WriteInbound(Unpooled.WrappedBuffer(StringUtil.DecodeHexDump(chunk)));
                }
            });
            Assert.IsAssignableFrom<NotSslRecordException>(e.InnerException);
            Assert.False(ch.IsActive);
        }
        finally
        {
            ch.FinishAndReleaseAll();
        }

        Assert.False(handler.LookupCalled(), "Expected no context selection");
        Assert.NotNull(eventRef.Get());
        Assert.IsAssignableFrom<NotSslRecordException>(eventRef.Get().Cause());
    }

    [Fact]
    public void TestManyZeroLengthHandshakeRecordsAreNotBuffered()
    {
        RecordingSniHandler handler = new RecordingSniHandler();
        EmbeddedChannel ch = new EmbeddedChannel(handler);

        byte[] records = new byte[5 * 200_000];

        for (int i = 0; i < records.Length; i += 5)
        {
            records[i] = SslUtils.SSL_CONTENT_TYPE_HANDSHAKE;
            records[i + 1] = 3;
            records[i + 2] = 3;
        }

        try
        {
            Assert.Throws<DecoderException>(() => ch.WriteInbound(Unpooled.WrappedBuffer(records)));
            Assert.False(ch.IsActive);
            Assert.False(handler.LookupCalled());
        }
        finally
        {
            ch.FinishAndReleaseAll();
        }
    }

    private sealed class ThrowingSniHandler : AbstractSniHandler<object>
    {
        private volatile bool nullRetryOccurred;

        internal bool NullRetryOccurred()
        {
            return nullRetryOccurred;
        }

        protected override Task<object> Lookup(IChannelHandlerContext ctx, string hostname)
        {
            if (hostname == null)
            {
                nullRetryOccurred = true;
            }

            return Task.FromResult(new object());
        }

        protected override void OnLookupComplete(IChannelHandlerContext ctx, string hostname,
                                                 Task<object> future)
        {
            throw new Exception("simulated user callback failure");
        }
    }

    private sealed class RecordingSniHandler : AbstractSniHandler<object>
    {
        private volatile bool lookupCalled;

        internal bool LookupCalled()
        {
            return lookupCalled;
        }

        protected override Task<object> Lookup(IChannelHandlerContext ctx, string hostname)
        {
            lookupCalled = true;
            return Task.FromResult(new object());
        }

        protected override void OnLookupComplete(IChannelHandlerContext ctx, string hostname,
                                                 Task<object> future)
        {
            // no-op
        }
    }

    private sealed class ExceptionRecorder : ChannelInboundHandlerAdapter
    {
        private Exception exceptionRef;

        internal Exception Get()
        {
            return Volatile.Read(ref exceptionRef);
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            Interlocked.CompareExchange(ref exceptionRef, cause, null);
        }
    }

    private sealed class SniEventRecorder : ChannelInboundHandlerAdapter
    {
        private SniCompletionEvent eventRef;

        internal SniCompletionEvent Get()
        {
            return Volatile.Read(ref eventRef);
        }

        public override void UserEventTriggered(IChannelHandlerContext ctx, object evt)
        {
            if (evt is SniCompletionEvent)
            {
                Volatile.Write(ref eventRef, (SniCompletionEvent) evt);
            }
        }
    }
}
