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
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Moq;
using Netty.NET.Common.Concurrent;
using Assert = XunitAssertMessages.AssertM;

namespace Netty.NET.Resolver.Tests;

public class HostsFileParserTest
{

    [Fact]
    public void TestParse()
    {
        string hostsString = new StringBuilder()
                .Append("127.0.0.1 host1").Append("\n") // single hostname, separated with blanks
                .Append("::1 host1").Append("\n") // same as above, but IPv6
                .Append("\n") // empty line
                .Append("192.168.0.1\thost2").Append("\n") // single hostname, separated with tabs
                .Append("#comment").Append("\n") // comment at the beginning of the line
                .Append(" #comment  ").Append("\n") // comment in the middle of the line
                .Append("192.168.0.2  host3  #comment").Append("\n") // comment after hostname
                .Append("192.168.0.3  host4  host5 host6").Append("\n") // multiple aliases
                .Append("192.168.0.4  host4").Append("\n") // host mapped to a second address, must be ignored
                .Append("192.168.0.5  HOST7").Append("\n") // uppercase host, should match lowercase host
                .Append("192.168.0.6  host7").Append("\n") // should be ignored since we have the uppercase host already
                .ToString();

        HostsFileEntries entries = HostsFileParser.Parse(new StringReader(hostsString));
        IReadOnlyDictionary<string, IPAddress> inet4Entries = entries.Inet4Entries();
        IReadOnlyDictionary<string, IPAddress> inet6Entries = entries.Inet6Entries();

        Assert.Equal(7, inet4Entries.Count, "Expected 7 IPv4 entries");
        Assert.Equal(1, inet6Entries.Count, "Expected 1 IPv6 entries");
        Assert.Equal("127.0.0.1", inet4Entries["host1"].ToString());
        Assert.Equal("192.168.0.1", inet4Entries["host2"].ToString());
        Assert.Equal("192.168.0.2", inet4Entries["host3"].ToString());
        Assert.Equal("192.168.0.3", inet4Entries["host4"].ToString());
        Assert.Equal("192.168.0.3", inet4Entries["host5"].ToString());
        Assert.Equal("192.168.0.3", inet4Entries["host6"].ToString());
        Assert.NotNull(inet4Entries["host7"], "uppercase host doesn't resolve");
        Assert.Equal("192.168.0.5", inet4Entries["host7"].ToString());
        Assert.Equal("::1" /* CLR uses compressed IPv6 formatting. */, inet6Entries["host1"].ToString());
    }

    [Fact]
    public void TestParseUnicode()
    {
        Encoding unicodeCharset = Encoding.BigEndianUnicode;
        TestParseFile(HostsFileParser.Parse(
                new FileInfo(Path.Combine(AppContext.BaseDirectory, "Resources", "hosts-unicode")), unicodeCharset));
    }

    [Fact]
    public void TestParseMultipleCharsets()
    {
        Encoding unicodeCharset = Encoding.BigEndianUnicode;
        TestParseFile(HostsFileParser.Parse(new FileInfo(Path.Combine(AppContext.BaseDirectory, "Resources", "hosts-unicode")),
                                            Encoding.UTF8, Encoding.Latin1, unicodeCharset));
    }

    private static void TestParseFile(HostsFileEntries entries)
    {
        IReadOnlyDictionary<string, IPAddress> inet4Entries = entries.Inet4Entries();
        IReadOnlyDictionary<string, IPAddress> inet6Entries = entries.Inet6Entries();

        Assert.Equal(2, inet4Entries.Count, "Expected 2 IPv4 entries");
        Assert.Equal(1, inet6Entries.Count, "Expected 1 IPv6 entries");
        Assert.Equal("127.0.0.1", inet4Entries["localhost"].ToString());
        Assert.Equal("255.255.255.255", inet4Entries["broadcasthost"].ToString());
        Assert.Equal("::1" /* CLR uses compressed IPv6 formatting. */, inet6Entries["localhost"].ToString());
    }
}
