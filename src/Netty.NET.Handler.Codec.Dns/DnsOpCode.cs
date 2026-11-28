/*
 * Copyright 2015 The Netty Project
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
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Dns;

/**
 * The DNS {@code OpCode} as defined in <a href="https://tools.ietf.org/html/rfc2929">RFC2929</a>.
 */
// CLR: Comparable<DnsOpCode> is IComparable<DnsOpCode>; hashCode()/equals(Object)/toString() are the BCL
// GetHashCode()/Equals(object)/ToString(), and byteValue() stays a method. The Java byte opcode is the unsigned
// System.Byte, as ByteBuf.getByte (owner decision; docs/buffer-porting.md, "Java signed byte and widened
// unsigned-byte APIs intentionally share GetByte/ReadByte returning CLR byte"). checkNotNull throws
// ArgumentNullException.
public class DnsOpCode : IComparable<DnsOpCode>
{

    /**
     * The 'Query' DNS OpCode, as defined in <a href="https://tools.ietf.org/html/rfc1035">RFC1035</a>.
     */
    public static readonly DnsOpCode QUERY = new DnsOpCode(0x00, "QUERY");

    /**
     * The 'IQuery' DNS OpCode, as defined in <a href="https://tools.ietf.org/html/rfc1035">RFC1035</a>.
     */
    public static readonly DnsOpCode IQUERY = new DnsOpCode(0x01, "IQUERY");

    /**
     * The 'Status' DNS OpCode, as defined in <a href="https://tools.ietf.org/html/rfc1035">RFC1035</a>.
     */
    public static readonly DnsOpCode STATUS = new DnsOpCode(0x02, "STATUS");

    /**
     * The 'Notify' DNS OpCode, as defined in <a href="https://tools.ietf.org/html/rfc1996">RFC1996</a>.
     */
    public static readonly DnsOpCode NOTIFY = new DnsOpCode(0x04, "NOTIFY");

    /**
     * The 'Update' DNS OpCode, as defined in <a href="https://tools.ietf.org/html/rfc2136">RFC2136</a>.
     */
    public static readonly DnsOpCode UPDATE = new DnsOpCode(0x05, "UPDATE");

    /**
     * Returns the {@link DnsOpCode} instance of the specified byte value.
     */
    public static DnsOpCode ValueOf(int b)
    {
        switch (b)
        {
        case 0x00:
            return QUERY;
        case 0x01:
            return IQUERY;
        case 0x02:
            return STATUS;
        case 0x04:
            return NOTIFY;
        case 0x05:
            return UPDATE;
        default:
            break;
        }

        return new DnsOpCode(b);
    }

    private readonly byte byteValue;
    private readonly string name;
    private string text;

    private DnsOpCode(int byteValue) : this(byteValue, "UNKNOWN")
    {
    }

    // CLR: the (byte) narrowing keeps the low 8 bits as Java does; unchecked keeps it from throwing in a checked build.
    public DnsOpCode(int byteValue, string name)
    {
        this.byteValue = unchecked((byte) byteValue);
        this.name = CheckNotNull(name, "name");
    }

    public byte ByteValue()
    {
        return byteValue;
    }

    // CLR: Java returns the signed byte widened to int ("For byte, from -128 to 127, inclusive", JLS 4.2.1,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.2.1); the System.Byte field is 0 to 255
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types), so the
    // value is reinterpreted as sbyte to keep the Java hash code for opcodes 128..255.
    public override int GetHashCode()
    {
        return unchecked((sbyte) byteValue);
    }

    public override bool Equals(object obj)
    {
        if (this == obj)
        {
            return true;
        }

        if (!(obj is DnsOpCode))
        {
            return false;
        }

        return byteValue == ((DnsOpCode) obj).byteValue;
    }

    // CLR: the Java difference of two signed bytes; the sbyte reinterpretation keeps the Java order (128..255 sort
    // before 0..127), see GetHashCode().
    public int CompareTo(DnsOpCode o)
    {
        return unchecked((sbyte) byteValue) - unchecked((sbyte) o.byteValue);
    }

    // CLR: byteValue & 0xFF is the unsigned System.Byte value itself; numbers use the invariant culture
    // (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
    public override string ToString()
    {
        string text = this.text;
        if (text == null)
        {
            this.text = text = name + '(' + byteValue.ToString(CultureInfo.InvariantCulture) + ')';
        }

        return text;
    }
}
