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
// collected to a list or joined is Enumerable.Repeat(x, n) with ToList() or string.Concat/string.Join. AssertJ
// assertThatThrownBy(...).isInstanceOf(C.class).hasMessageContaining(s) is Assert.ThrowsAny<C> (instanceof also
// accepts subclasses) and Assert.Contains(s, e.Message); assertThat(s).hasSize(n) is Assert.Equal(n, s.Length). The
// anonymous DefaultSocks5PasswordAuthRequest subclasses that override username()/password() are the named nested
// classes LongUsernameAuthRequest/LongPasswordAuthRequest (the accessors are virtual, see
// DefaultSocks5PasswordAuthRequest.cs), and the anonymous Socks5CommandRequest implementations are the named nested
// class TestCommandRequest with the dstAddr they return. Inside a class that declares DecoderResult() the
// DecoderResult type is qualified in expressions (as TestMessage in codec-base MessageAggregatorTest).
public class Socks5ClientEncoderTest
{
    [Fact]
    public void InitialRequestEncodingMustAcceptMaxNumberOfAuthMethods()
    {
        EmbeddedChannel encoder = new EmbeddedChannel(Socks5ClientEncoder.DEFAULT);
        Assert.True(encoder.WriteOutbound(new DefaultSocks5InitialRequest(
                Enumerable.Repeat(Socks5AuthMethod.PASSWORD, 255).ToList()
        )));
        ByteBuf buf = encoder.ReadOutbound<ByteBuf>();
        Assert.NotNull(buf);
        buf.Release();
        Assert.False(encoder.Finish());
    }

    [Fact]
    public void InitialRequestEncodingMustRejectTooManyAuthMethods()
    {
        EmbeddedChannel encoder = new EmbeddedChannel(Socks5ClientEncoder.DEFAULT);
        EncoderException e = Assert.ThrowsAny<EncoderException>(() => encoder.WriteOutbound(new DefaultSocks5InitialRequest(
                Enumerable.Repeat(Socks5AuthMethod.PASSWORD, 256).ToList()
        )));
        Assert.Contains("Invalid field length", e.Message);
        Assert.False(encoder.Finish());
    }

    [Fact]
    public void PasswordAuthRequestEncodingMustAcceptMaxLengthUsername()
    {
        EmbeddedChannel encoder = new EmbeddedChannel(Socks5ClientEncoder.DEFAULT);

        // max length username
        Assert.True(encoder.WriteOutbound(
                new LongUsernameAuthRequest(255)
        ));
        ByteBuf buf = encoder.ReadOutbound<ByteBuf>();
        Assert.NotNull(buf);
        buf.Release();

        // max length password
        Assert.True(encoder.WriteOutbound(
                new LongPasswordAuthRequest(255)
        ));
        buf = encoder.ReadOutbound<ByteBuf>();
        Assert.NotNull(buf);
        buf.Release();

        Assert.False(encoder.Finish());
    }

    [Fact]
    public void PasswordAuthRequestEncodingMustRejectTooLongUsernameOrPassword()
    {
        EmbeddedChannel encoder = new EmbeddedChannel(Socks5ClientEncoder.DEFAULT);

        // too long username
        EncoderException e = Assert.ThrowsAny<EncoderException>(() => encoder.WriteOutbound(
                new LongUsernameAuthRequest(256)
        ));
        Assert.Contains("Invalid field length", e.Message);

        // too long password
        e = Assert.ThrowsAny<EncoderException>(() => encoder.WriteOutbound(
                new LongPasswordAuthRequest(256)
        ));
        Assert.Contains("Invalid field length", e.Message);

        Assert.False(encoder.Finish());
    }

    [Fact]
    public void PrivateAuthRequestEncodingMustAcceptMaxLengthPrivateToken()
    {
        EmbeddedChannel encoder = new EmbeddedChannel(Socks5ClientEncoder.DEFAULT);
        Assert.True(encoder.WriteOutbound(new DefaultSocks5PrivateAuthRequest(new byte[255])));
        ByteBuf buf = encoder.ReadOutbound<ByteBuf>();
        Assert.NotNull(buf);
        buf.Release();
        Assert.False(encoder.Finish());
    }

