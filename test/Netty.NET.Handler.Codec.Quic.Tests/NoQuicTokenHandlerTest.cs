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

using System.Net;
using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: NoQuicTokenHandler is internal (package-private in Java). QuicConnectionAddress.random().id() is the internal
// Id() (a ReadOnlyMemory<byte>, the ByteBuffer that Unpooled.wrappedBuffer wraps); InetAddress.getByAddress(byte[]) is
// new IPAddress(byte[]) and InetSocketAddress IPEndPoint.
public class NoQuicTokenHandlerTest
{

    [Fact]
    public void TestMaxTokenLength()
    {
        Assert.Equal(0, NoQuicTokenHandler.INSTANCE.MaxTokenLength());
    }

    [Fact]
    public void TestDontWriteToken()
    {
        ByteBuf @out = Unpooled.Buffer();
        ByteBuf dcid = Unpooled.WrappedBuffer(QuicConnectionAddress.Random().Id());
        try
        {
            bool written = NoQuicTokenHandler.INSTANCE.WriteToken(@out, dcid, new IPEndPoint(
                    new IPAddress(new byte[] { 10, 10, 10, 10 }), 80));
            Assert.False(written);
            Assert.False(@out.IsReadable());
        }
        finally
        {
            @out.Release();
            dcid.Release();
        }
    }

    [Fact]
    public void TestValidateToken()
    {
        ByteBuf token = Unpooled.WrappedBuffer(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 });
        try
        {
            int offset = NoQuicTokenHandler.INSTANCE.ValidateToken(token, new IPEndPoint(
                    new IPAddress(new byte[] { 10, 10, 10, 10 }), 80));
            Assert.Equal(-1, offset);
        }
        finally
        {
            token.Release();
        }
    }
}
