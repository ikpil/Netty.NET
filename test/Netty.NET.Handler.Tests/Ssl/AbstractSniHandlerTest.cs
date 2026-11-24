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

using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Handler.Ssl;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: the static extractSniHostname of the generic AbstractSniHandler<T> needs a type argument in C#
// (AbstractSniHandler<object>); CharsetUtil.US_ASCII is Encoding.ASCII (docs/codec-smtp-porting.md).
public class AbstractSniHandlerTest
{

    [Fact]
    public void TestExtractSniHostnameParsesWellFormedClientHello()
    {
        ByteBuf buffer = ValidClientHelloBody("netty.io");
        try
        {
            Assert.Equal("netty.io", AbstractSniHandler<object>.ExtractSniHostname(buffer));
        }
        finally
        {
            buffer.Release();
        }
    }

    // See https://github.com/netty/netty security report: a ClientHello whose SessionID length
    // field is attacker-controlled and points past the end of the record used to make the
    // subsequent getUnsignedShort(...) call for cipher_suites throw an IndexOutOfBoundsException
    // instead of extractSniHostname() gracefully returning null.
    [Fact]
    public void TestExtractSniHostnameDoesNotThrowOnOversizedSessionIdLength()
    {
        ByteBuf buffer = Unpooled.Buffer();
        try
        {
            buffer.WriteZero(34); // client_version (2) + random (32)
            buffer.WriteByte(0xFF); // SessionID length: far larger than the remaining record
            buffer.WriteZero(5); // trailing bytes, mirroring a minimal non-fragmented record

            Assert.Null(AbstractSniHandler<object>.ExtractSniHostname(buffer));
        }
        finally
        {
            buffer.Release();
        }
    }

    // Same issue as above but for the cipher_suites length field feeding the unguarded read of
    // compression_methods length.
    [Fact]
    public void TestExtractSniHostnameDoesNotThrowOnOversizedCipherSuitesLength()
    {
        ByteBuf buffer = Unpooled.Buffer();
        try
        {
            buffer.WriteZero(34); // client_version (2) + random (32)
            buffer.WriteByte(0); // SessionID length
            buffer.WriteShort(0xFFFF); // cipher_suites length: far larger than the remaining record

            Assert.Null(AbstractSniHandler<object>.ExtractSniHostname(buffer));
        }
        finally
        {
            buffer.Release();
        }
    }

    // Same issue as above but for the compression_methods length field feeding the unguarded read
    // of the extensions length.
    [Fact]
    public void TestExtractSniHostnameDoesNotThrowOnOversizedCompressionMethodLength()
    {
        ByteBuf buffer = Unpooled.Buffer();
        try
        {
            buffer.WriteZero(34); // client_version (2) + random (32)
            buffer.WriteByte(0); // SessionID length
            buffer.WriteShort(0); // cipher_suites length
            buffer.WriteByte(0xFF); // compression_methods length: far larger than the remaining record

            Assert.Null(AbstractSniHandler<object>.ExtractSniHostname(buffer));
        }
        finally
        {
            buffer.Release();
        }
    }

    private static ByteBuf ValidClientHelloBody(string hostname)
    {
        byte[] hostBytes = Encoding.ASCII.GetBytes(hostname);
        int serverNameListLength = 1 + 2 + hostBytes.Length; // name type (1) + name length (2) + name
        int serverNameExtensionDataLength = 2 + serverNameListLength; // server_name_list length (2) + list
        int extensionsLength = 2 + 2 + serverNameExtensionDataLength; // ext type (2) + ext length (2) + data

        ByteBuf buffer = Unpooled.Buffer();
        buffer.WriteZero(34); // client_version (2) + random (32)
        buffer.WriteByte(0); // SessionID length
        buffer.WriteShort(0); // cipher_suites length
        buffer.WriteByte(0); // compression_methods length
        buffer.WriteShort(extensionsLength);
        buffer.WriteShort(0); // extension type: server_name
        buffer.WriteShort(serverNameExtensionDataLength);
        buffer.WriteShort(serverNameListLength);
        buffer.WriteByte(0); // server name type: host_name
        buffer.WriteShort(hostBytes.Length);
        buffer.WriteBytes(hostBytes);
        return buffer;
    }
}
