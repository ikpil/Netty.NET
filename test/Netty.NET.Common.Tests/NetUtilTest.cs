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

using System.Collections.Generic;
using System.Net;
using System.Text;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.NetUtil;

namespace Netty.NET.Common.Tests;

public class NetUtilTest
{
    private class TestMap : Dictionary<string, string>
    {
        public TestMap(params string[] values)
        {
            for (int i = 0; i < values.Length; i += 2)
            {
                string key = values[i];
                string value = values[i + 1];
                Add(key, value);
            }
        }
    }

    private static IReadOnlyDictionary<string, string> validIpV4Hosts = new TestMap(
        "192.168.1.0", "c0a80100",
        "10.255.255.254", "0afffffe",
        "172.18.5.4", "ac120504",
        "0.0.0.0", "00000000",
        "127.0.0.1", "7f000001",
        "255.255.255.255", "ffffffff",
        "1.2.3.4", "01020304");

    private static IReadOnlyDictionary<string, string> invalidIpV4Hosts = new TestMap(
        "1.256.3.4", null,
        "256.0.0.1", null,
        "1.1.1.1.1", null,
        "x.255.255.255", null,
        "0.1:0.0", null,
        "0.1.0.0:", null,
        "127.0.0.", null,
        "1.2..4", null,
        "192.0.1", null,
        "192.0.1.1.1", null,
        "192.0.1.a", null,
        "19a.0.1.1", null,
        "a.0.1.1", null,
        ".0.1.1", null,
        "127.0.0", null,
        "192.0.1.256", null,
        "0.0.200.259", null,
        "1.1.-1.1", null,
        "1.1. 1.1", null,
        "1.1.1.1 ", null,
        "1.1.+1.1", null,
        "0.0x1.0.255", null,
        "0.01x.0.255", null,
        "0.x01.0.255", null,
        "0.-.0.0", null,
        "0..0.0", null,
        "0.A.0.0", null,
        "0.1111.0.0", null,
        "...", null);

    private static IReadOnlyDictionary<string, string> validIpV6Hosts = new TestMap(
        "::ffff:5.6.7.8", "00000000000000000000ffff05060708",
        "fdf8:f53b:82e4::53", "fdf8f53b82e400000000000000000053",
        "fe80::200:5aee:feaa:20a2", "fe8000000000000002005aeefeaa20a2",
        "2001::1", "20010000000000000000000000000001",
        "2001:0000:4136:e378:8000:63bf:3fff:fdd2", "200100004136e378800063bf3ffffdd2",
        "2001:0002:6c::430", "20010002006c00000000000000000430",
        "2001:10:240:ab::a", "20010010024000ab000000000000000a",
        "2002:cb0a:3cdd:1::1", "2002cb0a3cdd00010000000000000001",
        "2001:db8:8:4::2", "20010db8000800040000000000000002",
        "ff01:0:0:0:0:0:0:2", "ff010000000000000000000000000002",
        "[fdf8:f53b:82e4::53]", "fdf8f53b82e400000000000000000053",
        "[fe80::200:5aee:feaa:20a2]", "fe8000000000000002005aeefeaa20a2",
        "[2001::1]", "20010000000000000000000000000001",
        "[2001:0000:4136:e378:8000:63bf:3fff:fdd2]", "200100004136e378800063bf3ffffdd2",
        "0:1:2:3:4:5:6:789a", "0000000100020003000400050006789a",
        "0:1:2:3::f", "0000000100020003000000000000000f",
        "0:0:0:0:0:0:10.0.0.1", "00000000000000000000ffff0a000001",
        "0:0:0:0:0::10.0.0.1", "00000000000000000000ffff0a000001",
        "0:0:0:0::10.0.0.1", "00000000000000000000ffff0a000001",
        "::0:0:0:0:0:10.0.0.1", "00000000000000000000ffff0a000001",
        "0::0:0:0:0:10.0.0.1", "00000000000000000000ffff0a000001",
        "0:0::0:0:0:10.0.0.1", "00000000000000000000ffff0a000001",
        "0:0:0::0:0:10.0.0.1", "00000000000000000000ffff0a000001",
        "0:0:0:0::0:10.0.0.1", "00000000000000000000ffff0a000001",
        "0:0:0:0:0:ffff:10.0.0.1", "00000000000000000000ffff0a000001",
        "::ffff:192.168.0.1", "00000000000000000000ffffc0a80001",
        // Test if various interface names after the percent sign are recognized.
        "[::1%1]", "00000000000000000000000000000001",
        "[::1%eth0]", "00000000000000000000000000000001",
        "[::1%%]", "00000000000000000000000000000001",
        "0:0:0:0:0:ffff:10.0.0.1%", "00000000000000000000ffff0a000001",
        "0:0:0:0:0:ffff:10.0.0.1%1", "00000000000000000000ffff0a000001",
        "[0:0:0:0:0:ffff:10.0.0.1%1]", "00000000000000000000ffff0a000001",
        "[0:0:0:0:0::10.0.0.1%1]", "00000000000000000000ffff0a000001",
        "[::0:0:0:0:ffff:10.0.0.1%1]", "00000000000000000000ffff0a000001",
        "::0:0:0:0:ffff:10.0.0.1%1", "00000000000000000000ffff0a000001",
        "::1%1", "00000000000000000000000000000001",
        "::1%eth0", "00000000000000000000000000000001",
        "::1%%", "00000000000000000000000000000001",
        // Tests with leading or trailing compression
        "0:0:0:0:0:0:0::", "00000000000000000000000000000000",
        "0:0:0:0:0:0::", "00000000000000000000000000000000",
        "0:0:0:0:0::", "00000000000000000000000000000000",
        "0:0:0:0::", "00000000000000000000000000000000",
        "0:0:0::", "00000000000000000000000000000000",
        "0:0::", "00000000000000000000000000000000",
        "0::", "00000000000000000000000000000000",
        "::", "00000000000000000000000000000000",
        "::0", "00000000000000000000000000000000",
        "::0:0", "00000000000000000000000000000000",
        "::0:0:0", "00000000000000000000000000000000",
        "::0:0:0:0", "00000000000000000000000000000000",
        "::0:0:0:0:0", "00000000000000000000000000000000",
        "::0:0:0:0:0:0", "00000000000000000000000000000000",
        "::0:0:0:0:0:0:0", "00000000000000000000000000000000");

