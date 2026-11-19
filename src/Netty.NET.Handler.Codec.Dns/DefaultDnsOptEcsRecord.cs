/*
 * Copyright 2016 The Netty Project
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
using System.Globalization;
using System.Text;
using Netty.NET.Common;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Dns;

/**
 * Default {@link DnsOptEcsRecord} implementation.
 */
// CLR: the @Deprecated DefaultDnsOptEcsRecord(int, InternetProtocolFamily) constructor is not ported: its @deprecated
// tag names the replacement DefaultDnsOptEcsRecord(int, SocketProtocolFamily) (owner decision 2026-11-16, recorded in
// docs/codec-dns-porting.md). SocketProtocolFamily is the Transport enum with the members Inet, Inet6 and Unix
// (src/Netty.NET.Transport/Channels/Sockets/SocketProtocolFamily.cs). The ADDRESS bytes are System.Byte; clone() is
// Array.Clone().
public sealed class DefaultDnsOptEcsRecord : AbstractDnsOptPseudoRrRecord, IDnsOptEcsRecord
{
    private readonly int srcPrefixLength;
    private readonly byte[] address;

    /**
     * Creates a new instance.
     *
     * @param maxPayloadSize the suggested max payload size in bytes
     * @param extendedRcode the extended rcode
     * @param version the version
     * @param srcPrefixLength the prefix length
     * @param address the bytes of the {@link InetAddress} to use
     */
    public DefaultDnsOptEcsRecord(int maxPayloadSize, int extendedRcode, int version,
                                  int srcPrefixLength, byte[] address) :
        base(maxPayloadSize, extendedRcode, version)
    {
        this.srcPrefixLength = srcPrefixLength;
        this.address = (byte[]) VerifyAddress(address).Clone();
    }

    /**
     * Creates a new instance.
     *
     * @param maxPayloadSize the suggested max payload size in bytes
     * @param srcPrefixLength the prefix length
     * @param address the bytes of the {@link InetAddress} to use
     */
    public DefaultDnsOptEcsRecord(int maxPayloadSize, int srcPrefixLength, byte[] address) :
        this(maxPayloadSize, 0, 0, srcPrefixLength, address)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param maxPayloadSize        the suggested max payload size in bytes
     * @param socketProtocolFamily  the {@link SocketProtocolFamily} to use. This should be the same as the one used to
     *                              send the query.
     */
    public DefaultDnsOptEcsRecord(int maxPayloadSize, SocketProtocolFamily socketProtocolFamily) :
        this(maxPayloadSize, 0, 0, 0, LocalAddress(socketProtocolFamily))
    {
    }

    // CLR: InetAddress.getAddress() is IPAddress.GetAddressBytes(), on the NetUtil copies of the loopback addresses.
    private static byte[] LocalAddress(SocketProtocolFamily family)
    {
        switch (family)
        {
            case SocketProtocolFamily.Inet:
                return NetUtil.LOCALHOST4.GetAddressBytes();
            case SocketProtocolFamily.Inet6:
                return NetUtil.LOCALHOST6.GetAddressBytes();
            default:
                return null;
        }
    }

    // CLR: as in the source, '&&' binds tighter than '||', so a null array fails on bytes.length (a
    // NullReferenceException in place of the NullPointerException). IllegalArgumentException is ArgumentException
    // (docs/codec-base-porting.md, Shared CLR adaptations).
    private static byte[] VerifyAddress(byte[] bytes)
    {
        if (bytes != null && bytes.Length == 4 || bytes.Length == 16)
        {
            return bytes;
        }
        throw new ArgumentException("bytes.length must either 4 or 16");
    }

    public int SourcePrefixLength()
    {
        return srcPrefixLength;
    }

    public int ScopePrefixLength()
    {
        return 0;
    }

    public byte[] Address()
    {
        return (byte[]) address.Clone();
    }

    // CLR: Arrays.toString(byte[]) lists the elements "enclosed in square brackets", separated by ", ", each
    // converted "as by String.valueOf(byte)", i.e. as a signed byte from -128 to 127
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Arrays.html#toString(byte%5B%5D),
    // JLS 4.2.1 https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.2.1). The System.Byte elements are
    // 0 to 255 (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types),
    // so each one is reinterpreted as sbyte to keep the Java text (192 prints as -64). Numbers use the invariant
    // culture (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
    public override string ToString()
    {
        StringBuilder sb = ToStringBuilder();
        sb.Length = sb.Length - 1;
        return sb.Append(" address:")
          .Append(ArraysToString(address))
          .Append(" sourcePrefixLength:")
          .Append(SourcePrefixLength().ToString(CultureInfo.InvariantCulture))
          .Append(" scopePrefixLength:")
          .Append(ScopePrefixLength().ToString(CultureInfo.InvariantCulture))
          .Append(')').ToString();
    }

    private static string ArraysToString(byte[] a)
    {
        StringBuilder b = new StringBuilder();
        b.Append('[');
        for (int i = 0; i < a.Length; i++)
        {
            if (i > 0)
            {
                b.Append(", ");
            }
            b.Append(unchecked((sbyte) a[i]).ToString(CultureInfo.InvariantCulture));
        }
        return b.Append(']').ToString();
    }
}
