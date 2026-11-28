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
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Stomp.Tests;

// CLR: xUnit creates a new test class instance for every test, so the @BeforeEach setUp() runs from the
// constructor and the @AfterEach tearDown() from Dispose() (https://xunit.net/docs/shared-context).
// @ParameterizedTest with @MethodSource("stompCommands") is a [Theory] with [MemberData(nameof(StompCommands))]
// over the public static data method, Arrays.asList(Object[][]) is the object[] rows and Boolean is bool;
// String.format("%s\n\n\0",...) is string.Format with the {0} placeholder in the invariant culture, and UTF_8 is
// Encoding.UTF8.
public class StompCommandDecodeTest : IDisposable
{

    private EmbeddedChannel channel;

    public StompCommandDecodeTest()
    {
        SetUp();
    }

    public void Dispose()
    {
        TearDown();
    }

    private void SetUp()
    {
        channel = new EmbeddedChannel(new StompSubframeDecoder(true));
    }

    private void TearDown()
    {
        Assert.False(channel.Finish());
    }

    [Theory]
    [MemberData(nameof(StompCommands))]
    public void TestDecodeCommand(string rawCommand, StompCommand expectedCommand, bool valid)
    {
        byte[] frameContent = Encoding.UTF8.GetBytes(string.Format(CultureInfo.InvariantCulture, "{0}\n\n\0", rawCommand));
        ByteBuf incoming = Unpooled.WrappedBuffer(frameContent);
        Assert.True(channel.WriteInbound(incoming));

        IStompHeadersSubframe frame = channel.ReadInbound<IStompHeadersSubframe>();

        Assert.NotNull(frame);
        Assert.Equal(expectedCommand, frame.Command());

        if (valid)
        {
            Assert.True(frame.DecoderResult().IsSuccess());

            IStompContentSubframe content = channel.ReadInbound<IStompContentSubframe>();
            Assert.Same(ILastStompContentSubframe.EMPTY_LAST_CONTENT, content);
            content.Release();
        }
        else
        {
            Assert.True(frame.DecoderResult().IsFailure());
            Assert.Null(channel.ReadInbound());
        }
    }

    public static IEnumerable<object[]> StompCommands()
    {
        return new List<object[]>(new object[][] {
                new object[] { "STOMP", StompCommand.STOMP, true },
                new object[] { "CONNECT", StompCommand.CONNECT, true },
                new object[] { "SEND", StompCommand.SEND, true },
                new object[] { "SUBSCRIBE", StompCommand.SUBSCRIBE, true },
                new object[] { "UNSUBSCRIBE", StompCommand.UNSUBSCRIBE, true },
                new object[] { "ACK", StompCommand.ACK, true },
                new object[] { "NACK", StompCommand.NACK, true },
                new object[] { "BEGIN", StompCommand.BEGIN, true },
                new object[] { "ABORT", StompCommand.ABORT, true },
                new object[] { "COMMIT", StompCommand.COMMIT, true },
                new object[] { "DISCONNECT", StompCommand.DISCONNECT, true },

                // invalid commands
                new object[] { "INVALID", StompCommand.UNKNOWN, false },
                new object[] { "disconnect", StompCommand.UNKNOWN , false }
        });
    }
}
