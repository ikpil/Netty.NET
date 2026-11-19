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

using System.Collections.Generic;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Socks.Tests;

#pragma warning disable xUnit2013 // assertEquals(n, size()) keeps the source assertions.

// CLR: assertInstanceOf(C.class, o) returns o cast to C, as Assert.IsType<C>(o, exactMatch: false) does; List is IList
// and get(0) is the indexer.
public class SocksInitRequestDecoderTest
{

    /**
     * NMETHODS values in [0x80, 0xFF] sign-extend to a negative Java {@code byte},
     * so a decoder that reads them with {@link ByteBuf#readByte()} instead of
     * {@link ByteBuf#readUnsignedByte()} would wrongly treat the greeting as if
     * it offered zero authentication methods, without consuming the method bytes
     * that follow on the wire. Those unconsumed bytes would then leak to whatever
     * handler follows once the decoder removes itself from the pipeline.
     */
    [Fact]
    public void TestHighNMethodsIsReadAsUnsigned()
    {
        int authSchemeNum = 0x80; // 128, negative if interpreted as a signed byte
        EmbeddedChannel e = new EmbeddedChannel(new SocksInitRequestDecoder());

        ByteBuf buf = Unpooled.Buffer();
        buf.WriteByte(SocksProtocolVersion.SOCKS5.ByteValue());
        buf.WriteByte(authSchemeNum);
        for (int i = 0; i < authSchemeNum; i++)
        {
            buf.WriteByte(SocksAuthScheme.NO_AUTH.ByteValue());
        }

        Assert.True(e.WriteInbound(buf));

        object o = e.ReadInbound();
        SocksInitRequest req = Assert.IsType<SocksInitRequest>(o, exactMatch: false);
        IList<SocksAuthScheme> schemes = req.AuthSchemes();
        Assert.Equal(authSchemeNum, schemes.Count);
        foreach (SocksAuthScheme scheme in schemes)
        {
            Assert.Equal(SocksAuthScheme.NO_AUTH, scheme);
        }

        // No leftover bytes must have been forwarded to the next handler after self-removal.
        Assert.Null(e.ReadInbound());
        Assert.False(e.Finish());
    }

    [Fact]
    public void TestMaxNMethodsIsReadAsUnsigned()
    {
        int authSchemeNum = 0xFF; // 255
        EmbeddedChannel e = new EmbeddedChannel(new SocksInitRequestDecoder());

        ByteBuf buf = Unpooled.Buffer();
        buf.WriteByte(SocksProtocolVersion.SOCKS5.ByteValue());
        buf.WriteByte(authSchemeNum);
        for (int i = 0; i < authSchemeNum; i++)
        {
            buf.WriteByte(SocksAuthScheme.NO_AUTH.ByteValue());
        }

        Assert.True(e.WriteInbound(buf));

        object o = e.ReadInbound();
        SocksInitRequest req = Assert.IsType<SocksInitRequest>(o, exactMatch: false);
        Assert.Equal(authSchemeNum, req.AuthSchemes().Count);

        Assert.Null(e.ReadInbound());
        Assert.False(e.Finish());
    }

    /**
     * RFC 1928 section 3 requires NMETHODS to be at least 1; a wire value of 0 must be rejected
     * rather than accepted as a zero-auth-scheme success.
     */
    [Fact]
    public void TestZeroNMethodsIsRejected()
    {
        EmbeddedChannel e = new EmbeddedChannel(new SocksInitRequestDecoder());

        ByteBuf buf = Unpooled.Buffer();
        buf.WriteByte(SocksProtocolVersion.SOCKS5.ByteValue());
        buf.WriteByte(0x00);

        Assert.True(e.WriteInbound(buf));

        object o = e.ReadInbound();
        Assert.IsType<UnknownSocksRequest>(o, exactMatch: false);

        Assert.Null(e.ReadInbound());
        Assert.False(e.Finish());
    }

    [Fact]
    public void TestNormalNMethodsStillWorks()
    {
        EmbeddedChannel e = new EmbeddedChannel(new SocksInitRequestDecoder());

        ByteBuf buf = Unpooled.Buffer();
        buf.WriteByte(SocksProtocolVersion.SOCKS5.ByteValue());
        buf.WriteByte(0x01);
        buf.WriteByte(SocksAuthScheme.NO_AUTH.ByteValue());

        Assert.True(e.WriteInbound(buf));

        object o = e.ReadInbound();
        SocksInitRequest req = Assert.IsType<SocksInitRequest>(o, exactMatch: false);
        Assert.Equal(1, req.AuthSchemes().Count);
        Assert.Equal(SocksAuthScheme.NO_AUTH, req.AuthSchemes()[0]);

        Assert.Null(e.ReadInbound());
        Assert.False(e.Finish());
    }
}
