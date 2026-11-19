/*
 * Copyright 2021 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Channels.Embedded;

namespace Netty.NET.Handler.Codec.Dns.Tests;

// CLR: new SplittableRandom().nextInt(bound) is Random.Shared.Next(bound), "a thread-safe Random instance that may be
// used concurrently from any thread" (https://learn.microsoft.com/en-us/dotnet/api/system.random.shared?view=net-10.0).
// recordAt(...) states its type argument because C# does not infer it from the assignment target; content() and
// refCnt() are the Content and RefCnt properties of the Buffer port.
public class TcpDnsTest
{
    private const string QUERY_DOMAIN = "www.example.com";
    private const long TTL = 600;
    private static readonly byte[] QUERY_RESULT = new byte[]{(byte) 192, (byte) 168, 1, 1};

    [Fact]
    public void TestQueryDecode()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new TcpDnsQueryDecoder());

        int randomID = Random.Shared.Next(60000 - 1000) + 1000;
        IDnsQuery query = new DefaultDnsQuery(randomID, DnsOpCode.QUERY)
                .SetRecord(DnsSection.QUESTION, new DefaultDnsQuestion(QUERY_DOMAIN, DnsRecordType.A));
        Assert.True(channel.WriteInbound(query));

        IDnsQuery readQuery = channel.ReadInbound<IDnsQuery>();
        Assert.Equal(query, readQuery);
        Assert.Equal(query.RecordAt<IDnsRecord>(DnsSection.QUESTION).Name(),
                     readQuery.RecordAt<IDnsRecord>(DnsSection.QUESTION).Name());
        readQuery.Release();
        Assert.False(channel.Finish());
    }

    [Fact]
    public void TestDecoderLeak()
    {
        EmbeddedChannel decoder = new EmbeddedChannel(new TcpDnsQueryDecoder());
        EmbeddedChannel encoder = new EmbeddedChannel(new TcpDnsQueryEncoder());
        int randomID = Random.Shared.Next(60000 - 1000) + 1000;
        IDnsQuery query = new DefaultDnsQuery(randomID, DnsOpCode.QUERY)
                .SetRecord(DnsSection.QUESTION, new DefaultDnsQuestion(QUERY_DOMAIN, DnsRecordType.A));
        Assert.True(encoder.WriteOutbound(query));
        ByteBuf encoded = encoder.ReadOutbound<ByteBuf>();
        Assert.True(decoder.WriteInbound(encoded));
        IDnsQuery decoded = decoder.ReadInbound<IDnsQuery>();
        Assert.Equal(query, decoded);

        ReferenceCountUtil.Release(decoded);

        // Make sure the ByteBuf is released by TcpDnsQueryDecoder
        Assert.True(encoded.RefCnt == 0);

        Assert.False(encoder.Finish());
        Assert.False(decoder.Finish());
    }

    [Fact]
    public void TestResponseEncode()
    {
        EmbeddedChannel channel = new EmbeddedChannel(new TcpDnsResponseEncoder());

        int randomID = Random.Shared.Next(60000 - 1000) + 1000;
        IDnsQuery query = new DefaultDnsQuery(randomID, DnsOpCode.QUERY)
                .SetRecord(DnsSection.QUESTION, new DefaultDnsQuestion(QUERY_DOMAIN, DnsRecordType.A));

        IDnsQuestion question = query.RecordAt<IDnsQuestion>(DnsSection.QUESTION);
        channel.WriteInbound(NewResponse(query, question, QUERY_RESULT));

        IDnsResponse readResponse = channel.ReadInbound<IDnsResponse>();
        Assert.Equal<IDnsRecord>(question, readResponse.RecordAt<IDnsRecord>(DnsSection.QUESTION));
        IDnsRawRecord record = new DefaultDnsRawRecord(question.Name(),
                DnsRecordType.A, TTL, Unpooled.WrappedBuffer(QUERY_RESULT));
        Assert.Equal<IDnsRecord>(record, readResponse.RecordAt<IDnsRecord>(DnsSection.ANSWER));
        Assert.Equal(record.Content, readResponse.RecordAt<IDnsRawRecord>(DnsSection.ANSWER).Content);
        ReferenceCountUtil.Release(readResponse);
        ReferenceCountUtil.Release(record);
        query.Release();
        Assert.False(channel.Finish());
    }

    private static DefaultDnsResponse NewResponse(IDnsQuery query, IDnsQuestion question, params byte[][] addresses)
    {
        DefaultDnsResponse response = new DefaultDnsResponse(query.Id());
        response.AddRecord(DnsSection.QUESTION, question);

        foreach (byte[] address in addresses)
        {
            DefaultDnsRawRecord queryAnswer = new DefaultDnsRawRecord(question.Name(),
                    DnsRecordType.A, TTL, Unpooled.WrappedBuffer(address));
            response.AddRecord(DnsSection.ANSWER, queryAnswer);
        }
        return response;
    }
}
