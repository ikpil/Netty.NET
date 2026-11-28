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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.HAProxy;

/**
 * Represents a {@link HAProxyTLV} of the type {@link HAProxyTLV.Type#PP2_TYPE_SSL}.
 * This TLV encapsulates other TLVs and has additional information like verification information and a client bitfield.
 */
// CLR: java.util.List is IList<T> and Collections.unmodifiableList, an unmodifiable view, is a ReadOnlyCollection<T>
// wrapper over the list (https://learn.microsoft.com/dotnet/api/system.collections.generic.list-1.asreadonly). The
// client bit field stays a byte (owner decision); the package-private constructor and contentNumBytes() are internal.
public sealed class HAProxySSLTLV : HAProxyTLV
{

    private readonly int verify;
    private readonly IList<HAProxyTLV> tlvs;
    private readonly byte clientBitField;

    /**
     * Creates a new HAProxySSLTLV
     *
     * @param verify the verification result as defined in the specification for the pp2_tlv_ssl struct (see
     * https://www.haproxy.org/download/1.8/doc/proxy-protocol.txt)
     * @param clientBitField the bitfield with client information
     * @param tlvs the encapsulated {@link HAProxyTLV}s
     */
    public HAProxySSLTLV(int verify, byte clientBitField, IList<HAProxyTLV> tlvs)
        : this(verify, clientBitField, tlvs, Unpooled.EmptyBuffer)
    {
    }

    /**
     * Creates a new HAProxySSLTLV
     *
     * @param verify the verification result as defined in the specification for the pp2_tlv_ssl struct (see
     * https://www.haproxy.org/download/1.8/doc/proxy-protocol.txt)
     * @param clientBitField the bitfield with client information
     * @param tlvs the encapsulated {@link HAProxyTLV}s
     * @param rawContent the raw TLV content
     */
    internal HAProxySSLTLV(int verify, byte clientBitField, IList<HAProxyTLV> tlvs, ByteBuf rawContent)
        : base(Type.PP2_TYPE_SSL, (byte) 0x20, rawContent)
    {

        this.verify = verify;
        this.tlvs = new ReadOnlyCollection<HAProxyTLV>(tlvs);
        this.clientBitField = clientBitField;
    }

    /**
     * Returns {@code true} if the bit field for PP2_CLIENT_CERT_CONN was set
     */
    public bool IsPP2ClientCertConn()
    {
        return (clientBitField & 0x2) != 0;
    }

    /**
     * Returns {@code true} if the bit field for PP2_CLIENT_SSL was set
     */
    public bool IsPP2ClientSSL()
    {
        return (clientBitField & 0x1) != 0;
    }

    /**
     * Returns {@code true} if the bit field for PP2_CLIENT_CERT_SESS was set
     */
    public bool IsPP2ClientCertSess()
    {
        return (clientBitField & 0x4) != 0;
    }

    /**
     * Returns the client bit field
     */
    public byte Client()
    {
        return clientBitField;
    }

    /**
     * Returns the verification result
     */
    public int Verify()
    {
        return verify;
    }

    /**
     * Returns an unmodifiable Set of encapsulated {@link HAProxyTLV}s.
     */
    public IList<HAProxyTLV> EncapsulatedTLVs()
    {
        return tlvs;
    }

    internal override int ContentNumBytes()
    {
        int tlvNumBytes = 0;

        for (int i = 0; i < tlvs.Count; i++)
        {
            tlvNumBytes += tlvs[i].TotalNumBytes();
        }

        return 5 + tlvNumBytes; // clientBit(1) + verify(4) + tlvs
    }

    // CLR: typeByteValue and client are Java bytes, which string conversion prints signed (-128..127,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.2.1); the C# bytes are unsigned, so they are
    // printed through sbyte with the invariant culture to keep the Java text. Numbers use the invariant culture.
    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) +
               "(type: " + Type_() +
               ", typeByteValue: " + ((sbyte) TypeByteValue()).ToString(CultureInfo.InvariantCulture) +
               ", client: " + ((sbyte) Client()).ToString(CultureInfo.InvariantCulture) +
               ", verify: " + Verify().ToString(CultureInfo.InvariantCulture) +
               ", numEncapsulatedTlvs: " + tlvs.Count.ToString(CultureInfo.InvariantCulture) + ')';
    }
}
