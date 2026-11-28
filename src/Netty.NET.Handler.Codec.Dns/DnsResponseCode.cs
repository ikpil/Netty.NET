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
 * The DNS {@code RCODE}, as defined in <a href="https://tools.ietf.org/html/rfc2929">RFC2929</a>.
 */
// CLR: Comparable<DnsResponseCode> is IComparable<DnsResponseCode>; hashCode()/equals(Object)/toString() are the
// BCL GetHashCode()/Equals(object)/ToString(), and intValue() stays a method. IllegalArgumentException is
// ArgumentException and checkNotNull throws ArgumentNullException. Numbers in text use the invariant culture
// (Int32.ToString() otherwise uses "the NumberFormatInfo object of the current culture",
// https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
public class DnsResponseCode : IComparable<DnsResponseCode>
{

    /**
     * The 'NoError' DNS RCODE (0), as defined in <a href="https://tools.ietf.org/html/rfc1035">RFC1035</a>.
     */
    public static readonly DnsResponseCode NOERROR = new DnsResponseCode(0, "NoError");

    /**
     * The 'FormErr' DNS RCODE (1), as defined in <a href="https://tools.ietf.org/html/rfc1035">RFC1035</a>.
     */
    public static readonly DnsResponseCode FORMERR = new DnsResponseCode(1, "FormErr");

    /**
     * The 'ServFail' DNS RCODE (2), as defined in <a href="https://tools.ietf.org/html/rfc1035">RFC1035</a>.
     */
    public static readonly DnsResponseCode SERVFAIL = new DnsResponseCode(2, "ServFail");

    /**
     * The 'NXDomain' DNS RCODE (3), as defined in <a href="https://tools.ietf.org/html/rfc1035">RFC1035</a>.
     */
    public static readonly DnsResponseCode NXDOMAIN = new DnsResponseCode(3, "NXDomain");

    /**
     * The 'NotImp' DNS RCODE (4), as defined in <a href="https://tools.ietf.org/html/rfc1035">RFC1035</a>.
     */
    public static readonly DnsResponseCode NOTIMP = new DnsResponseCode(4, "NotImp");

    /**
     * The 'Refused' DNS RCODE (5), as defined in <a href="https://tools.ietf.org/html/rfc1035">RFC1035</a>.
     */
    public static readonly DnsResponseCode REFUSED = new DnsResponseCode(5, "Refused");

    /**
     * The 'YXDomain' DNS RCODE (6), as defined in <a href="https://tools.ietf.org/html/rfc2136">RFC2136</a>.
     */
    public static readonly DnsResponseCode YXDOMAIN = new DnsResponseCode(6, "YXDomain");

    /**
     * The 'YXRRSet' DNS RCODE (7), as defined in <a href="https://tools.ietf.org/html/rfc2136">RFC2136</a>.
     */
    public static readonly DnsResponseCode YXRRSET = new DnsResponseCode(7, "YXRRSet");

    /**
     * The 'NXRRSet' DNS RCODE (8), as defined in <a href="https://tools.ietf.org/html/rfc2136">RFC2136</a>.
     */
    public static readonly DnsResponseCode NXRRSET = new DnsResponseCode(8, "NXRRSet");

    /**
     * The 'NotAuth' DNS RCODE (9), as defined in <a href="https://tools.ietf.org/html/rfc2136">RFC2136</a>.
     */
    public static readonly DnsResponseCode NOTAUTH = new DnsResponseCode(9, "NotAuth");

    /**
     * The 'NotZone' DNS RCODE (10), as defined in <a href="https://tools.ietf.org/html/rfc2136">RFC2136</a>.
     */
    public static readonly DnsResponseCode NOTZONE = new DnsResponseCode(10, "NotZone");

    /**
     * The 'BADVERS' or 'BADSIG' DNS RCODE (16), as defined in <a href="https://tools.ietf.org/html/rfc2671">RFC2671</a>
     * and <a href="https://tools.ietf.org/html/rfc2845">RFC2845</a>.
     */
    public static readonly DnsResponseCode BADVERS_OR_BADSIG = new DnsResponseCode(16, "BADVERS_OR_BADSIG");

