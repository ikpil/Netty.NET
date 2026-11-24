/*
 * Copyright 2014 The Netty Project
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
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using Netty.NET.Common;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Resolver;

namespace Netty.NET.Handler.IpFilter;

/**
 * Use this class to create rules for {@link RuleBasedIpFilter} that group IP addresses into subnets.
 * Supports both, IPv4 and IPv6.
 */
// CLR: InetAddress is IPAddress and InetSocketAddress is IPEndPoint (docs/codec-dns-porting.md); instanceof
// Inet4Address/Inet6Address is AddressFamily.InterNetwork/InterNetworkV6. The JDK never makes an Inet6Address of an
// IPv4-mapped literal, and the NIO channels report the IPv4 peers of a dual-mode socket in IPv4 form
// (AbstractNioChannel.Normalize in Transport), so the family test sees the remote addresses the JDK would.
// SocketUtils.addressByName (InetAddress.getByName) is the DefaultNameResolver lookup that Transport's
// AbstractBootstrap uses for the same call (docs/common-clr-design.md, "Native socket operations"): it keeps the JDK
// literal rules (an IPv4-mapped literal becomes an IPv4 address) and reports UnknownHostException as SocketException.
// Comparable<IpSubnetFilterRule> is IComparable<IpSubnetFilterRule>; the package-private members are internal.
public sealed class IpSubnetFilterRule : IIpFilterRule, IComparable<IpSubnetFilterRule>
{
    private readonly IIpFilterRule filterRule;
    private readonly string ipAddress;

