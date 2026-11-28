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

namespace Netty.NET.Handler.Codec.Dns.Tests;

public class AbstractDnsRecordTest
{

    [Fact]
    public void TestValidDomainName()
    {
        string name = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        AbstractDnsRecord record = new TestDnsRecord(name, DnsRecordType.A, 0);
        Assert.Equal(name + '.', record.Name());
    }

    [Fact]
    public void TestValidDomainNameUmlaut()
    {
        string name = "ä";
        AbstractDnsRecord record = new TestDnsRecord(name, DnsRecordType.A, 0);
        Assert.Equal("xn--4ca.", record.Name());
    }

    [Fact]
    public void TestValidDomainNameTrailingDot()
    {
        string name = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.";
        AbstractDnsRecord record = new TestDnsRecord(name, DnsRecordType.A, 0);
        Assert.Equal(name, record.Name());
    }

    [Fact]
    public void TestValidDomainNameUmlautTrailingDot()
    {
        string name = "ä.";
        AbstractDnsRecord record = new TestDnsRecord(name, DnsRecordType.A, 0);
        Assert.Equal("xn--4ca.", record.Name());
    }

    [Fact]
    public void TestValidDomainNameLength()
    {
        string name = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        Assert.Throws<ArgumentException>(() =>
        {
            new TestDnsRecord(name, DnsRecordType.A, 0);
        });
    }

    [Fact]
    public void TestValidDomainNameUmlautLength()
    {
        string name = "äaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        Assert.Throws<ArgumentException>(() =>
        {
            new TestDnsRecord(name, DnsRecordType.A, 0);
        });
    }

    /*
     * RFC-1034 Section 3.1 (page 7 & 8)
     * RFC-1035 Section 2.3.3 (page 9 & 10)
     */
    [Fact]
    public void TestEqualsAndHashCodeIgnoreCase()
    {
        AbstractDnsRecord lowerCase = new TestDnsRecord("example.com.", DnsRecordType.A, 0);
        AbstractDnsRecord upperCase = new TestDnsRecord("EXAMPLE.COM.", DnsRecordType.A, 0);
        Assert.Equal(lowerCase.GetHashCode(), upperCase.GetHashCode());
        Assert.Equal(lowerCase, upperCase);
    }

    private sealed class TestDnsRecord : AbstractDnsRecord
    {
        internal TestDnsRecord(string name, DnsRecordType type, long timeToLive) : base(name, type, timeToLive)
        {
        }
    }
}
