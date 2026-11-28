/*
 * Copyright 2012 The Netty Project
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
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Socksx.V4;

/**
 * The status of {@link Socks4CommandResponse}.
 */
// CLR: Comparable<T> is IComparable<T> (CompareTo "indicates whether the position of the current instance in the sort
// order is before, after, or the same", https://learn.microsoft.com/en-us/dotnet/api/system.icomparable-1?view=net-10.0);
// hashCode/equals/toString are the BCL GetHashCode/Equals/ToString. The byte value is the unsigned System.Byte (owner
// decision, docs/common-clr-design.md) where the Java byte is signed ("For byte, from -128 to 127, inclusive",
// https://docs.oracle.com/javase/specs/jls/se21/html/jls-4.html#jls-4.2.1), so GetHashCode and CompareTo use
// (sbyte) byteValue to return the Java results (0x80-0xFF hash and order as -128..-1). ToString keeps
// byteValue & 0xFF (unsigned in both) and appends it with the invariant culture.
public class Socks4CommandStatus : IComparable<Socks4CommandStatus>
{

    public static readonly Socks4CommandStatus SUCCESS = new Socks4CommandStatus(0x5a, "SUCCESS");
    public static readonly Socks4CommandStatus REJECTED_OR_FAILED = new Socks4CommandStatus(0x5b, "REJECTED_OR_FAILED");
    public static readonly Socks4CommandStatus IDENTD_UNREACHABLE = new Socks4CommandStatus(0x5c, "IDENTD_UNREACHABLE");
    public static readonly Socks4CommandStatus IDENTD_AUTH_FAILURE = new Socks4CommandStatus(0x5d, "IDENTD_AUTH_FAILURE");

    public static Socks4CommandStatus ValueOf(byte b)
    {
        switch (b)
        {
        case 0x5a:
            return SUCCESS;
        case 0x5b:
            return REJECTED_OR_FAILED;
        case 0x5c:
            return IDENTD_UNREACHABLE;
        case 0x5d:
            return IDENTD_AUTH_FAILURE;
        }

        return new Socks4CommandStatus(b);
    }

    private readonly byte byteValue;
    private readonly string name;
    private string text;

    public Socks4CommandStatus(int byteValue) : this(byteValue, "UNKNOWN")
    {
    }

    public Socks4CommandStatus(int byteValue, string name)
    {
        this.name = ObjectUtil.CheckNotNull(name, "name");
        this.byteValue = (byte) byteValue;
    }

    public byte ByteValue()
    {
        return byteValue;
    }

    public bool IsSuccess()
    {
        return byteValue == 0x5a;
    }

    public override int GetHashCode()
    {
        return (sbyte) byteValue;
    }

    public override bool Equals(object obj)
    {
        if (!(obj is Socks4CommandStatus))
        {
            return false;
        }

        return byteValue == ((Socks4CommandStatus) obj).byteValue;
    }

    public int CompareTo(Socks4CommandStatus o)
    {
        return (sbyte) byteValue - (sbyte) o.byteValue;
    }

    public override string ToString()
    {
        string text = this.text;
        if (text == null)
        {
            this.text = text = name + '(' + (byteValue & 0xFF).ToString(CultureInfo.InvariantCulture) + ')';
        }

        return text;
    }
}
