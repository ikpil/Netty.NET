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
using System.Net;
using System.Net.Sockets;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Dns.Tests;

// CLR: the char literals of the byte[] initializers are cast to byte, which Java narrows implicitly. The tests reach the
// protected encodeName of the same package through its protected internal C# declaration. SocketUtils.addressByName of
// a literal is IPAddress.Parse and InetAddress.getAddress() is GetAddressBytes() (docs/common-clr-design.md, "Native
// socket operations and network interface access"); 'address instanceof Inet4Address' is AddressFamily.InterNetwork.
// ThreadLocalRandom.current() is Random.Shared, "a thread-safe Random instance that may be used concurrently from any
// thread" (https://learn.microsoft.com/en-us/dotnet/api/system.random.shared?view=net-10.0). readUnsignedByte() is
// ByteBuf.ReadByte(), which returns the unsigned System.Byte (docs/buffer-porting.md).
public class DefaultDnsRecordEncoderTest
{

    [Fact]
    public void TestEncodePtr()
    {
        DefaultDnsRecordEncoder encoder = new DefaultDnsRecordEncoder();
        IDnsPtrRecord ptrRecord = new DefaultDnsPtrRecord("131.186.250.142.in-addr.arpa.",
                IDnsRecord.CLASS_IN, 80, "fra24s07-in-f3.1e100.net.");
        ByteBuf @out = Unpooled.Buffer();
        ByteBuf expectedBuf = Unpooled.Buffer();
        expectedBuf.WriteBytes(
                new byte[] {
                        3, (byte) '1', (byte) '3', (byte) '1',
                        3, (byte) '1', (byte) '8', (byte) '6',
                        3, (byte) '2', (byte) '5', (byte) '0',
                        3, (byte) '1', (byte) '4', (byte) '2',
                        7, (byte) 'i', (byte) 'n', (byte) '-', (byte) 'a', (byte) 'd', (byte) 'd', (byte) 'r',
                        4, (byte) 'a', (byte) 'r', (byte) 'p', (byte) 'a',
                        0
        });
        expectedBuf.WriteShort(DnsRecordType.PTR.IntValue());
        expectedBuf.WriteShort(IDnsRecord.CLASS_IN);
        expectedBuf.WriteInt(80);
        byte[] hostname = new byte[] {
                14, (byte) 'f', (byte) 'r', (byte) 'a', (byte) '2', (byte) '4', (byte) 's', (byte) '0', (byte) '7',
                (byte) '-', (byte) 'i', (byte) 'n', (byte) '-', (byte) 'f', (byte) '3',
                5, (byte) '1', (byte) 'e', (byte) '1', (byte) '0', (byte) '0', 3, (byte) 'n', (byte) 'e', (byte) 't',
                0
        };
        expectedBuf.WriteShort(hostname.Length);
        expectedBuf.WriteBytes(hostname);
        try
        {
            encoder.EncodeRecord(ptrRecord, @out);
            Assert.Equal(expectedBuf, @out);
        }
        finally
        {
            @out.Release();
            expectedBuf.Release();
        }
    }