    private static IReadOnlyDictionary<string, string> invalidIpV6Hosts = new TestMap(
        // Test method with garbage.
        "Obvious Garbage", null,
        // Test method with preferred style, too many :
        "0:1:2:3:4:5:6:7:8", null,
        // Test method with preferred style, not enough :
        "0:1:2:3:4:5:6", null,
        // Test method with preferred style, bad digits.
        "0:1:2:3:4:5:6:x", null,
        // Test method with preferred style, adjacent :
        "0:1:2:3:4:5:6::7", null,
        // Too many : separators trailing
        "0:1:2:3:4:5:6:7::", null,
        // Too many : separators leading
        "::0:1:2:3:4:5:6:7", null,
        // Too many : separators trailing
        "1:2:3:4:5:6:7:", null,
        // Too many : separators leading
        ":1:2:3:4:5:6:7", null,
        // Compression with : separators trailing
        "0:1:2:3:4:5::7:", null,
        "0:1:2:3:4::7:", null,
        "0:1:2:3::7:", null,
        "0:1:2::7:", null,
        "0:1::7:", null,
        "0::7:", null,
        // Compression at start with : separators trailing
        "::0:1:2:3:4:5:7:", null,
        "::0:1:2:3:4:7:", null,
        "::0:1:2:3:7:", null,
        "::0:1:2:7:", null,
        "::0:1:7:", null,
        "::7:", null,
        // The : separators leading and trailing
        ":1:2:3:4:5:6:7:", null,
        ":1:2:3:4:5:6:", null,
        ":1:2:3:4:5:", null,
        ":1:2:3:4:", null,
        ":1:2:3:", null,
        ":1:2:", null,
        ":1:", null,
        // Compression with : separators leading
        ":1::2:3:4:5:6:7", null,
        ":1::3:4:5:6:7", null,
        ":1::4:5:6:7", null,
        ":1::5:6:7", null,
        ":1::6:7", null,
        ":1::7", null,
        ":1:2:3:4:5:6::7", null,
        ":1:3:4:5:6::7", null,
        ":1:4:5:6::7", null,
        ":1:5:6::7", null,
        ":1:6::7", null,
        ":1::", null,
        // Compression trailing with : separators leading
        ":1:2:3:4:5:6:7::", null,
        ":1:3:4:5:6:7::", null,
        ":1:4:5:6:7::", null,
        ":1:5:6:7::", null,
        ":1:6:7::", null,
        ":1:7::", null,
        // Double compression
        "1::2:3:4:5:6::", null,
        "::1:2:3:4:5::6", null,
        "::1:2:3:4:5:6::", null,
        "::1:2:3:4:5::", null,
        "::1:2:3:4::", null,
        "::1:2:3::", null,
        "::1:2::", null,
        "::0::", null,
        "12::0::12", null,
        // Too many : separators leading 0
        "0::1:2:3:4:5:6:7", null,
        // Test method with preferred style, too many digits.
        "0:1:2:3:4:5:6:789abcdef", null,
        // Test method with compressed style, bad digits.
        "0:1:2:3::x", null,
        // Test method with compressed style, too many adjacent :
        "0:1:2:::3", null,
        // Test method with compressed style, too many digits.
        "0:1:2:3::abcde", null,
        // Test method with compressed style, not enough :
        "0:1", null,
        // Test method with ipv4 style, bad ipv6 digits.
        "0:0:0:0:0:x:10.0.0.1", null,
        // Test method with ipv4 style, bad ipv4 digits.
        "0:0:0:0:0:0:10.0.0.x", null,
        // Test method with ipv4 style, too many ipv6 digits.
        "0:0:0:0:0:00000:10.0.0.1", null,
        // Test method with ipv4 style, too many :
        "0:0:0:0:0:0:0:10.0.0.1", null,
        // Test method with ipv4 style, not enough :
        "0:0:0:0:0:10.0.0.1", null,
        // Test method with ipv4 style, too many .
        "0:0:0:0:0:0:10.0.0.0.1", null,
        // Test method with ipv4 style, not enough .
        "0:0:0:0:0:0:10.0.1", null,
        // Test method with ipv4 style, adjacent .
        "0:0:0:0:0:0:10..0.0.1", null,
        // Test method with ipv4 style, leading .
        "0:0:0:0:0:0:.0.0.1", null,
        // Test method with ipv4 style, leading .
        "0:0:0:0:0:0:.10.0.0.1", null,
        // Test method with ipv4 style, trailing .
        "0:0:0:0:0:0:10.0.0.", null,
        // Test method with ipv4 style, trailing .
        "0:0:0:0:0:0:10.0.0.1.", null,
        // Test method with compressed ipv4 style, bad ipv6 digits.
        "::fffx:192.168.0.1", null,
        // Test method with compressed ipv4 style, bad ipv4 digits.
        "::ffff:192.168.0.x", null,
        // Test method with compressed ipv4 style, too many adjacent :
        ":::ffff:192.168.0.1", null,
        // Test method with compressed ipv4 style, too many ipv6 digits.
        "::fffff:192.168.0.1", null,
        // Test method with compressed ipv4 style, too many ipv4 digits.
        "::ffff:1923.168.0.1", null,
        // Test method with compressed ipv4 style, not enough :
        ":ffff:192.168.0.1", null,
        // Test method with compressed ipv4 style, too many .
        "::ffff:192.168.0.1.2", null,
        // Test method with compressed ipv4 style, not enough .
        "::ffff:192.168.0", null,
        // Test method with compressed ipv4 style, adjacent .
        "::ffff:192.168..0.1", null,
        // Test method, bad ipv6 digits.
        "x:0:0:0:0:0:10.0.0.1", null,
        // Test method, bad ipv4 digits.
        "0:0:0:0:0:0:x.0.0.1", null,
        // Test method, too many ipv6 digits.
        "00000:0:0:0:0:0:10.0.0.1", null,
        // Test method, too many ipv4 digits.
        "0:0:0:0:0:0:10.0.0.1000", null,
        // Test method, too many :
        "0:0:0:0:0:0:0:10.0.0.1", null,
        // Test method, not enough :
        "0:0:0:0:0:10.0.0.1", null,
        // Test method, out of order trailing :
        "0:0:0:0:0:10.0.0.1:", null,
        // Test method, out of order leading :
        ":0:0:0:0:0:10.0.0.1", null,
        // Test method, out of order leading :
        "0:0:0:0::10.0.0.1:", null,
        // Test method, out of order trailing :
        ":0:0:0:0::10.0.0.1", null,
        // Test method, too many .
        "0:0:0:0:0:0:10.0.0.0.1", null,
        // Test method, not enough .
        "0:0:0:0:0:0:10.0.1", null,
        // Test method, adjacent .
        "0:0:0:0:0:0:10.0.0..1", null,
        // Empty contents
        "", null,
        // Invalid single compression
        ":", null,
        ":::", null,
        // Trailing : (max number of : = 8)
        "2001:0:4136:e378:8000:63bf:3fff:fdd2:", null,
        // Leading : (max number of : = 8)
        ":aaaa:bbbb:cccc:dddd:eeee:ffff:1111:2222", null,
        // Invalid character
        "1234:2345:3456:4567:5678:6789::X890", null,
        // Trailing . in IPv4
        "::ffff:255.255.255.255.", null,
        // To many characters in IPv4
        "::ffff:0.0.1111.0", null,
        // Test method, adjacent .
        "::ffff:0.0..0", null,
        // Not enough IPv4 entries trailing .
        "::ffff:127.0.0.", null,
        // Invalid trailing IPv4 character
        "::ffff:127.0.0.a", null,
        // Invalid leading IPv4 character
        "::ffff:a.0.0.1", null,
        // Invalid middle IPv4 character
        "::ffff:127.a.0.1", null,
        // Invalid middle IPv4 character
        "::ffff:127.0.a.1", null,
        // Not enough IPv4 entries no trailing .
        "::ffff:1.2.4", null,
        // Extra IPv4 entry
        "::ffff:192.168.0.1.255", null,
        // Not enough IPv6 content
        ":ffff:192.168.0.1.255", null,
        // Intermixed IPv4 and IPv6 symbols
        "::ffff:255.255:255.255.", null,
        // Invalid IPv4 mapped address - invalid ipv4 separator
        "0:0:0::0:0:00f.0.0.1", null,
        // Invalid IPv4 mapped address - not enough f's
        "0:0:0:0:0:fff:1.0.0.1", null,
        // Invalid IPv4 mapped address - not IPv4 mapped, not IPv4 compatible
        "0:0:0:0:0:ff00:1.0.0.1", null,
        // Invalid IPv4 mapped address - not IPv4 mapped, not IPv4 compatible
        "0:0:0:0:0:ff:1.0.0.1", null,
        // Invalid IPv4 mapped address - too many f's
        "0:0:0:0:0:fffff:1.0.0.1", null,
        // Invalid IPv4 mapped address - too many bytes (too many 0's)
        "0:0:0:0:0:0:ffff:1.0.0.1", null,
        // Invalid IPv4 mapped address - too many bytes (too many 0's)
        "::0:0:0:0:0:ffff:1.0.0.1", null,
        // Invalid IPv4 mapped address - too many bytes (too many 0's)
        "0:0:0:0:0:0::1.0.0.1", null,
        // Invalid IPv4 mapped address - too many bytes (too many 0's)
        "0:0:0:0:0:00000:1.0.0.1", null,
        // Invalid IPv4 mapped address - too few bytes (not enough 0's)
        "0:0:0:0:ffff:1.0.0.1", null,
        // Invalid IPv4 mapped address - too few bytes (not enough 0's)
        "ffff:192.168.0.1", null,
        // Invalid IPv4 mapped address - 0's after the mapped ffff indicator
        "0:0:0:0:0:ffff::10.0.0.1", null,
        // Invalid IPv4 mapped address - 0's after the mapped ffff indicator
        "0:0:0:0:ffff::10.0.0.1", null,
        // Invalid IPv4 mapped address - 0's after the mapped ffff indicator
        "0:0:0:ffff::10.0.0.1", null,
        // Invalid IPv4 mapped address - 0's after the mapped ffff indicator
        "0:0:ffff::10.0.0.1", null,
        // Invalid IPv4 mapped address - 0's after the mapped ffff indicator
        "0:ffff::10.0.0.1", null,
        // Invalid IPv4 mapped address - 0's after the mapped ffff indicator
        "ffff::10.0.0.1", null,
        // Invalid IPv4 mapped address - not all 0's before the mapped separator
        "1:0:0:0:0:ffff:10.0.0.1", null,
        // Address that is similar to IPv4 mapped, but is invalid
        "0:0:0:0:ffff:ffff:1.0.0.1", null,
        // Valid number of separators, but invalid IPv4 format
        "::1:2:3:4:5:6.7.8.9", null,
        // Too many digits
        "0:0:0:0:0:0:ffff:10.0.0.1", null,
        // Invalid IPv4 format
        ":1.2.3.4", null,
        // Invalid IPv4 format
        "::.2.3.4", null,
        // Invalid IPv4 format
        "::ffff:0.1.2.", null);

