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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Pcap;

/**
 * <p> {@link PcapWriteHandler} captures {@link ByteBuf} from {@link SocketChannel} / {@link ServerChannel}
 * or {@link DatagramPacket} and writes it into Pcap {@link OutputStream}. </p>
 *
 * <p>
 * Things to keep in mind when using {@link PcapWriteHandler} with TCP:
 *
 *    <ul>
 *        <li> Whenever {@link ChannelInboundHandlerAdapter#channelActive(ChannelHandlerContext)} is called,
 *        a fake TCP 3-way handshake (SYN, SYN+ACK, ACK) is simulated as new connection in Pcap. </li>
 *
 *        <li> Whenever {@link ChannelInboundHandlerAdapter#handlerRemoved(ChannelHandlerContext)} is called,
 *        a fake TCP 3-way handshake (FIN+ACK, FIN+ACK, ACK) is simulated as connection shutdown in Pcap.  </li>
 *
 *        <li> Whenever {@link ChannelInboundHandlerAdapter#exceptionCaught(ChannelHandlerContext, Throwable)}
 *        is called, a fake TCP RST is sent to simulate connection Reset in Pcap. </li>
 *
 *        <li> ACK is sent each time data is send / received. </li>
 *
 *        <li> Zero Length Data Packets can cause TCP Double ACK error in Wireshark. To tackle this,
 *        set {@code captureZeroByte} to {@code false}. </li>
 *    </ul>
 * </p>
 */
// CLR: java.io.OutputStream is System.IO.Stream (owner decision: JVM-only I/O is replaced by the BCL), always written
// in full inside Netty.NET.Handler, where the simple name Stream is the sibling namespace. InetSocketAddress is
// IPEndPoint (docs/codec-dns-porting.md); getPort() is Port, getAddress() is Address and InetAddress.getAddress() is
// GetAddressBytes(). SocketChannel, ServerSocketChannel and DatagramChannel are the Transport interfaces
// ISocketChannel, IServerSocketChannel and IDatagramChannel (docs/transport-porting.md, Interfaces added; isConnected()
// is IsConnected). instanceof Inet4Address/Inet6Address is AddressFamily.InterNetwork/InterNetworkV6: Java "will never
// return an IPv4-mapped address" and converts one "into an IPv4 address"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/Inet6Address.html); the Transport channels
// report IPv4-mapped endpoints in IPv4 form (AbstractNioChannel.Normalize), while a caller-supplied IPEndPoint keeps
// the family it was created with. write(ctx, msg, promise) is WriteAsync(ctx, msg, cancellationToken), whose returned
// Task replaces the promise (docs/codec-base-porting.md). The AtomicReference<State> is a volatile State field (get/set
// are volatile reads and writes) and compareAndSet is Interlocked.CompareExchange, as Interlocked replaces Java's
// atomic updaters in Transport (docs/transport-porting.md); inside this class the enum is written Pcap.State, because
// the simple name State is the state() method here. Closeable keeps close() as Close() and IDisposable.Dispose() calls
// it, as codec-compression's IDecompressor does for AutoCloseable (docs/codec-compression-porting.md). The two
// @Deprecated constructors, whose @deprecated text names Builder, are not ported (owner decision 2026-11-16). builder()
// is Builder_(), because a method cannot share the name of the nested Builder type (owner decision 2026-11-23, CS0102).
public sealed class PcapWriteHandler : ChannelDuplexHandler, IDisposable
{

