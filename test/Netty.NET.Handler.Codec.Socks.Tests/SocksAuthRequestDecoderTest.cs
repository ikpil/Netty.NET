/*
 * Copyright 2012 The Netty Project
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

using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Socks.Tests;

// CLR: String.getBytes() uses the platform charset; the ASCII user name and password give the same bytes with
// Encoding.ASCII.
public class SocksAuthRequestDecoderTest
{

    private const string username = "testUserName";
    private const string password = "testPassword";

    [Fact]
    public void TestAuthRequestDecoder()
    {
        SocksAuthRequest msg = new SocksAuthRequest(username, password);
        SocksAuthRequestDecoder decoder = new SocksAuthRequestDecoder();
        EmbeddedChannel embedder = new EmbeddedChannel(decoder);
        SocksCommonTestUtils.WriteMessageIntoEmbedder(embedder, msg);
        msg = embedder.ReadInbound<SocksAuthRequest>();
        Assert.Equal(username, msg.Username());
        Assert.Equal(password, msg.Password());
        Assert.Null(embedder.ReadInbound());
    }

    [Fact]
    public void TestAuthRequestDecoderPartialSend()
    {
        EmbeddedChannel ch = new EmbeddedChannel(new SocksAuthRequestDecoder());
        ByteBuf byteBuf = Unpooled.Buffer(16);

        // Send username and password size
        byteBuf.WriteByte(SocksSubnegotiationVersion.AUTH_PASSWORD.ByteValue());
        byteBuf.WriteByte(username.Length);
        byteBuf.WriteBytes(Encoding.ASCII.GetBytes(username));
        byteBuf.WriteByte(password.Length);
        ch.WriteInbound(byteBuf);

        // Check that channel is empty
        Assert.Null(ch.ReadInbound());

        // Send password
        ByteBuf byteBuf2 = Unpooled.Buffer();
        byteBuf2.WriteBytes(Encoding.ASCII.GetBytes(password));
        ch.WriteInbound(byteBuf2);

        // Read message from channel
        SocksAuthRequest msg = ch.ReadInbound<SocksAuthRequest>();

        // Check message
        Assert.Equal(username, msg.Username());
        Assert.Equal(password, msg.Password());

        Assert.False(ch.FinishAndReleaseAll());
    }
}
