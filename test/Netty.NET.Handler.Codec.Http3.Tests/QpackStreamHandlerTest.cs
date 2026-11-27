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

using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Quic;
using Assert = XunitAssertMessages.AssertM;
using static Netty.NET.Handler.Codec.Http3.Tests.Http3TestUtils;

namespace Netty.NET.Handler.Codec.Http3.Tests;

// CLR: parent.createStream(...).get() and parent.close().get() are awaits of CreateStreamAsync and CloseAsync with
// TestContext.Current.CancellationToken, so the test methods are async Task. QpackDecoderHandler and QpackEncoder are
// internal (package-private in Java) and reached through InternalsVisibleTo; QpackSensitivityDetector is the interface
// IQpackSensitivityDetector. refCnt() is the RefCnt property. The throws clauses have no counterpart.
public class QpackStreamHandlerTest
{

    [Fact]
    public async Task TestStreamClosedWhileParentStillActive()
    {
        EmbeddedQuicChannel parent = new EmbeddedQuicChannel(true);

        EmbeddedQuicStreamChannel channel =
                (EmbeddedQuicStreamChannel) await parent.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL,
                        new QpackDecoderHandler(new QpackEncoder(IQpackSensitivityDetector.NEVER_SENSITIVE)),
                        TestContext.Current.CancellationToken);
        Assert.False(channel.Finish());
        VerifyClose(1, Http3ErrorCode.H3_CLOSED_CRITICAL_STREAM, parent);
    }

    [Fact]
    public async Task TestStreamClosedWhileParentIsInactive()
    {
        EmbeddedQuicChannel parent = new EmbeddedQuicChannel(true);
        await parent.CloseAsync(TestContext.Current.CancellationToken);

        EmbeddedQuicStreamChannel channel =
                (EmbeddedQuicStreamChannel) await parent.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL,
                        new QpackDecoderHandler(new QpackEncoder(IQpackSensitivityDetector.NEVER_SENSITIVE)),
                        TestContext.Current.CancellationToken);
        Assert.False(channel.Finish());
    }

    [Fact]
    public async Task TestStreamDropsInboundData()
    {
        EmbeddedQuicChannel parent = new EmbeddedQuicChannel(true);
        await parent.CloseAsync(TestContext.Current.CancellationToken);

        EmbeddedQuicStreamChannel channel =
                (EmbeddedQuicStreamChannel) await parent.CreateStreamAsync(QuicStreamType.UNIDIRECTIONAL,
                        new QpackDecoderHandler(new QpackEncoder(IQpackSensitivityDetector.NEVER_SENSITIVE)),
                        TestContext.Current.CancellationToken);
        ByteBuf buffer = Unpooled.Buffer();
        Assert.False(channel.WriteInbound(buffer));
        Assert.Equal(0, buffer.RefCnt);
        Assert.False(channel.Finish());
    }
}
