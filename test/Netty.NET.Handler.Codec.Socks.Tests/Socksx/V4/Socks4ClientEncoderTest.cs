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

using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Socksx.V4;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Socks.Tests.Socksx.V4;

// CLR: AssertJ assertThatThrownBy(...).isInstanceOf(C.class).hasMessageContaining(s) is Assert.ThrowsAny<C>
// (instanceof also accepts subclasses) and Assert.Contains(s, e.Message).
public class Socks4ClientEncoderTest
{
    [Fact]
    public void MustEncodeCommandRequestToIPv4()
    {
        EmbeddedChannel encoder = new EmbeddedChannel(Socks4ClientEncoder.INSTANCE);
        DefaultSocks4CommandRequest request = new DefaultSocks4CommandRequest(
                Socks4CommandType.CONNECT, "127.0.0.1", 8008, "user");
        Assert.True(encoder.WriteOutbound(request));
        ByteBuf buf = encoder.ReadOutbound<ByteBuf>();
        Assert.NotNull(buf);
        buf.Release();
        Assert.False(encoder.Finish());
    }

    [Fact]
    public void MustEncodeCommandRequestToDomain()
    {
        EmbeddedChannel encoder = new EmbeddedChannel(Socks4ClientEncoder.INSTANCE);
        DefaultSocks4CommandRequest request = new DefaultSocks4CommandRequest(
                Socks4CommandType.CONNECT, "unix://uds.sock", 8008, "user");
        Assert.True(encoder.WriteOutbound(request));
        ByteBuf buf = encoder.ReadOutbound<ByteBuf>();
        Assert.NotNull(buf);
        buf.Release();
        Assert.False(encoder.Finish());
    }

    [Fact]
    public void MustRejectNulByteInUserIdWithIPv4Destination()
    {
        EmbeddedChannel encoder = new EmbeddedChannel(Socks4ClientEncoder.INSTANCE);
        DefaultSocks4CommandRequest request = new DefaultSocks4CommandRequest(
                Socks4CommandType.CONNECT, "127.0.0.1", 8008, "use\0r");
        EncoderException e = Assert.ThrowsAny<EncoderException>(() => encoder.WriteOutbound(request));
        Assert.Contains("Illegal character", e.Message);
        Assert.False(encoder.Finish());
    }

    [Fact]
    public void MustRejectNulByteInUserIdWithDomainDestination()
    {
        EmbeddedChannel encoder = new EmbeddedChannel(Socks4ClientEncoder.INSTANCE);
        DefaultSocks4CommandRequest request = new DefaultSocks4CommandRequest(
                Socks4CommandType.CONNECT, "unix://uds.sock", 8008, "use\0r");
        EncoderException e = Assert.ThrowsAny<EncoderException>(() => encoder.WriteOutbound(request));
        Assert.Contains("Illegal character", e.Message);
        Assert.False(encoder.Finish());
    }

    [Fact]
    public void MustRejectNulByteInDstAddr()
    {
        EmbeddedChannel encoder = new EmbeddedChannel(Socks4ClientEncoder.INSTANCE);
        DefaultSocks4CommandRequest request = new DefaultSocks4CommandRequest(
                Socks4CommandType.CONNECT, "unix://uds\0.sock", 8008, "user");
        EncoderException e = Assert.ThrowsAny<EncoderException>(() => encoder.WriteOutbound(request));
        Assert.Contains("Illegal character", e.Message);
        Assert.False(encoder.Finish());
    }
}
