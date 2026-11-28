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

using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Quic;
using Netty.NET.Transport.Channels;
using Assert = XunitAssertMessages.AssertM;
using static Netty.NET.Handler.Codec.Http3.Tests.Http3TestUtils;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: the three tests of AbtractHttp3ConnectionHandlerTest run for this class too (xUnit runs inherited [Fact]s). The
// override of assertBidirectionalStreamHandled is private protected, as the abstract member. The anonymous sharable
// ChannelInboundHandlerAdapter REQUEST_HANDLER is the private nested class SharableRequestHandler, whose isSharable()
// overrides the IsSharable property; pipeline().context(handler) is Pipeline.Context(handler). QpackSensitivityDetector
// is the interface IQpackSensitivityDetector; the package-private qpackEncoder field and QpackEncoder.encodeHeaders,
// QpackAttributes and Http3CodecUtils are internal and reached through InternalsVisibleTo. A Java String passed as a
// CharSequence (Http3Headers.add) is a StringCharSequence (Http3TestUtils.Cs). UnpooledByteBufAllocator.DEFAULT is
// UnpooledByteBufAllocator.Default; getByte(int) returns the unsigned CLR byte, and only bit 4 is read, so the sign of
// Java's byte does not matter; >>> is the C# 11 unsigned right shift.
public class Http3ServerConnectionHandlerTest : AbtractHttp3ConnectionHandlerTest
{
    private static readonly IChannelHandler REQUEST_HANDLER = new SharableRequestHandler();

    // CLR: the anonymous ChannelInboundHandlerAdapter of REQUEST_HANDLER.
    private sealed class SharableRequestHandler : ChannelInboundHandlerAdapter
    {
        public override bool IsSharable => true;
    }

    public Http3ServerConnectionHandlerTest() : base(true)
    {
    }

    protected override Http3ConnectionHandler NewConnectionHandler()
    {
        return new Http3ServerConnectionHandler(REQUEST_HANDLER);
    }

    private protected override void AssertBidirectionalStreamHandled(EmbeddedQuicChannel channel,
                                                                     IQuicStreamChannel streamChannel)
    {
        Assert.NotNull(streamChannel.Pipeline.Context(REQUEST_HANDLER));
    }

    [Fact]
    public void CustomSensitivityDetectorIsForwardedToQpackEncoder()
    {
        Http3ServerConnectionHandler handler = new Http3ServerConnectionHandler(
                REQUEST_HANDLER, null, null, null,
                true, null, IQpackSensitivityDetector.ALWAYS_SENSITIVE,
                Http3CodecUtils.DEFAULT_MAX_UNKNOWN_FRAME_PAYLOAD_LENGTH);

        IHttp3Headers headers = new DefaultHttp3Headers(false);
        headers.Add(Cs("x-custom"), Cs("value"));
        ByteBuf @out = Unpooled.Buffer();

        try
        {
            handler.qpackEncoder.EncodeHeaders(new QpackAttributes(null, true), @out,
                    UnpooledByteBufAllocator.Default, 1L, headers);
            byte first = @out.GetByte(2);
            Assert.Equal(1, (first & 0b0001_0000) >>> 4,
                    "N bit must be set when ALWAYS_SENSITIVE is configured");
        }
        finally
        {
            @out.Release();
        }
    }
}
