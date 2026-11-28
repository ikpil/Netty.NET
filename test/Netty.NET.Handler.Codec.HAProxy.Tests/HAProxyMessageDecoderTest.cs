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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Channels.Embedded;
using static Netty.NET.Buffer.Unpooled;
using static Netty.NET.Handler.Codec.HAProxy.HAProxyProxiedProtocol;
using Assert = XunitAssertMessages.AssertM;

#pragma warning disable xUnit2013 // assertEquals(1, size()) keeps the source assertion.
#pragma warning disable xUnit2017 // assertTrue(list.contains(x)) keeps the source assertions.

namespace Netty.NET.Handler.Codec.HAProxy.Tests;

// CLR: @BeforeEach setUp() runs from the constructor; buffer() is Unpooled.Buffer(), because inside the Netty.NET
// namespaces the simple name Buffer binds to the Netty.NET.Buffer namespace; isEmpty() is Count == 0; anonymous Executable classes are lambdas passed to Assert.Throws,
// whose exception type must match exactly (the thrown types are the asserted ones); assertThat(..).isInstanceOf is
// Assert.IsAssignableFrom; CharsetUtil.US_ASCII is Encoding.ASCII; the static Unpooled imports are a using static
// directive; ch.pipeline().names().size() is ch.Pipeline.Names().Count; HAProxyTLV.type() is Type_() (CS0102, see
// HAProxyTLV). Java byte literals below zero in byte[] initializers are unchecked((byte) ...) and char literals are cast
// to byte, which Java narrows implicitly. JUnit asserts with messages are XunitAssertMessages.AssertM.
public class HAProxyMessageDecoderTest
{
    private EmbeddedChannel ch;

    public HAProxyMessageDecoderTest()
    {
        SetUp();
    }

    private void SetUp()
    {
        ch = new EmbeddedChannel(new HAProxyMessageDecoder());
    }

    [Fact]
    public void TestIPV4Decode()
    {
        int startChannels = ch.Pipeline.Names().Count;
        string header = "PROXY TCP4 192.168.0.1 192.168.0.11 56324 443\r\n";
        ch.WriteInbound(CopiedBuffer(header, Encoding.ASCII));
        object msgObj = ch.ReadInbound();
        Assert.Equal(startChannels - 1, ch.Pipeline.Names().Count);
        Assert.IsAssignableFrom<HAProxyMessage>(msgObj);
        HAProxyMessage msg = (HAProxyMessage) msgObj;
        Assert.Equal(HAProxyProtocolVersion.V1, msg.ProtocolVersion());
        Assert.Equal(HAProxyCommand.PROXY, msg.Command());
        Assert.Equal(HAProxyProxiedProtocol.TCP4, msg.ProxiedProtocol());
        Assert.Equal("192.168.0.1", msg.SourceAddress());
        Assert.Equal("192.168.0.11", msg.DestinationAddress());
        Assert.Equal(56324, msg.SourcePort());
        Assert.Equal(443, msg.DestinationPort());
        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
        Assert.True(msg.Release());
    }

    [Fact]
    public void TestIPV6Decode()
    {
        int startChannels = ch.Pipeline.Names().Count;
        string header = "PROXY TCP6 2001:0db8:85a3:0000:0000:8a2e:0370:7334 1050:0:0:0:5:600:300c:326b 56324 443\r\n";
        ch.WriteInbound(CopiedBuffer(header, Encoding.ASCII));
        object msgObj = ch.ReadInbound();
        Assert.Equal(startChannels - 1, ch.Pipeline.Names().Count);
        Assert.IsAssignableFrom<HAProxyMessage>(msgObj);
        HAProxyMessage msg = (HAProxyMessage) msgObj;
        Assert.Equal(HAProxyProtocolVersion.V1, msg.ProtocolVersion());
        Assert.Equal(HAProxyCommand.PROXY, msg.Command());
        Assert.Equal(HAProxyProxiedProtocol.TCP6, msg.ProxiedProtocol());
        Assert.Equal("2001:0db8:85a3:0000:0000:8a2e:0370:7334", msg.SourceAddress());
        Assert.Equal("1050:0:0:0:5:600:300c:326b", msg.DestinationAddress());
        Assert.Equal(56324, msg.SourcePort());
        Assert.Equal(443, msg.DestinationPort());
        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
        Assert.True(msg.Release());
    }

    [Fact]
    public void TestUnknownProtocolDecode()
    {
        int startChannels = ch.Pipeline.Names().Count;
        string header = "PROXY UNKNOWN 192.168.0.1 192.168.0.11 56324 443\r\n";
        ch.WriteInbound(CopiedBuffer(header, Encoding.ASCII));
        object msgObj = ch.ReadInbound();
        Assert.Equal(startChannels - 1, ch.Pipeline.Names().Count);
        Assert.IsAssignableFrom<HAProxyMessage>(msgObj);
        HAProxyMessage msg = (HAProxyMessage) msgObj;
        Assert.Equal(HAProxyProtocolVersion.V1, msg.ProtocolVersion());
        Assert.Equal(HAProxyCommand.PROXY, msg.Command());
        Assert.Equal(HAProxyProxiedProtocol.UNKNOWN, msg.ProxiedProtocol());
        Assert.Null(msg.SourceAddress());
        Assert.Null(msg.DestinationAddress());
        Assert.Equal(0, msg.SourcePort());
        Assert.Equal(0, msg.DestinationPort());
        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
        Assert.True(msg.Release());
    }

    [Fact]
    public void TestV1NoUDP()
    {
        string header = "PROXY UDP4 192.168.0.1 192.168.0.11 56324 443\r\n";
        Assert.Throws<HAProxyProtocolException>(() => ch.WriteInbound(CopiedBuffer(header, Encoding.ASCII)));
    }

    [Fact]
    public void TestInvalidPort()
    {
        string header = "PROXY TCP4 192.168.0.1 192.168.0.11 80000 443\r\n";
        Assert.Throws<HAProxyProtocolException>(() => ch.WriteInbound(CopiedBuffer(header, Encoding.ASCII)));
    }

    [Fact]
    public void TestInvalidIPV4Address()
    {
        string header = "PROXY TCP4 299.168.0.1 192.168.0.11 56324 443\r\n";
        Assert.Throws<HAProxyProtocolException>(() => ch.WriteInbound(CopiedBuffer(header, Encoding.ASCII)));
    }

    [Fact]
    public void TestInvalidIPV6Address()
    {
        string header =
                "PROXY TCP6 r001:0db8:85a3:0000:0000:8a2e:0370:7334 1050:0:0:0:5:600:300c:326b 56324 443\r\n";
        Assert.Throws<HAProxyProtocolException>(() => ch.WriteInbound(CopiedBuffer(header, Encoding.ASCII)));
    }

    [Fact]
    public void TestInvalidProtocol()
    {
        string header = "PROXY TCP7 192.168.0.1 192.168.0.11 56324 443\r\n";
        Assert.Throws<HAProxyProtocolException>(() => ch.WriteInbound(CopiedBuffer(header, Encoding.ASCII)));
    }

    [Fact]
    public void TestMissingParams()
    {
        string header = "PROXY TCP4 192.168.0.1 192.168.0.11 56324\r\n";
        Assert.Throws<HAProxyProtocolException>(() => ch.WriteInbound(CopiedBuffer(header, Encoding.ASCII)));
    }

    [Fact]
    public void TestTooManyParams()
    {
        string header = "PROXY TCP4 192.168.0.1 192.168.0.11 56324 443 123\r\n";
        Assert.Throws<HAProxyProtocolException>(() => ch.WriteInbound(CopiedBuffer(header, Encoding.ASCII)));
    }

    [Fact]
    public void TestInvalidCommand()
    {
        string header = "PING TCP4 192.168.0.1 192.168.0.11 56324 443\r\n";
        Assert.Throws<HAProxyProtocolException>(() => ch.WriteInbound(CopiedBuffer(header, Encoding.ASCII)));
    }

    [Fact]
    public void TestInvalidEOL()
    {
        string header = "PROXY TCP4 192.168.0.1 192.168.0.11 56324 443\nGET / HTTP/1.1\r\n";
        Assert.Throws<HAProxyProtocolException>(() => ch.WriteInbound(CopiedBuffer(header, Encoding.ASCII)));
    }