    private static byte IToB(int a)
    {
        return unchecked((byte)a);
    }

    private class TestMap2 : Dictionary<byte[], string>
    {
        public TestMap2()
        {
            // From the RFC 5952 https://tools.ietf.org/html/rfc5952#section-4
            Add(new byte[]
                {
                    32, 1, 13, IToB(-72),
                    0, 0, 0, 0,
                    0, 0, 0, 0,
                    0, 0, 0, 1
                },
                "2001:db8::1");
            Add(new byte[]
                {
                    32, 1, 13, IToB(-72),
                    0, 0, 0, 0,
                    0, 0, 0, 0,
                    0, 2, 0, 1
                },
                "2001:db8::2:1");
            Add(new byte[]
                {
                    32, 1, 13, IToB(-72),
                    0, 0, 0, 1,
                    0, 1, 0, 1,
                    0, 1, 0, 1
                },
                "2001:db8:0:1:1:1:1:1");

            // Other examples
            Add(new byte[]
                {
                    32, 1, 13, IToB(-72),
                    0, 0, 0, 0,
                    0, 0, 0, 0,
                    0, 2, 0, 1
                },
                "2001:db8::2:1");
            Add(new byte[]
                {
                    32, 1, 0, 0,
                    0, 0, 0, 1,
                    0, 0, 0, 0,
                    0, 0, 0, 1
                },
                "2001:0:0:1::1");
            Add(new byte[]
                {
                    32, 1, 13, IToB(-72),
                    0, 0, 0, 0,
                    0, 1, 0, 0,
                    0, 0, 0, 1
                },
                "2001:db8::1:0:0:1");
            Add(new byte[]
                {
                    32, 1, 13, IToB(-72),
                    0, 0, 0, 0,
                    0, 1, 0, 0,
                    0, 0, 0, 0
                },
                "2001:db8:0:0:1::");
            Add(new byte[]
                {
                    32, 1, 13, IToB(-72),
                    0, 0, 0, 0,
                    0, 0, 0, 0,
                    0, 2, 0, 0
                },
                "2001:db8::2:0");
            Add(new byte[]
                {
                    0, 0, 0, 0,
                    0, 0, 0, 0,
                    0, 0, 0, 0,
                    0, 0, 0, 1
                },
                "::1");
            Add(new byte[]
                {
                    0, 0, 0, 0,
                    0, 0, 0, 1,
                    0, 0, 0, 0,
                    0, 0, 0, 1
                },
                "::1:0:0:0:1");
            Add(new byte[]
                {
                    0, 0, 0, 0,
                    1, 0, 0, 1,
                    0, 0, 0, 0,
                    1, 0, 0, 0
                },
                "::100:1:0:0:100:0");
            Add(new byte[]
                {
                    32, 1, 0, 0,
                    65, 54, IToB(-29), 120,
                    IToB(-128), 0, 99, IToB(-65),
                    63, IToB(-1), IToB(-3), IToB(-46),
                },
                "2001:0:4136:e378:8000:63bf:3fff:fdd2");
            Add(new byte[]
                {
                    IToB(-86), IToB(-86), IToB(-69), IToB(-69),
                    IToB(-52), IToB(-52), IToB(-35), IToB(-35),
                    IToB(-18), IToB(-18), IToB(-1), IToB(-1),
                    17, 17, 34, 34
                },
                "aaaa:bbbb:cccc:dddd:eeee:ffff:1111:2222");
            Add(new byte[]
                {
                    0, 0, 0, 0,
                    0, 0, 0, 0,
                    0, 0, 0, 0,
                    0, 0, 0, 0
                },
                "::");
        }
    };