    /**
     * The 'BADKEY' DNS RCODE (17), as defined in <a href="https://tools.ietf.org/html/rfc2845">RFC2845</a>.
     */
    public static readonly DnsResponseCode BADKEY = new DnsResponseCode(17, "BADKEY");

    /**
     * The 'BADTIME' DNS RCODE (18), as defined in <a href="https://tools.ietf.org/html/rfc2845">RFC2845</a>.
     */
    public static readonly DnsResponseCode BADTIME = new DnsResponseCode(18, "BADTIME");

    /**
     * The 'BADMODE' DNS RCODE (19), as defined in <a href="https://tools.ietf.org/html/rfc2930">RFC2930</a>.
     */
    public static readonly DnsResponseCode BADMODE = new DnsResponseCode(19, "BADMODE");

    /**
     * The 'BADNAME' DNS RCODE (20), as defined in <a href="https://tools.ietf.org/html/rfc2930">RFC2930</a>.
     */
    public static readonly DnsResponseCode BADNAME = new DnsResponseCode(20, "BADNAME");

    /**
     * The 'BADALG' DNS RCODE (21), as defined in <a href="https://tools.ietf.org/html/rfc2930">RFC2930</a>.
     */
    public static readonly DnsResponseCode BADALG = new DnsResponseCode(21, "BADALG");

    /**
     * Returns the {@link DnsResponseCode} that corresponds with the given {@code responseCode}.
     *
     * @param responseCode the DNS RCODE
     *
     * @return the corresponding {@link DnsResponseCode}
     */
    public static DnsResponseCode ValueOf(int responseCode)
    {
        switch (responseCode)
        {
        case 0:
            return NOERROR;
        case 1:
            return FORMERR;
        case 2:
            return SERVFAIL;
        case 3:
            return NXDOMAIN;
        case 4:
            return NOTIMP;
        case 5:
            return REFUSED;
        case 6:
            return YXDOMAIN;
        case 7:
            return YXRRSET;
        case 8:
            return NXRRSET;
        case 9:
            return NOTAUTH;
        case 10:
            return NOTZONE;
        case 16:
            return BADVERS_OR_BADSIG;
        case 17:
            return BADKEY;
        case 18:
            return BADTIME;
        case 19:
            return BADMODE;
        case 20:
            return BADNAME;
        case 21:
            return BADALG;
        default:
            return new DnsResponseCode(responseCode);
        }
    }

    private readonly int code;
    private readonly string name;
    private string text;

    private DnsResponseCode(int code) : this(code, "UNKNOWN")
    {
    }

    public DnsResponseCode(int code, string name)
    {
        if (code < 0 || code > 65535)
        {
            throw new ArgumentException("code: " + code.ToString(CultureInfo.InvariantCulture) + " (expected: 0 ~ 65535)");
        }

        this.code = code;
        this.name = CheckNotNull(name, "name");
    }

    /**
     * Returns the error code for this {@link DnsResponseCode}.
     */
    public int IntValue()
    {
        return code;
    }

    public int CompareTo(DnsResponseCode o)
    {
        return IntValue() - o.IntValue();
    }

    public override int GetHashCode()
    {
        return IntValue();
    }

    /**
     * Equality of {@link DnsResponseCode} only depends on {@link #intValue()}.
     */
    public override bool Equals(object o)
    {
        if (!(o is DnsResponseCode))
        {
            return false;
        }

        return IntValue() == ((DnsResponseCode) o).IntValue();
    }

    /**
     * Returns a formatted error message for this {@link DnsResponseCode}.
     */
    public override string ToString()
    {
        string text = this.text;
        if (text == null)
        {
            this.text = text = name + '(' + IntValue().ToString(CultureInfo.InvariantCulture) + ')';
        }

        return text;
    }
}