    [Fact]
    public void PrivateAuthRequestEncodingMustRejectTooLongPrivateToken()
    {
        EmbeddedChannel encoder = new EmbeddedChannel(Socks5ClientEncoder.DEFAULT);
        EncoderException e = Assert.ThrowsAny<EncoderException>(
                () => encoder.WriteOutbound(new DefaultSocks5PrivateAuthRequest(new byte[256])));
        Assert.Contains("Invalid field length", e.Message);
        Assert.False(encoder.Finish());
    }

    [Fact]
    public void CommandRequestEncodingMustAcceptMaxLengthDstAddr()
    {
        EmbeddedChannel encoder = new EmbeddedChannel(Socks5ClientEncoder.DEFAULT);
        string dstAddr = string.Join(".", Enumerable.Repeat("aaa", 64));
        Assert.Equal(255, dstAddr.Length);
        Assert.True(encoder.WriteOutbound(new DefaultSocks5CommandRequest(
                Socks5CommandType.CONNECT, Socks5AddressType.DOMAIN,
                dstAddr, 8080)));
        ByteBuf buf = encoder.ReadOutbound<ByteBuf>();
        Assert.NotNull(buf);
        buf.Release();
        Assert.False(encoder.Finish());
    }

    [Fact]
    public void CommandRequestEncodingMustAcceptNullDstAddr()
    {
        EmbeddedChannel encoder = new EmbeddedChannel(Socks5ClientEncoder.DEFAULT);
        Assert.True(encoder.WriteOutbound(new TestCommandRequest(null)));
        ByteBuf buf = encoder.ReadOutbound<ByteBuf>();
        Assert.NotNull(buf);
        buf.Release();
        Assert.False(encoder.Finish());
    }

    [Fact]
    public void CommandRequestEncodingMustRejectTooLongDstAddr()
    {
        EmbeddedChannel encoder = new EmbeddedChannel(Socks5ClientEncoder.DEFAULT);
        EncoderException e = Assert.ThrowsAny<EncoderException>(() => encoder.WriteOutbound(
                new TestCommandRequest(string.Concat(Enumerable.Repeat("a", 256)))));
        Assert.Contains("Invalid field length", e.Message);
        Assert.False(encoder.Finish());
    }

    // CLR: the anonymous DefaultSocks5PasswordAuthRequest("user", "pass") subclass overriding username().
    private sealed class LongUsernameAuthRequest : DefaultSocks5PasswordAuthRequest
    {
        private readonly int length;

        internal LongUsernameAuthRequest(int length) : base("user", "pass")
        {
            this.length = length;
        }

        public override string Username()
        {
            return string.Concat(Enumerable.Repeat("a", length));
        }
    }

    // CLR: the anonymous DefaultSocks5PasswordAuthRequest("user", "pass") subclass overriding password().
    private sealed class LongPasswordAuthRequest : DefaultSocks5PasswordAuthRequest
    {
        private readonly int length;

        internal LongPasswordAuthRequest(int length) : base("user", "pass")
        {
            this.length = length;
        }

        public override string Password()
        {
            return string.Concat(Enumerable.Repeat("a", length));
        }
    }

    // CLR: the anonymous Socks5CommandRequest implementations; dstAddr is the value their dstAddr() returns.
    private sealed class TestCommandRequest : ISocks5CommandRequest
    {
        private readonly string dstAddr;

        internal TestCommandRequest(string dstAddr)
        {
            this.dstAddr = dstAddr;
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

        public Socks5CommandType Type()
        {
            return Socks5CommandType.CONNECT;
        }

        public Socks5AddressType DstAddrType()
        {
            return Socks5AddressType.DOMAIN;
        }

        public string DstAddr()
        {
            return dstAddr;
        }

        public int DstPort()
        {
            return 8080;
        }
    }
}