    private static IReadOnlyDictionary<byte[], string> ipv6ToAddressStrings = new TestMap2();

    private static IReadOnlyDictionary<string, string> ipv4MappedToIPv6AddressStrings = new TestMap(
        // IPv4 addresses
        "255.255.255.255", "::ffff:255.255.255.255",
        "0.0.0.0", "::ffff:0.0.0.0",
        "127.0.0.1", "::ffff:127.0.0.1",
        "1.2.3.4", "::ffff:1.2.3.4",
        "192.168.0.1", "::ffff:192.168.0.1",

        // IPv4 compatible addresses are deprecated [1], so we don't support outputting them, but we do support
        // parsing them into IPv4 mapped addresses. These values are treated the same as a plain IPv4 address above.
        // [1] https://tools.ietf.org/html/rfc4291#section-2.5.5.1
        "0:0:0:0:0:0:255.254.253.252", "::ffff:255.254.253.252",
        "0:0:0:0:0::1.2.3.4", "::ffff:1.2.3.4",
        "0:0:0:0::1.2.3.4", "::ffff:1.2.3.4",
        "::0:0:0:0:0:1.2.3.4", "::ffff:1.2.3.4",
        "0::0:0:0:0:1.2.3.4", "::ffff:1.2.3.4",
        "0:0::0:0:0:1.2.3.4", "::ffff:1.2.3.4",
        "0:0:0::0:0:1.2.3.4", "::ffff:1.2.3.4",
        "0:0:0:0::0:1.2.3.4", "::ffff:1.2.3.4",
        "0:0:0:0:0::1.2.3.4", "::ffff:1.2.3.4",
        "::0:0:0:0:1.2.3.4", "::ffff:1.2.3.4",
        "0::0:0:0:1.2.3.4", "::ffff:1.2.3.4",
        "0:0::0:0:1.2.3.4", "::ffff:1.2.3.4",
        "0:0:0::0:1.2.3.4", "::ffff:1.2.3.4",
        "0:0:0:0::1.2.3.4", "::ffff:1.2.3.4",
        "::0:0:0:0:1.2.3.4", "::ffff:1.2.3.4",
        "0::0:0:0:1.2.3.4", "::ffff:1.2.3.4",
        "0:0::0:0:1.2.3.4", "::ffff:1.2.3.4",
        "0:0:0::0:1.2.3.4", "::ffff:1.2.3.4",
        "0:0:0:0::1.2.3.4", "::ffff:1.2.3.4",
        "::0:0:0:1.2.3.4", "::ffff:1.2.3.4",
        "0::0:0:1.2.3.4", "::ffff:1.2.3.4",
        "0:0::0:1.2.3.4", "::ffff:1.2.3.4",
        "0:0:0::1.2.3.4", "::ffff:1.2.3.4",
        "::0:0:1.2.3.4", "::ffff:1.2.3.4",
        "0::0:1.2.3.4", "::ffff:1.2.3.4",
        "0:0::1.2.3.4", "::ffff:1.2.3.4",
        "::0:1.2.3.4", "::ffff:1.2.3.4",
        "::1.2.3.4", "::ffff:1.2.3.4",

        // IPv4 mapped (fully specified)
        "0:0:0:0:0:ffff:1.2.3.4", "::ffff:1.2.3.4",

        // IPv6 addresses
        // Fully specified
        "2001:0:4136:e378:8000:63bf:3fff:fdd2", "2001:0:4136:e378:8000:63bf:3fff:fdd2",
        "aaaa:bbbb:cccc:dddd:eeee:ffff:1111:2222", "aaaa:bbbb:cccc:dddd:eeee:ffff:1111:2222",
        "0:0:0:0:0:0:0:0", "::",
        "0:0:0:0:0:0:0:1", "::1",

        // Compressing at the beginning
        "::1:0:0:0:1", "::1:0:0:0:1",
        "::1:ffff:ffff", "::1:ffff:ffff",
        "::", "::",
        "::1", "::1",
        "::ffff", "::ffff",
        "::ffff:0", "::ffff:0",
        "::ffff:ffff", "::ffff:ffff",
        "::0987:9876:8765", "::987:9876:8765",
        "::0987:9876:8765:7654", "::987:9876:8765:7654",
        "::0987:9876:8765:7654:6543", "::987:9876:8765:7654:6543",
        "::0987:9876:8765:7654:6543:5432", "::987:9876:8765:7654:6543:5432",
        // Note the compression is removed (rfc 5952 section 4.2.2)
        "::0987:9876:8765:7654:6543:5432:3210", "0:987:9876:8765:7654:6543:5432:3210",

        // Compressing at the end
        // Note the compression is removed (rfc 5952 section 4.2.2)
        "2001:db8:abcd:bcde:cdef:def1:ef12::", "2001:db8:abcd:bcde:cdef:def1:ef12:0",
        "2001:db8:abcd:bcde:cdef:def1::", "2001:db8:abcd:bcde:cdef:def1::",
        "2001:db8:abcd:bcde:cdef::", "2001:db8:abcd:bcde:cdef::",
        "2001:db8:abcd:bcde::", "2001:db8:abcd:bcde::",
        "2001:db8:abcd::", "2001:db8:abcd::",
        "2001:1234::", "2001:1234::",
        "2001::", "2001::",
        "0::", "::",

        // Compressing in the middle
        "1234:2345::7890", "1234:2345::7890",
        "1234::2345:7890", "1234::2345:7890",
        "1234:2345:3456::7890", "1234:2345:3456::7890",
        "1234:2345::3456:7890", "1234:2345::3456:7890",
        "1234::2345:3456:7890", "1234::2345:3456:7890",
        "1234:2345:3456:4567::7890", "1234:2345:3456:4567::7890",
        "1234:2345:3456::4567:7890", "1234:2345:3456::4567:7890",
        "1234:2345::3456:4567:7890", "1234:2345::3456:4567:7890",
        "1234::2345:3456:4567:7890", "1234::2345:3456:4567:7890",
        "1234:2345:3456:4567:5678::7890", "1234:2345:3456:4567:5678::7890",
        "1234:2345:3456:4567::5678:7890", "1234:2345:3456:4567::5678:7890",
        "1234:2345:3456::4567:5678:7890", "1234:2345:3456::4567:5678:7890",
        "1234:2345::3456:4567:5678:7890", "1234:2345::3456:4567:5678:7890",
        "1234::2345:3456:4567:5678:7890", "1234::2345:3456:4567:5678:7890",
        // Note the compression is removed (rfc 5952 section 4.2.2)
        "1234:2345:3456:4567:5678:6789::7890", "1234:2345:3456:4567:5678:6789:0:7890",
        // Note the compression is removed (rfc 5952 section 4.2.2)
        "1234:2345:3456:4567:5678::6789:7890", "1234:2345:3456:4567:5678:0:6789:7890",
        // Note the compression is removed (rfc 5952 section 4.2.2)
        "1234:2345:3456:4567::5678:6789:7890", "1234:2345:3456:4567:0:5678:6789:7890",
        // Note the compression is removed (rfc 5952 section 4.2.2)
        "1234:2345:3456::4567:5678:6789:7890", "1234:2345:3456:0:4567:5678:6789:7890",
        // Note the compression is removed (rfc 5952 section 4.2.2)
        "1234:2345::3456:4567:5678:6789:7890", "1234:2345:0:3456:4567:5678:6789:7890",
        // Note the compression is removed (rfc 5952 section 4.2.2)
        "1234::2345:3456:4567:5678:6789:7890", "1234:0:2345:3456:4567:5678:6789:7890",

        // IPv4 mapped addresses
        "::ffff:255.255.255.255", "::ffff:255.255.255.255",
        "::ffff:0.0.0.0", "::ffff:0.0.0.0",
        "::ffff:127.0.0.1", "::ffff:127.0.0.1",
        "::ffff:1.2.3.4", "::ffff:1.2.3.4",
        "::ffff:192.168.0.1", "::ffff:192.168.0.1");