    /**
     * Create a new {@link IpSubnetFilterRule} instance
     *
     * @param ipAddressWithCidr IP Address with CIDR notation, e.g. (192.168.0.0/16) or (2001:db8::/32)
     * @param ruleType {@link IpFilterRuleType} to use
     */
    // CLR: String.split("/") works "as if by invoking the two-argument split method with the given expression and a
    // limit argument of zero. Trailing empty strings are therefore not included in the resulting array"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html#split(java.lang.String)),
    // and returns the whole string when the separator does not occur; string.Split('/') keeps the trailing empty
    // strings, so they are removed after a split ("10.0.0.0/" has one part, as in Java). Integer.parseInt is int.Parse
    // with a leading sign and the invariant culture; its NumberFormatException, an IllegalArgumentException subclass
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/NumberFormatException.html), is
    // FormatException or OverflowException (https://learn.microsoft.com/en-us/dotnet/api/system.int32.parse?view=net-10.0),
    // the mapping of docs/codec-base-porting.md (CharSequenceValueConverter). catch (UnknownHostException) is
    // catch (SocketException); an invalid host name that Dns rejects with ArgumentException is not wrapped, which keeps
    // it an ArgumentException as the wrapping IllegalArgumentException is in Java.
    public IpSubnetFilterRule(string ipAddressWithCidr, IpFilterRuleType ruleType)
    {
        try
        {
            string[] ipAndCidr = ipAddressWithCidr.Split('/');
            if (ipAndCidr.Length > 1)
            {
                int count = ipAndCidr.Length;
                while (count > 0 && ipAndCidr[count - 1].Length == 0)
                {
                    count--;
                }
                Array.Resize(ref ipAndCidr, count);
            }
            if (ipAndCidr.Length != 2)
            {
                throw new ArgumentException("ipAddressWithCidr: " + ipAddressWithCidr +
                        " (expected: \"<ip-address>/<mask-size>\")");
            }

            ipAddress = ipAndCidr[0];
            int cidrPrefix = int.Parse(ipAndCidr[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            filterRule = SelectFilterRule(
                    new DefaultNameResolver(ImmediateEventExecutor.INSTANCE).ResolveAsync(ipAddress)
                            .GetAwaiter().GetResult(), cidrPrefix, ruleType);
        }
        catch (SocketException e)
        {
            throw new ArgumentException("ipAddressWithCidr", e);
        }
    }

    /**
     * Create a new {@link IpSubnetFilterRule} instance
     *
     * @param ipAddress IP Address as {@link String}
     * @param cidrPrefix CIDR Prefix
     * @param ruleType {@link IpFilterRuleType} to use
     */
    // CLR: SocketUtils.addressByName and catch (UnknownHostException) as in the constructor above.
    public IpSubnetFilterRule(string ipAddress, int cidrPrefix, IpFilterRuleType ruleType)
    {
        try
        {
            this.ipAddress = ipAddress;
            filterRule = SelectFilterRule(
                    new DefaultNameResolver(ImmediateEventExecutor.INSTANCE).ResolveAsync(ipAddress)
                            .GetAwaiter().GetResult(), cidrPrefix, ruleType);
        }
        catch (SocketException e)
        {
            throw new ArgumentException("ipAddress", e);
        }
    }

    /**
     * Create a new {@link IpSubnetFilterRule} instance
     *
     * @param ipAddress IP Address as {@link InetAddress}
     * @param cidrPrefix CIDR Prefix
     * @param ruleType {@link IpFilterRuleType} to use
     */
    public IpSubnetFilterRule(IPAddress ipAddress, int cidrPrefix, IpFilterRuleType ruleType)
    {
        this.ipAddress = GetHostAddress(ipAddress);
        filterRule = SelectFilterRule(ipAddress, cidrPrefix, ruleType);
    }

    // CLR: InetAddress.getHostAddress(). An Inet4Address prints the dotted quad, as IPAddress.ToString() does. An
    // Inet6Address prints numericToTextFormat(ipaddress): the eight 16-bit groups as Integer.toHexString, separated by
    // ':' and not compressed, followed by '%' and the scope id when one was set (OpenJDK
    // https://raw.githubusercontent.com/openjdk/jdk17u/master/src/java.base/share/classes/java/net/Inet6Address.java),
    // while IPAddress.ToString() prints the "IPv6 colon-hexadecimal notation" with "::" compression
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.tostring?view=net-10.0). The JDK text is
    // built here so that getIpAddress() and toString() keep the Java result; a scope id of 0 counts as none, because
    // IPAddress has no "scope set" flag.
    private static string GetHostAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }
        byte[] src = address.GetAddressBytes();
        StringBuilder sb = new StringBuilder(39);
        for (int i = 0; i < 16 / 2; i++)
        {
            sb.Append((((src[i << 1] << 8) & 0xff00) | (src[(i << 1) + 1] & 0xff)).ToString("x", CultureInfo.InvariantCulture));
            if (i < 16 / 2 - 1)
            {
                sb.Append(':');
            }
        }
        if (address.ScopeId != 0)
        {
            sb.Append('%').Append(address.ScopeId.ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    // CLR: IpFilterRuleType is a C# enum, a value type that cannot be null, so checkNotNull(ruleType, "ruleType") has
    // no counterpart (see IpFilterRuleType.cs).
    private static IIpFilterRule SelectFilterRule(IPAddress ipAddress, int cidrPrefix, IpFilterRuleType ruleType)
    {
        ObjectUtil.CheckNotNull(ipAddress, "ipAddress");

        if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
        {
            return new Ip4SubnetFilterRule(ipAddress, cidrPrefix, ruleType);
        }
        else if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return new Ip6SubnetFilterRule(ipAddress, cidrPrefix, ruleType);
        }
        else
        {
            throw new ArgumentException("Only IPv4 and IPv6 addresses are supported");
        }
    }

    public bool Matches(IPEndPoint remoteAddress)
    {
        return filterRule.Matches(remoteAddress);
    }

    public IpFilterRuleType RuleType()
    {
        return filterRule.RuleType();
    }

    /**
     * Get IP Address of this rule
     */
    internal string GetIpAddress()
    {
        return ipAddress;
    }

    /**
     * {@link Ip4SubnetFilterRule} or {@link Ip6SubnetFilterRule}
     */
    internal IIpFilterRule GetFilterRule()
    {
        return filterRule;
    }

    public int CompareTo(IpSubnetFilterRule ipSubnetFilterRule)
    {
        if (filterRule is Ip4SubnetFilterRule)
        {
            return CompareInt(((Ip4SubnetFilterRule) filterRule).networkAddress,
                    ((Ip4SubnetFilterRule) ipSubnetFilterRule.filterRule).networkAddress);
        }
        else
        {
            return ((Ip6SubnetFilterRule) filterRule).networkAddress
                    .CompareTo(((Ip6SubnetFilterRule) ipSubnetFilterRule.filterRule).networkAddress);
        }
    }

    /**
     * It'll compare IP address with {@link Ip4SubnetFilterRule#networkAddress} or
     * {@link Ip6SubnetFilterRule#networkAddress}.
     *
     * @param inetSocketAddress {@link InetSocketAddress} to match
     * @return 0 if IP Address match else difference index.
     */
    internal int CompareTo(IPEndPoint inetSocketAddress)
    {
        if (filterRule is Ip4SubnetFilterRule)
        {
            Ip4SubnetFilterRule ip4SubnetFilterRule = (Ip4SubnetFilterRule) filterRule;
            return CompareInt(ip4SubnetFilterRule.networkAddress, NetUtil.Ipv4AddressToInt(
                    inetSocketAddress.Address) & ip4SubnetFilterRule.subnetMask);
        }
        else
        {
            Ip6SubnetFilterRule ip6SubnetFilterRule = (Ip6SubnetFilterRule) filterRule;
            return ip6SubnetFilterRule.networkAddress
                    .CompareTo(Ip6SubnetFilterRule.IpToInt(inetSocketAddress.Address)
                            & ip6SubnetFilterRule.subnetMask);
        }
    }

    /**
     * {@code true} if every address matched by {@code other} is also matched by this rule.
     */
    // CLR: the IPv6 locals are parent6/child6 because a C# local may not share the name of a local in a nested block of
    // the same method (CS0136), which the IPv4 branch's parent/child are.
    internal bool Contains(IpSubnetFilterRule other)
    {
        if (filterRule is Ip4SubnetFilterRule)
        {
            Ip4SubnetFilterRule parent = (Ip4SubnetFilterRule) filterRule;
            Ip4SubnetFilterRule child = (Ip4SubnetFilterRule) other.filterRule;
            return (parent.subnetMask & child.subnetMask) == parent.subnetMask &&
                    (child.networkAddress & parent.subnetMask) == parent.networkAddress;
        }
        Ip6SubnetFilterRule parent6 = (Ip6SubnetFilterRule) filterRule;
        Ip6SubnetFilterRule child6 = (Ip6SubnetFilterRule) other.filterRule;
        return (parent6.subnetMask & child6.subnetMask).Equals(parent6.subnetMask) &&
                (child6.networkAddress & parent6.subnetMask).Equals(parent6.networkAddress);
    }

    /**
     * IPv4 addresses are compared as unsigned values, so 128.0.0.0 and above sort after 127.255.255.255.
     */
    // CLR: C# int is a signed 32-bit two's-complement value like Java int, so x ^ int.MinValue flips the sign bit as in
    // the source, and Int32.CompareTo has the sign of Integer.compare.
    private static int CompareInt(int x, int y)
    {
        return (x ^ int.MinValue).CompareTo(y ^ int.MinValue);
    }

    public override string ToString()
    {
        return "IpSubnetFilterRule(" + ipAddress + ", " + RuleType() + ')';
    }

    // CLR: C# does not open a nested class's private members to the enclosing class, which the source reads
    // (networkAddress, subnetMask, ipToInt, the constructors), so those members are internal.
    internal sealed class Ip4SubnetFilterRule : IIpFilterRule
    {
        internal readonly int networkAddress;
        internal readonly int subnetMask;
        private readonly IpFilterRuleType ruleType;

        // CLR: String.format("%d") is the invariant-culture decimal text.
        internal Ip4SubnetFilterRule(IPAddress ipAddress, int cidrPrefix, IpFilterRuleType ruleType)
        {
            if (cidrPrefix < 0 || cidrPrefix > 32)
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                        "IPv4 requires the subnet prefix to be in range of "
                        + "[0,32]. The prefix was: {0}", cidrPrefix));
            }

            subnetMask = PrefixToSubnetMask(cidrPrefix);
            networkAddress = NetUtil.Ipv4AddressToInt(ipAddress) & subnetMask;
            this.ruleType = ruleType;
        }

        public bool Matches(IPEndPoint remoteAddress)
        {
            IPAddress inetAddress = remoteAddress.Address;
            if (inetAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                int ipAddress = NetUtil.Ipv4AddressToInt(inetAddress);
                return (ipAddress & subnetMask) == networkAddress;
            }
            return false;
        }

        public IpFilterRuleType RuleType()
        {
            return ruleType;
        }

        // CLR: C# masks the shift count of a long with 0x3F and of an int with 0x1F, as Java does
        // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/bitwise-and-shift-operators),
        // and the unchecked long-to-int conversion keeps the low 32 bits ("the source value is truncated by discarding
        // its "extra" most significant bits",
        // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions), so the
        // masks equal the Java ints, including the negative ones (prefix 1..32) and 0 for prefix 0.
        private static int PrefixToSubnetMask(int cidrPrefix)
        {
            /*
             * Perform the shift on a long and downcast it to int afterwards.
             * This is necessary to handle a cidrPrefix of zero correctly.
             * The left shift operator on an int only uses the five least
             * significant bits of the right-hand operand. Thus -1 << 32 evaluates
             * to -1 instead of 0. The left shift operator applied on a long
             * uses the six least significant bits.
             *
             * Also see https://github.com/netty/netty/issues/2767
             */
            return unchecked((int) (-1L << 32 - cidrPrefix));
        }
    }

    // CLR: java.math.BigInteger is System.Numerics.BigInteger. Java's operations "behave as if BigIntegers were
    // represented in two's-complement notation" (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/math/BigInteger.html),
    // and the CLR & works "as if they were both in two's complement representation with virtual sign extension"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.numerics.biginteger.op_bitwiseand?view=net-10.0) while << "preserves
    // the sign of the original BigInteger value" (https://learn.microsoft.com/en-us/dotnet/api/system.numerics.biginteger.op_leftshift?view=net-10.0),
    // so MINUS_ONE << n is -2^n and every mask, network address and comparison equals the Java value.
    // new BigInteger(1, octets) (a positive big-endian magnitude) is new BigInteger(octets, isUnsigned: true,
    // isBigEndian: true) (https://learn.microsoft.com/en-us/dotnet/api/system.numerics.biginteger.-ctor?view=net-10.0),
    // and getAddress() is GetAddressBytes(), "a copy of the IPAddress as an array of bytes in network order"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.getaddressbytes?view=net-10.0).
    internal sealed class Ip6SubnetFilterRule : IIpFilterRule
    {
        private static readonly BigInteger MINUS_ONE = new BigInteger(-1);

        internal readonly BigInteger networkAddress;
        internal readonly BigInteger subnetMask;
        private readonly IpFilterRuleType ruleType;

        // CLR: String.format("%d") is the invariant-culture decimal text.
        internal Ip6SubnetFilterRule(IPAddress ipAddress, int cidrPrefix, IpFilterRuleType ruleType)
        {
            if (cidrPrefix < 0 || cidrPrefix > 128)
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture,
                        "IPv6 requires the subnet prefix to be in range of "
                        + "[0,128]. The prefix was: {0}", cidrPrefix));
            }

            subnetMask = PrefixToSubnetMask(cidrPrefix);
            networkAddress = IpToInt(ipAddress) & subnetMask;
            this.ruleType = ruleType;
        }

        public bool Matches(IPEndPoint remoteAddress)
        {
            IPAddress inetAddress = remoteAddress.Address;
            if (inetAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                BigInteger ipAddress = IpToInt(inetAddress);
                return (ipAddress & subnetMask).Equals(subnetMask) || (ipAddress & subnetMask).Equals(networkAddress);
            }
            return false;
        }

        public IpFilterRuleType RuleType()
        {
            return ruleType;
        }

        internal static BigInteger IpToInt(IPAddress ipAddress)
        {
            byte[] octets = ipAddress.GetAddressBytes();
            Debug.Assert(octets.Length == 16);

            return new BigInteger(octets, isUnsigned: true, isBigEndian: true);
        }

        private static BigInteger PrefixToSubnetMask(int cidrPrefix)
        {
            return MINUS_ONE << 128 - cidrPrefix;
        }
    }
}
