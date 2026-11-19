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
using System.Text;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Dns;

/**
 * A skeletal implementation of {@link DnsRecord}.
 */
// CLR: name()/type()/dnsClass()/timeToLive() stay methods and are virtual like the non-final Java methods;
// hashCode()/equals(Object)/toString() are the BCL GetHashCode()/Equals(object)/ToString(). The interface constants are
// named through IDnsRecord (CLASS_IN), since C# classes do not inherit static interface members
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface). Numbers in text use the
// invariant culture (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
public abstract class AbstractDnsRecord : IDnsRecord
{

    private readonly string name;
    private readonly DnsRecordType type;
    private readonly short dnsClass;
    private readonly long timeToLive;
    private int hashCode;

    /**
     * Creates a new {@link #CLASS_IN IN-class} record.
     *
     * @param name the domain name
     * @param type the type of the record
     * @param timeToLive the TTL value of the record
     */
    protected AbstractDnsRecord(string name, DnsRecordType type, long timeToLive) :
        this(name, type, IDnsRecord.CLASS_IN, timeToLive)
    {
    }

    /**
     * Creates a new record.
     *
     * @param name the domain name
     * @param type the type of the record
     * @param dnsClass the class of the record, usually one of the following:
     *                 <ul>
     *                     <li>{@link #CLASS_IN}</li>
     *                     <li>{@link #CLASS_CSNET}</li>
     *                     <li>{@link #CLASS_CHAOS}</li>
     *                     <li>{@link #CLASS_HESIOD}</li>
     *                     <li>{@link #CLASS_NONE}</li>
     *                     <li>{@link #CLASS_ANY}</li>
     *                 </ul>
     * @param timeToLive the TTL value of the record
     */
    // CLR: the (short) narrowing keeps the low 16 bits as Java does (unchecked, so a checked build does not throw).
    protected AbstractDnsRecord(string name, DnsRecordType type, int dnsClass, long timeToLive)
    {
        CheckPositiveOrZero(timeToLive, "timeToLive");
        // Convert to ASCII which will also check that the length is not too big.
        // See:
        //   - https://github.com/netty/netty/issues/4937
        //   - https://github.com/netty/netty/issues/4935
        this.name = AppendTrailingDot(IDNtoASCII(name));
        this.type = CheckNotNull(type, "type");
        this.dnsClass = unchecked((short) dnsClass);
        this.timeToLive = timeToLive;
    }

    private static string IDNtoASCII(string name)
    {
        CheckNotNull(name, "name");
        if (PlatformDependent.IsAndroid() && DefaultDnsRecordDecoder.ROOT.Equals(name))
        {
            // Prior Android 10 there was a bug that did not correctly parse ".".
            //
            // See https://github.com/netty/netty/issues/10034
            return name;
        }
        // CLR: IDN.toASCII is the shared Netty.NET.Common.Internal.IDN.ToASCII, which keeps the OpenJDK label loop
        // and its ArgumentException failures (src/Netty.NET.Common/Internal/IDN.cs).
        return IDN.ToASCII(name);
    }

    private static string AppendTrailingDot(string name)
    {
        if (name.Length > 0 && name[name.Length - 1] != '.')
        {
            return name + '.';
        }
        return name;
    }

    public virtual string Name()
    {
        return name;
    }

    public virtual DnsRecordType Type()
    {
        return type;
    }

    public virtual int DnsClass()
    {
        return dnsClass & 0xFFFF;
    }

    public virtual long TimeToLive()
    {
        return timeToLive;
    }

    // CLR: String.equalsIgnoreCase compares code points "ignoring case" without the locale
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html); StringComparison.
    // OrdinalIgnoreCase compares "using ordinal (binary) sort rules and ignoring the case"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.stringcomparison?view=net-10.0).
    public override bool Equals(object obj)
    {
        if (this == obj)
        {
            return true;
        }

        if (!(obj is IDnsRecord))
        {
            return false;
        }

        IDnsRecord that = (IDnsRecord) obj;
        int hashCode = this.hashCode;
        if (hashCode != 0 && hashCode != that.GetHashCode())
        {
            return false;
        }

        return Type().IntValue() == that.Type().IntValue() &&
               DnsClass() == that.DnsClass() &&
               string.Equals(Name(), that.Name(), StringComparison.OrdinalIgnoreCase);
    }

    // CLR: String.toLowerCase() uses "the rules of the default locale" and String.hashCode() is the polynomial
    // s[0]*31^(n-1) + ... (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html).
    // ToLowerInvariant uses "the casing rules of the invariant culture"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.string.tolowerinvariant?view=net-10.0), and string.GetHashCode()
    // returns identical values for equal strings but "is not guaranteed to be stable" across runs
    // (https://learn.microsoft.com/en-us/dotnet/api/system.string.gethashcode?view=net-10.0), so the value differs from
    // Java while equal records still hash equally, which is the contract Equals relies on.
    public override int GetHashCode()
    {
        int hashCode = this.hashCode;
        if (hashCode != 0)
        {
            return hashCode;
        }

        return this.hashCode = name.ToLowerInvariant().GetHashCode() * 31 + Type().IntValue() * 31 + DnsClass();
    }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder(64);

        buf.Append(StringUtil.SimpleClassName(this))
           .Append('(')
           .Append(Name())
           .Append(' ')
           .Append(TimeToLive().ToString(CultureInfo.InvariantCulture))
           .Append(' ');

        DnsMessageUtil.AppendRecordClass(buf, DnsClass())
                      .Append(' ')
                      .Append(Type().Name())
                      .Append(')');

        return buf.ToString();
    }
}
