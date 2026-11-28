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

using System;
using System.Net;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Codec.Dns.Tests;

// CLR: the Java byte[] packet literals hold signed values (-127, -64,...), so they are sbyte arrays here and
// Bytes() reinterprets each element as the unsigned System.Byte that ByteBuf.WriteBytes/Unpooled.WrappedBuffer
// take (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
// AddressedEnvelope<DnsResponse, InetSocketAddress> is IAddressedEnvelope<IDnsResponse, IPEndPoint> with the
// Content property; assertInstanceOf is Assert.IsAssignableFrom; new InetSocketAddress(0) is new
// IPEndPoint(IPAddress.Any, 0).
public class DnsResponseTest
{

    private static readonly sbyte[][] packets = {
            new sbyte[] {
                    0, 1, -127, -128, 0, 1, 0, 1, 0, 0, 0, 0, 3, 119, 119, 119, 7, 101, 120, 97, 109, 112, 108, 101, 3,
                    99, 111, 109, 0, 0, 1, 0, 1, -64, 12, 0, 1, 0, 1, 0, 0, 16, -113, 0, 4, -64, 0, 43, 10
            },
            new sbyte[] {
                    0, 1, -127, -128, 0, 1, 0, 1, 0, 0, 0, 0, 3, 119, 119, 119, 7, 101, 120, 97, 109, 112, 108, 101, 3,
                    99, 111, 109, 0, 0, 28, 0, 1, -64, 12, 0, 28, 0, 1, 0, 0, 69, -8, 0, 16, 32, 1, 5, 0, 0, -120, 2,
                    0, 0, 0, 0, 0, 0, 0, 0, 16
            },
            new sbyte[] {
                    0, 2, -127, -128, 0, 1, 0, 0, 0, 1, 0, 0, 3, 119, 119, 119, 7, 101, 120, 97, 109, 112, 108, 101, 3,
                    99, 111, 109, 0, 0, 15, 0, 1, -64, 16, 0, 6, 0, 1, 0, 0, 3, -43, 0, 45, 3, 115, 110, 115, 3, 100,
                    110, 115, 5, 105, 99, 97, 110, 110, 3, 111, 114, 103, 0, 3, 110, 111, 99, -64, 49, 119, -4, 39,
                    112, 0, 0, 28, 32, 0, 0, 14, 16, 0, 18, 117, 0, 0, 0, 14, 16
            },
            new sbyte[] {
                    0, 3, -127, -128, 0, 1, 0, 1, 0, 0, 0, 0, 3, 119, 119, 119, 7, 101, 120, 97, 109, 112, 108, 101, 3,
                    99, 111, 109, 0, 0, 16, 0, 1, -64, 12, 0, 16, 0, 1, 0, 0, 84, 75, 0, 12, 11, 118, 61, 115, 112,
                    102, 49, 32, 45, 97, 108, 108
            },
            new sbyte[] {
                    -105, 19, -127, 0, 0, 1, 0, 0, 0, 13, 0, 0, 2, 104, 112, 11, 116, 105, 109, 98, 111, 117, 100, 114,
                    101, 97, 117, 3, 111, 114, 103, 0, 0, 1, 0, 1, 0, 0, 2, 0, 1, 0, 7, -23, 0, 0, 20, 1, 68, 12, 82,
                    79, 79, 84, 45, 83, 69, 82, 86, 69, 82, 83, 3, 78, 69, 84, 0, 0, 0, 2, 0, 1, 0, 7, -23, 0, 0, 4, 1,
                    70, -64, 49, 0, 0, 2, 0, 1, 0, 7, -23, 0, 0, 4, 1, 69, -64, 49, 0, 0, 2, 0, 1, 0, 7, -23, 0, 0, 4,
                    1, 75, -64, 49, 0, 0, 2, 0, 1, 0, 7, -23, 0, 0, 4, 1, 67, -64, 49, 0, 0, 2, 0, 1, 0, 7, -23, 0, 0,
                    4, 1, 76, -64, 49, 0, 0, 2, 0, 1, 0, 7, -23, 0, 0, 4, 1, 71, -64, 49, 0, 0, 2, 0, 1, 0, 7, -23, 0,
                    0, 4, 1, 73, -64, 49, 0, 0, 2, 0, 1, 0, 7, -23, 0, 0, 4, 1, 66, -64, 49, 0, 0, 2, 0, 1, 0, 7, -23,
                    0, 0, 4, 1, 77, -64, 49, 0, 0, 2, 0, 1, 0, 7, -23, 0, 0, 4, 1, 65, -64, 49, 0, 0, 2, 0, 1, 0, 7,
                    -23, 0, 0, 4, 1, 72, -64, 49, 0, 0, 2, 0, 1, 0, 7, -23, 0, 0, 4, 1, 74, -64, 49
            }
    };