    [Fact]
    public void TestHeaderTooLong()
    {
        string header = "PROXY TCP4 192.168.0.1 192.168.0.11 56324 " +
                        "00000000000000000000000000000000000000000000000000000000000000000443\r\n";
        Assert.Throws<HAProxyProtocolException>(() => ch.WriteInbound(CopiedBuffer(header, Encoding.ASCII)));
    }

    [Fact]
    public void TestFailSlowHeaderTooLong()
    {
        EmbeddedChannel slowFailCh = new EmbeddedChannel(new HAProxyMessageDecoder(false));

        try
        {
            string headerPart1 = "PROXY TCP4 192.168.0.1 192.168.0.11 56324 " +
                                 "000000000000000000000000000000000000000000000000000000000000000000000443";
            // Should not throw exception
            Assert.False(slowFailCh.WriteInbound(CopiedBuffer(headerPart1, Encoding.ASCII)));
            string headerPart2 = "more header data";
            // Should not throw exception
            Assert.False(slowFailCh.WriteInbound(CopiedBuffer(headerPart2, Encoding.ASCII)));
            string headerPart3 = "end of header\r\n";

            int discarded = headerPart1.Length + headerPart2.Length + headerPart3.Length - 2;
            Assert.Throws<HAProxyProtocolException>(() => slowFailCh.WriteInbound(CopiedBuffer(headerPart3, Encoding.ASCII)), "over " + discarded);
        }
        finally
        {
            Assert.False(slowFailCh.FinishAndReleaseAll());
        }
    }

    [Fact]
    public void TestFailFastHeaderTooLong()
    {
        EmbeddedChannel fastFailCh = new EmbeddedChannel(new HAProxyMessageDecoder(true));

        try
        {
            string headerPart1 = "PROXY TCP4 192.168.0.1 192.168.0.11 56324 " +
                                 "000000000000000000000000000000000000000000000000000000000000000000000443";
            Assert.Throws<HAProxyProtocolException>(() => fastFailCh.WriteInbound(CopiedBuffer(headerPart1, Encoding.ASCII)), "over " + headerPart1.Length);
        }
        finally
        {
            Assert.False(fastFailCh.FinishAndReleaseAll());
        }
    }

