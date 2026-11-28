/*
 * Copyright 2018 The Netty Project
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
using Netty.NET.Handler.Codec.Dns;

namespace Netty.NET.Resolver.Dns;

/**
 * Decodes an {@link InetAddress} from an A or AAAA {@link DnsRawRecord}.
 */
// CLR: the final class with a private constructor is a static class. DnsRecord/DnsRawRecord/ByteBufHolder are the
// codec-dns IDnsRecord/IDnsRawRecord and Buffer IByteBufHolder. InetAddress is IPAddress, which carries no host name:
// InetAddress.getByAddress(host, addr) "Creates an InetAddress based on the provided host name and IP address"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/InetAddress.html), while new IPAddress(byte[])
// keeps only the address bytes (https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.-ctor?view=net-10.0).
// The name, and its IDN.toUnicode form when decodeIdn is set, is therefore not kept on the result (getHostName() of a
// resolved address has no counterpart); the parameters stay for the callers and the error text.
internal static class DnsAddressDecoder
{

    private const int INADDRSZ4 = 4;
    private const int INADDRSZ6 = 16;

    /**
     * Decodes an {@link InetAddress} from an A or AAAA {@link DnsRawRecord}.
     *
     * @param record the {@link DnsRecord}, most likely a {@link DnsRawRecord}
     * @param name the host name of the decoded address
     * @param decodeIdn whether to convert {@code name} to a unicode host name
     *
     * @return the {@link InetAddress}, or {@code null} if {@code record} is not a {@link DnsRawRecord} or
     *         its content is malformed
     */
    internal static IPAddress DecodeAddress(IDnsRecord record, string name, bool decodeIdn)
    {
        if (!(record is IDnsRawRecord))
        {
            return null;
        }

        ByteBuf content = ((IByteBufHolder) record).Content;
        int contentLen = content.ReadableBytes;
        if (contentLen != INADDRSZ4 && contentLen != INADDRSZ6)
        {
            return null;
        }

        byte[] addrBytes = new byte[contentLen];
        content.GetBytes(content.ReaderIndex, addrBytes);

        try
        {
            return new IPAddress(addrBytes);
        }
        // CLR: UnknownHostException (an address of another length) is the ArgumentException of IPAddress(byte[]);
        // java.lang.Error is InvalidOperationException, as codec-base's AsciiHeadersEncoder maps it.
        catch (ArgumentException e)
        {
            // Should never reach here.
            throw new InvalidOperationException("Failed to decode address \"" + name + '"', e);
        }
    }
}
