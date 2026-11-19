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

using System.Linq;
using Netty.NET.Buffer;
using Netty.NET.Handler.Codec.Socksx;
using Netty.NET.Handler.Codec.Socksx.V5;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Socks.Tests.Socksx.V5;

// CLR: the package-private test class is public (xUnit discovers public classes). Stream.generate(() -> x).limit(n)
// joined is string.Join/string.Concat over Enumerable.Repeat(x, n); AssertJ
// assertThatThrownBy(...).isInstanceOf(C.class).hasMessageContaining(s) is Assert.ThrowsAny<C> and
// Assert.Contains(s, e.Message); assertThat(s).hasSize(n) is Assert.Equal(n, s.Length). The anonymous
// Socks5CommandResponse implementations are the named nested class TestCommandResponse with the bndAddr they return;
// inside it the DecoderResult type is qualified in expressions (see Socks5ClientEncoderTest).
public class Socks5ServerEncoderTest
{
    [Fact]
    public void CommandResponseEncodingMustAcceptMaxLengthDstAddr()
    {
        EmbeddedChannel encoder = new EmbeddedChannel(Socks5ServerEncoder.DEFAULT);
        string dstAddr = string.Join(".", Enumerable.Repeat("aaa", 64));
        Assert.Equal(255, dstAddr.Length);
        Assert.True(encoder.WriteOutbound(new DefaultSocks5CommandResponse(
                Socks5CommandStatus.SUCCESS, Socks5AddressType.DOMAIN,
                dstAddr, 8080)));
        ByteBuf buf = encoder.ReadOutbound<ByteBuf>();
        Assert.NotNull(buf);
        buf.Release();
        Assert.False(encoder.Finish());
    }

    [Fact]
    public void CommandResponseEncodingMustAcceptNullDstAddr()
    {
        EmbeddedChannel encoder = new EmbeddedChannel(Socks5ServerEncoder.DEFAULT);
        Assert.True(encoder.WriteOutbound(new TestCommandResponse(null)));
        ByteBuf buf = encoder.ReadOutbound<ByteBuf>();
        Assert.NotNull(buf);
        buf.Release();
        Assert.False(encoder.Finish());
    }

    [Fact]
    public void CommandResponseEncodingMustRejectTooLongDstAddr()
    {
        EmbeddedChannel encoder = new EmbeddedChannel(Socks5ServerEncoder.DEFAULT);
        EncoderException e = Assert.ThrowsAny<EncoderException>(() => encoder.WriteOutbound(
                new TestCommandResponse(string.Concat(Enumerable.Repeat("a", 256)))));
        Assert.Contains("Invalid field length", e.Message);
        Assert.False(encoder.Finish());
    }

    // CLR: the anonymous Socks5CommandResponse implementations; bndAddr is the value their bndAddr() returns.
    private sealed class TestCommandResponse : ISocks5CommandResponse
    {
        private readonly string bndAddr;

        internal TestCommandResponse(string bndAddr)
        {
            this.bndAddr = bndAddr;
        }

        public DecoderResult DecoderResult()
        {
            return global::Netty.NET.Handler.Codec.DecoderResult.SUCCESS;
        }

        public void SetDecoderResult(DecoderResult result)
        {
        }

        public SocksVersion Version()
        {
            return SocksVersion.SOCKS5;
        }

        public Socks5CommandStatus Status()
        {
            return Socks5CommandStatus.SUCCESS;
        }

        public Socks5AddressType BndAddrType()
        {
            return Socks5AddressType.DOMAIN;
        }

        public string BndAddr()
        {
            return bndAddr;
        }

        public int BndPort()
        {
            return 8080;
        }
    }
}