    [Fact]
    public void testLocalhost()
    {
        Assert.NotNull(LOCALHOST);
    }

    [Fact]
    public void testLoopback()
    {
        Assert.NotNull(LOOPBACK_IF);
    }

    [Fact]
    public void testIsValidIpV4Address()
    {
        foreach (string host in validIpV4Hosts.Keys)
        {
            Assert.True(isValidIpV4Address(host), host);
        }

        foreach (string host in invalidIpV4Hosts.Keys)
        {
            Assert.False(isValidIpV4Address(host), host);
        }
    }

    [Fact]
    public void testIsValidIpV6Address()
    {
        foreach (string host in validIpV6Hosts.Keys)
        {
            Assert.True(isValidIpV6Address(host), host);
            if (host.charAt(0) != '[' && !host.Contains("%"))
            {
                Assert.NotNull(getByName(host, true)); //, host);

                string hostMod = '[' + host + ']';
                Assert.True(isValidIpV6Address(hostMod), hostMod);

                hostMod = host + '%';
                Assert.True(isValidIpV6Address(hostMod), hostMod);

                hostMod = host + "%eth1";
                Assert.True(isValidIpV6Address(hostMod), hostMod);

                hostMod = '[' + host + "%]";
                Assert.True(isValidIpV6Address(hostMod), hostMod);

                hostMod = '[' + host + "%1]";
                Assert.True(isValidIpV6Address(hostMod), hostMod);

                hostMod = '[' + host + "]%";
                Assert.False(isValidIpV6Address(hostMod), hostMod);

                hostMod = '[' + host + "]%1";
                Assert.False(isValidIpV6Address(hostMod), hostMod);
            }
        }

        foreach (string host in invalidIpV6Hosts.Keys)
        {
            Assert.False(isValidIpV6Address(host), host);
            Assert.Null(getByName(host)); //, host);

            string hostMod = '[' + host + ']';
            Assert.False(isValidIpV6Address(hostMod), hostMod);

            hostMod = host + '%';
            Assert.False(isValidIpV6Address(hostMod), hostMod);

            hostMod = host + "%eth1";
            Assert.False(isValidIpV6Address(hostMod), hostMod);

            hostMod = '[' + host + "%]";
            Assert.False(isValidIpV6Address(hostMod), hostMod);

            hostMod = '[' + host + "%1]";
            Assert.False(isValidIpV6Address(hostMod), hostMod);

            hostMod = '[' + host + "]%";
            Assert.False(isValidIpV6Address(hostMod), hostMod);

            hostMod = '[' + host + "]%1";
            Assert.False(isValidIpV6Address(hostMod), hostMod);

            hostMod = host + ']';
            Assert.False(isValidIpV6Address(hostMod), hostMod);

            hostMod = '[' + host;
            Assert.False(isValidIpV6Address(hostMod), hostMod);
        }
    }

