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
using System.Net;
using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: QuicTokenHandler is IQuicTokenHandler and its nested TokenValidationResult IQuicTokenHandler.TokenValidationResult.
// validateToken(token, address, dcid) is a default interface member, so it is called through IQuicTokenHandler (C# does
// not expose a default member on the implementing class). Quiche.QUICHE_MAX_CONN_ID_LEN is Quic.MAX_CONN_ID_LEN (20),
// ThreadLocalRandom.current() is Random.Shared, InetAddress.getByAddress(byte[]) is new IPAddress(byte[]) and
// InetSocketAddress IPEndPoint. assertThat(x).isTrue()/isLessThanOrEqualTo(y) are Assert.True/Assert.InRange; ByteBuf
// equality compares the readable bytes, as in Java.
public class InsecureQuicTokenHandlerTest : AbstractQuicTest
{

    [Fact]
    public void TestMaxTokenLength()
    {
        Assert.Equal(InsecureQuicTokenHandler.MAX_TOKEN_LEN, InsecureQuicTokenHandler.INSTANCE.MaxTokenLength());
    }

    [Fact]
    public void TestTokenProcessingIpv4()
    {
        TestTokenProcessing(true);
    }

    [Fact]
    public void TestTokenProcessingIpv6()
    {
        TestTokenProcessing(false);
    }

    [Fact]
    public void TestTokenValidationResultCanUseDestinationConnectionIdAsOdcid()
    {
        ByteBuf token = Unpooled.Buffer().WriteInt(1);
        ByteBuf dcid = Unpooled.WrappedBuffer(new byte[] { 1, 2, 3, 4 });

        try
        {
            IQuicTokenHandler.TokenValidationResult result =
                    IQuicTokenHandler.TokenValidationResult.OdcidFromDestinationConnectionId();
            Assert.True(result.IsValid());
            Assert.Equal(dcid, result.OriginalDestinationConnectionId(token, dcid));
        }
        finally
        {
            token.Release();
            dcid.Release();
        }
    }

    private static void TestTokenProcessing(bool ipv4)
    {
        byte[] bytes = new byte[Quic.MAX_CONN_ID_LEN];
        Random.Shared.NextBytes(bytes);
        ByteBuf dcid = Unpooled.WrappedBuffer(bytes);
        ByteBuf @out = Unpooled.Buffer();
        IQuicTokenHandler handler = InsecureQuicTokenHandler.INSTANCE;

        try
        {
            IPEndPoint validAddress;
            IPEndPoint invalidAddress;

            if (ipv4)
            {
                validAddress = new IPEndPoint(
                        new IPAddress(new byte[] { 10, 10, 10, 1 }), 9999);
                invalidAddress = new IPEndPoint(
                        new IPAddress(new byte[] { 10, 10, 10, 10 }), 9999);
            }
            else
            {
                validAddress = new IPEndPoint(new IPAddress(
                        new byte[] { 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 1 }), 9999);
                invalidAddress = new IPEndPoint(new IPAddress(
                        new byte[] { 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10 }), 9999);
            }

            InsecureQuicTokenHandler.INSTANCE.WriteToken(@out, dcid, validAddress);
            Assert.InRange(@out.ReadableBytes, int.MinValue, InsecureQuicTokenHandler.INSTANCE.MaxTokenLength());
            Assert.NotEqual(-1, InsecureQuicTokenHandler.INSTANCE.ValidateToken(@out, validAddress));
            IQuicTokenHandler.TokenValidationResult result =
                    handler.ValidateToken(@out, validAddress, dcid);
            Assert.True(result.IsValid());
            Assert.Equal(dcid, result.OriginalDestinationConnectionId(@out, dcid));

            // Use another address and check that the validate fails.
            Assert.Equal(-1, InsecureQuicTokenHandler.INSTANCE.ValidateToken(@out, invalidAddress));
            Assert.Same(IQuicTokenHandler.TokenValidationResult.InvalidToken(),
                    handler.ValidateToken(@out, invalidAddress, dcid));
        }
        finally
        {
            dcid.Release();
            @out.Release();
        }
    }
}
