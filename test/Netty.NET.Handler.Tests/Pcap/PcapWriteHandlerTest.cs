/*
 * Copyright 2020 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Handler.Pcap;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Embedded;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Handler.Tests.Pcap;

// CLR: InetSocketAddress is IPEndPoint and new InetSocketAddress(literal, port) parses the literal; OutputStream
// is System.IO.Stream and ByteArrayOutputStream is MemoryStream; "...".getBytes() uses the default charset,
// Encoding.Default; builder() is Builder_(). The Transport bootstrap API is asynchronous, so sync() is an await of
// BindAsync/ConnectAsync/WriteAndFlushAsync/ CloseAsync/ShutdownGracefullyAsync followed by isSuccess() as
// IsCompletedSuccessfully, and CountDownLatch is a TaskCompletionSource awaited for 5 seconds (as codec-haproxy's
// HAProxyIntegrationTest). ByteBuf readByte()/readUnsignedShort()/readUnsignedInt() return System.Byte, ushort and
// uint; the Java long/int expectations compare through Assert.Equal<long>/<int>, and the signed byte literals
// ((byte) 0xff, -1 in a byte[] initializer, the int literal 0xa1b2c3d4) keep the bits Java compares. AssertJ's
// assertThat(x).isGreaterThan(y) is Assert.True(x > y).
public class PcapWriteHandlerTest
{

    [Fact]
    public async Task UdpV4SharedOutputStreamTest()
    {
        await UdpV4(true, true);
    }

    [Fact]
    public async Task UdpV4NonOutputStream()
    {
        await UdpV4(false, true);
    }

    [Fact]
    public async Task UdpV4NoGlobalHeaderOutputStream()
    {
        await UdpV4(false, false);
    }

    private static async Task UdpV4(bool sharedOutputStream, bool writeGlobalHeaders)
    {
        ByteBuf byteBuf = Unpooled.Buffer();
        ByteBuf payload = Unpooled.WrappedBuffer(Encoding.Default.GetBytes("Meow"));

        try
        {
            IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 0);
            IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 0);

            IEventLoopGroup eventLoopGroup = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

            // We'll bootstrap a UDP Server to avoid "Network Unreachable errors" when sending UDP Packet.
            Bootstrap server = new Bootstrap()
                    .Group(eventLoopGroup)
                    .Channel<NioDatagramChannel>()
                    .Handler(new DiscardingDatagramHandler());

            Task<IChannel> channelFutureServer = server.BindAsync(serverAddr);
            IChannel serverChannel = await channelFutureServer;
            Assert.True(channelFutureServer.IsCompletedSuccessfully);

            CloseDetectingByteBufOutputStream outputStream = new CloseDetectingByteBufOutputStream(byteBuf);

            // We'll bootstrap a UDP Client for sending UDP Packets to UDP Server.
            Bootstrap client = new Bootstrap()
                    .Group(eventLoopGroup)
                    .Channel<NioDatagramChannel>()
                    .Handler(PcapWriteHandler.Builder_()
                            .SharedOutputStream(sharedOutputStream)
                            .WritePcapGlobalHeader(writeGlobalHeaders)
                            .Build(outputStream));

            Task<IChannel> channelFutureClient =
                    client.BindAsync(clientAddr);
            IChannel clientChannel = await channelFutureClient;
            Assert.True(channelFutureClient.IsCompletedSuccessfully);

            DatagramPacket datagram = new DatagramPacket(payload.Copy(),
                    (IPEndPoint) serverChannel.LocalAddress);
            Task write = clientChannel.WriteAndFlushAsync(datagram);
            await write;
            Assert.True(write.IsCompletedSuccessfully);
            Task shutdown = eventLoopGroup.ShutdownGracefullyAsync();
            await shutdown;
            Assert.True(shutdown.IsCompletedSuccessfully);

            // if sharedOutputStream is true or writeGlobalHeaders is false, we don't verify the global headers.
            VerifyUdpCapture(!sharedOutputStream && writeGlobalHeaders,
                    byteBuf, payload,
                    (IPEndPoint) serverChannel.LocalAddress,
                    (IPEndPoint) clientChannel.LocalAddress
            );

            // If sharedOutputStream is true, we don't close the outputStream.
            // If sharedOutputStream is false, we close the outputStream.
            Assert.Equal(!sharedOutputStream, outputStream.CloseCalled());
        }
        finally
        {
            byteBuf.Release();
            payload.Release();
        }
    }

    [Fact]
    public void EmbeddedUdp()
    {
        ByteBuf pcapBuffer = Unpooled.Buffer();
        ByteBuf payload = Unpooled.WrappedBuffer(Encoding.Default.GetBytes("Meow"));

        try
        {
            IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 1234);
            IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("2.2.2.2"), 3456);

            // We fake a client
            EmbeddedChannel embeddedChannel = new EmbeddedChannel(
                    PcapWriteHandler.Builder_()
                            .ForceUdpChannel(clientAddr, serverAddr)
                            .Build(new ByteBufOutputStream(pcapBuffer))
            );

            Assert.True(embeddedChannel.WriteOutbound(payload.RetainedDuplicate()));
            ByteBuf read = embeddedChannel.ReadOutbound<ByteBuf>();
            Assert.Equal(payload, read);
            read.Release();

            Assert.True(embeddedChannel.WriteInbound(payload.RetainedDuplicate()));
            read = embeddedChannel.ReadInbound<ByteBuf>();
            Assert.Equal(payload, read);
            read.Release();

            // Verify the capture data
            VerifyUdpCapture(true, pcapBuffer, payload, serverAddr, clientAddr);

            VerifyUdpCapture(false, pcapBuffer, payload, clientAddr, serverAddr);

            Assert.False(embeddedChannel.FinishAndReleaseAll());
        }
        finally
        {
            pcapBuffer.Release();
            payload.Release();
        }
    }

    [Fact]
    public void UdpMixedAddress()
    {
        ByteBuf pcapBuffer = Unpooled.Buffer();
        ByteBuf payload = Unpooled.WrappedBuffer(Encoding.Default.GetBytes("Meow"));

        try
        {
            IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 1234);
            // for ipv6 ::, it's allowed to connect to ipv4 on some systems
            IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("::"), 3456);

            // We fake a client
            EmbeddedChannel embeddedChannel = new EmbeddedDatagramChannel(clientAddr, serverAddr);
            embeddedChannel.Pipeline.AddLast(PcapWriteHandler.Builder_()
                    .Build(new ByteBufOutputStream(pcapBuffer)));

            Assert.True(embeddedChannel.WriteOutbound(payload.RetainedDuplicate()));
            ByteBuf read = embeddedChannel.ReadOutbound<ByteBuf>();
            Assert.Equal(payload, read);
            read.Release();

            // Verify the capture data
            VerifyUdpCapture(true, pcapBuffer, payload, serverAddr, new IPEndPoint(IPAddress.Parse("0.0.0.0"), 3456));

            Assert.False(embeddedChannel.FinishAndReleaseAll());
        }
        finally
        {
            pcapBuffer.Release();
            payload.Release();
        }
    }

    [Fact]
    public void UdpLargeByteBufPayload()
    {
        ByteBuf pcapBuffer = Unpooled.Buffer();
        //Create payload 1 byte larger than maximum
        string payloadString = new string('X', 65507 + 1);
        ByteBuf payload = Unpooled.WrappedBuffer(Encoding.Default.GetBytes(payloadString));

        try
        {
            IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 1234);
            IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("2.2.2.2"), 3456);

            // We fake a client
            EmbeddedChannel embeddedChannel = new EmbeddedDatagramChannel(clientAddr, serverAddr);
            embeddedChannel.Pipeline.AddLast(PcapWriteHandler.Builder_()
                    .Build(new ByteBufOutputStream(pcapBuffer)));

            Assert.True(embeddedChannel.WriteOutbound(payload.RetainedDuplicate()));
            ByteBuf read = embeddedChannel.ReadOutbound<ByteBuf>();
            Assert.Equal(payload, read);
            read.Release();

            // Verify only the PCAP global header was written. Large UDP payload should be discarded
            Assert.Equal(24, pcapBuffer.ReadableBytes);

            Assert.False(embeddedChannel.FinishAndReleaseAll());
        }
        finally
        {
            pcapBuffer.Release();
            payload.Release();
        }
    }

    [Fact]
    public void UdpLargeDatagramPayload()
    {
        IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 1234);
        IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("2.2.2.2"), 3456);

        ByteBuf pcapBuffer = Unpooled.Buffer();
        //Create payload 1 byte larger than maximum
        string payloadString = new string('X', 65507 + 1);
        DatagramPacket datagram =
                new DatagramPacket(Unpooled.WrappedBuffer(Encoding.Default.GetBytes(payloadString)), serverAddr);

        try
        {
            // We fake a client
            EmbeddedChannel embeddedChannel = new EmbeddedDatagramChannel(clientAddr, serverAddr);
            embeddedChannel.Pipeline.AddLast(PcapWriteHandler.Builder_()
                    .Build(new ByteBufOutputStream(pcapBuffer)));

            Assert.True(embeddedChannel.WriteOutbound(datagram.RetainedDuplicate()));
            DatagramPacket read = embeddedChannel.ReadOutbound<DatagramPacket>();
            Assert.Equal(datagram.Content, read.Content);
            read.Release();

            // Verify only the PCAP global header was written. Large UDP payload should be discarded
            Assert.Equal(24, pcapBuffer.ReadableBytes);

            Assert.False(embeddedChannel.FinishAndReleaseAll());
        }
        finally
        {
            pcapBuffer.Release();
            datagram.Release();
        }
    }

    [Fact]
    public void UdpZeroLengthByteBufCaptured()
    {
        ByteBuf pcapBuffer = Unpooled.Buffer();
        ByteBuf payload = Unpooled.Buffer();

        try
        {
            IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 1234);
            IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("2.2.2.2"), 3456);

            // We fake a client
            EmbeddedChannel embeddedChannel = new EmbeddedDatagramChannel(clientAddr, serverAddr);
            embeddedChannel.Pipeline.AddLast(PcapWriteHandler.Builder_()
                    .CaptureZeroByte(true)
                    .Build(new ByteBufOutputStream(pcapBuffer)));

            Assert.True(embeddedChannel.WriteOutbound(payload.RetainedDuplicate()));
            ByteBuf read = embeddedChannel.ReadOutbound<ByteBuf>();
            Assert.Equal(payload, read);
            read.Release();

            Assert.True(embeddedChannel.WriteInbound(payload.RetainedDuplicate()));
            read = embeddedChannel.ReadInbound<ByteBuf>();
            Assert.Equal(payload, read);
            read.Release();

            VerifyUdpCapture(true, pcapBuffer, payload, serverAddr, clientAddr);

            VerifyUdpCapture(false, pcapBuffer, payload, clientAddr, serverAddr);

            Assert.False(embeddedChannel.FinishAndReleaseAll());
        }
        finally
        {
            pcapBuffer.Release();
            payload.Release();
        }
    }

    [Fact]
    public void UdpZeroLengthByteBufDiscarded()
    {
        ByteBuf pcapBuffer = Unpooled.Buffer();
        ByteBuf payload = Unpooled.Buffer();

        try
        {
            IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 1234);
            IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("2.2.2.2"), 3456);

            // We fake a client
            EmbeddedChannel embeddedChannel = new EmbeddedDatagramChannel(clientAddr, serverAddr);
            embeddedChannel.Pipeline.AddLast(PcapWriteHandler.Builder_()
                    .Build(new ByteBufOutputStream(pcapBuffer)));

            Assert.True(embeddedChannel.WriteOutbound(payload.RetainedDuplicate()));
            ByteBuf read = embeddedChannel.ReadOutbound<ByteBuf>();
            Assert.Equal(payload, read);
            read.Release();

            Assert.True(embeddedChannel.WriteInbound(payload.RetainedDuplicate()));
            read = embeddedChannel.ReadInbound<ByteBuf>();
            Assert.Equal(payload, read);
            read.Release();

            // Verify only the PCAP global header was written. Large UDP payload should be discarded
            Assert.Equal(24, pcapBuffer.ReadableBytes);

            Assert.False(embeddedChannel.FinishAndReleaseAll());
        }
        finally
        {
            pcapBuffer.Release();
            payload.Release();
        }
    }

    [Fact]
    public void UdpZeroLengthDatagramCaptured()
    {
        ByteBuf pcapBuffer = Unpooled.Buffer();
        ByteBuf payload = Unpooled.Buffer();
        IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 1234);
        IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("2.2.2.2"), 3456);
        DatagramPacket outbound = new DatagramPacket(payload.RetainedDuplicate(), serverAddr);
        DatagramPacket inbound = new DatagramPacket(payload.RetainedDuplicate(), clientAddr, serverAddr);

        try
        {
            // We fake a client
            EmbeddedChannel embeddedChannel = new EmbeddedDatagramChannel(clientAddr, serverAddr);
            embeddedChannel.Pipeline.AddLast(PcapWriteHandler.Builder_()
                    .CaptureZeroByte(true)
                    .Build(new ByteBufOutputStream(pcapBuffer)));

            Assert.True(embeddedChannel.WriteOutbound(outbound.RetainedDuplicate()));
            DatagramPacket read = embeddedChannel.ReadOutbound<DatagramPacket>();
            Assert.Equal(outbound.Content, read.Content);
            read.Release();

            Assert.True(embeddedChannel.WriteInbound(inbound.RetainedDuplicate()));
            read = embeddedChannel.ReadInbound<DatagramPacket>();
            Assert.Equal(inbound.Content, read.Content);
            read.Release();

            VerifyUdpCapture(true, pcapBuffer, payload, serverAddr, clientAddr);

            VerifyUdpCapture(false, pcapBuffer, payload, clientAddr, serverAddr);

            Assert.False(embeddedChannel.FinishAndReleaseAll());
        }
        finally
        {
            pcapBuffer.Release();
            payload.Release();
            outbound.Release();
            inbound.Release();
        }
    }

    [Fact]
    public void UdpZeroLengthDatagramDiscarded()
    {
        ByteBuf pcapBuffer = Unpooled.Buffer();
        ByteBuf payload = Unpooled.Buffer();
        IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 1234);
        IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("2.2.2.2"), 3456);
        DatagramPacket outbound = new DatagramPacket(payload.RetainedDuplicate(), serverAddr);
        DatagramPacket inbound = new DatagramPacket(payload.RetainedDuplicate(), clientAddr, serverAddr);

        try
        {
            // We fake a client
            EmbeddedChannel embeddedChannel = new EmbeddedDatagramChannel(clientAddr, serverAddr);
            embeddedChannel.Pipeline.AddLast(PcapWriteHandler.Builder_()
                    .Build(new ByteBufOutputStream(pcapBuffer)));

            Assert.True(embeddedChannel.WriteOutbound(outbound.RetainedDuplicate()));
            DatagramPacket read = embeddedChannel.ReadOutbound<DatagramPacket>();
            Assert.Equal(outbound.Content, read.Content);
            read.Release();

            Assert.True(embeddedChannel.WriteInbound(inbound.RetainedDuplicate()));
            read = embeddedChannel.ReadInbound<DatagramPacket>();
            Assert.Equal(inbound.Content, read.Content);
            read.Release();

            // Verify only the PCAP global header was written. Large UDP payload should be discarded
            Assert.Equal(24, pcapBuffer.ReadableBytes);

            Assert.False(embeddedChannel.FinishAndReleaseAll());
        }
        finally
        {
            pcapBuffer.Release();
            payload.Release();
            outbound.Release();
            inbound.Release();
        }
    }

    [Fact]
    public void UdpExceptionCaught()
    {
        ByteBuf pcapBuffer = Unpooled.Buffer();

        try
        {
            Exception exception = new Exception();

            IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 1234);
            IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("2.2.2.2"), 3456);

            CloseDetectingByteBufOutputStream outputStream = new CloseDetectingByteBufOutputStream(pcapBuffer);
            // We fake a client
            EmbeddedChannel embeddedChannel = new EmbeddedDatagramChannel(clientAddr, serverAddr);
            embeddedChannel.Pipeline.AddLast(PcapWriteHandler.Builder_()
                    .Build(outputStream));
            embeddedChannel.Pipeline.FireExceptionCaught(exception);

            Assert.True(outputStream.CloseCalled());

            // Verify only the PCAP global header was written.
            Assert.Equal(24, pcapBuffer.ReadableBytes);

            // Verify thrown exception
            Exception t = Assert.ThrowsAny<Exception>(() => embeddedChannel.CheckException());
            Assert.Same(exception, t);

            Assert.False(embeddedChannel.FinishAndReleaseAll());
        }
        finally
        {
            pcapBuffer.Release();
        }
    }

    [Fact]
    public async Task TcpV4SharedOutputStreamTest()
    {
        await TcpV4(true, true);
    }

    [Fact]
    public async Task TcpV4NoGlobalHeaderOutputStream()
    {
        await TcpV4(false, false);
    }

    [Fact]
    public async Task TcpV4NonOutputStream()
    {
        await TcpV4(false, true);
    }

    private static async Task TcpV4(bool sharedOutputStream, bool writeGlobalHeaders)
    {
        ByteBuf byteBuf = Unpooled.Buffer();
        ByteBuf payload = Unpooled.WrappedBuffer(Encoding.Default.GetBytes("Meow"));

        try
        {
            IEventLoopGroup group = new MultiThreadIoEventLoopGroup(1, NioIoHandler.NewFactory());

            // Configure the echo server
            ServerBootstrap sb = new ServerBootstrap();
            TaskCompletionSource serverLatch =
                    new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            sb.Group(group)
                    .Channel<NioServerSocketChannel>()
                    .Option(ChannelOption.SO_BACKLOG, 100)
                    .ChildHandler(new EchoServerInitializer(byteBuf, payload, serverLatch, sharedOutputStream,
                                                            writeGlobalHeaders));

            // Start the server.
            Task<IChannel> serverChannelFuture = sb.BindAsync(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 0));
            IChannel serverChannel = await serverChannelFuture;
            Assert.True(serverChannelFuture.IsCompletedSuccessfully);

            // configure the client
            Bootstrap cb = new Bootstrap();
            TaskCompletionSource clientLatch =
                    new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            cb.Group(group)
                    .Channel<NioSocketChannel>()
                    .Option(ChannelOption.TCP_NODELAY, true)
                    .Handler(new ClientInitializer(payload, clientLatch));

            IPEndPoint serverAddress = (IPEndPoint) serverChannel.LocalAddress;
            // Start the client.
            Task<IChannel> clientChannelFuture = cb.ConnectAsync(serverAddress);
            IChannel clientChannel = await clientChannelFuture;
            Assert.True(clientChannelFuture.IsCompletedSuccessfully);

            IPEndPoint clientAddress = (IPEndPoint) clientChannel.LocalAddress;

            Assert.True(await AwaitLatch(serverLatch, 5));
            Assert.True(await AwaitLatch(clientLatch, 5));

            await clientChannel.CloseAsync();
            await serverChannel.CloseAsync();

            // Shut down all event loops to terminate all threads.
            Task shutdown = group.ShutdownGracefullyAsync();
            await shutdown;
            Assert.True(shutdown.IsCompletedSuccessfully);

            // if sharedOutputStream is true or writeGlobalHeaders is false, we don't verify the global headers.
            VerifyTcpHandshakeCapture(
                    writeGlobalHeaders && !sharedOutputStream,
                    byteBuf,
                    serverAddress,
                    clientAddress);

            VerifyTcpCapture(
                    byteBuf, payload,
                    serverAddress,
                    clientAddress,
                    1, 1); //After handshake, sequence and ack are 1

            VerifyTcpCapture(
                    byteBuf, payload,
                    clientAddress,
                    serverAddress,
                    1, 5); //Server has read 4 bytes so its ack is now 5

            VerifyTcpCloseCapture(byteBuf,
                    serverAddress,
                    clientAddress,
                    5, 5); //Server and client have both read 4 bytes
        }
        finally
        {
            byteBuf.Release();
            payload.Release();
        }
    }

    [Fact]
    public void EmbeddedTcpClient()
    {
        ByteBuf pcapBuffer = Unpooled.Buffer();
        //Differing size payloads so that sequence numbers and ack numbers are different
        ByteBuf readPayload = Unpooled.WrappedBuffer(Encoding.Default.GetBytes("Read"));
        ByteBuf writePayload = Unpooled.WrappedBuffer(Encoding.Default.GetBytes("Write"));

        try
        {
            IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 1234);
            IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("2.2.2.2"), 3456);

            EmbeddedChannel embeddedChannel = new EmbeddedChannel(
                    PcapWriteHandler.Builder_()
                            .ForceTcpChannel(serverAddr, clientAddr, false)
                            .Build(new ByteBufOutputStream(pcapBuffer))
            );

            Assert.True(embeddedChannel.WriteInbound(readPayload.RetainedDuplicate()));
            ByteBuf read = embeddedChannel.ReadInbound<ByteBuf>();
            Assert.Equal(readPayload, read);
            read.Release();

            Assert.True(embeddedChannel.WriteOutbound(writePayload.RetainedDuplicate()));
            read = embeddedChannel.ReadOutbound<ByteBuf>();
            Assert.Equal(writePayload, read);
            read.Release();

            Assert.False(embeddedChannel.FinishAndReleaseAll());

            // Verify the capture data
            VerifyTcpHandshakeCapture(true, pcapBuffer, serverAddr, clientAddr);

            //Verify client read
            VerifyTcpCapture(pcapBuffer, readPayload,
                    clientAddr, serverAddr,
                    1, 1);

            //Verify client write
            VerifyTcpCapture(pcapBuffer, writePayload,
                    serverAddr, clientAddr,
                    1, 5);

            VerifyTcpCloseCapture(pcapBuffer, serverAddr, clientAddr,
                    6, 5); //Client has received 4 bytes and sent 5 bytes
        }
        finally
        {
            pcapBuffer.Release();
            readPayload.Release();
            writePayload.Release();
        }
    }

    [Fact]
    public void EmbeddedTcpServer()
    {
        ByteBuf pcapBuffer = Unpooled.Buffer();
        //Differing size payloads so that sequence numbers and ack numbers are different
        ByteBuf readPayload = Unpooled.WrappedBuffer(Encoding.Default.GetBytes("Read"));
        ByteBuf writePayload = Unpooled.WrappedBuffer(Encoding.Default.GetBytes("Write"));

        try
        {
            IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 1234);
            IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("2.2.2.2"), 3456);

            EmbeddedChannel embeddedChannel = new EmbeddedChannel(
                    PcapWriteHandler.Builder_()
                            .ForceTcpChannel(serverAddr, clientAddr, true)
                            .Build(new ByteBufOutputStream(pcapBuffer))
            );

            Assert.True(embeddedChannel.WriteInbound(readPayload.RetainedDuplicate()));
            ByteBuf read = embeddedChannel.ReadInbound<ByteBuf>();
            Assert.Equal(readPayload, read);
            read.Release();

            Assert.True(embeddedChannel.WriteOutbound(writePayload.RetainedDuplicate()));
            read = embeddedChannel.ReadOutbound<ByteBuf>();
            Assert.Equal(writePayload, read);
            read.Release();

            Assert.False(embeddedChannel.FinishAndReleaseAll());

            // Verify the capture data
            VerifyTcpHandshakeCapture(true, pcapBuffer, serverAddr, clientAddr);

            //Verify server read
            VerifyTcpCapture(pcapBuffer, readPayload,
                    serverAddr, clientAddr,
                    1, 1);

            //Verify server write
            VerifyTcpCapture(pcapBuffer, writePayload,
                    clientAddr, serverAddr,
                    1, 5);

            VerifyTcpCloseCapture(pcapBuffer, serverAddr, clientAddr,
                    5, 6); //Client has received 5 bytes and sent 4
        }
        finally
        {
            pcapBuffer.Release();
            readPayload.Release();
            writePayload.Release();
        }
    }

    [Fact]
    public void TcpLargePayload()
    {
        ByteBuf pcapBuffer = Unpooled.Buffer();
        string payloadString = new string('X', 65495 + 1);
        ByteBuf payload = Unpooled.WrappedBuffer(Encoding.Default.GetBytes(payloadString));

        try
        {
            IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 1234);
            IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("2.2.2.2"), 3456);

            EmbeddedChannel embeddedChannel = new EmbeddedChannel(
                    PcapWriteHandler.Builder_()
                            .ForceTcpChannel(serverAddr, clientAddr, false)
                            .Build(new ByteBufOutputStream(pcapBuffer))
            );

            Assert.True(embeddedChannel.WriteOutbound(payload.RetainedDuplicate()));
            ByteBuf read = embeddedChannel.ReadOutbound<ByteBuf>();
            Assert.Equal(payload, read);
            read.Release();

            Assert.False(embeddedChannel.FinishAndReleaseAll());

            // Verify the capture data
            VerifyTcpHandshakeCapture(true, pcapBuffer, serverAddr, clientAddr);
            //Verify client write
            VerifyTcpCapture(pcapBuffer, payload.Slice(0, 65495),
                    serverAddr, clientAddr,
                    1, 1);
            //Verify client write
            VerifyTcpCapture(pcapBuffer, payload.Slice(65495, 1),
                    serverAddr, clientAddr,
                    65496, 1);

            VerifyTcpCloseCapture(pcapBuffer, serverAddr, clientAddr,
                    65497, 1); //Client has received 4 bytes and sent 5 bytes
        }
        finally
        {
            pcapBuffer.Release();
            payload.Release();
        }
    }

    [Fact]
    public void TcpZeroByteCaptured()
    {
        ByteBuf pcapBuffer = Unpooled.Buffer();
        ByteBuf payload = Unpooled.Buffer();

        try
        {
            IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 1234);
            IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("2.2.2.2"), 3456);

            EmbeddedChannel embeddedChannel = new EmbeddedChannel(
                    PcapWriteHandler.Builder_()
                            .ForceTcpChannel(serverAddr, clientAddr, false)
                            .CaptureZeroByte(true) //Enable zero byte capture
                            .Build(new ByteBufOutputStream(pcapBuffer))
            );

            Assert.True(embeddedChannel.WriteOutbound(payload.RetainedDuplicate()));
            ByteBuf read = embeddedChannel.ReadOutbound<ByteBuf>();
            Assert.Equal(payload, read);
            read.Release();

            Assert.True(embeddedChannel.WriteInbound(payload.RetainedDuplicate()));
            read = embeddedChannel.ReadInbound<ByteBuf>();
            Assert.Equal(payload, read);

            Assert.False(embeddedChannel.FinishAndReleaseAll());

            // Verify the capture data
            VerifyTcpHandshakeCapture(true, pcapBuffer, serverAddr, clientAddr);

            //Verify client write empty payload
            VerifyTcpCapture(pcapBuffer, payload,
                    serverAddr, clientAddr,
                    1, 1);

            //Verify client read empty payload
            VerifyTcpCapture(pcapBuffer, payload,
                    clientAddr, serverAddr,
                    1, 1);

            VerifyTcpCloseCapture(pcapBuffer, serverAddr, clientAddr,
                    1, 1);
        }
        finally
        {
            pcapBuffer.Release();
            payload.Release();
        }
    }

    [Fact]
    public void TcpZeroByteDiscarded()
    {
        ByteBuf pcapBuffer = Unpooled.Buffer();
        ByteBuf payload = Unpooled.Buffer();

        try
        {
            IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 1234);
            IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("2.2.2.2"), 3456);

            EmbeddedChannel embeddedChannel = new EmbeddedChannel(
                    PcapWriteHandler.Builder_()
                            .ForceTcpChannel(serverAddr, clientAddr, false)
                            .Build(new ByteBufOutputStream(pcapBuffer))
            );

            Assert.True(embeddedChannel.WriteOutbound(payload.RetainedDuplicate()));
            ByteBuf read = embeddedChannel.ReadOutbound<ByteBuf>();
            Assert.Equal(payload, read);
            read.Release();

            Assert.True(embeddedChannel.WriteInbound(payload.RetainedDuplicate()));
            read = embeddedChannel.ReadInbound<ByteBuf>();
            Assert.Equal(payload, read);
            read.Release();

            Assert.False(embeddedChannel.FinishAndReleaseAll());

            //Verify client discard empty payloads. Pcap should only contain header, syn, and fin messages

            VerifyTcpHandshakeCapture(true, pcapBuffer, serverAddr, clientAddr);
            VerifyTcpCloseCapture(pcapBuffer, serverAddr, clientAddr,
                    1, 1);
        }
        finally
        {
            pcapBuffer.Release();
            payload.Release();
        }
    }

    [Fact]
    public void TcpExceptionCaught()
    {
        ByteBuf pcapBuffer = Unpooled.Buffer();
        Exception exception = new Exception();

        IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 1234);
        IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("2.2.2.2"), 3456);

        CloseDetectingByteBufOutputStream outputStream = new CloseDetectingByteBufOutputStream(pcapBuffer);
        EmbeddedChannel embeddedChannel = new EmbeddedChannel(
                new ExceptionOnActiveHandler(exception),
                PcapWriteHandler.Builder_()
                                .ForceTcpChannel(serverAddr, clientAddr, false)
                                .Build(outputStream)
        );

        // Verify tcp handshake, tcp rst, and stream close

        Assert.True(outputStream.CloseCalled());

        VerifyTcpHandshakeCapture(true, pcapBuffer, serverAddr, clientAddr);

        // Verify rst
        ByteBuf tcpPacket = VerifyTcpBaseHeaders(pcapBuffer, serverAddr, 0);

        Assert.Equal(clientAddr.Port & 0xffff, tcpPacket.ReadUnsignedShort()); // Source Port
        Assert.Equal(serverAddr.Port & 0xffff, tcpPacket.ReadUnsignedShort()); // Destination Port
        Assert.Equal(1L, tcpPacket.ReadUnsignedInt()); // Sequence number
        Assert.Equal(1L, tcpPacket.ReadUnsignedInt()); // Ack number
        tcpPacket.ReadByte(); // Offset/Reserved
        Assert.Equal(20, tcpPacket.ReadByte()); // RST control bit
        Assert.Equal(65535, tcpPacket.ReadUnsignedShort()); // Window Size
        Assert.Equal(1, tcpPacket.ReadUnsignedShort()); // Checksum
        Assert.Equal(0, tcpPacket.ReadUnsignedShort()); // Urgent Pointer

        // Verify thrown exception
        try
        {
            Exception t = Assert.ThrowsAny<Exception>(() => embeddedChannel.CheckException());
            Assert.Same(exception, t);
        }
        finally
        {
            pcapBuffer.Release();
        }

        Assert.False(embeddedChannel.FinishAndReleaseAll());
    }

    [Fact]
    public void WritePcapGreaterThan4Gb()
    {
        IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 1234);
        IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("2.2.2.2"), 3456);

        RecordingOutputStream outputStream = new RecordingOutputStream();
        EmbeddedChannel embeddedChannel = new EmbeddedChannel(
                new DiscardingWritesAndFlushesHandler(), //Discard writes/flushes
                PcapWriteHandler.Builder_()
                                .ForceTcpChannel(serverAddr, clientAddr, true)
                                .Build(outputStream),
                new DiscardingReadsHandler() //Discard reads
        );

        int chunkSize = 0xFFFF - 40; // 40 bytes for header data
        string payloadString = new string('X', chunkSize);
        ByteBuf payload = Unpooled.WrappedBuffer(Encoding.Default.GetBytes(payloadString));

        try
        {
            long fourGB = 0xFFFFFFFFL;

            // Let's send 4 GiB inbound, ...
            long msgCount = (fourGB / chunkSize) + 2;

            for (int i = 0; i < msgCount; i++)
            {
                //Only store the last data/ack
                if (i == msgCount - 1)
                {
                    outputStream.Record();
                }

                embeddedChannel.WriteInbound(payload.RetainedDuplicate());
            }

            //Validate the wrapped number
            long sequenceNumber = ((msgCount - 1) * chunkSize) - fourGB;
            long ackNumber = 1;
            ByteBuf buf = Unpooled.WrappedBuffer(outputStream.RecordedBytes());
            VerifyTcpCapture(buf, payload, serverAddr, clientAddr, sequenceNumber, ackNumber);
            buf.Release();
            outputStream.Reset();

            // ... and 4 GiB outbound.
            for (int i = 0; i < msgCount; i++)
            {
                //Only store the last data/ack
                if (i == msgCount - 1)
                {
                    outputStream.Record();
                }

                embeddedChannel.WriteOutbound(payload.RetainedDuplicate());
            }

            //Validate the wrapped number
            ackNumber = sequenceNumber + chunkSize;
            buf = Unpooled.WrappedBuffer(outputStream.RecordedBytes());
            VerifyTcpCapture(buf, payload, clientAddr, serverAddr, sequenceNumber, ackNumber);
            buf.Release();
            outputStream.Reset();
            Assert.True(outputStream.BytesWritten() > 2 * (fourGB + 1000));

            Assert.False(embeddedChannel.FinishAndReleaseAll());
        }
        finally
        {
            payload.Release();
        }
    }

    [Fact]
    public void WriterStateTest()
    {
        ByteBuf payload = Unpooled.WrappedBuffer(Encoding.Default.GetBytes("Meow"));

        try
        {
            IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 1234);
            IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("2.2.2.2"), 3456);

            PcapWriteHandler pcapWriteHandler = PcapWriteHandler.Builder_()
                    .ForceTcpChannel(serverAddr, clientAddr, true)
                    .Build(new DiscardEverythingOutputStream());

            // State is INIT because we haven't written anything yet
            // and 'channelActive' is not called yet as this Handler
            // is yet to be attached to `EmbeddedChannel`.
            Assert.Equal(State.INIT, pcapWriteHandler.State());

            // Create a new 'EmbeddedChannel' and add the 'PcapWriteHandler'
            EmbeddedChannel embeddedChannel = new EmbeddedChannel(pcapWriteHandler);

            // Write and read some data and verify it.
            Assert.True(embeddedChannel.WriteInbound(payload.RetainedDuplicate()));
            ByteBuf read = embeddedChannel.ReadInbound<ByteBuf>();
            Assert.Equal(payload, read);
            read.Release();

            Assert.True(embeddedChannel.WriteOutbound(payload.RetainedDuplicate()));
            read = embeddedChannel.ReadOutbound<ByteBuf>();
            Assert.Equal(payload, read);
            read.Release();

            // State is now WRITING because we attached Handler to 'EmbeddedChannel'.
            Assert.Equal(State.WRITING, pcapWriteHandler.State());

            // Close the PcapWriter. This should trigger closure of PcapWriteHandler too.
            pcapWriteHandler.PCapWriter().Close();

            // State should be changed to closed by now
            Assert.Equal(State.CLOSED, pcapWriteHandler.State());

            // Close PcapWriteHandler again. This should be a no-op.
            pcapWriteHandler.Close();

            // State should still be CLOSED. No change.
            Assert.Equal(State.CLOSED, pcapWriteHandler.State());

            // Close the 'EmbeddedChannel'.
            Assert.False(embeddedChannel.FinishAndReleaseAll());
        }
        finally
        {
            payload.Release();
        }
    }

    [Fact]
    public void PauseResumeTest()
    {
        byte[] payload = Encoding.Default.GetBytes("Meow");
        IPEndPoint serverAddr = new IPEndPoint(IPAddress.Parse("1.1.1.1"), 1234);
        IPEndPoint clientAddr = new IPEndPoint(IPAddress.Parse("2.2.2.2"), 3456);

        DiscardingStatsOutputStream discardingStatsOutputStream = new DiscardingStatsOutputStream();
        PcapWriteHandler pcapWriteHandler = PcapWriteHandler.Builder_()
                                                            .ForceTcpChannel(serverAddr, clientAddr, true)
                                                            .Build(discardingStatsOutputStream);

        // Verify that no writes have been called yet.
        Assert.Equal(0, discardingStatsOutputStream.WritesCalled());

        // Create a new 'EmbeddedChannel' and add the 'PcapWriteHandler'
        EmbeddedChannel embeddedChannel = new EmbeddedChannel(pcapWriteHandler);

        for (int i = 0; i < 10; i++)
        {
            Assert.True(embeddedChannel.WriteInbound(Unpooled.WrappedBuffer(payload)));
        }

        // Since we have written 10 times, we should have a value greater than 0.
        // We can't say it will be 10 exactly because there will be pcap headers also.
        int initialWritesCalled = discardingStatsOutputStream.WritesCalled();
        Assert.True(initialWritesCalled > 0);

        // Pause the Pcap
        pcapWriteHandler.Pause();
        Assert.Equal(State.PAUSED, pcapWriteHandler.State());
        Assert.False(pcapWriteHandler.IsWriting());

        // Write 100 times. No writes should be called in OutputStream.
        for (int i = 0; i < 100; i++)
        {
            Assert.True(embeddedChannel.WriteInbound(Unpooled.WrappedBuffer(payload)));
        }

        // Current stats and previous stats should be same.
        Assert.Equal(initialWritesCalled, discardingStatsOutputStream.WritesCalled());

        // Let's resume the Pcap now.
        pcapWriteHandler.Resume();
        Assert.Equal(State.WRITING, pcapWriteHandler.State());
        Assert.True(pcapWriteHandler.IsWriting());

        // Write 100 times. Writes should be called in OutputStream now.
        for (int i = 0; i < 100; i++)
        {
            Assert.True(embeddedChannel.WriteInbound(Unpooled.WrappedBuffer(payload)));
        }

        // Verify we have written more than before.
        Assert.True(discardingStatsOutputStream.WritesCalled() > initialWritesCalled);

        // Close PcapWriteHandler again. This should be a no-op.
        pcapWriteHandler.Close();

        // State should still be CLOSED. No change.
        Assert.Equal(State.CLOSED, pcapWriteHandler.State());

        // Close the 'EmbeddedChannel'.
        Assert.True(embeddedChannel.FinishAndReleaseAll());
    }

    [Fact]
    public void GlobalHeadersStandalone()
    {
        ByteBuf pcapBuffer = Unpooled.Buffer();
        ByteBufOutputStream outputStream = new ByteBufOutputStream(pcapBuffer, true);
        PcapWriteHandler.WriteGlobalHeader(outputStream);

        VerifyGlobalHeaders(pcapBuffer);

        outputStream.Close();
    }

    // CLR: assertEquals(0xa1b2c3d4, byteBuf.readInt()): the Java hexadecimal int literal is negative, so the C# uint
    // literal is converted unchecked to the same int bits.
    private static void VerifyGlobalHeaders(ByteBuf byteBuf)
    {
        Assert.Equal(unchecked((int) 0xa1b2c3d4), byteBuf.ReadInt()); // magic_number
        Assert.Equal(2, byteBuf.ReadShort());        // version_major
        Assert.Equal(4, byteBuf.ReadShort());        // version_minor
        Assert.Equal(0, byteBuf.ReadInt());          // thiszone
        Assert.Equal(0, byteBuf.ReadInt());          // sigfigs
        Assert.Equal(0xffff, byteBuf.ReadInt());     // snaplen
        Assert.Equal(1, byteBuf.ReadInt());          // network
    }

    private static void VerifyTcpCapture(ByteBuf byteBuf, ByteBuf expectedPayload,
                                         IPEndPoint destinationAddr, IPEndPoint sourceAddr,
                                         long sequenceNumber,
                                         long ackNumber)
    {
        int expectedPayloadLength = expectedPayload.ReadableBytes;

        // Verify data
        ByteBuf tcpPacket = VerifyTcpBaseHeaders(byteBuf, destinationAddr, expectedPayloadLength);

        Assert.Equal(sourceAddr.Port & 0xffff, tcpPacket.ReadUnsignedShort()); // Source Port
        Assert.Equal(destinationAddr.Port & 0xffff, tcpPacket.ReadUnsignedShort()); // Destination Port
        Assert.Equal(sequenceNumber, tcpPacket.ReadUnsignedInt()); // Sequence number
        Assert.Equal(ackNumber, tcpPacket.ReadUnsignedInt()); // Ack number
        tcpPacket.ReadByte(); // Offset/Reserved
        Assert.Equal(16, tcpPacket.ReadByte()); // ACK Control bit
        Assert.Equal(65535, tcpPacket.ReadUnsignedShort()); // Window Size
        Assert.Equal(1, tcpPacket.ReadUnsignedShort()); // Checksum
        Assert.Equal(0, tcpPacket.ReadUnsignedShort()); // Urgent Pointer
        Assert.Equal(ByteBufUtil.GetBytes(expectedPayload), ByteBufUtil.GetBytes(tcpPacket));

        // Verify ACK
        tcpPacket = VerifyTcpBaseHeaders(byteBuf, sourceAddr, 0);

        Assert.Equal(destinationAddr.Port & 0xffff, tcpPacket.ReadUnsignedShort()); // Source Port
        Assert.Equal(sourceAddr.Port & 0xffff, tcpPacket.ReadUnsignedShort()); // Destination Port
        Assert.Equal(ackNumber, tcpPacket.ReadUnsignedInt()); // Sequence number
        Assert.Equal(sequenceNumber + expectedPayloadLength, tcpPacket.ReadUnsignedInt()); // Ack number
        tcpPacket.ReadByte(); // Offset/Reserved
        Assert.Equal(16, tcpPacket.ReadByte()); // ACK Control bit
        Assert.Equal(65535, tcpPacket.ReadUnsignedShort()); // Window Size
        Assert.Equal(1, tcpPacket.ReadUnsignedShort()); // Checksum
        Assert.Equal(0, tcpPacket.ReadUnsignedShort()); // Urgent Pointer
    }

    private static void VerifyTcpHandshakeCapture(bool verifyGlobalHeaders, ByteBuf byteBuf,
                                                  IPEndPoint serverAddr, IPEndPoint clientAddr)
    {
        if (verifyGlobalHeaders)
        {
            VerifyGlobalHeaders(byteBuf);
        }

        // Verify syn

        ByteBuf tcpPacket = VerifyTcpBaseHeaders(byteBuf, serverAddr, 0);

        Assert.Equal(clientAddr.Port & 0xffff, tcpPacket.ReadUnsignedShort()); // Source Port
        Assert.Equal(serverAddr.Port & 0xffff, tcpPacket.ReadUnsignedShort()); // Destination Port
        Assert.Equal(0L, tcpPacket.ReadUnsignedInt()); // Sequence number
        Assert.Equal(0L, tcpPacket.ReadUnsignedInt()); // Ack number
        tcpPacket.ReadByte(); // Offset/Reserved
        Assert.Equal(2, tcpPacket.ReadByte()); // SYN control bit
        Assert.Equal(65535, tcpPacket.ReadUnsignedShort()); // Window Size
        Assert.Equal(1, tcpPacket.ReadUnsignedShort()); // Checksum
        Assert.Equal(0, tcpPacket.ReadUnsignedShort()); // Urgent Pointer

        // Verify syn+ack

        tcpPacket = VerifyTcpBaseHeaders(byteBuf, clientAddr, 0);

        Assert.Equal(serverAddr.Port & 0xffff, tcpPacket.ReadUnsignedShort()); // Source Port
        Assert.Equal(clientAddr.Port & 0xffff, tcpPacket.ReadUnsignedShort()); // Destination Port
        Assert.Equal(0L, tcpPacket.ReadUnsignedInt()); // Sequence number
        Assert.Equal(1L, tcpPacket.ReadUnsignedInt()); // Ack number
        tcpPacket.ReadByte(); // Offset/Reserved
        Assert.Equal(18, tcpPacket.ReadByte()); // SYN+ACK control bit
        Assert.Equal(65535, tcpPacket.ReadUnsignedShort()); // Window Size
        Assert.Equal(1, tcpPacket.ReadUnsignedShort()); // Checksum
        Assert.Equal(0, tcpPacket.ReadUnsignedShort()); // Urgent Pointer

        // Verify ack
        tcpPacket = VerifyTcpBaseHeaders(byteBuf, serverAddr, 0);

        Assert.Equal(clientAddr.Port & 0xffff, tcpPacket.ReadUnsignedShort()); // Source Port
        Assert.Equal(serverAddr.Port & 0xffff, tcpPacket.ReadUnsignedShort()); // Destination Port
        Assert.Equal(1L, tcpPacket.ReadUnsignedInt()); // Sequence number
        Assert.Equal(1L, tcpPacket.ReadUnsignedInt()); // Ack number
        tcpPacket.ReadByte(); // Offset/Reserved
        Assert.Equal(16, tcpPacket.ReadByte()); // ACK control bit
        Assert.Equal(65535, tcpPacket.ReadUnsignedShort()); // Window Size
        Assert.Equal(1, tcpPacket.ReadUnsignedShort()); // Checksum
        Assert.Equal(0, tcpPacket.ReadUnsignedShort()); // Urgent Pointer
    }

    private static void VerifyTcpCloseCapture(ByteBuf byteBuf,
                                              IPEndPoint serverAddr, IPEndPoint clientAddr,
                                              int sequenceNumber, int ackNumber)
    {
        // Verify fin
        ByteBuf tcpPacket = VerifyTcpBaseHeaders(byteBuf, serverAddr, 0);

        Assert.Equal(clientAddr.Port & 0xffff, tcpPacket.ReadUnsignedShort()); // Source Port
        Assert.Equal(serverAddr.Port & 0xffff, tcpPacket.ReadUnsignedShort()); // Destination Port
        Assert.Equal<long>(sequenceNumber, tcpPacket.ReadUnsignedInt()); // Sequence number
        Assert.Equal<long>(ackNumber, tcpPacket.ReadUnsignedInt()); // Ack number
        tcpPacket.ReadByte(); // Offset/Reserved
        Assert.Equal(17, tcpPacket.ReadByte()); // FIN+ACK control bit
        Assert.Equal(65535, tcpPacket.ReadUnsignedShort()); // Window Size
        Assert.Equal(1, tcpPacket.ReadUnsignedShort()); // Checksum
        Assert.Equal(0, tcpPacket.ReadUnsignedShort()); // Urgent Pointer

        // Verify fin+ack
        tcpPacket = VerifyTcpBaseHeaders(byteBuf, clientAddr, 0);

        Assert.Equal(serverAddr.Port & 0xffff, tcpPacket.ReadUnsignedShort()); // Source Port
        Assert.Equal(clientAddr.Port & 0xffff, tcpPacket.ReadUnsignedShort()); // Destination Port
        Assert.Equal<long>(ackNumber, tcpPacket.ReadUnsignedInt()); // Sequence number
        Assert.Equal<long>(sequenceNumber, tcpPacket.ReadUnsignedInt()); // Ack number
        tcpPacket.ReadByte(); // Offset/Reserved
        Assert.Equal(17, tcpPacket.ReadByte()); // FIN+ACK control bit
        Assert.Equal(65535, tcpPacket.ReadUnsignedShort()); // Window Size
        Assert.Equal(1, tcpPacket.ReadUnsignedShort()); // Checksum
        Assert.Equal(0, tcpPacket.ReadUnsignedShort()); // Urgent Pointer

        // Verify ack
        tcpPacket = VerifyTcpBaseHeaders(byteBuf, serverAddr, 0);

        Assert.Equal(clientAddr.Port & 0xffff, tcpPacket.ReadUnsignedShort()); // Source Port
        Assert.Equal(serverAddr.Port & 0xffff, tcpPacket.ReadUnsignedShort()); // Destination Port
        Assert.Equal<long>(sequenceNumber + 1, tcpPacket.ReadUnsignedInt()); // Sequence number
        Assert.Equal<long>(ackNumber + 1, tcpPacket.ReadUnsignedInt()); // Ack number
        tcpPacket.ReadByte(); // Offset/Reserved
        Assert.Equal(16, tcpPacket.ReadByte()); // ACK control bit
        Assert.Equal(65535, tcpPacket.ReadUnsignedShort()); // Window Size
        Assert.Equal(1, tcpPacket.ReadUnsignedShort()); // Checksum
        Assert.Equal(0, tcpPacket.ReadUnsignedShort()); // Urgent Pointer
    }

    private static ByteBuf VerifyTcpBaseHeaders(ByteBuf byteBuf, IPEndPoint destinationAddress,
                                                int expectedPayloadLength)
    {
        // Verify Pcap Packet Header
        byteBuf.ReadInt(); // Just read, we don't care about timestamps for now
        byteBuf.ReadInt(); // Just read, we don't care about timestamps for now
        Assert.Equal(54 + expectedPayloadLength, byteBuf.ReadInt()); // Length of Packet Saved In Pcap
        Assert.Equal(54 + expectedPayloadLength, byteBuf.ReadInt()); // Actual Length of Packet

        // -------------------------------------------- Verify Packet --------------------------------------------
        // Verify Ethernet Packet
        ByteBuf ethernetPacket = byteBuf.ReadSlice(54 + expectedPayloadLength);
        ByteBuf dstMac = ethernetPacket.ReadSlice(6);
        ByteBuf srcMac = ethernetPacket.ReadSlice(6);
        Assert.Equal(unchecked(new byte[] { 0, 0, 94, 0, 83, (byte) -1 }), ByteBufUtil.GetBytes(dstMac));
        Assert.Equal(new byte[] { 0, 0, 94, 0, 83, 0 }, ByteBufUtil.GetBytes(srcMac));
        Assert.Equal(0x0800, ethernetPacket.ReadShort());

        // Verify IPv4 Packet
        ByteBuf ipv4Packet = ethernetPacket.ReadSlice(40 + expectedPayloadLength);
        Assert.Equal(0x45, ipv4Packet.ReadByte());    // Version + IHL
        Assert.Equal(0x00, ipv4Packet.ReadByte());    // DSCP
        Assert.Equal(40 + expectedPayloadLength, ipv4Packet.ReadUnsignedShort());     // Length
        Assert.Equal(0x0000, ipv4Packet.ReadShort()); // Identification
        Assert.Equal(0x0000, ipv4Packet.ReadShort()); // Fragment
        Assert.Equal((byte) 0xff, ipv4Packet.ReadByte());      // TTL
        Assert.Equal((byte) 6, ipv4Packet.ReadByte());        // Protocol
        Assert.Equal(0, ipv4Packet.ReadShort());      // Checksum
        // Source IPv4 Address
        ipv4Packet.ReadInt();
        // Destination IPv4 Address
        Assert.Equal(NetUtil.Ipv4AddressToInt(destinationAddress.Address), ipv4Packet.ReadInt());

        return ipv4Packet.ReadSlice(20 + expectedPayloadLength);
    }

    private static void VerifyUdpCapture(bool verifyGlobalHeaders, ByteBuf byteBuf, ByteBuf expectedPayload,
                                         IPEndPoint remoteAddress, IPEndPoint localAddress)
    {
        if (verifyGlobalHeaders)
        {
            VerifyGlobalHeaders(byteBuf);
        }

        int expectedPayloadLength = expectedPayload.ReadableBytes;

        // Verify Pcap Packet Header
        byteBuf.ReadInt(); // Just read, we don't care about timestamps for now
        byteBuf.ReadInt(); // Just read, we don't care about timestamps for now
        Assert.Equal(42 + expectedPayloadLength, byteBuf.ReadInt()); // Length of Packet Saved In Pcap
        Assert.Equal(42 + expectedPayloadLength, byteBuf.ReadInt()); // Actual Length of Packet

        // -------------------------------------------- Verify Packet --------------------------------------------
        // Verify Ethernet Packet
        ByteBuf ethernetPacket = byteBuf.ReadBytes(42 + expectedPayloadLength);
        ByteBuf dstMac = ethernetPacket.ReadBytes(6);
        ByteBuf srcMac = ethernetPacket.ReadBytes(6);
        Assert.Equal(unchecked(new byte[] { 0, 0, 94, 0, 83, (byte) -1 }), ByteBufUtil.GetBytes(dstMac));
        Assert.Equal(new byte[] { 0, 0, 94, 0, 83, 0 }, ByteBufUtil.GetBytes(srcMac));
        Assert.Equal(0x0800, ethernetPacket.ReadShort());

        // Verify IPv4 Packet
        ByteBuf ipv4Packet = ethernetPacket.ReadBytes(28 + expectedPayloadLength);
        Assert.Equal(0x45, ipv4Packet.ReadByte());    // Version + IHL
        Assert.Equal(0x00, ipv4Packet.ReadByte());    // DSCP
        Assert.Equal(28 + expectedPayloadLength, ipv4Packet.ReadShort());     // Length
        Assert.Equal(0x0000, ipv4Packet.ReadShort()); // Identification
        Assert.Equal(0x0000, ipv4Packet.ReadShort()); // Fragment
        Assert.Equal((byte) 0xff, ipv4Packet.ReadByte());      // TTL
        Assert.Equal((byte) 17, ipv4Packet.ReadByte());        // Protocol
        Assert.Equal(0, ipv4Packet.ReadShort());      // Checksum

        // Source IPv4 Address
        Assert.Equal(NetUtil.Ipv4AddressToInt(localAddress.Address), ipv4Packet.ReadInt());

        // Destination IPv4 Address
        Assert.Equal(NetUtil.Ipv4AddressToInt(remoteAddress.Address), ipv4Packet.ReadInt());

        // Verify UDP Packet
        ByteBuf udpPacket = ipv4Packet.ReadBytes(8 + expectedPayloadLength);
        Assert.Equal(localAddress.Port & 0xffff, udpPacket.ReadUnsignedShort()); // Source Port
        Assert.Equal(remoteAddress.Port & 0xffff, udpPacket.ReadUnsignedShort()); // Destination Port
        Assert.Equal(8 + expectedPayloadLength, udpPacket.ReadShort());     // Length
        Assert.Equal(0x0001, udpPacket.ReadShort()); // Checksum

        // Verify Payload
        ByteBuf payload = udpPacket.ReadBytes(expectedPayloadLength);
        Assert.Equal(ByteBufUtil.GetBytes(expectedPayload), ByteBufUtil.GetBytes(payload)); // Payload

        // Release all ByteBuf
        Assert.True(dstMac.Release());
        Assert.True(srcMac.Release());

        if (expectedPayloadLength > 0)
        {
            Assert.True(payload.Release());
        }

        Assert.True(ethernetPacket.Release());
        Assert.True(ipv4Packet.Release());
        Assert.True(udpPacket.Release());
    }

    // CLR: CountDownLatch.await(timeout, SECONDS) for a latch of one count.
    private static async Task<bool> AwaitLatch(TaskCompletionSource latch, int seconds)
    {
        return await Task.WhenAny(latch.Task,
                Task.Delay(TimeSpan.FromSeconds(seconds), TestContext.Current.CancellationToken)) == latch.Task;
    }

    private sealed class DiscardingDatagramHandler : SimpleChannelInboundHandler<DatagramPacket>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, DatagramPacket msg)
        {
            // Discard
        }
    }

    // CLR: the anonymous ChannelInitializer<SocketChannel> of the echo server in tcpV4 (initChannel is protected in C#,
    // which cannot widen an overridden member's access) and its anonymous ChannelInboundHandlerAdapter;
    // ctx.writeAndFlush(buf) and ctx.close() are WriteAndFlushAsync and CloseAsync.
    private sealed class EchoServerInitializer : ChannelInitializer<ISocketChannel>
    {
        private readonly ByteBuf byteBuf;
        private readonly ByteBuf payload;
        private readonly TaskCompletionSource serverLatch;
        private readonly bool sharedOutputStream;
        private readonly bool writeGlobalHeaders;

        internal EchoServerInitializer(ByteBuf byteBuf, ByteBuf payload, TaskCompletionSource serverLatch,
                                       bool sharedOutputStream, bool writeGlobalHeaders)
        {
            this.byteBuf = byteBuf;
            this.payload = payload;
            this.serverLatch = serverLatch;
            this.sharedOutputStream = sharedOutputStream;
            this.writeGlobalHeaders = writeGlobalHeaders;
        }

        protected override void InitChannel(ISocketChannel ch)
        {
            IChannelPipeline p = ch.Pipeline;
            p.AddLast(PcapWriteHandler.Builder_().SharedOutputStream(sharedOutputStream)
                    .WritePcapGlobalHeader(writeGlobalHeaders)
                    .Build(new ByteBufOutputStream(byteBuf)));
            p.AddLast(new EchoServerHandler(payload, serverLatch));
        }
    }

    private sealed class EchoServerHandler : ChannelInboundHandlerAdapter
    {
        private readonly ByteBuf payload;
        private readonly TaskCompletionSource serverLatch;
        private int read;

        internal EchoServerHandler(ByteBuf payload, TaskCompletionSource serverLatch)
        {
            this.payload = payload;
            this.serverLatch = serverLatch;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ByteBuf buf = (ByteBuf) msg;
            read += buf.ReadableBytes;
            _ = ctx.WriteAndFlushAsync(buf);

            if (read == payload.ReadableBytes)
            {
                serverLatch.TrySetResult();
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            _ = ctx.CloseAsync();
        }
    }

    // CLR: the anonymous ChannelInitializer<SocketChannel> of the client in tcpV4 and its anonymous
    // ChannelInboundHandlerAdapter.
    private sealed class ClientInitializer : ChannelInitializer<ISocketChannel>
    {
        private readonly ByteBuf payload;
        private readonly TaskCompletionSource clientLatch;

        internal ClientInitializer(ByteBuf payload, TaskCompletionSource clientLatch)
        {
            this.payload = payload;
            this.clientLatch = clientLatch;
        }

        protected override void InitChannel(ISocketChannel ch)
        {
            IChannelPipeline p = ch.Pipeline;
            p.AddLast(new ClientHandler(payload, clientLatch));
        }
    }

    private sealed class ClientHandler : ChannelInboundHandlerAdapter
    {
        private readonly ByteBuf payload;
        private readonly TaskCompletionSource clientLatch;
        private int read;

        internal ClientHandler(ByteBuf payload, TaskCompletionSource clientLatch)
        {
            this.payload = payload;
            this.clientLatch = clientLatch;
        }

        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            ByteBuf buf = (ByteBuf) msg;
            read += buf.ReadableBytes;
            buf.Release();

            if (read == payload.ReadableBytes)
            {
                clientLatch.TrySetResult();
            }
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            _ = ctx.WriteAndFlushAsync(payload.Copy());
        }

        public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
        {
            _ = ctx.CloseAsync();
        }
    }

    private sealed class ExceptionOnActiveHandler : ChannelInboundHandlerAdapter
    {
        private readonly Exception exception;

        internal ExceptionOnActiveHandler(Exception exception)
        {
            this.exception = exception;
        }

        public override void ChannelActive(IChannelHandlerContext ctx)
        {
            base.ChannelActive(ctx);

            ctx.FireExceptionCaught(exception);
        }
    }

    // CLR: the local class of writePcapGreaterThan4Gb. write(int) is WriteByte(byte) and write(byte[], int, int) is
    // Write(byte[], int, int), which the span-based Stream.Write of ByteBuf.ReadBytes(Stream, int) reaches through the
    // base implementation; ByteArrayOutputStream is MemoryStream and toByteArray() is ToArray(). The other abstract
    // Stream members describe a write-only stream whose flush() does nothing, as OutputStream.flush() "does nothing"
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/OutputStream.html).
    private sealed class RecordingOutputStream : System.IO.Stream
    {
        private long bytesWritten;
        private MemoryStream @out;

        public override void WriteByte(byte b)
        {
            bytesWritten++;

            if (@out != null)
            {
                @out.WriteByte(b);
            }
        }

        public override void Write(byte[] b, int off, int len)
        {
            bytesWritten += len;

            if (@out != null)
            {
                @out.Write(b, off, len);
            }
        }

        internal void Record()
        {
            @out = new MemoryStream();
        }

        internal void Reset()
        {
            @out = null;
        }

        internal byte[] RecordedBytes()
        {
            return @out.ToArray();
        }

        internal long BytesWritten()
        {
            return bytesWritten;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }

    // CLR: The other abstract Stream members describe a write-only stream whose flush() and close() do nothing, as
    // in OutputStream.
    private sealed class DiscardEverythingOutputStream : System.IO.Stream
    {
        public override void WriteByte(byte b)
        {
            // Discard everything
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                WriteByte(buffer[offset + i]);
            }
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }

    // CLR: an EmbeddedChannel that implements IDatagramChannel. The DatagramChannel methods with a ChannelPromise
    // fold into the Task-returning IDatagramChannel methods, so the sixteen joinGroup/leaveGroup/block overrides
    // are eight; UnsupportedOperationException is NotSupportedException. The localAddress()/remoteAddress()
    // narrowings are covariant property overrides. Java's config() returns a DefaultDatagramChannelConfig over a
    // new DatagramSocket because DatagramChannel narrows config() to DatagramChannelConfig; IDatagramChannel
    // inherits Config unchanged and the Transport DefaultDatagramChannelConfig takes a NioDatagramChannel, so
    // NewConfig() (the lazily read config() of the Transport port) keeps the EmbeddedChannel config. isConnected()
    // is the IsConnected property of IDatagramChannel.
    private class EmbeddedDatagramChannel : EmbeddedChannel, IDatagramChannel
    {
        private readonly IPEndPoint local;
        private readonly IPEndPoint remote;
        private IChannelConfig config;

        internal EmbeddedDatagramChannel(IPEndPoint local, IPEndPoint remote)
            : base(DefaultChannelId.NewInstance(), false)
        {
            this.local = local;
            this.remote = remote;
        }

        public bool IsConnected => true;

        protected override IChannelConfig NewConfig()
        {
            if (config == null)
            {
                // ick! config() is called by the super constructor, so we need to do this.
                config = base.NewConfig();
            }

            return config;
        }

        public override IPEndPoint LocalAddress => (IPEndPoint) base.LocalAddress;

        public override IPEndPoint RemoteAddress => (IPEndPoint) base.RemoteAddress;

        protected override EndPoint LocalAddress0()
        {
            return local;
        }

        protected override EndPoint RemoteAddress0()
        {
            return remote;
        }

        public Task JoinGroupAsync(IPAddress multicastAddress)
        {
            throw new NotSupportedException();
        }

        public Task JoinGroupAsync(IPEndPoint multicastAddress, NetworkInterface networkInterface)
        {
            throw new NotSupportedException();
        }

        public Task JoinGroupAsync(
                IPAddress multicastAddress,
                NetworkInterface networkInterface,
                IPAddress source)
        {
            throw new NotSupportedException();
        }

        public Task LeaveGroupAsync(IPAddress multicastAddress)
        {
            throw new NotSupportedException();
        }

        public Task LeaveGroupAsync(IPEndPoint multicastAddress, NetworkInterface networkInterface)
        {
            throw new NotSupportedException();
        }

        public Task LeaveGroupAsync(
                IPAddress multicastAddress,
                NetworkInterface networkInterface,
                IPAddress source)
        {
            throw new NotSupportedException();
        }

        public Task BlockAsync(
                IPAddress multicastAddress,
                NetworkInterface networkInterface,
                IPAddress sourceToBlock)
        {
            throw new NotSupportedException();
        }

        public Task BlockAsync(IPAddress multicastAddress, IPAddress sourceToBlock)
        {
            throw new NotSupportedException();
        }
    }

    internal sealed class DiscardingReadsHandler : ChannelInboundHandlerAdapter
    {
        public override void ChannelRead(IChannelHandlerContext ctx, object msg)
        {
            //Discard
            ReferenceCountUtil.Release(msg);
        }
    }

    internal class DiscardingWritesAndFlushesHandler : ChannelOutboundHandlerAdapter
    {
        public override void Flush(IChannelHandlerContext ctx)
        {
            //Discard
        }

        public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
        {
            //Discard
            ReferenceCountUtil.Release(msg);
            return Task.CompletedTask;
        }
    }
}