    [Fact]
    public void testCreateByteArrayFromIpAddressString()
    {
        foreach (var e in validIpV4Hosts)
        {
            string ip = e.Key;
            assertHexDumpEquals(e.Value, createByteArrayFromIpAddressString(ip), ip);
        }

        foreach (var e in invalidIpV4Hosts)
        {
            string ip = e.Key;
            assertHexDumpEquals(e.Value, createByteArrayFromIpAddressString(ip), ip);
        }

        foreach (var e in validIpV6Hosts)
        {
            string ip = e.Key;
            assertHexDumpEquals(e.Value, createByteArrayFromIpAddressString(ip), ip);
        }

        foreach (var e in invalidIpV6Hosts)
        {
            string ip = e.Key;
            assertHexDumpEquals(e.Value, createByteArrayFromIpAddressString(ip), ip);
        }
    }

    [Fact]
    public void testBytesToIpAddress()
    {
        foreach (var e in validIpV4Hosts)
        {
            Assert.Equal(e.Key, bytesToIpAddress(createByteArrayFromIpAddressString(e.Key)));
            Assert.Equal(e.Key, bytesToIpAddress(validIpV4ToBytes(e.Key)));
        }

        foreach (var testEntry in ipv6ToAddressStrings)
        {
            Assert.Equal(testEntry.Value, bytesToIpAddress(testEntry.Key));
        }
    }