    [Fact]
    public void TestIncompleteHeader()
    {
        string header = "PROXY TCP4 192.168.0.1 192.168.0.11 56324";
        ch.WriteInbound(CopiedBuffer(header, Encoding.ASCII));
        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    // CLR: closeFuture() is the CloseFuture Task; awaitUninterruptibly(5000) is an await of Task.WhenAny with a 5 second
    // delay, and isDone()/isSuccess() are IsCompleted/IsCompletedSuccessfully.
    [Fact]
    public async Task TestCloseOnInvalid()
    {
        Task closeFuture = ch.CloseFuture;
        string header = "GET / HTTP/1.1\r\n";

        try
        {
            ch.WriteInbound(CopiedBuffer(header, Encoding.ASCII));
        }
        catch (HAProxyProtocolException)
        {
            // swallow this exception since we're just testing to be sure the channel was closed
        }

        bool isComplete = await Task.WhenAny(closeFuture,
                Task.Delay(5000, TestContext.Current.CancellationToken)) == closeFuture;
        if (!isComplete || !closeFuture.IsCompleted || !closeFuture.IsCompletedSuccessfully)
        {
            Assert.Fail("Expected channel close");
        }
    }

    [Fact]
    public void TestTransportProtocolAndAddressFamily()
    {
        byte unknown = HAProxyProxiedProtocol.UNKNOWN.ByteValue();
        byte tcp4 = HAProxyProxiedProtocol.TCP4.ByteValue();
        byte tcp6 = HAProxyProxiedProtocol.TCP6.ByteValue();
        byte udp4 = HAProxyProxiedProtocol.UDP4.ByteValue();
        byte udp6 = HAProxyProxiedProtocol.UDP6.ByteValue();
        byte unix_stream = HAProxyProxiedProtocol.UNIX_STREAM.ByteValue();
        byte unix_dgram = HAProxyProxiedProtocol.UNIX_DGRAM.ByteValue();

        Assert.Equal(TransportProtocol.UNSPEC, TransportProtocol.ValueOf(unknown));
        Assert.Equal(TransportProtocol.STREAM, TransportProtocol.ValueOf(tcp4));
        Assert.Equal(TransportProtocol.STREAM, TransportProtocol.ValueOf(tcp6));
        Assert.Equal(TransportProtocol.STREAM, TransportProtocol.ValueOf(unix_stream));
        Assert.Equal(TransportProtocol.DGRAM, TransportProtocol.ValueOf(udp4));
        Assert.Equal(TransportProtocol.DGRAM, TransportProtocol.ValueOf(udp6));
        Assert.Equal(TransportProtocol.DGRAM, TransportProtocol.ValueOf(unix_dgram));

        Assert.Equal(AddressFamily.AF_UNSPEC, AddressFamily.ValueOf(unknown));
        Assert.Equal(AddressFamily.AF_IPv4, AddressFamily.ValueOf(tcp4));
        Assert.Equal(AddressFamily.AF_IPv4, AddressFamily.ValueOf(udp4));
        Assert.Equal(AddressFamily.AF_IPv6, AddressFamily.ValueOf(tcp6));
        Assert.Equal(AddressFamily.AF_IPv6, AddressFamily.ValueOf(udp6));
        Assert.Equal(AddressFamily.AF_UNIX, AddressFamily.ValueOf(unix_stream));
        Assert.Equal(AddressFamily.AF_UNIX, AddressFamily.ValueOf(unix_dgram));
    }

    [Fact]
    public void TestV2IPV4Decode()
    {
        byte[] header = new byte[28];
        header[0] = 0x0D; // Binary Prefix
        header[1] = 0x0A; // -----
        header[2] = 0x0D; // -----
        header[3] = 0x0A; // -----
        header[4] = 0x00; // -----
        header[5] = 0x0D; // -----
        header[6] = 0x0A; // -----
        header[7] = 0x51; // -----
        header[8] = 0x55; // -----
        header[9] = 0x49; // -----
        header[10] = 0x54; // -----
        header[11] = 0x0A; // -----

        header[12] = 0x21; // v2, cmd=PROXY
        header[13] = 0x11; // TCP over IPv4

        header[14] = 0x00; // Remaining Bytes
        header[15] = 0x0c; // -----

        header[16] = (byte) 0xc0; // Source Address
        header[17] = (byte) 0xa8; // -----
        header[18] = 0x00; // -----
        header[19] = 0x01; // -----

        header[20] = (byte) 0xc0; // Destination Address
        header[21] = (byte) 0xa8; // -----
        header[22] = 0x00; // -----
        header[23] = 0x0b; // -----

        header[24] = (byte) 0xdc; // Source Port
        header[25] = 0x04; // -----

        header[26] = 0x01; // Destination Port
        header[27] = (byte) 0xbb; // -----

        int startChannels = ch.Pipeline.Names().Count;
        ch.WriteInbound(CopiedBuffer(header));
        object msgObj = ch.ReadInbound();
        Assert.Equal(startChannels - 1, ch.Pipeline.Names().Count);
        Assert.IsAssignableFrom<HAProxyMessage>(msgObj);
        HAProxyMessage msg = (HAProxyMessage) msgObj;
        Assert.Equal(HAProxyProtocolVersion.V2, msg.ProtocolVersion());
        Assert.Equal(HAProxyCommand.PROXY, msg.Command());
        Assert.Equal(HAProxyProxiedProtocol.TCP4, msg.ProxiedProtocol());
        Assert.Equal("192.168.0.1", msg.SourceAddress());
        Assert.Equal("192.168.0.11", msg.DestinationAddress());
        Assert.Equal(56324, msg.SourcePort());
        Assert.Equal(443, msg.DestinationPort());
        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
        Assert.True(msg.Release());
    }

    [Fact]
    public void TestV2UDPDecode()
    {
        byte[] header = new byte[28];
        header[0] = 0x0D; // Binary Prefix
        header[1] = 0x0A; // -----
        header[2] = 0x0D; // -----
        header[3] = 0x0A; // -----
        header[4] = 0x00; // -----
        header[5] = 0x0D; // -----
        header[6] = 0x0A; // -----
        header[7] = 0x51; // -----
        header[8] = 0x55; // -----
        header[9] = 0x49; // -----
        header[10] = 0x54; // -----
        header[11] = 0x0A; // -----

        header[12] = 0x21; // v2, cmd=PROXY
        header[13] = 0x12; // UDP over IPv4

        header[14] = 0x00; // Remaining Bytes
        header[15] = 0x0c; // -----

        header[16] = (byte) 0xc0; // Source Address
        header[17] = (byte) 0xa8; // -----
        header[18] = 0x00; // -----
        header[19] = 0x01; // -----

        header[20] = (byte) 0xc0; // Destination Address
        header[21] = (byte) 0xa8; // -----
        header[22] = 0x00; // -----
        header[23] = 0x0b; // -----

        header[24] = (byte) 0xdc; // Source Port
        header[25] = 0x04; // -----

        header[26] = 0x01; // Destination Port
        header[27] = (byte) 0xbb; // -----

        int startChannels = ch.Pipeline.Names().Count;
        ch.WriteInbound(CopiedBuffer(header));
        object msgObj = ch.ReadInbound();
        Assert.Equal(startChannels - 1, ch.Pipeline.Names().Count);
        Assert.IsAssignableFrom<HAProxyMessage>(msgObj);
        HAProxyMessage msg = (HAProxyMessage) msgObj;
        Assert.Equal(HAProxyProtocolVersion.V2, msg.ProtocolVersion());
        Assert.Equal(HAProxyCommand.PROXY, msg.Command());
        Assert.Equal(HAProxyProxiedProtocol.UDP4, msg.ProxiedProtocol());
        Assert.Equal("192.168.0.1", msg.SourceAddress());
        Assert.Equal("192.168.0.11", msg.DestinationAddress());
        Assert.Equal(56324, msg.SourcePort());
        Assert.Equal(443, msg.DestinationPort());
        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
        Assert.True(msg.Release());
    }

    [Fact]
    public void Testv2IPV6Decode()
    {
        byte[] header = new byte[52];
        header[0] = 0x0D; // Binary Prefix
        header[1] = 0x0A; // -----
        header[2] = 0x0D; // -----
        header[3] = 0x0A; // -----
        header[4] = 0x00; // -----
        header[5] = 0x0D; // -----
        header[6] = 0x0A; // -----
        header[7] = 0x51; // -----
        header[8] = 0x55; // -----
        header[9] = 0x49; // -----
        header[10] = 0x54; // -----
        header[11] = 0x0A; // -----

        header[12] = 0x21; // v2, cmd=PROXY
        header[13] = 0x21; // TCP over IPv6

        header[14] = 0x00; // Remaining Bytes
        header[15] = 0x24; // -----

        header[16] = 0x20; // Source Address
        header[17] = 0x01; // -----
        header[18] = 0x0d; // -----
        header[19] = (byte) 0xb8; // -----
        header[20] = (byte) 0x85; // -----
        header[21] = (byte) 0xa3; // -----
        header[22] = 0x00; // -----
        header[23] = 0x00; // -----
        header[24] = 0x00; // -----
        header[25] = 0x00; // -----
        header[26] = (byte) 0x8a; // -----
        header[27] = 0x2e; // -----
        header[28] = 0x03; // -----
        header[29] = 0x70; // -----
        header[30] = 0x73; // -----
        header[31] = 0x34; // -----

        header[32] = 0x10; // Destination Address
        header[33] = 0x50; // -----
        header[34] = 0x00; // -----
        header[35] = 0x00; // -----
        header[36] = 0x00; // -----
        header[37] = 0x00; // -----
        header[38] = 0x00; // -----
        header[39] = 0x00; // -----
        header[40] = 0x00; // -----
        header[41] = 0x05; // -----
        header[42] = 0x06; // -----
        header[43] = 0x00; // -----
        header[44] = 0x30; // -----
        header[45] = 0x0c; // -----
        header[46] = 0x32; // -----
        header[47] = 0x6b; // -----

        header[48] = (byte) 0xdc; // Source Port
        header[49] = 0x04; // -----

        header[50] = 0x01; // Destination Port
        header[51] = (byte) 0xbb; // -----

        int startChannels = ch.Pipeline.Names().Count;
        ch.WriteInbound(CopiedBuffer(header));
        object msgObj = ch.ReadInbound();
        Assert.Equal(startChannels - 1, ch.Pipeline.Names().Count);
        Assert.IsAssignableFrom<HAProxyMessage>(msgObj);
        HAProxyMessage msg = (HAProxyMessage) msgObj;
        Assert.Equal(HAProxyProtocolVersion.V2, msg.ProtocolVersion());
        Assert.Equal(HAProxyCommand.PROXY, msg.Command());
        Assert.Equal(HAProxyProxiedProtocol.TCP6, msg.ProxiedProtocol());
        Assert.Equal("2001:db8:85a3:0:0:8a2e:370:7334", msg.SourceAddress());
        Assert.Equal("1050:0:0:0:5:600:300c:326b", msg.DestinationAddress());
        Assert.Equal(56324, msg.SourcePort());
        Assert.Equal(443, msg.DestinationPort());
        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
        Assert.True(msg.Release());
    }

    [Fact]
    public void Testv2UnixDecode()
    {
        byte[] header = new byte[232];
        header[0] = 0x0D; // Binary Prefix
        header[1] = 0x0A; // -----
        header[2] = 0x0D; // -----
        header[3] = 0x0A; // -----
        header[4] = 0x00; // -----
        header[5] = 0x0D; // -----
        header[6] = 0x0A; // -----
        header[7] = 0x51; // -----
        header[8] = 0x55; // -----
        header[9] = 0x49; // -----
        header[10] = 0x54; // -----
        header[11] = 0x0A; // -----

        header[12] = 0x21; // v2, cmd=PROXY
        header[13] = 0x31; // UNIX_STREAM

        header[14] = 0x00; // Remaining Bytes
        header[15] = (byte) 0xd8; // -----

        header[16] = 0x2f; // Source Address
        header[17] = 0x76; // -----
        header[18] = 0x61; // -----
        header[19] = 0x72; // -----
        header[20] = 0x2f; // -----
        header[21] = 0x72; // -----
        header[22] = 0x75; // -----
        header[23] = 0x6e; // -----
        header[24] = 0x2f; // -----
        header[25] = 0x73; // -----
        header[26] = 0x72; // -----
        header[27] = 0x63; // -----
        header[28] = 0x2e; // -----
        header[29] = 0x73; // -----
        header[30] = 0x6f; // -----
        header[31] = 0x63; // -----
        header[32] = 0x6b; // -----
        header[33] = 0x00; // -----

        header[124] = 0x2f; // Destination Address
        header[125] = 0x76; // -----
        header[126] = 0x61; // -----
        header[127] = 0x72; // -----
        header[128] = 0x2f; // -----
        header[129] = 0x72; // -----
        header[130] = 0x75; // -----
        header[131] = 0x6e; // -----
        header[132] = 0x2f; // -----
        header[133] = 0x64; // -----
        header[134] = 0x65; // -----
        header[135] = 0x73; // -----
        header[136] = 0x74; // -----
        header[137] = 0x2e; // -----
        header[138] = 0x73; // -----
        header[139] = 0x6f; // -----
        header[140] = 0x63; // -----
        header[141] = 0x6b; // -----
        header[142] = 0x00; // -----

        int startChannels = ch.Pipeline.Names().Count;
        ch.WriteInbound(CopiedBuffer(header));
        object msgObj = ch.ReadInbound();
        Assert.Equal(startChannels - 1, ch.Pipeline.Names().Count);
        Assert.IsAssignableFrom<HAProxyMessage>(msgObj);
        HAProxyMessage msg = (HAProxyMessage) msgObj;
        Assert.Equal(HAProxyProtocolVersion.V2, msg.ProtocolVersion());
        Assert.Equal(HAProxyCommand.PROXY, msg.Command());
        Assert.Equal(HAProxyProxiedProtocol.UNIX_STREAM, msg.ProxiedProtocol());
        Assert.Equal("/var/run/src.sock", msg.SourceAddress());
        Assert.Equal("/var/run/dest.sock", msg.DestinationAddress());
        Assert.Equal(0, msg.SourcePort());
        Assert.Equal(0, msg.DestinationPort());
        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
        Assert.True(msg.Release());
    }

    [Fact]
    public void TestV2LocalProtocolDecode()
    {
        byte[] header = new byte[28];
        header[0] = 0x0D; // Binary Prefix
        header[1] = 0x0A; // -----
        header[2] = 0x0D; // -----
        header[3] = 0x0A; // -----
        header[4] = 0x00; // -----
        header[5] = 0x0D; // -----
        header[6] = 0x0A; // -----
        header[7] = 0x51; // -----
        header[8] = 0x55; // -----
        header[9] = 0x49; // -----
        header[10] = 0x54; // -----
        header[11] = 0x0A; // -----

        header[12] = 0x20; // v2, cmd=LOCAL
        header[13] = 0x00; // Unspecified transport protocol and address family

        header[14] = 0x00; // Remaining Bytes
        header[15] = 0x0c; // -----

        header[16] = (byte) 0xc0; // Source Address
        header[17] = (byte) 0xa8; // -----
        header[18] = 0x00; // -----
        header[19] = 0x01; // -----

        header[20] = (byte) 0xc0; // Destination Address
        header[21] = (byte) 0xa8; // -----
        header[22] = 0x00; // -----
        header[23] = 0x0b; // -----

        header[24] = (byte) 0xdc; // Source Port
        header[25] = 0x04; // -----

        header[26] = 0x01; // Destination Port
        header[27] = (byte) 0xbb; // -----

        int startChannels = ch.Pipeline.Names().Count;
        ch.WriteInbound(CopiedBuffer(header));
        object msgObj = ch.ReadInbound();
        Assert.Equal(startChannels - 1, ch.Pipeline.Names().Count);
        Assert.IsAssignableFrom<HAProxyMessage>(msgObj);
        HAProxyMessage msg = (HAProxyMessage) msgObj;
        Assert.Equal(HAProxyProtocolVersion.V2, msg.ProtocolVersion());
        Assert.Equal(HAProxyCommand.LOCAL, msg.Command());
        Assert.Equal(HAProxyProxiedProtocol.UNKNOWN, msg.ProxiedProtocol());
        Assert.Null(msg.SourceAddress());
        Assert.Null(msg.DestinationAddress());
        Assert.Equal(0, msg.SourcePort());
        Assert.Equal(0, msg.DestinationPort());
        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
        Assert.True(msg.Release());
    }

    [Fact]
    public void TestV2UnknownProtocolDecode()
    {
        byte[] header = new byte[28];
        header[0] = 0x0D; // Binary Prefix
        header[1] = 0x0A; // -----
        header[2] = 0x0D; // -----
        header[3] = 0x0A; // -----
        header[4] = 0x00; // -----
        header[5] = 0x0D; // -----
        header[6] = 0x0A; // -----
        header[7] = 0x51; // -----
        header[8] = 0x55; // -----
        header[9] = 0x49; // -----
        header[10] = 0x54; // -----
        header[11] = 0x0A; // -----

        header[12] = 0x21; // v2, cmd=PROXY
        header[13] = 0x00; // Unspecified transport protocol and address family

        header[14] = 0x00; // Remaining Bytes
        header[15] = 0x0c; // -----

        header[16] = (byte) 0xc0; // Source Address
        header[17] = (byte) 0xa8; // -----
        header[18] = 0x00; // -----
        header[19] = 0x01; // -----

        header[20] = (byte) 0xc0; // Destination Address
        header[21] = (byte) 0xa8; // -----
        header[22] = 0x00; // -----
        header[23] = 0x0b; // -----

        header[24] = (byte) 0xdc; // Source Port
        header[25] = 0x04; // -----

        header[26] = 0x01; // Destination Port
        header[27] = (byte) 0xbb; // -----

        int startChannels = ch.Pipeline.Names().Count;
        ch.WriteInbound(CopiedBuffer(header));
        object msgObj = ch.ReadInbound();
        Assert.Equal(startChannels - 1, ch.Pipeline.Names().Count);
        Assert.IsAssignableFrom<HAProxyMessage>(msgObj);
        HAProxyMessage msg = (HAProxyMessage) msgObj;
        Assert.Equal(HAProxyProtocolVersion.V2, msg.ProtocolVersion());
        Assert.Equal(HAProxyCommand.PROXY, msg.Command());
        Assert.Equal(HAProxyProxiedProtocol.UNKNOWN, msg.ProxiedProtocol());
        Assert.Null(msg.SourceAddress());
        Assert.Null(msg.DestinationAddress());
        Assert.Equal(0, msg.SourcePort());
        Assert.Equal(0, msg.DestinationPort());
        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
        Assert.True(msg.Release());
    }

    [Fact]
    public void TestV2WithSslTLVs()
    {
        ch = new EmbeddedChannel(new HAProxyMessageDecoder());

        byte[] bytes = {
                13, 10, 13, 10, 0, 13, 10, 81, 85, 73, 84, 10, 33, 17, 0, 35, 127, 0, 0, 1, 127, 0, 0, 1,
                unchecked((byte) -55), unchecked((byte) -90), 7, 89, 32, 0, 20, 5, 0, 0, 0, 0, 33, 0, 5, 84, 76, 83, 118, 49, 34, 0, 4, 76, 69, 65, 70
        };

        int startChannels = ch.Pipeline.Names().Count;
        Assert.True(ch.WriteInbound(CopiedBuffer(bytes)));
        object msgObj = ch.ReadInbound();
        Assert.Equal(startChannels - 1, ch.Pipeline.Names().Count);
        HAProxyMessage msg = (HAProxyMessage) msgObj;

        Assert.Equal(HAProxyProtocolVersion.V2, msg.ProtocolVersion());
        Assert.Equal(HAProxyCommand.PROXY, msg.Command());
        Assert.Equal(HAProxyProxiedProtocol.TCP4, msg.ProxiedProtocol());
        Assert.Equal("127.0.0.1", msg.SourceAddress());
        Assert.Equal("127.0.0.1", msg.DestinationAddress());
        Assert.Equal(51622, msg.SourcePort());
        Assert.Equal(1881, msg.DestinationPort());
        IList<HAProxyTLV> tlvs = msg.Tlvs();

        Assert.Equal(3, tlvs.Count);
        HAProxyTLV firstTlv = tlvs[0];
        Assert.Equal(HAProxyTLV.Type.PP2_TYPE_SSL, firstTlv.Type_());
        HAProxySSLTLV sslTlv = (HAProxySSLTLV) firstTlv;
        Assert.Equal(0, sslTlv.Verify());
        Assert.True(sslTlv.IsPP2ClientSSL());
        Assert.True(sslTlv.IsPP2ClientCertSess());
        Assert.False(sslTlv.IsPP2ClientCertConn());

        HAProxyTLV secondTlv = tlvs[1];

        Assert.Equal(HAProxyTLV.Type.PP2_TYPE_SSL_VERSION, secondTlv.Type_());
        ByteBuf secondContentBuf = secondTlv.Content;
        byte[] secondContent = new byte[secondContentBuf.ReadableBytes];
        secondContentBuf.ReadBytes(secondContent);
        Assert.Equal(Encoding.ASCII.GetBytes("TLSv1"), secondContent);

        HAProxyTLV thirdTLV = tlvs[2];
        Assert.Equal(HAProxyTLV.Type.PP2_TYPE_SSL_CN, thirdTLV.Type_());
        ByteBuf thirdContentBuf = thirdTLV.Content;
        byte[] thirdContent = new byte[thirdContentBuf.ReadableBytes];
        thirdContentBuf.ReadBytes(thirdContent);
        Assert.Equal(Encoding.ASCII.GetBytes("LEAF"), thirdContent);

        Assert.True(sslTlv.EncapsulatedTLVs().Contains(secondTlv));
        Assert.True(sslTlv.EncapsulatedTLVs().Contains(thirdTLV));

        Assert.True(0 < firstTlv.RefCnt);
        Assert.True(0 < secondTlv.RefCnt);
        Assert.True(0 < thirdTLV.RefCnt);
        Assert.True(msg.Release());
        Assert.Equal(0, firstTlv.RefCnt);
        Assert.Equal(0, secondTlv.RefCnt);
        Assert.Equal(0, thirdTLV.RefCnt);

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestV2WithNestedSslTLVs()
    {
        ch = new EmbeddedChannel(new HAProxyMessageDecoder());

        // Outer SSL TLV (type=0x20, content=28):
        //   client(1)=0x05  verify(4)=0
        //   Inner SSL TLV (type=0x20, content=13):     <-- depth-1 nested SSL
        //     client(1)=0x01  verify(4)=0
        //     PP2_TYPE_SSL_VERSION (type=0x21, len=5): "TLSv1"   <-- depth-2 leaf
        //   PP2_TYPE_SSL_CN (type=0x22, len=4): "LEAF"           <-- depth-1 leaf
        byte[] bytes = {
                13, 10, 13, 10, 0, 13, 10, 81, 85, 73, 84, 10,   // v2 signature
                33, 17,                                            // v2|PROXY, TCP4
                0, 43,                                             // remaining: 12 + 31
                127, 0, 0, 1, 127, 0, 0, 1, unchecked((byte) -55), unchecked((byte) -90), 7, 89,     // addresses + ports
                32, 0, 28,                                         // outer SSL: type=0x20, len=28
                5, 0, 0, 0, 0,                                    // outer: client=0x05, verify=0
                32, 0, 13,                                         // inner SSL: type=0x20, len=13
                1, 0, 0, 0, 0,                                    // inner: client=0x01, verify=0
                33, 0, 5, 84, 76, 83, 118, 49,                    // SSL_VERSION: "TLSv1"
                34, 0, 4, 76, 69, 65, 70                           // SSL_CN: "LEAF"
        };

        int startChannels = ch.Pipeline.Names().Count;
        Assert.True(ch.WriteInbound(CopiedBuffer(bytes)));
        object msgObj = ch.ReadInbound();
        Assert.Equal(startChannels - 1, ch.Pipeline.Names().Count);
        HAProxyMessage msg = (HAProxyMessage) msgObj;

        Assert.Equal(HAProxyProtocolVersion.V2, msg.ProtocolVersion());
        Assert.Equal(HAProxyCommand.PROXY, msg.Command());
        Assert.Equal(HAProxyProxiedProtocol.TCP4, msg.ProxiedProtocol());
        Assert.Equal("127.0.0.1", msg.SourceAddress());
        Assert.Equal("127.0.0.1", msg.DestinationAddress());
        Assert.Equal(51622, msg.SourcePort());
        Assert.Equal(1881, msg.DestinationPort());
        IList<HAProxyTLV> tlvs = msg.Tlvs();

        // Flattened list: [outerSSL, innerSSL, SSL_CN]
        // SSL_CN is a direct child of outer, so it is flattened.
        // innerSSL is also a direct child of outer, so it is flattened.
        // But "TLSv1" (SSL_VERSION) is a child of innerSSL (depth 2) — NOT flattened.
        Assert.Equal(3, tlvs.Count);
        HAProxyTLV firstTlv = tlvs[0];
        Assert.Equal(HAProxyTLV.Type.PP2_TYPE_SSL, firstTlv.Type_());
        HAProxySSLTLV sslTlv = (HAProxySSLTLV) firstTlv;
        Assert.Equal(0, sslTlv.Verify());
        Assert.True(sslTlv.IsPP2ClientSSL());
        Assert.True(sslTlv.IsPP2ClientCertSess());
        Assert.False(sslTlv.IsPP2ClientCertConn());

        HAProxyTLV secondTlv = tlvs[1];

        Assert.Equal(HAProxyTLV.Type.PP2_TYPE_SSL, secondTlv.Type_());
        HAProxySSLTLV innerSslTlv = (HAProxySSLTLV) secondTlv;

        // The depth-2 leaf: SSL_VERSION "TLSv1" lives inside innerSslTlv
        Assert.Equal(1, innerSslTlv.EncapsulatedTLVs().Count);
        HAProxyTLV depth2Leaf = innerSslTlv.EncapsulatedTLVs()[0];
        Assert.Equal(HAProxyTLV.Type.PP2_TYPE_SSL_VERSION, depth2Leaf.Type_());
        ByteBuf versionBuf = depth2Leaf.Content;
        byte[] versionContent = new byte[versionBuf.ReadableBytes];
        versionBuf.ReadBytes(versionContent);
        Assert.Equal(Encoding.ASCII.GetBytes("TLSv1"), versionContent);

        HAProxyTLV thirdTLV = tlvs[2];
        Assert.Equal(HAProxyTLV.Type.PP2_TYPE_SSL_CN, thirdTLV.Type_());
        ByteBuf thirdContentBuf = thirdTLV.Content;
        byte[] thirdContent = new byte[thirdContentBuf.ReadableBytes];
        thirdContentBuf.ReadBytes(thirdContent);
        Assert.Equal(Encoding.ASCII.GetBytes("LEAF"), thirdContent);

        Assert.True(sslTlv.EncapsulatedTLVs().Contains(secondTlv));
        Assert.True(sslTlv.EncapsulatedTLVs().Contains(thirdTLV));

        Assert.True(0 < firstTlv.RefCnt);
        Assert.True(0 < secondTlv.RefCnt);
        Assert.True(0 < thirdTLV.RefCnt);
        Assert.True(0 < depth2Leaf.RefCnt);
        Assert.True(msg.Release());

        // The depth-2 leaf TLV must be fully released after message.release().
        // It is a child of the inner SSL TLV (depth 1), but readTlvs() only flattens
        // one level of encapsulated TLVs.
        Assert.Equal(0, depth2Leaf.RefCnt, "Depth-2 leaf TLV leaked");
        Assert.Equal(0, firstTlv.RefCnt);
        Assert.Equal(0, secondTlv.RefCnt);
        Assert.Equal(0, thirdTLV.RefCnt);

        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestReleaseHAProxyMessage()
    {
        ch = new EmbeddedChannel(new HAProxyMessageDecoder());

        byte[] bytes = {
                13, 10, 13, 10, 0, 13, 10, 81, 85, 73, 84, 10, 33, 17, 0, 35, 127, 0, 0, 1, 127, 0, 0, 1,
                unchecked((byte) -55), unchecked((byte) -90), 7, 89, 32, 0, 20, 5, 0, 0, 0, 0, 33, 0, 5, 84, 76, 83, 118, 49, 34, 0, 4, 76, 69, 65, 70
        };

        int startChannels = ch.Pipeline.Names().Count;
        Assert.True(ch.WriteInbound(CopiedBuffer(bytes)));
        object msgObj = ch.ReadInbound();
        Assert.Equal(startChannels - 1, ch.Pipeline.Names().Count);
        HAProxyMessage msg = (HAProxyMessage) msgObj;

        IList<HAProxyTLV> tlvs = msg.Tlvs();
        Assert.Equal(3, tlvs.Count);

        Assert.Equal(1, msg.RefCnt);

        foreach (HAProxyTLV tlv in tlvs)
        {
            Assert.Equal(3, tlv.RefCnt);
        }

        // Retain the haproxy message
        msg.Retain();
        Assert.Equal(2, msg.RefCnt);

        foreach (HAProxyTLV tlv in tlvs)
        {
            Assert.Equal(3, tlv.RefCnt);
        }

        // Decrease the haproxy message refCnt
        msg.Release();
        Assert.Equal(1, msg.RefCnt);

        foreach (HAProxyTLV tlv in tlvs)
        {
            Assert.Equal(3, tlv.RefCnt);
        }

        // Release haproxy message, TLVs will be released with it
        msg.Release();
        Assert.Equal(0, msg.RefCnt);

        foreach (HAProxyTLV tlv in tlvs)
        {
            Assert.Equal(0, tlv.RefCnt);
        }
    }

    [Fact]
    public void TestV2WithTLV()
    {
        ch = new EmbeddedChannel(new HAProxyMessageDecoder(4));

        byte[] header = new byte[236];
        header[0] = 0x0D; // Binary Prefix
        header[1] = 0x0A; // -----
        header[2] = 0x0D; // -----
        header[3] = 0x0A; // -----
        header[4] = 0x00; // -----
        header[5] = 0x0D; // -----
        header[6] = 0x0A; // -----
        header[7] = 0x51; // -----
        header[8] = 0x55; // -----
        header[9] = 0x49; // -----
        header[10] = 0x54; // -----
        header[11] = 0x0A; // -----

        header[12] = 0x21; // v2, cmd=PROXY
        header[13] = 0x31; // UNIX_STREAM

        header[14] = 0x00; // Remaining Bytes
        header[15] = (byte) 0xdc; // -----

        header[16] = 0x2f; // Source Address
        header[17] = 0x76; // -----
        header[18] = 0x61; // -----
        header[19] = 0x72; // -----
        header[20] = 0x2f; // -----
        header[21] = 0x72; // -----
        header[22] = 0x75; // -----
        header[23] = 0x6e; // -----
        header[24] = 0x2f; // -----
        header[25] = 0x73; // -----
        header[26] = 0x72; // -----
        header[27] = 0x63; // -----
        header[28] = 0x2e; // -----
        header[29] = 0x73; // -----
        header[30] = 0x6f; // -----
        header[31] = 0x63; // -----
        header[32] = 0x6b; // -----
        header[33] = 0x00; // -----

        header[124] = 0x2f; // Destination Address
        header[125] = 0x76; // -----
        header[126] = 0x61; // -----
        header[127] = 0x72; // -----
        header[128] = 0x2f; // -----
        header[129] = 0x72; // -----
        header[130] = 0x75; // -----
        header[131] = 0x6e; // -----
        header[132] = 0x2f; // -----
        header[133] = 0x64; // -----
        header[134] = 0x65; // -----
        header[135] = 0x73; // -----
        header[136] = 0x74; // -----
        header[137] = 0x2e; // -----
        header[138] = 0x73; // -----
        header[139] = 0x6f; // -----
        header[140] = 0x63; // -----
        header[141] = 0x6b; // -----
        header[142] = 0x00; // -----

        // ---- Additional data (TLV) ---- \\

        header[232] = 0x01; // Type
        header[233] = 0x00; // Remaining bytes
        header[234] = 0x01; // -----
        header[235] = 0x01; // Payload

        int startChannels = ch.Pipeline.Names().Count;
        ch.WriteInbound(CopiedBuffer(header));
        object msgObj = ch.ReadInbound();
        Assert.Equal(startChannels - 1, ch.Pipeline.Names().Count);
        Assert.IsAssignableFrom<HAProxyMessage>(msgObj);
        HAProxyMessage msg = (HAProxyMessage) msgObj;
        Assert.Equal(HAProxyProtocolVersion.V2, msg.ProtocolVersion());
        Assert.Equal(HAProxyCommand.PROXY, msg.Command());
        Assert.Equal(HAProxyProxiedProtocol.UNIX_STREAM, msg.ProxiedProtocol());
        Assert.Equal("/var/run/src.sock", msg.SourceAddress());
        Assert.Equal("/var/run/dest.sock", msg.DestinationAddress());
        Assert.Equal(0, msg.SourcePort());
        Assert.Equal(0, msg.DestinationPort());
        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
        Assert.True(msg.Release());
    }

    [Fact]
    public void TestV2InvalidProtocol()
    {
        byte[] header = new byte[28];
        header[0] = 0x0D; // Binary Prefix
        header[1] = 0x0A; // -----
        header[2] = 0x0D; // -----
        header[3] = 0x0A; // -----
        header[4] = 0x00; // -----
        header[5] = 0x0D; // -----
        header[6] = 0x0A; // -----
        header[7] = 0x51; // -----
        header[8] = 0x55; // -----
        header[9] = 0x49; // -----
        header[10] = 0x54; // -----
        header[11] = 0x0A; // -----

        header[12] = 0x21; // v2, cmd=PROXY
        header[13] = 0x41; // Bogus transport protocol

        header[14] = 0x00; // Remaining Bytes
        header[15] = 0x0c; // -----

        header[16] = (byte) 0xc0; // Source Address
        header[17] = (byte) 0xa8; // -----
        header[18] = 0x00; // -----
        header[19] = 0x01; // -----

        header[20] = (byte) 0xc0; // Destination Address
        header[21] = (byte) 0xa8; // -----
        header[22] = 0x00; // -----
        header[23] = 0x0b; // -----

        header[24] = (byte) 0xdc; // Source Port
        header[25] = 0x04; // -----

        header[26] = 0x01; // Destination Port
        header[27] = (byte) 0xbb; // -----

        Assert.Throws<HAProxyProtocolException>(() => ch.WriteInbound(CopiedBuffer(header)));
    }

    [Fact]
    public void TestV2MissingParams()
    {
        byte[] header = new byte[26];
        header[0] = 0x0D; // Binary Prefix
        header[1] = 0x0A; // -----
        header[2] = 0x0D; // -----
        header[3] = 0x0A; // -----
        header[4] = 0x00; // -----
        header[5] = 0x0D; // -----
        header[6] = 0x0A; // -----
        header[7] = 0x51; // -----
        header[8] = 0x55; // -----
        header[9] = 0x49; // -----
        header[10] = 0x54; // -----
        header[11] = 0x0A; // -----

        header[12] = 0x21; // v2, cmd=PROXY
        header[13] = 0x11; // TCP over IPv4

        header[14] = 0x00; // Remaining Bytes
        header[15] = 0x0a; // -----

        header[16] = (byte) 0xc0; // Source Address
        header[17] = (byte) 0xa8; // -----
        header[18] = 0x00; // -----
        header[19] = 0x01; // -----

        header[20] = (byte) 0xc0; // Destination Address
        header[21] = (byte) 0xa8; // -----
        header[22] = 0x00; // -----
        header[23] = 0x0b; // -----

        header[24] = (byte) 0xdc; // Source Port
        header[25] = 0x04; // -----

        Assert.Throws<HAProxyProtocolException>(() => ch.WriteInbound(CopiedBuffer(header)));
    }

    [Fact]
    public void TestV2InvalidCommand()
    {
        byte[] header = new byte[28];
        header[0] = 0x0D; // Binary Prefix
        header[1] = 0x0A; // -----
        header[2] = 0x0D; // -----
        header[3] = 0x0A; // -----
        header[4] = 0x00; // -----
        header[5] = 0x0D; // -----
        header[6] = 0x0A; // -----
        header[7] = 0x51; // -----
        header[8] = 0x55; // -----
        header[9] = 0x49; // -----
        header[10] = 0x54; // -----
        header[11] = 0x0A; // -----

        header[12] = 0x22; // v2, Bogus command
        header[13] = 0x11; // TCP over IPv4

        header[14] = 0x00; // Remaining Bytes
        header[15] = 0x0c; // -----

        header[16] = (byte) 0xc0; // Source Address
        header[17] = (byte) 0xa8; // -----
        header[18] = 0x00; // -----
        header[19] = 0x01; // -----

        header[20] = (byte) 0xc0; // Destination Address
        header[21] = (byte) 0xa8; // -----
        header[22] = 0x00; // -----
        header[23] = 0x0b; // -----

        header[24] = (byte) 0xdc; // Source Port
        header[25] = 0x04; // -----

        header[26] = 0x01; // Destination Port
        header[27] = (byte) 0xbb; // -----

        Assert.Throws<HAProxyProtocolException>(() => ch.WriteInbound(CopiedBuffer(header)));
    }

    [Fact]
    public void TestV2InvalidVersion()
    {
        byte[] header = new byte[28];
        header[0] = 0x0D; // Binary Prefix
        header[1] = 0x0A; // -----
        header[2] = 0x0D; // -----
        header[3] = 0x0A; // -----
        header[4] = 0x00; // -----
        header[5] = 0x0D; // -----
        header[6] = 0x0A; // -----
        header[7] = 0x51; // -----
        header[8] = 0x55; // -----
        header[9] = 0x49; // -----
        header[10] = 0x54; // -----
        header[11] = 0x0A; // -----

        header[12] = 0x31; // Bogus version, cmd=PROXY
        header[13] = 0x11; // TCP over IPv4

        header[14] = 0x00; // Remaining Bytes
        header[15] = 0x0c; // -----

        header[16] = (byte) 0xc0; // Source Address
        header[17] = (byte) 0xa8; // -----
        header[18] = 0x00; // -----
        header[19] = 0x01; // -----

        header[20] = (byte) 0xc0; // Destination Address
        header[21] = (byte) 0xa8; // -----
        header[22] = 0x00; // -----
        header[23] = 0x0b; // -----

        header[24] = (byte) 0xdc; // Source Port
        header[25] = 0x04; // -----

        header[26] = 0x01; // Destination Port
        header[27] = (byte) 0xbb; // -----

        Assert.Throws<HAProxyProtocolException>(() => ch.WriteInbound(CopiedBuffer(header)));
    }

    [Fact]
    public void TestV2HeaderTooLong()
    {
        ch = new EmbeddedChannel(new HAProxyMessageDecoder(0));

        byte[] header = new byte[248];
        header[0] = 0x0D; // Binary Prefix
        header[1] = 0x0A; // -----
        header[2] = 0x0D; // -----
        header[3] = 0x0A; // -----
        header[4] = 0x00; // -----
        header[5] = 0x0D; // -----
        header[6] = 0x0A; // -----
        header[7] = 0x51; // -----
        header[8] = 0x55; // -----
        header[9] = 0x49; // -----
        header[10] = 0x54; // -----
        header[11] = 0x0A; // -----

        header[12] = 0x21; // v2, cmd=PROXY
        header[13] = 0x11; // TCP over IPv4

        header[14] = 0x00; // Remaining Bytes
        header[15] = (byte) 0xe8; // -----

        header[16] = (byte) 0xc0; // Source Address
        header[17] = (byte) 0xa8; // -----
        header[18] = 0x00; // -----
        header[19] = 0x01; // -----

        header[20] = (byte) 0xc0; // Destination Address
        header[21] = (byte) 0xa8; // -----
        header[22] = 0x00; // -----
        header[23] = 0x0b; // -----

        header[24] = (byte) 0xdc; // Source Port
        header[25] = 0x04; // -----

        header[26] = 0x01; // Destination Port
        header[27] = (byte) 0xbb; // -----

        Assert.Throws<HAProxyProtocolException>(() => ch.WriteInbound(CopiedBuffer(header)));
    }

    [Fact]
    public void TestV2IncompleteHeader()
    {
        byte[] header = new byte[13];
        header[0] = 0x0D; // Binary Prefix
        header[1] = 0x0A; // -----
        header[2] = 0x0D; // -----
        header[3] = 0x0A; // -----
        header[4] = 0x00; // -----
        header[5] = 0x0D; // -----
        header[6] = 0x0A; // -----
        header[7] = 0x51; // -----
        header[8] = 0x55; // -----
        header[9] = 0x49; // -----
        header[10] = 0x54; // -----
        header[11] = 0x0A; // -----

        header[12] = 0x21; // v2, cmd=PROXY

        ch.WriteInbound(CopiedBuffer(header));
        Assert.Null(ch.ReadInbound());
        Assert.False(ch.Finish());
    }

    [Fact]
    public void TestDetectProtocol()
    {
        ByteBuf validHeaderV1 = CopiedBuffer("PROXY TCP4 192.168.0.1 192.168.0.11 56324 443\r\n",
                                                   Encoding.ASCII);
        ProtocolDetectionResult<HAProxyProtocolVersion> result = HAProxyMessageDecoder.DetectProtocol(validHeaderV1);
        Assert.Equal(ProtocolDetectionState.DETECTED, result.State());
        Assert.Equal(HAProxyProtocolVersion.V1, result.DetectedProtocol());
        validHeaderV1.Release();

        ByteBuf invalidHeader = CopiedBuffer("Invalid header", Encoding.ASCII);
        result = HAProxyMessageDecoder.DetectProtocol(invalidHeader);
        Assert.Equal(ProtocolDetectionState.INVALID, result.State());
        Assert.Null(result.DetectedProtocol());
        invalidHeader.Release();

        ByteBuf validHeaderV2 = Unpooled.Buffer();
        validHeaderV2.WriteByte(0x0D);
        validHeaderV2.WriteByte(0x0A);
        validHeaderV2.WriteByte(0x0D);
        validHeaderV2.WriteByte(0x0A);
        validHeaderV2.WriteByte(0x00);
        validHeaderV2.WriteByte(0x0D);
        validHeaderV2.WriteByte(0x0A);
        validHeaderV2.WriteByte(0x51);
        validHeaderV2.WriteByte(0x55);
        validHeaderV2.WriteByte(0x49);
        validHeaderV2.WriteByte(0x54);
        validHeaderV2.WriteByte(0x0A);
        result = HAProxyMessageDecoder.DetectProtocol(validHeaderV2);
        Assert.Equal(ProtocolDetectionState.DETECTED, result.State());
        Assert.Equal(HAProxyProtocolVersion.V2, result.DetectedProtocol());
        validHeaderV2.Release();

        ByteBuf incompleteHeader = Unpooled.Buffer();
        incompleteHeader.WriteByte(0x0D);
        incompleteHeader.WriteByte(0x0A);
        incompleteHeader.WriteByte(0x0D);
        incompleteHeader.WriteByte(0x0A);
        incompleteHeader.WriteByte(0x00);
        incompleteHeader.WriteByte(0x0D);
        incompleteHeader.WriteByte(0x0A);
        result = HAProxyMessageDecoder.DetectProtocol(incompleteHeader);
        Assert.Equal(ProtocolDetectionState.NEEDS_MORE_DATA, result.State());
        Assert.Null(result.DetectedProtocol());
        incompleteHeader.Release();
    }

    [Fact]
    public void TestInvalidProtocolUnsigned()
    {
        ByteBuf invalidData = Unpooled.Buffer().WriteBytes(
                new byte[] { 0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A, (byte) 0xFF });
        invalidData.WriteZero(64);
        Assert.Throws<HAProxyProtocolException>(() => ch.WriteInbound(invalidData));
    }

    [Fact]
    // CLR: ByteArrayOutputStream is MemoryStream (write(int) is WriteByte) and the big-endian ByteBuffer of two bytes is a
    // byte[2] written with BinaryPrimitives.WriteInt16BigEndian
    // (https://learn.microsoft.com/dotnet/api/system.buffers.binary.binaryprimitives.writeint16bigendian); clear() is
    // Array.Clear.
    public void TestNestedTLV()
    {
        MemoryStream headerWriter = new MemoryStream();
        //src_ip = "AAAA", dst_ip = "BBBB", src_port = "CC", dst_port = "DD"
        headerWriter.Write(new byte[] {(byte) 'A', (byte) 'A', (byte) 'A', (byte) 'A', (byte) 'B', (byte) 'B', (byte) 'B', (byte) 'B', (byte) 'C', (byte) 'C', (byte) 'D', (byte) 'D'});
        //write TLVs
        int countOfTLVs = 8100;
        byte[] tlvLengthBuf = new byte[2];
        short totalLength = (short) (countOfTLVs * (1 + 2 + 1 + 4));

        for (int i = 0; i < countOfTLVs; i++)
        {
            //write PP2_TYPE_SSL TLV
            headerWriter.WriteByte(0x20); //PP2_TYPE_SSL
            //notice that the TLV length cannot be bigger than 0xffff
            totalLength -= 1 + 2; //exclude type and length themselves
            Array.Clear(tlvLengthBuf);
            BinaryPrimitives.WriteInt16BigEndian(tlvLengthBuf, totalLength);
            //add to the header
            headerWriter.Write(tlvLengthBuf);
            //write client field
            headerWriter.WriteByte(1);
            //write verify field
            headerWriter.Write(new byte[] {(byte) 'V', (byte) 'V', (byte) 'V', (byte) 'V'});
            //subtract the client and verify fields
            totalLength -= 1 + 4;
        }

        byte[] header = headerWriter.ToArray();
        byte[] numsWrite = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(numsWrite, (short) header.Length);

        ByteBuf data = Unpooled.Buffer();
        data.WriteBytes(new byte[] {
                (byte) 0x0D,
                (byte) 0x0A,
                (byte) 0x0D,
                (byte) 0x0A,
                (byte) 0x00,
                (byte) 0x0D,
                (byte) 0x0A,
                (byte) 0x51,
                (byte) 0x55,
                (byte) 0x49,
                (byte) 0x54,
                (byte) 0x0A
        });
        //verCmd = 32
        byte versionCmd = 0x20 | 1; //V2 | ProxyCmd
        data.WriteByte(versionCmd);
        data.WriteByte(17); //TPAF_TCP4_BYTE
        data.WriteBytes(numsWrite);
        data.WriteBytes(header);

        Assert.Throws<HAProxyProtocolException>(() => ch.WriteInbound(data));
    }

    // CLR: @ParameterizedTest with @ValueSource(shorts) is [Theory] with [InlineData]; the stream and buffer mappings are
    // those of testNestedTLV.
    [Theory]
    [InlineData(
            (short) 4)] // Use a length which is < 5.
    [InlineData(
            short.MaxValue)] // Use a length which is > readable bytes.
    public void TestInvalidTLVLengthCorrectlyHandled(short length)
    {
        MemoryStream headerWriter = new MemoryStream();
        //src_ip = "AAAA", dst_ip = "BBBB", src_port = "CC", dst_port = "DD"
        headerWriter.Write(new byte[] {(byte) 'A', (byte) 'A', (byte) 'A', (byte) 'A', (byte) 'B', (byte) 'B', (byte) 'B', (byte) 'B', (byte) 'C', (byte) 'C', (byte) 'D', (byte) 'D'});
        //write TLV
        byte[] tlvLengthBuf = new byte[2];
        //write PP2_TYPE_SSL TLV
        headerWriter.WriteByte(0x20); //PP2_TYPE_SSL
        //notice that the TLV length cannot be bigger than 0xffff
        Array.Clear(tlvLengthBuf);
        BinaryPrimitives.WriteInt16BigEndian(tlvLengthBuf, length);
        //add to the header
        headerWriter.Write(tlvLengthBuf);
        //write client field
        headerWriter.WriteByte(1);
        //write verify field
        headerWriter.Write(new byte[] {(byte) 'V', (byte) 'V', (byte) 'V', (byte) 'V'});
        //subtract the client and verify fields

        byte[] header = headerWriter.ToArray();
        byte[] numsWrite = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(numsWrite, (short) header.Length);

        ByteBuf data = Unpooled.Buffer();
        data.WriteBytes(new byte[] {
                (byte) 0x0D,
                (byte) 0x0A,
                (byte) 0x0D,
                (byte) 0x0A,
                (byte) 0x00,
                (byte) 0x0D,
                (byte) 0x0A,
                (byte) 0x51,
                (byte) 0x55,
                (byte) 0x49,
                (byte) 0x54,
                (byte) 0x0A
        });
        //verCmd = 32
        byte versionCmd = 0x20 | 1; //V2 | ProxyCmd
        data.WriteByte(versionCmd);
        data.WriteByte(17); //TPAF_TCP4_BYTE
        data.WriteBytes(numsWrite);
        data.WriteBytes(header);

        Assert.Throws<HAProxyProtocolException>(() => ch.WriteInbound(data));
    }

    [Fact]
    public void TestReadTlvsLeaksRetainedBufferWhenSecondSSLTLVIsMalformed()
    {
        ByteBuf data = Unpooled.Buffer();
        data.WriteBytes(new byte[] {
                13, 10, 13, 10, 0, 13, 10, 81, 85, 73, 84, 10,   // v2 signature
                33, 17,                                            // V2|PROXY, TCP4
                0, 26,                                             // remaining = 26 (12 addr + 8 TLV#1 + 6 TLV#2)
                65, 65, 65, 65, 66, 66, 66, 66, 67, 67, 68, 68,  // addr + ports
                32, 0, 5, 1, 0, 0, 0, 0,                         // TLV #1: PP2_TYPE_SSL len=5, client=1, verify=0
                32, 0, 3, 65, 66, 67                              // TLV #2: PP2_TYPE_SSL len=3 (MALFORMED: len < 5)
        });

        Assert.Equal(1, data.RefCnt);
        Assert.Throws<HAProxyProtocolException>(() => HAProxyMessage.DecodeHeader(data));

        try
        {
            Assert.Equal(1, data.RefCnt,
                    "TLV #1 rawContent leaked in readTlvs() - expected refCnt=1, got " + data.RefCnt);
        }
        finally
        {
            data.Release();
        }
    }

    [Fact]
    public void TestEncapsulatedTLVsLeakWhenInnerSSLTLVIsMalformed()
    {
        ByteBuf data = Unpooled.Buffer();
        data.WriteBytes(new byte[] {
                13, 10, 13, 10, 0, 13, 10, 81, 85, 73, 84, 10,     // v2 signature
                33, 17,                                            // V2|PROXY, TCP4
                0, 34,                                             // remaining = 34 (12 addr + 22 outer SSL TLV)
                65, 65, 65, 65, 66, 66, 66, 66, 67, 67, 68, 68,    // addr + ports
                32, 0, 19,                                         // outer SSL: type=0x20, len=19
                5, 0, 0, 0, 0,                                     // outer: client=0x05, verify=0
                33, 0, 5, 84, 76, 83, 118, 49,                     // inner SSL_VERSION: "TLSv1" (readRetainedSlice)
                32, 0, 3, 65, 66, 67                               // inner SSL: len=3 (MALFORMED) → throws
        });

        Assert.Equal(1, data.RefCnt);
        Assert.Throws<HAProxyProtocolException>(() => HAProxyMessage.DecodeHeader(data));

        try
        {
            Assert.Equal(1, data.RefCnt,
                    "Inner PP2_TYPE_SSL_VERSION buffer leaked in encapsulated TLV loop - " +
                            "expected refCnt=1, got " + data.RefCnt);
        }
        finally
        {
            data.Release();
        }
    }

    [Fact]
    public void GrandchildByteBufIsReleasedOnErrorPath()
    {
        byte[] wire = new byte[] {
            // -- 12-byte v2 signature (decodeHeader only skipBytes(12); contents unchecked) --
            0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A,
            0x21,             // verCmd: version 2, PROXY command
            0x11,             // protFam: AF_IPv4 + STREAM (TCP4)
            0x00, 0x0C,       // addressInfoLen = 12 (min for IPv4; does not bound the TLV region)
            0x00, 0x00, 0x00, 0x00,  // src addr 0.0.0.0
            0x00, 0x00, 0x00, 0x00,  // dst addr 0.0.0.0
            0x00, 0x00,       // src port
            0x00, 0x00,       // dst port
            // -- outer SSL TLV: type 0x20, len 21 --
            0x20, 0x00, 0x15,
            0x00,                     //   client
            0x00, 0x00, 0x00, 0x00,   //   verify
            // ---- child SSL TLV: type 0x20, len 9 (a grandchild-holder) ----
            0x20, 0x00, 0x09,
            0x00,                     //     client
            0x00, 0x00, 0x00, 0x00,   //     verify
            // ------ ALPN grandchild TLV: type 0x01, len 1 -> readRetainedSlice(1) leaks here
            0x01, 0x00, 0x01,
            (byte) 0xAA,              //       1 sentinel content byte (the leaked slice)
            // ---- malformed sibling SSL TLV: type 0x20, len 1 (< 5) -> throws ----
            0x20, 0x00, 0x01,
            (byte) 0xFF               //     pad so readableBytes() >= 4 at the loop re-entry guard
        };
        ByteBuf header = Unpooled.Buffer().WriteBytes(wire);
        Assert.Equal(1, header.RefCnt, "precondition: freshly allocated buffer");

        Assert.Throws<HAProxyProtocolException>(() => HAProxyMessage.DecodeHeader(header));
        int refCnt = header.RefCnt;

        // Regression gate: the fix must return the shared buffer to refCnt 1.
        Assert.Equal(1, refCnt,
            "grandchild TLV ByteBuf leaked on the readNextTLV error path (refCnt should be 1)");
        Assert.True(header.Release(), "buffer should be fully released");
        Assert.Equal(0, header.RefCnt);
    }

    /**
     * Negative control: the SAME nesting (outer SSL -> child SSL -> ALPN grandchild) WITHOUT the
     * malformed sibling decodes successfully and releases cleanly on both trees. This isolates the
     * defect to the error path, not the nesting itself.
     */
    [Fact]
    public void WellFormedNestedHeaderDecodesAndReleasesCleanly()
    {
        byte[] wire = {
            0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A,
            0x21, 0x11, 0x00, 0x0C,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00,
            0x00, 0x00,
            // outer SSL TLV: type 0x20, len 17 (client+verify+child, NO malformed sibling)
            0x20, 0x00, 0x11,
            0x00, 0x00, 0x00, 0x00, 0x00,
            // child SSL TLV: type 0x20, len 9
            0x20, 0x00, 0x09,
            0x00, 0x00, 0x00, 0x00, 0x00,
            // ALPN grandchild: type 0x01, len 1
            0x01, 0x00, 0x01, (byte) 0xAA
        };
        ByteBuf header = Unpooled.Buffer().WriteBytes(wire);

        HAProxyMessage msg = HAProxyMessage.DecodeHeader(header);
        Assert.True(header.RefCnt > 1, "decode retains slices of the header");
        Assert.False(msg.Tlvs().Count == 0, "expected the top-level SSL TLV");
        Assert.True(msg.Release(), "message should be fully released");
        Assert.Equal(1, header.RefCnt, "no leak on the happy path (only the test's own ref remains)");
        Assert.True(header.Release(), "buffer fully released");
        Assert.Equal(0, header.RefCnt);
    }
}
