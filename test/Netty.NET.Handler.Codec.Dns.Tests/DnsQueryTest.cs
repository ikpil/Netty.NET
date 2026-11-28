/*
 * Copyright 2013 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Dns.Tests;

// CLR: @ParameterizedTest with @MethodSource is [Theory] with [MemberData]; the Stream<DnsOpCode>/Stream<Arguments>
// sources are IEnumerable<object[]> rows. SocketUtils.socketAddress(host, port) is an IPEndPoint over IPAddress.Parse
// (docs/common-clr-design.md, "Native socket operations and network interface access"), and new InetSocketAddress(0),
// "the wildcard address and the port number a specified value"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/InetSocketAddress.html), is
// new IPEndPoint(IPAddress.Any, 0). EmbeddedChannel.readOutbound()/readInbound() state their type argument, and
// channel.alloc() is the IChannel.Alloc() of the Transport port.
public class DnsQueryTest
{

    public static IEnumerable<object[]> OpCodes()
    {
        return new List<object[]> {
                new object[] { DnsOpCode.QUERY },
                new object[] { DnsOpCode.IQUERY },
                new object[] { DnsOpCode.STATUS },
                new object[] { DnsOpCode.NOTIFY },
                new object[] { DnsOpCode.UPDATE } };
    }

    public static IEnumerable<object[]> OpCodesAndExpectedFlags()
    {
        return new List<object[]> {
                // RFC 1035 section 4.1.1: QR(1) OPCODE(4) AA TC RD RA Z(3) RCODE(4), so the OPCODE
                // occupies bits 14-11 and RD is bit 8.
                new object[] { DnsOpCode.QUERY, false, 0x0000 },
                new object[] { DnsOpCode.QUERY, true, 0x0100 },
                new object[] { DnsOpCode.IQUERY, false, 0x0800 },
                new object[] { DnsOpCode.IQUERY, true, 0x0900 },
                new object[] { DnsOpCode.STATUS, false, 0x1000 },
                new object[] { DnsOpCode.STATUS, true, 0x1100 },
                new object[] { DnsOpCode.NOTIFY, false, 0x2000 },
                new object[] { DnsOpCode.NOTIFY, true, 0x2100 },
                new object[] { DnsOpCode.UPDATE, false, 0x2800 },
                new object[] { DnsOpCode.UPDATE, true, 0x2900 },

                // DnsOpCode does not range check, and an OPCODE that does not fit the four bits
                // reserved for it must not reach the neighbouring QR bit.
                new object[] { DnsOpCode.ValueOf(16), false, 0x0000 } };
    }

    [Fact]
    public void TestEncodeAndDecodeQuery()
    {
        IPEndPoint addr = new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53);
        EmbeddedChannel writeChannel = new EmbeddedChannel(new DatagramDnsQueryEncoder());
        EmbeddedChannel readChannel = new EmbeddedChannel(new DatagramDnsQueryDecoder());

        List<IDnsQuery> queries = new List<IDnsQuery>(5);
        queries.Add(new DatagramDnsQuery(null, addr, 1).SetRecord(
                DnsSection.QUESTION,
                new DefaultDnsQuestion("1.0.0.127.in-addr.arpa", DnsRecordType.PTR)));
        queries.Add(new DatagramDnsQuery(null, addr, 1).SetRecord(
                DnsSection.QUESTION,
                new DefaultDnsQuestion("www.example.com", DnsRecordType.A)));
        queries.Add(new DatagramDnsQuery(null, addr, 1).SetRecord(
                DnsSection.QUESTION,
                new DefaultDnsQuestion("example.com", DnsRecordType.AAAA)));
        queries.Add(new DatagramDnsQuery(null, addr, 1).SetRecord(
                DnsSection.QUESTION,
                new DefaultDnsQuestion("example.com", DnsRecordType.MX)));
        queries.Add(new DatagramDnsQuery(null, addr, 1).SetRecord(
                DnsSection.QUESTION,
                new DefaultDnsQuestion("example.com", DnsRecordType.CNAME)));

        foreach (IDnsQuery query in queries)
        {
            Assert.Equal(1, query.Count(DnsSection.QUESTION));
            Assert.Equal(0, query.Count(DnsSection.ANSWER));
            Assert.Equal(0, query.Count(DnsSection.AUTHORITY));
            Assert.Equal(0, query.Count(DnsSection.ADDITIONAL));

            Assert.True(writeChannel.WriteOutbound(query));

            DatagramPacket packet = writeChannel.ReadOutbound<DatagramPacket>();
            Assert.True(packet.Content.IsReadable());
            Assert.True(readChannel.WriteInbound(packet));

            IDnsQuery decodedDnsQuery = readChannel.ReadInbound<IDnsQuery>();
            Assert.Equal(query, decodedDnsQuery);
            Assert.True(decodedDnsQuery.Release());

            Assert.Null(writeChannel.ReadOutbound());
            Assert.Null(readChannel.ReadInbound());
        }

        Assert.False(writeChannel.Finish());
        Assert.False(readChannel.Finish());
    }

    [Theory]
    [MemberData(nameof(OpCodes))]
    public void TestOpCodeSurvivesEncodeAndDecode(DnsOpCode opCode)
    {
        IPEndPoint addr = new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53);
        EmbeddedChannel writeChannel = new EmbeddedChannel(new DatagramDnsQueryEncoder());
        EmbeddedChannel readChannel = new EmbeddedChannel(new DatagramDnsQueryDecoder());

        IDnsQuery query = new DatagramDnsQuery(null, addr, 1, opCode).SetRecord(
                DnsSection.QUESTION,
                new DefaultDnsQuestion("example.com", DnsRecordType.A));

        Assert.True(writeChannel.WriteOutbound(query));
        DatagramPacket packet = writeChannel.ReadOutbound<DatagramPacket>();
        Assert.True(readChannel.WriteInbound(packet));

        IDnsQuery decodedDnsQuery = readChannel.ReadInbound<IDnsQuery>();
        Assert.Equal(opCode, decodedDnsQuery.OpCode());
        Assert.True(decodedDnsQuery.Release());

        Assert.False(writeChannel.Finish());
        Assert.False(readChannel.Finish());
    }

    [Theory]
    [MemberData(nameof(OpCodesAndExpectedFlags))]
    public void TestOpCodeIsEncodedIntoBits14To11(DnsOpCode opCode, bool recursionDesired, int expectedFlags)
    {
        IPEndPoint addr = new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53);
        EmbeddedChannel writeChannel = new EmbeddedChannel(new DatagramDnsQueryEncoder());

        IDnsQuery query = new DatagramDnsQuery(null, addr, 1, opCode)
                .SetRecursionDesired(recursionDesired);
        query.SetRecord(DnsSection.QUESTION, new DefaultDnsQuestion("example.com", DnsRecordType.A));

        Assert.True(writeChannel.WriteOutbound(query));
        DatagramPacket packet = writeChannel.ReadOutbound<DatagramPacket>();
        Assert.Equal(expectedFlags, packet.Content.GetUnsignedShort(2));

        Assert.True(packet.Release());
        Assert.False(writeChannel.Finish());
    }

    [Fact]
    public void RejectTooManyRecordsInQuery()
    {
        // QDCOUNT, ANCOUNT, NSCOUNT, ARCOUNT
        for (int section = 0; section < 4; section++)
        {
            EmbeddedChannel channel = new EmbeddedChannel(new DatagramDnsQueryDecoder());
            ByteBuf buf = ((IChannel) channel).Alloc().Buffer(12);
            buf.WriteShort(1).WriteShort(0);

            for (int i = 0; i < 4; i++)
            {
                buf.WriteShort(i == section ? DnsCodecUtil.MAX_RECORDS_PER_SECTION + 1 : 0);
            }

            DatagramPacket packet = new DatagramPacket(buf, null, new IPEndPoint(IPAddress.Any, 0));

            try
            {
                Assert.Throws<TooLongFrameException>(() => channel.WriteInbound(packet));
            }
            finally
            {
                Assert.False(channel.Finish());
            }
        }
    }
}