    [Fact]
    public void testBytesToIpAddressWithOffset()
    {
        foreach (var e in validIpV4Hosts)
        {
            byte[] bytes = copyWithOffset(createByteArrayFromIpAddressString(e.Key));
            Assert.Equal(e.Key, bytesToIpAddress(bytes, 1, bytes.Length - 2));

            byte[] bytes2 = copyWithOffset(createByteArrayFromIpAddressString(e.Key));
            Assert.Equal(e.Key, bytesToIpAddress(bytes2, 1, bytes2.Length - 2));
        }

        foreach (var testEntry in ipv6ToAddressStrings)
        {
            byte[] bytes = copyWithOffset(testEntry.Key);
            Assert.Equal(testEntry.Value, bytesToIpAddress(bytes, 1, bytes.Length - 2));
        }
    }

    private static byte[] copyWithOffset(byte[] bytes)
    {
        if (bytes == null)
        {
            return null;
        }

        byte[] array = new byte[bytes.Length + 2];
        Arrays.arraycopy(bytes, 0, array, 1, bytes.Length);
        return array;
    }

    [Fact]
    public void testIp6AddressToString()
    {
        foreach (var testEntry in ipv6ToAddressStrings)
        {
            Assert.Equal(testEntry.Value, toAddressString(new IPAddress(testEntry.Key)));
        }
    }