    private static readonly sbyte[] malformedLoopPacket = {
            0, 4, -127, -128, 0, 1, 0, 0, 0, 0, 0, 0, -64, 12, 0, 1, 0, 1
    };

    private static byte[] Bytes(sbyte[] p)
    {
        return Array.ConvertAll(p, b => unchecked((byte) b));
    }

    [Fact]
    public void ReadResponseTest()
    {
        EmbeddedChannel embedder = new EmbeddedChannel(new DatagramDnsResponseDecoder());

        foreach (sbyte[] p in packets)
        {
            ByteBuf packet = ((IChannel) embedder).Alloc().Buffer(512).WriteBytes(Bytes(p));
            embedder.WriteInbound(new DatagramPacket(packet, null, new IPEndPoint(IPAddress.Any, 0)));
            IAddressedEnvelope<IDnsResponse, IPEndPoint> envelope =
                    embedder.ReadInbound<IAddressedEnvelope<IDnsResponse, IPEndPoint>>();
            Assert.IsAssignableFrom<DatagramDnsResponse>(envelope);
            IDnsResponse response = envelope.Content;
            Assert.Same(envelope, response);

            ByteBuf raw = Unpooled.WrappedBuffer(Bytes(p));
            Assert.Equal(raw.GetUnsignedShort(0), response.Id());
            Assert.Equal(raw.GetUnsignedShort(4), response.Count(DnsSection.QUESTION));
            Assert.Equal(raw.GetUnsignedShort(6), response.Count(DnsSection.ANSWER));
            Assert.Equal(raw.GetUnsignedShort(8), response.Count(DnsSection.AUTHORITY));
            Assert.Equal(raw.GetUnsignedShort(10), response.Count(DnsSection.ADDITIONAL));

            envelope.Release();
        }

        Assert.False(embedder.Finish());
    }

    [Fact]
    public void ReadMalformedResponseTest()
    {
        EmbeddedChannel embedder = new EmbeddedChannel(new DatagramDnsResponseDecoder());
        ByteBuf packet = ((IChannel) embedder).Alloc().Buffer(512).WriteBytes(Bytes(malformedLoopPacket));

        try
        {
            Assert.Throws<CorruptedFrameException>(() =>
            {
                embedder.WriteInbound(new DatagramPacket(packet, null, new IPEndPoint(IPAddress.Any, 0)));
            });
        }
        finally
        {
            Assert.False(embedder.Finish());
        }
    }

    [Fact]
    public void ReadIncompleteResponseTest()
    {
        EmbeddedChannel embedder = new EmbeddedChannel(new DatagramDnsResponseDecoder());
        ByteBuf packet = ((IChannel) embedder).Alloc().Buffer(512);

        try
        {
            Assert.Throws<CorruptedFrameException>(() =>
            {
                embedder.WriteInbound(new DatagramPacket(packet, null, new IPEndPoint(IPAddress.Any, 0)));
            });
        }
        finally
        {
            Assert.False(embedder.Finish());
        }
    }

    [Fact]
    public void RejectTooManyRecordsInResponse()
    {
        // QDCOUNT, ANCOUNT, NSCOUNT, ARCOUNT
        for (int section = 0; section < 4; section++)
        {
            EmbeddedChannel embedder = new EmbeddedChannel(new DatagramDnsResponseDecoder());
            ByteBuf packet = NewHeader(embedder, 0x8000, section, DnsCodecUtil.MAX_RECORDS_PER_SECTION + 1);

            try
            {
                Assert.Throws<TooLongFrameException>(
                        () => embedder.WriteInbound(new DatagramPacket(packet, null, new IPEndPoint(IPAddress.Any, 0))));
            }
            finally
            {
                Assert.False(embedder.Finish());
            }
        }
    }

    [Fact]
    public void AcceptMaxRecordsInResponse()
    {
        EmbeddedChannel embedder = new EmbeddedChannel(new DatagramDnsResponseDecoder());
        // No records follow the header, so the response is considered truncated but must not be rejected.
        ByteBuf packet = NewHeader(embedder, 0x8000, 1, DnsCodecUtil.MAX_RECORDS_PER_SECTION);
        Assert.True(embedder.WriteInbound(new DatagramPacket(packet, null, new IPEndPoint(IPAddress.Any, 0))));
        embedder.ReadInbound<DatagramDnsResponse>().Release();
        Assert.False(embedder.Finish());
    }

    private static ByteBuf NewHeader(EmbeddedChannel channel, int flags, int section, int count)
    {
        ByteBuf buf = ((IChannel) channel).Alloc().Buffer(12);
        buf.WriteShort(1).WriteShort(flags);

        for (int i = 0; i < 4; i++)
        {
            buf.WriteShort(i == section ? count : 0);
        }

        return buf;
    }
}