    [Fact]
    public void TestEncodeName()
    {
        TestEncodeName0(new byte[] { 5, (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y', 2, (byte) 'i', (byte) 'o', 0 }, "netty.io.");
    }

    [Fact]
    public void TestEncodeNameWithoutTerminator()
    {
        TestEncodeName0(new byte[] { 5, (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y', 2, (byte) 'i', (byte) 'o', 0 }, "netty.io");
    }

    [Fact]
    public void TestEncodeNameWithExtraTerminator()
    {
        TestEncodeName0(new byte[] { 5, (byte) 'n', (byte) 'e', (byte) 't', (byte) 't', (byte) 'y', 2, (byte) 'i', (byte) 'o', 0 }, "netty.io..");
    }

    // Test for https://github.com/netty/netty/issues/5014
    [Fact]
    public void TestEncodeEmptyName()
    {
        TestEncodeName0(new byte[] { 0 }, StringUtil.EMPTY_STRING);
    }

    [Fact]
    public void TestEncodeRootName()
    {
        TestEncodeName0(new byte[] { 0 }, ".");
    }

    // CLR: the private helper overload testEncodeName(byte[], String) is TestEncodeName0 because xUnit1024 forbids a
    // helper overload with the name of a test method (as codec-base's ReleaseWhenMergeCumulateThrowsInExpand0).
    private static void TestEncodeName0(byte[] expected, string name)
    {
        DefaultDnsRecordEncoder encoder = new DefaultDnsRecordEncoder();
        ByteBuf @out = Unpooled.Buffer();
        ByteBuf expectedBuf = Unpooled.WrappedBuffer(expected);
        try
        {
            encoder.EncodeName(name, @out);
            Assert.Equal(expectedBuf, @out);
        }
        finally
        {
            @out.Release();
            expectedBuf.Release();
        }
    }

    [Fact]
    public void TestOptEcsRecordIpv4()
    {
        TestOptEcsRecordIp(IPAddress.Parse("1.2.3.4"));
        TestOptEcsRecordIp(IPAddress.Parse("1.2.3.255"));
    }

    [Fact]
    public void TestOptEcsRecordIpv6()
    {
        TestOptEcsRecordIp(IPAddress.Parse("::0"));
        TestOptEcsRecordIp(IPAddress.Parse("::FF"));
    }

    // CLR: Byte.SIZE is 8 (the bits of a byte).
    private static void TestOptEcsRecordIp(IPAddress address)
    {
        int addressBits = address.GetAddressBytes().Length * 8;
        for (int i = 0; i <= addressBits; ++i)
        {
            TestIp(address, i);
        }
    }

    // CLR: Byte.MAX_VALUE is the signed Java maximum 127, which is sbyte.MaxValue (byte.MaxValue is 255,
    // https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
    // getUnsignedInt returns uint and is compared as long, the Java result type.
    private static void TestIp(IPAddress address, int prefix)
    {
        int lowOrderBitsToPreserve = prefix % 8;

        ByteBuf addressPart = Unpooled.WrappedBuffer(address.GetAddressBytes(), 0,
                DefaultDnsRecordEncoder.CalculateEcsAddressLength(prefix, lowOrderBitsToPreserve));

        if (lowOrderBitsToPreserve > 0)
        {
            // Pad the leftover of the last byte with zeros.
            int idx = addressPart.WriterIndex - 1;
            byte lastByte = addressPart.GetByte(idx);
            int paddingMask = -1 << 8 - lowOrderBitsToPreserve;
            addressPart.SetByte(idx, lastByte & paddingMask);
        }

        int payloadSize = NextInt(short.MaxValue);
        int extendedRcode = NextInt(sbyte.MaxValue * 2); // Unsigned
        int version = NextInt(sbyte.MaxValue * 2); // Unsigned

        DefaultDnsRecordEncoder encoder = new DefaultDnsRecordEncoder();
        ByteBuf @out = Unpooled.Buffer();
        try
        {
            IDnsOptEcsRecord record = new DefaultDnsOptEcsRecord(
                    payloadSize, extendedRcode, version, prefix, address.GetAddressBytes());
            encoder.EncodeRecord(record, @out);

            Assert.Equal(0, @out.ReadByte()); // Name
            Assert.Equal(DnsRecordType.OPT.IntValue(), @out.ReadUnsignedShort()); // Opt
            Assert.Equal(payloadSize, @out.ReadUnsignedShort()); // payload
            Assert.Equal(record.TimeToLive(), (long) @out.GetUnsignedInt(@out.ReaderIndex));

            // Read unpacked TTL.
            Assert.Equal(extendedRcode, @out.ReadByte());
            Assert.Equal(version, @out.ReadByte());
            Assert.Equal(extendedRcode, record.ExtendedRcode());
            Assert.Equal(version, record.Version());
            Assert.Equal(0, record.Flags());

            Assert.Equal(0, @out.ReadShort());

            int payloadLength = @out.ReadUnsignedShort();
            Assert.Equal(payloadLength, @out.ReadableBytes);

            Assert.Equal(8, @out.ReadShort()); // As defined by RFC.

            int rdataLength = @out.ReadUnsignedShort();
            Assert.Equal(rdataLength, @out.ReadableBytes);

            Assert.Equal((short) (address.AddressFamily == AddressFamily.InterNetwork ? 1 : 2), @out.ReadShort());

            Assert.Equal(prefix, @out.ReadByte());
            Assert.Equal(0, @out.ReadByte()); // This must be 0 for requests.
            Assert.Equal(addressPart, @out);
        }
        finally
        {
            addressPart.Release();
            @out.Release();
        }
    }

    private static int NextInt(int max)
    {
        return Random.Shared.Next(max);
    }
}