    [Fact]
    public void testIp4AddressToString()
    {
        foreach (var e in validIpV4Hosts)
        {
            Assert.Equal(e.Key, toAddressString(new IPAddress(unhex(e.Value))));
        }
    }

    [Fact]
    public void testIPv4ToInt()
    {
        Assert.Equal(2130706433, ipv4AddressToInt((IPAddress)IPAddress.Parse("127.0.0.1")));
        Assert.Equal(-1062731519, ipv4AddressToInt((IPAddress)IPAddress.Parse("192.168.1.1")));
    }

    [Fact]
    public void testIpv4MappedIp6GetByName()
    {
        foreach (var testEntry in ipv4MappedToIPv6AddressStrings)
        {
            string srcIp = testEntry.Key;
            string dstIp = testEntry.Value;
            IPAddress inet6Address = getByName(srcIp, true);
            Assert.NotNull(inet6Address); //, srcIp + ", " + dstIp);
            Assert.Equal(dstIp, toAddressString(inet6Address, true)); //, srcIp);
        }
    }

    [Fact]
    public void testInvalidIpv4MappedIp6GetByName()
    {
        foreach (string host in invalidIpV4Hosts.Keys)
        {
            Assert.Null(getByName(host, true)); //, host);
        }

        foreach (string host in invalidIpV6Hosts.Keys)
        {
            Assert.Null(getByName(host, true)); //, host);
        }
    }

    [Fact]
    public void testIp6InetSocketAddressToString()
    {
        foreach (var testEntry in ipv6ToAddressStrings)
        {
            Assert.Equal('[' + testEntry.Value + "]:9999",
                toSocketAddressString(new IPEndPoint(new IPAddress(testEntry.Key), 9999)));
        }
    }

    [Fact]
    public void testIp4SocketAddressToString()
    {
        foreach (var e in validIpV4Hosts)
        {
            Assert.Equal(e.Key + ":9999",
                toSocketAddressString(new IPEndPoint(new IPAddress(unhex(e.Value)), 9999)));
        }
    }

    private static void assertHexDumpEquals(string expected, byte[] actual, string message)
    {
        Assert.Equal(expected, hex(actual)); //, message);
    }

    private static string hex(byte[] value)
    {
        if (value == null || value.Length == 0)
        {
            return null;
        }

        StringBuilder buf = new StringBuilder(value.Length << 1);
        foreach (byte b in value)
        {
            string hex = StringUtil.byteToHexString(b);
            if (hex.length() == 1)
            {
                buf.Append('0');
            }

            buf.Append(hex);
        }

        return buf.ToString();
    }

    private static byte[] unhex(string value)
    {
        return value != null ? StringUtil.decodeHexDump(value) : null;
    }
}