    /**
     * Logger for logging events
     */
    private readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(PcapWriteHandler));

    /**
     * {@link PcapWriter} Instance
     */
    private PcapWriter pCapWriter;

    /**
     * {@link OutputStream} where we'll write Pcap data.
     */
    private readonly System.IO.Stream outputStream;

    /**
     * {@code true} if we want to capture packets with zero bytes else {@code false}.
     */
    private readonly bool captureZeroByte;

    /**
     * {@code true} if we want to write Pcap Global Header on initialization of
     * {@link PcapWriter} else {@code false}.
     */
    private readonly bool writePcapGlobalHeader;

    /**
     * {@code true} if we want to synchronize on the {@link OutputStream} while writing
     * else {@code false}.
     */
    private readonly bool sharedOutputStream;

    /**
     * TCP Sender Segment Number.
     * It'll start with 1 and keep incrementing with number of bytes read/sent and wrap at the uint32 max.
     */
    private long sendSegmentNumber = 1;

    /**
     * TCP Receiver Segment Number.
     * It'll start with 1 and keep incrementing with number of bytes read/sent and wrap at the uint32 max
     */
    private long receiveSegmentNumber = 1;

    /**
     * Type of the channel this handler is registered on
     */
    private ChannelType? channelType;

    /**
     * Address of the initiator of the connection
     */
    private IPEndPoint initiatorAddr;

    /**
     * Address of the receiver of the connection
     */
    private IPEndPoint handlerAddr;

    /**
     * Set to {@code true} if this handler is registered on a server pipeline
     */
    private bool isServerPipeline;

    /**
     * Current of this {@link PcapWriteHandler}
     */
    private volatile State state = Pcap.State.INIT;

    private PcapWriteHandler(Builder builder, System.IO.Stream outputStream)
    {
        this.outputStream = outputStream;
        captureZeroByte = builder.captureZeroByte;
        sharedOutputStream = builder.sharedOutputStream;
        writePcapGlobalHeader = builder.writePcapGlobalHeader;
        channelType = builder.channelType;
        handlerAddr = builder.handlerAddr;
        initiatorAddr = builder.initiatorAddr;
        isServerPipeline = builder.isServerPipeline;
    }

    /**
     * Writes the Pcap Global Header to the provided {@code OutputStream}
     *
     * @param outputStream OutputStream where Pcap data will be written.
     * @throws IOException if there is an error writing to the {@code OutputStream}
     */
    public static void WriteGlobalHeader(System.IO.Stream outputStream)
    {
        PcapHeaders.WriteGlobalHeader(outputStream);
    }

    private void InitializeIfNecessary(IChannelHandlerContext ctx)
    {
        // If State is not 'INIT' then it means we're already initialized so then no need to initiaize again.
        if (state != Pcap.State.INIT)
        {
            return;
        }

        pCapWriter = new PcapWriter(this);

        if (channelType == null)
        {
            // infer channel type
            if (ctx.Channel is ISocketChannel)
            {
                channelType = ChannelType.TCP;

                // If Channel belongs to `SocketChannel` then we're handling TCP.
                // Capture correct `localAddress` and `remoteAddress`
                if (ctx.Channel.Parent is IServerSocketChannel)
                {
                    isServerPipeline = true;
                    initiatorAddr = (IPEndPoint) ctx.Channel.RemoteAddress;
                    handlerAddr = GetLocalAddress(ctx.Channel, initiatorAddr);
                }
                else
                {
                    isServerPipeline = false;
                    handlerAddr = (IPEndPoint) ctx.Channel.RemoteAddress;
                    initiatorAddr = GetLocalAddress(ctx.Channel, handlerAddr);
                }
            }
            else if (ctx.Channel is IDatagramChannel)
            {
                channelType = ChannelType.UDP;

                IDatagramChannel datagramChannel = (IDatagramChannel) ctx.Channel;

                // If `DatagramChannel` is connected then we can get
                // `localAddress` and `remoteAddress` from Channel.
                if (datagramChannel.IsConnected)
                {
                    handlerAddr = (IPEndPoint) ctx.Channel.RemoteAddress;
                    initiatorAddr = GetLocalAddress(ctx.Channel, handlerAddr);
                }
            }
        }

        if (channelType == ChannelType.TCP)
        {
            logger.Debug("Initiating Fake TCP 3-Way Handshake");

            ByteBuf tcpBuf = ctx.Alloc().Buffer();

            try
            {
                // Write SYN with Normal Source and Destination Address
                TCPPacket.WritePacket(tcpBuf, null, 0, 0,
                                      initiatorAddr.Port, handlerAddr.Port, TCPPacket.TCPFlag.SYN);
                CompleteTCPWrite(initiatorAddr, handlerAddr, tcpBuf, ctx.Alloc(), ctx);

                // Write SYN+ACK with Reversed Source and Destination Address
                TCPPacket.WritePacket(tcpBuf, null, 0, 1,
                                      handlerAddr.Port, initiatorAddr.Port, TCPPacket.TCPFlag.SYN,
                                      TCPPacket.TCPFlag.ACK);
                CompleteTCPWrite(handlerAddr, initiatorAddr, tcpBuf, ctx.Alloc(), ctx);

                // Write ACK with Normal Source and Destination Address
                TCPPacket.WritePacket(tcpBuf, null, 1, 1, initiatorAddr.Port,
                                      handlerAddr.Port, TCPPacket.TCPFlag.ACK);
                CompleteTCPWrite(initiatorAddr, handlerAddr, tcpBuf, ctx.Alloc(), ctx);
            }
            finally
            {
                tcpBuf.Release();
            }

            logger.Debug("Finished Fake TCP 3-Way Handshake");
        }

        state = Pcap.State.WRITING;
    }

    public override void ChannelActive(IChannelHandlerContext ctx)
    {
        InitializeIfNecessary(ctx);
        base.ChannelActive(ctx);
    }

    public override void ChannelRead(IChannelHandlerContext ctx, object msg)
    {
        // Initialize if needed
        if (state == Pcap.State.INIT)
        {
            try
            {
                InitializeIfNecessary(ctx);
            }
            catch (Exception)
            {
                ReferenceCountUtil.Release(msg);
                throw;
            }
        }

        // Only write if State is STARTED
        if (state == Pcap.State.WRITING)
        {
            if (channelType == ChannelType.TCP)
            {
                HandleTCP(ctx, msg, false);
            }
            else if (channelType == ChannelType.UDP)
            {
                HandleUDP(ctx, msg, false);
            }
            else
            {
                LogDiscard();
            }
        }
        base.ChannelRead(ctx, msg);
    }

    public override Task WriteAsync(IChannelHandlerContext ctx, object msg, CancellationToken cancellationToken)
    {
        // Initialize if needed
        if (state == Pcap.State.INIT)
        {
            try
            {
                InitializeIfNecessary(ctx);
            }
            catch (Exception ex)
            {
                ReferenceCountUtil.Release(msg);
                // CLR: promise.setFailure(ex) and return: the returned Task fails with ex.
                return Task.FromException(ex);
            }
        }

        // Only write if State is STARTED
        if (state == Pcap.State.WRITING)
        {
            if (channelType == ChannelType.TCP)
            {
                HandleTCP(ctx, msg, true);
            }
            else if (channelType == ChannelType.UDP)
            {
                HandleUDP(ctx, msg, true);
            }
            else
            {
                LogDiscard();
            }
        }
        return base.WriteAsync(ctx, msg, cancellationToken);
    }

    /**
     * Handle TCP L4
     *
     * @param ctx              {@link ChannelHandlerContext} for {@link ByteBuf} allocation and
     *                         {@code fireExceptionCaught}
     * @param msg              {@link Object} must be {@link ByteBuf} else it'll be discarded
     * @param isWriteOperation Set {@code true} if we have to process packet when packets are being sent out
     *                         else set {@code false}
     */
    private void HandleTCP(IChannelHandlerContext ctx, object msg, bool isWriteOperation)
    {
        if (msg is ByteBuf)
        {

            // If bytes are 0 and `captureZeroByte` is false, we won't capture this.
            int totalBytes = ((ByteBuf) msg).ReadableBytes;
            if (totalBytes == 0 && !captureZeroByte)
            {
                logger.Debug("Discarding Zero Byte TCP Packet. isWriteOperation {}", isWriteOperation);
                return;
            }

            IByteBufAllocator byteBufAllocator = ctx.Alloc();
            if (totalBytes == 0)
            {
                HandleTcpPacket(ctx, (ByteBuf) msg, isWriteOperation, byteBufAllocator);
                return;
            }

            // If the payload exceeds the max size of that can fit in a single TCP IPv4 packet, fragment the payload
            int maxTcpPayload = 65495;

            for (int i = 0; i < totalBytes; i += maxTcpPayload)
            {
                ByteBuf packet = ((ByteBuf) msg).Slice(i, Math.Min(maxTcpPayload, totalBytes - i));
                HandleTcpPacket(ctx, packet, isWriteOperation, byteBufAllocator);
            }
        }
        else
        {
            logger.Debug("Discarding Pcap Write for TCP Object: {}", msg);
        }
    }

    private void HandleTcpPacket(IChannelHandlerContext ctx, ByteBuf packet, bool isWriteOperation,
                                 IByteBufAllocator byteBufAllocator)
    {
        ByteBuf tcpBuf = byteBufAllocator.Buffer();
        int bytes = packet.ReadableBytes;

        try
        {
            if (isWriteOperation)
            {
                IPEndPoint srcAddr;
                IPEndPoint dstAddr;
                if (isServerPipeline)
                {
                    srcAddr = handlerAddr;
                    dstAddr = initiatorAddr;
                }
                else
                {
                    srcAddr = initiatorAddr;
                    dstAddr = handlerAddr;
                }

                TCPPacket.WritePacket(tcpBuf, packet, sendSegmentNumber, receiveSegmentNumber,
                                      srcAddr.Port,
                                      dstAddr.Port, TCPPacket.TCPFlag.ACK);
                CompleteTCPWrite(srcAddr, dstAddr, tcpBuf, byteBufAllocator, ctx);
                LogTCP(true, bytes, sendSegmentNumber, receiveSegmentNumber, srcAddr, dstAddr, false);

                sendSegmentNumber = IncrementUintSegmentNumber(sendSegmentNumber, bytes);

                TCPPacket.WritePacket(tcpBuf, null, receiveSegmentNumber, sendSegmentNumber, dstAddr.Port,
                                      srcAddr.Port, TCPPacket.TCPFlag.ACK);
                CompleteTCPWrite(dstAddr, srcAddr, tcpBuf, byteBufAllocator, ctx);
                LogTCP(true, bytes, sendSegmentNumber, receiveSegmentNumber, dstAddr, srcAddr, true);
            }
            else
            {
                IPEndPoint srcAddr;
                IPEndPoint dstAddr;
                if (isServerPipeline)
                {
                    srcAddr = initiatorAddr;
                    dstAddr = handlerAddr;
                }
                else
                {
                    srcAddr = handlerAddr;
                    dstAddr = initiatorAddr;
                }

                TCPPacket.WritePacket(tcpBuf, packet, receiveSegmentNumber, sendSegmentNumber,
                                      srcAddr.Port,
                                      dstAddr.Port, TCPPacket.TCPFlag.ACK);
                CompleteTCPWrite(srcAddr, dstAddr, tcpBuf, byteBufAllocator, ctx);
                LogTCP(false, bytes, receiveSegmentNumber, sendSegmentNumber, srcAddr, dstAddr, false);

                receiveSegmentNumber = IncrementUintSegmentNumber(receiveSegmentNumber, bytes);

                TCPPacket.WritePacket(tcpBuf, null, sendSegmentNumber, receiveSegmentNumber, dstAddr.Port,
                                      srcAddr.Port, TCPPacket.TCPFlag.ACK);
                CompleteTCPWrite(dstAddr, srcAddr, tcpBuf, byteBufAllocator, ctx);
                LogTCP(false, bytes, sendSegmentNumber, receiveSegmentNumber, dstAddr, srcAddr, true);
            }
        }
        finally
        {
            tcpBuf.Release();
        }
    }

    /**
     * Write TCP/IP L3 and L2 here.
     *
     * @param srcAddr          {@link InetSocketAddress} Source Address of this Packet
     * @param dstAddr          {@link InetSocketAddress} Destination Address of this Packet
     * @param tcpBuf           {@link ByteBuf} containing TCP L4 Data
     * @param byteBufAllocator {@link ByteBufAllocator} for allocating bytes for TCP/IP L3 and L2 data.
     * @param ctx              {@link ChannelHandlerContext} for {@code fireExceptionCaught}
     */
    private void CompleteTCPWrite(IPEndPoint srcAddr, IPEndPoint dstAddr, ByteBuf tcpBuf,
                                  IByteBufAllocator byteBufAllocator, IChannelHandlerContext ctx)
    {

        ByteBuf ipBuf = byteBufAllocator.Buffer();
        ByteBuf ethernetBuf = byteBufAllocator.Buffer();
        ByteBuf pcap = byteBufAllocator.Buffer();

        try
        {
            if (srcAddr.Address.AddressFamily == AddressFamily.InterNetwork &&
                dstAddr.Address.AddressFamily == AddressFamily.InterNetwork)
            {
                IPPacket.WriteTCPv4(ipBuf, tcpBuf,
                                    NetUtil.Ipv4AddressToInt(srcAddr.Address),
                                    NetUtil.Ipv4AddressToInt(dstAddr.Address));

                EthernetPacket.WriteIPv4(ethernetBuf, ipBuf);
            }
            else if (srcAddr.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                     dstAddr.Address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                IPPacket.WriteTCPv6(ipBuf, tcpBuf,
                                    srcAddr.Address.GetAddressBytes(),
                                    dstAddr.Address.GetAddressBytes());

                EthernetPacket.WriteIPv6(ethernetBuf, ipBuf);
            }
            else
            {
                logger.Error("Source and Destination IP Address versions are not same. Source Address: {}, " +
                             "Destination Address: {}", srcAddr.Address, dstAddr.Address);
                return;
            }

            // Write Packet into Pcap
            pCapWriter.WritePacket(pcap, ethernetBuf);
        }
        catch (IOException ex)
        {
            logger.Error("Caught Exception While Writing Packet into Pcap", ex);
            ctx.FireExceptionCaught(ex);
        }
        finally
        {
            ipBuf.Release();
            ethernetBuf.Release();
            pcap.Release();
        }
    }

    private static long IncrementUintSegmentNumber(long sequenceNumber, int value)
    {
        // If the sequence number would go above the max for uint32, wrap around
        return (sequenceNumber + value) % (0xFFFFFFFFL + 1);
    }

    /**
     * Handle UDP l4
     *
     * @param ctx {@link ChannelHandlerContext} for {@code localAddress} / {@code remoteAddress},
     *            {@link ByteBuf} allocation and {@code fireExceptionCaught}
     * @param msg {@link DatagramPacket} or {@link ByteBuf}
     */
    private void HandleUDP(IChannelHandlerContext ctx, object msg, bool isWriteOperation)
    {
        ByteBuf udpBuf = ctx.Alloc().Buffer();

        try
        {
            if (msg is DatagramPacket)
            {

                // If bytes are 0 and `captureZeroByte` is false, we won't capture this.
                if (((DatagramPacket) msg).Content.ReadableBytes == 0 && !captureZeroByte)
                {
                    logger.Debug("Discarding Zero Byte UDP Packet");
                    return;
                }

                if (((DatagramPacket) msg).Content.ReadableBytes > 65507)
                {
                    logger.Warn("Unable to write UDP packet to PCAP. Payload of size {} exceeds max size of 65507");
                    return;
                }

                DatagramPacket datagramPacket = ((DatagramPacket) msg).Duplicate();
                IPEndPoint srcAddr = datagramPacket.Sender;
                IPEndPoint dstAddr = datagramPacket.Recipient;

                // If `datagramPacket.sender()` is `null` then DatagramPacket is initialized
                // `sender` (local) address. In this case, we'll get source address from Channel.
                if (srcAddr == null)
                {
                    srcAddr = GetLocalAddress(ctx.Channel, dstAddr);
                }

                logger.Debug("Writing UDP Data of {} Bytes, isWriteOperation {}, Src Addr {}, Dst Addr {}",
                             datagramPacket.Content.ReadableBytes, isWriteOperation, srcAddr, dstAddr);

                UDPPacket.WritePacket(udpBuf, datagramPacket.Content, srcAddr.Port, dstAddr.Port);
                CompleteUDPWrite(srcAddr, dstAddr, udpBuf, ctx.Alloc(), ctx);
            }
            else if (msg is ByteBuf &&
                     (!(ctx.Channel is IDatagramChannel) ||
                      ((IDatagramChannel) ctx.Channel).IsConnected))
            {

                // If bytes are 0 and `captureZeroByte` is false, we won't capture this.
                if (((ByteBuf) msg).ReadableBytes == 0 && !captureZeroByte)
                {
                    logger.Debug("Discarding Zero Byte UDP Packet");
                    return;
                }

                if (((ByteBuf) msg).ReadableBytes > 65507)
                {
                    logger.Warn("Unable to write UDP packet to PCAP. Payload of size {} exceeds max size of 65507");
                    return;
                }

                ByteBuf byteBuf = ((ByteBuf) msg).Duplicate();

                IPEndPoint sourceAddr = isWriteOperation? initiatorAddr : handlerAddr;
                IPEndPoint destinationAddr = isWriteOperation? handlerAddr : initiatorAddr;

                logger.Debug("Writing UDP Data of {} Bytes, Src Addr {}, Dst Addr {}",
                             byteBuf.ReadableBytes, sourceAddr, destinationAddr);

                UDPPacket.WritePacket(udpBuf, byteBuf, sourceAddr.Port, destinationAddr.Port);
                CompleteUDPWrite(sourceAddr, destinationAddr, udpBuf, ctx.Alloc(), ctx);
            }
            else
            {
                logger.Debug("Discarding Pcap Write for UDP Object: {}", msg);
            }
        }
        finally
        {
            udpBuf.Release();
        }
    }

    /**
     * Write UDP/IP L3 and L2 here.
     *
     * @param srcAddr          {@link InetSocketAddress} Source Address of this Packet
     * @param dstAddr          {@link InetSocketAddress} Destination Address of this Packet
     * @param udpBuf           {@link ByteBuf} containing UDP L4 Data
     * @param byteBufAllocator {@link ByteBufAllocator} for allocating bytes for UDP/IP L3 and L2 data.
     * @param ctx              {@link ChannelHandlerContext} for {@code fireExceptionCaught}
     */
    private void CompleteUDPWrite(IPEndPoint srcAddr, IPEndPoint dstAddr, ByteBuf udpBuf,
                                  IByteBufAllocator byteBufAllocator, IChannelHandlerContext ctx)
    {

        ByteBuf ipBuf = byteBufAllocator.Buffer();
        ByteBuf ethernetBuf = byteBufAllocator.Buffer();
        ByteBuf pcap = byteBufAllocator.Buffer();

        try
        {
            if (srcAddr.Address.AddressFamily == AddressFamily.InterNetwork &&
                dstAddr.Address.AddressFamily == AddressFamily.InterNetwork)
            {
                IPPacket.WriteUDPv4(ipBuf, udpBuf,
                                    NetUtil.Ipv4AddressToInt(srcAddr.Address),
                                    NetUtil.Ipv4AddressToInt(dstAddr.Address));

                EthernetPacket.WriteIPv4(ethernetBuf, ipBuf);
            }
            else if (srcAddr.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                     dstAddr.Address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                IPPacket.WriteUDPv6(ipBuf, udpBuf,
                                    srcAddr.Address.GetAddressBytes(),
                                    dstAddr.Address.GetAddressBytes());

                EthernetPacket.WriteIPv6(ethernetBuf, ipBuf);
            }
            else
            {
                logger.Error("Source and Destination IP Address versions are not same. Source Address: {}, " +
                             "Destination Address: {}", srcAddr.Address, dstAddr.Address);
                return;
            }

            // Write Packet into Pcap
            pCapWriter.WritePacket(pcap, ethernetBuf);
        }
        catch (IOException ex)
        {
            logger.Error("Caught Exception While Writing Packet into Pcap", ex);
            ctx.FireExceptionCaught(ex);
        }
        finally
        {
            ipBuf.Release();
            ethernetBuf.Release();
            pcap.Release();
        }
    }

    /**
     * Get the local address of a channel. If the address is a wildcard address ({@code 0.0.0.0} or {@code ::}), and
     * the address family does not match that of the {@code remote}, return the wildcard address of the {@code remote}'s
     * family instead.
     *
     * @param ch     The channel to get the local address from
     * @param remote The remote address
     * @return The fixed local address
     */
    // CLR: InetAddress.isAnyLocalAddress() is equality with IPAddress.Any or IPAddress.IPv6Any, as in the Transport
    // AbstractChannel.IsAnyLocalAddress (internal to Transport).
    private static IPEndPoint GetLocalAddress(IChannel ch, IPEndPoint remote)
    {
        IPEndPoint local = (IPEndPoint) ch.LocalAddress;
        if (remote != null && (local.Address.Equals(IPAddress.Any) || local.Address.Equals(IPAddress.IPv6Any)))
        {
            if (local.Address.AddressFamily == AddressFamily.InterNetwork &&
                remote.Address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return new IPEndPoint(WildcardAddressHolder.wildcard6, local.Port);
            }
            if (local.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                remote.Address.AddressFamily == AddressFamily.InterNetwork)
            {
                return new IPEndPoint(WildcardAddressHolder.wildcard4, local.Port);
            }
        }
        return local;
    }

    public override void HandlerRemoved(IChannelHandlerContext ctx)
    {

        // If `isTCP` is true and state is WRITING, then we'll simulate a `FIN` flow.
        if (channelType == ChannelType.TCP && state == Pcap.State.WRITING)
        {
            logger.Debug("Starting Fake TCP FIN+ACK Flow to close connection");

            IByteBufAllocator byteBufAllocator = ctx.Alloc();
            ByteBuf tcpBuf = byteBufAllocator.Buffer();

            try
            {
                long initiatorSegmentNumber = isServerPipeline? receiveSegmentNumber : sendSegmentNumber;
                long initiatorAckNumber = isServerPipeline? sendSegmentNumber : receiveSegmentNumber;
                // Write FIN+ACK with Normal Source and Destination Address
                TCPPacket.WritePacket(tcpBuf, null, initiatorSegmentNumber, initiatorAckNumber, initiatorAddr.Port,
                                      handlerAddr.Port, TCPPacket.TCPFlag.FIN, TCPPacket.TCPFlag.ACK);
                CompleteTCPWrite(initiatorAddr, handlerAddr, tcpBuf, byteBufAllocator, ctx);

                // Write FIN+ACK with Reversed Source and Destination Address
                TCPPacket.WritePacket(tcpBuf, null, initiatorAckNumber, initiatorSegmentNumber, handlerAddr.Port,
                                      initiatorAddr.Port, TCPPacket.TCPFlag.FIN, TCPPacket.TCPFlag.ACK);
                CompleteTCPWrite(handlerAddr, initiatorAddr, tcpBuf, byteBufAllocator, ctx);

                // Increment by 1 when responding to FIN
                sendSegmentNumber = IncrementUintSegmentNumber(sendSegmentNumber, 1);
                receiveSegmentNumber = IncrementUintSegmentNumber(receiveSegmentNumber, 1);
                initiatorSegmentNumber = isServerPipeline? receiveSegmentNumber : sendSegmentNumber;
                initiatorAckNumber = isServerPipeline? sendSegmentNumber : receiveSegmentNumber;

                // Write ACK with Normal Source and Destination Address
                TCPPacket.WritePacket(tcpBuf, null, initiatorSegmentNumber, initiatorAckNumber,
                                      initiatorAddr.Port, handlerAddr.Port, TCPPacket.TCPFlag.ACK);
                CompleteTCPWrite(initiatorAddr, handlerAddr, tcpBuf, byteBufAllocator, ctx);
            }
            finally
            {
                tcpBuf.Release();
            }

            logger.Debug("Finished Fake TCP FIN+ACK Flow to close connection");
        }

        Close();
        base.HandlerRemoved(ctx);
    }

    public override void ExceptionCaught(IChannelHandlerContext ctx, Exception cause)
    {
        // Only write RST if this is an initialized TCP stream
        if (channelType == ChannelType.TCP && state == Pcap.State.WRITING)
        {
            ByteBuf tcpBuf = ctx.Alloc().Buffer();

            try
            {
                // Write RST with Normal Source and Destination Address
                TCPPacket.WritePacket(tcpBuf, null, sendSegmentNumber, receiveSegmentNumber, initiatorAddr.Port,
                                      handlerAddr.Port, TCPPacket.TCPFlag.RST, TCPPacket.TCPFlag.ACK);
                CompleteTCPWrite(initiatorAddr, handlerAddr, tcpBuf, ctx.Alloc(), ctx);
            }
            finally
            {
                tcpBuf.Release();
            }

            logger.Debug("Sent Fake TCP RST to close connection");
        }

        Close();
        ctx.FireExceptionCaught(cause);
    }

    /**
     * Logger for TCP
     */
    private void LogTCP(bool isWriteOperation, int bytes, long sendSegmentNumber, long receiveSegmentNumber,
                        IPEndPoint srcAddr, IPEndPoint dstAddr, bool ackOnly)
    {
        // If `ackOnly` is `true` when we don't need to write any data so we'll not
        // log number of bytes being written and mark the operation as "TCP ACK".
        if (logger.IsDebugEnabled())
        {
            if (ackOnly)
            {
                logger.Debug("Writing TCP ACK, isWriteOperation {}, Segment Number {}, Ack Number {}, Src Addr {}, "
                             + "Dst Addr {}", isWriteOperation, sendSegmentNumber, receiveSegmentNumber, dstAddr,
                             srcAddr);
            }
            else
            {
                logger.Debug("Writing TCP Data of {} Bytes, isWriteOperation {}, Segment Number {}, Ack Number {}, " +
                             "Src Addr {}, Dst Addr {}", bytes, isWriteOperation, sendSegmentNumber,
                             receiveSegmentNumber, srcAddr, dstAddr);
            }
        }
    }

    internal System.IO.Stream OutputStream()
    {
        return outputStream;
    }

    internal bool SharedOutputStream()
    {
        return sharedOutputStream;
    }

    internal bool WritePcapGlobalHeader()
    {
        return writePcapGlobalHeader;
    }

    /**
     * Returns {@code true} if the {@link PcapWriteHandler} is currently
     * writing packets to the {@link OutputStream} else returns {@code false}.
     */
    public bool IsWriting()
    {
        return state == Pcap.State.WRITING;
    }

    internal State State()
    {
        return state;
    }

    /**
     * Pause the {@link PcapWriteHandler} from writing packets to the {@link OutputStream}.
     */
    public void Pause()
    {
        if (Interlocked.CompareExchange(ref state, Pcap.State.PAUSED, Pcap.State.WRITING) != Pcap.State.WRITING)
        {
            throw new InvalidOperationException("State must be 'STARTED' to pause but current state is: " +
                                                state);
        }
    }

    /**
     * Resume the {@link PcapWriteHandler} to writing packets to the {@link OutputStream}.
     */
    public void Resume()
    {
        if (Interlocked.CompareExchange(ref state, Pcap.State.WRITING, Pcap.State.PAUSED) != Pcap.State.PAUSED)
        {
            throw new InvalidOperationException("State must be 'PAUSED' to resume but current state is: " +
                                                state);
        }
    }

    internal void MarkClosed()
    {
        if (state != Pcap.State.CLOSED)
        {
            state = Pcap.State.CLOSED;
        }
    }

    // Visible for testing only.
    internal PcapWriter PCapWriter()
    {
        return pCapWriter;
    }

    private void LogDiscard()
    {
        logger.Warn("Discarding pcap write because channel type is unknown. The channel this handler is registered " +
                    "on is not a SocketChannel or DatagramChannel, so the inference does not work. Please call " +
                    "forceTcpChannel or forceUdpChannel before registering the handler.");
    }

    // CLR: Java prints booleans as true/false and numbers without a culture, so the booleans are the Java words and the
    // numbers use the invariant culture (docs/codec-redis-porting.md); a null field prints "null" as Java string
    // concatenation does. The addresses are IPEndPoint.ToString() (host:port), not InetSocketAddress's
    // host/literal:port.
    public override string ToString()
    {
        return "PcapWriteHandler{" +
               "captureZeroByte=" + (captureZeroByte ? "true" : "false") +
               ", writePcapGlobalHeader=" + (writePcapGlobalHeader ? "true" : "false") +
               ", sharedOutputStream=" + (sharedOutputStream ? "true" : "false") +
               ", sendSegmentNumber=" + sendSegmentNumber.ToString(CultureInfo.InvariantCulture) +
               ", receiveSegmentNumber=" + receiveSegmentNumber.ToString(CultureInfo.InvariantCulture) +
               ", channelType=" + (channelType?.ToString() ?? "null") +
               ", initiatorAddr=" + (initiatorAddr?.ToString() ?? "null") +
               ", handlerAddr=" + (handlerAddr?.ToString() ?? "null") +
               ", isServerPipeline=" + (isServerPipeline ? "true" : "false") +
               ", state=" + state +
               '}';
    }

    /**
     * <p> Close {@code PcapWriter} and {@link OutputStream}. </p>
     * <p> Note: Calling this method does not close {@link PcapWriteHandler}.
     * Only Pcap Writes are closed. </p>
     *
     * @throws IOException If {@link OutputStream#close()} throws an exception
     */
    public void Close()
    {
        if (state == Pcap.State.CLOSED)
        {
            logger.Debug("PcapWriterHandler is already closed");
        }
        else
        {
            // If close is called prematurely, create writer to close output stream
            if (pCapWriter == null)
            {
                pCapWriter = new PcapWriter(this);
            }
            pCapWriter.Close();
            MarkClosed();
            logger.Debug("PcapWriterHandler is now closed");
        }
    }

    void IDisposable.Dispose()
    {
        Close();
    }

    // CLR: internal instead of private: the Builder's channelType field, which this class reads, has this type, and a
    // field cannot be more accessible than its type (CS0052).
    internal enum ChannelType
    {
        TCP, UDP
    }

    public static Builder Builder_()
    {
        return new Builder();
    }

    /**
     * Builder for {@link PcapWriteHandler}
     */
    // CLR: the fields and the constructor are internal because C# does not open a nested class's private members to the
    // enclosing class, which reads the fields and constructs the builder.
    public sealed class Builder
    {
        internal bool captureZeroByte;
        internal bool sharedOutputStream;
        internal bool writePcapGlobalHeader = true;

        internal ChannelType? channelType;
        internal IPEndPoint initiatorAddr;
        internal IPEndPoint handlerAddr;
        internal bool isServerPipeline;

        internal Builder()
        {
        }

        /**
         * Set to {@code true} to enable capturing packets with empty (0 bytes) payload. Otherwise, if set to
         * {@code false}, empty packets will be filtered out.
         *
         * @param captureZeroByte Whether to filter out empty packets.
         * @return this builder
         */
        public Builder CaptureZeroByte(bool captureZeroByte)
        {
            this.captureZeroByte = captureZeroByte;
            return this;
        }

        /**
         * Set to {@code true} if multiple {@link PcapWriteHandler} instances will be
         * writing to the same {@link OutputStream} concurrently, and write locking is
         * required. Otherwise, if set to {@code false}, no locking will be done.
         * Additionally, {@link #close} will not close the underlying {@code OutputStream}.
         * Note: it is probably an error to have both {@code writePcapGlobalHeader} and
         * {@code sharedOutputStream} set to {@code true} at the same time.
         *
         * @param sharedOutputStream Whether {@link OutputStream} is shared or not
         * @return this builder
         */
        public Builder SharedOutputStream(bool sharedOutputStream)
        {
            this.sharedOutputStream = sharedOutputStream;
            return this;
        }

        /**
         * Set to {@code true} to write Pcap Global Header on initialization. Otherwise, if set to {@code false}, Pcap
         * Global Header will not be written on initialization. This could when writing Pcap data on a existing file
         * where Pcap Global Header is already present.
         *
         * @param writePcapGlobalHeader Whether to write the pcap global header.
         * @return this builder
         */
        public Builder WritePcapGlobalHeader(bool writePcapGlobalHeader)
        {
            this.writePcapGlobalHeader = writePcapGlobalHeader;
            return this;
        }

        /**
         * Force this handler to write data as if they were TCP packets, with the given connection metadata. If this
         * method isn't called, we determine the metadata from the channel.
         *
         * @param serverAddress    The address of the TCP server (handler)
         * @param clientAddress    The address of the TCP client (initiator)
         * @param isServerPipeline Whether the handler is part of the server channel
         * @return this builder
         */
        public Builder ForceTcpChannel(IPEndPoint serverAddress, IPEndPoint clientAddress,
                                       bool isServerPipeline)
        {
            channelType = ChannelType.TCP;
            handlerAddr = CheckNotNull(serverAddress, "serverAddress");
            initiatorAddr = CheckNotNull(clientAddress, "clientAddress");
            this.isServerPipeline = isServerPipeline;
            return this;
        }

        /**
         * Force this handler to write data as if they were UDP packets, with the given connection metadata. If this
         * method isn't called, we determine the metadata from the channel.
         * <br>
         * Note that even if this method is called, the address information on {@link DatagramPacket} takes precedence
         * if it is present.
         *
         * @param localAddress  The address of the UDP local
         * @param remoteAddress The address of the UDP remote
         * @return this builder
         */
        public Builder ForceUdpChannel(IPEndPoint localAddress, IPEndPoint remoteAddress)
        {
            channelType = ChannelType.UDP;
            handlerAddr = CheckNotNull(remoteAddress, "remoteAddress");
            initiatorAddr = CheckNotNull(localAddress, "localAddress");
            return this;
        }

        /**
         * Build the {@link PcapWriteHandler}.
         *
         * @param outputStream The output stream to write the pcap data to.
         * @return The handler.
         */
        public PcapWriteHandler Build(System.IO.Stream outputStream)
        {
            CheckNotNull(outputStream, "outputStream");
            return new PcapWriteHandler(this, outputStream);
        }
    }

    // CLR: InetAddress.getByAddress(byte[]) is new IPAddress(byte[]), which takes the 4 bytes of an IPv4 or the 16
    // bytes of an IPv6 address and throws ArgumentException for another length (UnknownHostException in Java). The
    // static initializer is the static constructor; AssertionError is UnreachableException, "The exception that is
    // thrown when the program executes an instruction that was thought to be unreachable"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.unreachableexception?view=net-10.0).
    private static class WildcardAddressHolder
    {
        internal static readonly IPAddress wildcard4; // 0.0.0.0
        internal static readonly IPAddress wildcard6; // ::

        static WildcardAddressHolder()
        {
            try
            {
                wildcard4 = new IPAddress(new byte[4]);
                wildcard6 = new IPAddress(new byte[16]);
            }
            catch (ArgumentException e)
            {
                // would only happen if the byte array was of incorrect size
                throw new UnreachableException(null, e);
            }
        }
    }
}
