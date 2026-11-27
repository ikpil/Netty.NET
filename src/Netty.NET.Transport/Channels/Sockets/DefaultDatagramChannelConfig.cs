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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Common.Internal;

namespace Netty.NET.Transport.Channels.Sockets;

/// <summary>UDP socket options mapped to the CLR socket owned by a datagram channel.</summary>
/**
 * The default {@link DatagramChannelConfig} implementation.
 */
/**
 * The default {@link NioDatagramChannelConfig} implementation.
 */
// CLR: DefaultDatagramChannelConfig and its NIO subclass NioDatagramChannelConfig are one class over
// the CLR UDP Socket. Multicast TTL, interface and loopback follow the NIO channel-option forms
// (IP_MULTICAST_TTL, IP_MULTICAST_IF) rather than java.net.MulticastSocket. The disabled-loopback
// contract of DatagramChannelConfig is kept with !Socket.MulticastLoopback. Linux dual-mode sockets
// mirror multicast TTL, loopback and traffic class to the IPv4 level. Typed accessors wrap
// SocketException in ChannelException where the source wraps java.net.SocketException/IOException.
#pragma warning disable CS0612, CS0618 // The source config supports the deprecated active-on-registration option.
public class DefaultDatagramChannelConfig : DefaultChannelConfig, IDatagramChannelConfig
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(DefaultDatagramChannelConfig));
    private readonly Socket _javaSocket;
    private readonly bool _linuxDualMode;
    private volatile bool _activeOnOpen;

    /**
     * Creates a new instance.
     */
    public DefaultDatagramChannelConfig(NioDatagramChannel channel, Socket javaSocket)
        : base(channel, new FixedRecvByteBufAllocator(2048))
    {
        _javaSocket = javaSocket ?? throw new ArgumentNullException(nameof(javaSocket));
        _linuxDualMode = channel.LinuxDualMode && Socket.OSSupportsIPv4;
    }

    protected Socket JavaSocket() => _javaSocket;

    public override IReadOnlyDictionary<ChannelOption, object> GetOptions()
    {
        var options = new Dictionary<ChannelOption, object>(base.GetOptions());
        foreach (var option in new ChannelOption[] { ChannelOption.SO_BROADCAST, ChannelOption.SO_RCVBUF,
            ChannelOption.SO_SNDBUF, ChannelOption.SO_REUSEADDR, ChannelOption.IP_MULTICAST_LOOP_DISABLED,
            ChannelOption.IP_MULTICAST_ADDR, ChannelOption.IP_MULTICAST_IF, ChannelOption.IP_MULTICAST_TTL,
            ChannelOption.IP_TOS, ChannelOption.DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION, ChannelOption.UDP_GRO,
            ChannelOption.UDP_MAX_DATAGRAM_PAYLOAD_SIZE, ChannelOption.IP_RECVORIGDSTADDR, ChannelOption.IP_FREEBIND,
            ChannelOption.SO_REUSEPORT })
            if (TryGetValue(option, out var value)) options[option] = value;
        return options;
    }

    protected override bool TryGetValue(ChannelOption option, out object value)
    {
        if (ReferenceEquals(option, ChannelOption.SO_BROADCAST))
            value = IsBroadcast();
        else if (ReferenceEquals(option, ChannelOption.SO_RCVBUF))
            value = GetReceiveBufferSize();
        else if (ReferenceEquals(option, ChannelOption.SO_SNDBUF))
            value = GetSendBufferSize();
        else if (ReferenceEquals(option, ChannelOption.SO_REUSEADDR))
            value = IsReuseAddress();
        else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_LOOP_DISABLED))
            value = IsLoopbackModeDisabled();
        else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_ADDR))
            value = GetInterface();
        else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_IF))
            value = GetNetworkInterface();
        else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_TTL))
            value = GetTimeToLive();
        else if (ReferenceEquals(option, ChannelOption.IP_TOS))
            value = GetTrafficClass();
        else if (ReferenceEquals(option, ChannelOption.DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION))
            value = _activeOnOpen;
        else if (ReferenceEquals(option, ChannelOption.UDP_GRO))
            value = false;
        else if (ReferenceEquals(option, ChannelOption.UDP_MAX_DATAGRAM_PAYLOAD_SIZE))
            value = 0;
        /**
         * Returns {@code true} if <a href="https://man7.org/linux/man-pages/man7/ip.7.html">IP_RECVORIGDSTADDR</a> is
         * enabled, {@code false} otherwise.
         */
        else if (ReferenceEquals(option, ChannelOption.IP_RECVORIGDSTADDR))
            value = false;
        /**
         * Returns {@code true} if <a href="https://man7.org/linux/man-pages/man7/ip.7.html">IP_FREEBIND</a> is enabled,
         * {@code false} otherwise.
         */
        else if (ReferenceEquals(option, ChannelOption.IP_FREEBIND) && OperatingSystem.IsLinux())
            value = LinuxSocketOptions.GetFlag(_javaSocket, (int)SocketOptionLevel.IP, LinuxSocketOptions.IpFreeBind);
        else if (ReferenceEquals(option, ChannelOption.SO_REUSEPORT) && OperatingSystem.IsLinux())
            value = LinuxSocketOptions.GetFlag(_javaSocket, LinuxSocketOptions.SolSocket, LinuxSocketOptions.SoReusePort);
        else
            return base.TryGetValue(option, out value);
        return true;
    }

    protected override bool SetValue(ChannelOption option, object value)
    {
        if (ReferenceEquals(option, ChannelOption.SO_BROADCAST))
            SetBroadcast((bool)value);
        else if (ReferenceEquals(option, ChannelOption.SO_RCVBUF))
            SetReceiveBufferSize((int)value);
        else if (ReferenceEquals(option, ChannelOption.SO_SNDBUF))
            SetSendBufferSize((int)value);
        else if (ReferenceEquals(option, ChannelOption.SO_REUSEADDR))
            SetReuseAddress((bool)value);
        else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_LOOP_DISABLED))
            SetLoopbackModeDisabled((bool)value);
        else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_ADDR))
            SetInterface((IPAddress)value);
        else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_IF))
            SetNetworkInterface((NetworkInterface)value);
        else if (ReferenceEquals(option, ChannelOption.IP_MULTICAST_TTL))
            SetTimeToLive((int)value);
        else if (ReferenceEquals(option, ChannelOption.IP_TOS))
            SetTrafficClass((int)value);
        else if (ReferenceEquals(option, ChannelOption.DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION))
            SetActiveOnOpen((bool)value);
        else if (ReferenceEquals(option, ChannelOption.UDP_GRO))
            ((NioDatagramChannel)Channel).SetUdpGro((bool)value);
        else if (ReferenceEquals(option, ChannelOption.UDP_MAX_DATAGRAM_PAYLOAD_SIZE))
            ((NioDatagramChannel)Channel).SetUdpSlotSize((int)value);
        else if (ReferenceEquals(option, ChannelOption.IP_RECVORIGDSTADDR))
            ((NioDatagramChannel)Channel).SetReceiveOriginalDestination((bool)value);
        /**
         * If {@code true} is used <a href="https://man7.org/linux/man-pages/man7/ip.7.html">IP_FREEBIND</a> is enabled,
         * {@code false} for disable it. Default is disabled.
         */
        else if (ReferenceEquals(option, ChannelOption.IP_FREEBIND) && OperatingSystem.IsLinux())
            LinuxSocketOptions.SetFlag(_javaSocket, (int)SocketOptionLevel.IP, LinuxSocketOptions.IpFreeBind, (bool)value);
        else if (ReferenceEquals(option, ChannelOption.SO_REUSEPORT) && OperatingSystem.IsLinux())
            LinuxSocketOptions.SetFlag(_javaSocket, LinuxSocketOptions.SolSocket, LinuxSocketOptions.SoReusePort, (bool)value);
        else
            return base.SetValue(option, value);
        return true;
    }

    private void SetActiveOnOpen(bool activeOnOpen)
    {
        if (Channel.IsRegistered)
            throw new InvalidOperationException("Can only changed before channel was registered");
        _activeOnOpen = activeOnOpen;
    }

    // CLR: Socket.EnableBroadcast reads and writes SO_BROADCAST.
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.enablebroadcast
    public bool IsBroadcast()
    {
        try
        {
            return _javaSocket.EnableBroadcast;
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
    }

    public virtual IDatagramChannelConfig SetBroadcast(bool broadcast)
    {
        try
        {
            // See: https://github.com/netty/netty/issues/576
            if (broadcast &&
                !IsAnyLocalAddress(_javaSocket.LocalEndPoint) &&
                !PlatformDependent.IsWindows() && !MaybeSuperUser())
            {
                // Warn a user about the fact that a non-root user can't receive a
                // broadcast packet on *nix if the socket is bound on non-wildcard address.
                logger.Warn(
                        "A non-root user can't receive a broadcast packet if the socket " +
                        "is not bound to a wildcard address; setting the SO_BROADCAST flag " +
                        "anyway as requested on the socket which is bound to " +
                        _javaSocket.LocalEndPoint + '.');
            }

            _javaSocket.EnableBroadcast = broadcast;
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
        return this;
    }

    // CLR: DatagramSocket.getLocalAddress() is the wildcard address while unbound.
    private static bool IsAnyLocalAddress(EndPoint local)
    {
        return local is not IPEndPoint ip || AbstractChannel.IsAnyLocalAddress(ip.Address);
    }

    // CLR: PlatformDependent.maybeSuperUser(); see AbstractChannel.MaybeSuperUser.
    private static bool MaybeSuperUser() => AbstractChannel.MaybeSuperUser();

    // CLR: NioDatagramChannelConfig returns an address of the IP_MULTICAST_IF interface; the CLR
    // socket uses the interface's first unicast address of the socket family.
    public IPAddress GetInterface()
    {
        NetworkInterface inf = GetNetworkInterface();
        return inf == null ? null : NioDatagramChannel.InterfaceAddress(inf, _javaSocket.AddressFamily);
    }

    // CLR: NetworkInterface.getByInetAddress is the local interface owning the unicast address.
    public virtual IDatagramChannelConfig SetInterface(IPAddress interfaceAddress)
    {
        SetNetworkInterface(NioDatagramChannel.FindInterface(interfaceAddress)
            ?? throw new ArgumentException("Address does not belong to a local network interface.", nameof(interfaceAddress)));
        return this;
    }

    // CLR: !Socket.MulticastLoopback keeps the DatagramChannelConfig disabled-loopback contract.
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.multicastloopback
    public bool IsLoopbackModeDisabled()
    {
        try
        {
            return !_javaSocket.MulticastLoopback;
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
    }

    public virtual IDatagramChannelConfig SetLoopbackModeDisabled(bool loopbackModeDisabled)
    {
        try
        {
            bool enabled = !loopbackModeDisabled;
            _javaSocket.MulticastLoopback = enabled;
            SetMappedIPv4Option(SocketOptionName.MulticastLoopback, enabled ? 1 : 0);
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
        return this;
    }

    public NetworkInterface GetNetworkInterface()
    {
        try
        {
            return GetNetworkInterface0();
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
    }

    // CLR: IPv4 IP_MULTICAST_IF holds an interface address and IPv6 IPV6_MULTICAST_IF an interface
    // index; both map back to a NetworkInterface. The unset value (any address or index 0) is null.
    internal NetworkInterface GetNetworkInterface0()
    {
        if (_javaSocket.AddressFamily == AddressFamily.InterNetwork)
        {
            var address = new IPAddress(_javaSocket.GetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, 4));
            return address.Equals(IPAddress.Any) ? null : NioDatagramChannel.FindInterface(address);
        }
        int index = (int)_javaSocket.GetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface);
        return index == 0 ? null : NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.Supports(NetworkInterfaceComponent.IPv6) && n.GetIPProperties().GetIPv6Properties()?.Index == index);
    }

    public virtual IDatagramChannelConfig SetNetworkInterface(NetworkInterface networkInterface)
    {
        ArgumentNullException.ThrowIfNull(networkInterface);
        try
        {
            if (_javaSocket.AddressFamily == AddressFamily.InterNetwork)
                _javaSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface,
                    NioDatagramChannel.InterfaceAddress(networkInterface, AddressFamily.InterNetwork).GetAddressBytes());
            else
            {
                _javaSocket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface,
                    NioDatagramChannel.InterfaceIndex(networkInterface, AddressFamily.InterNetworkV6));
                if (_linuxDualMode && networkInterface.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(info => info.Address.AddressFamily == AddressFamily.InterNetwork) is { } ipv4)
                    _javaSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, ipv4.Address.GetAddressBytes());
            }
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
        return this;
    }

    // CLR: SocketOptionName.ReuseAddress at the socket level is SO_REUSEADDR.
    public bool IsReuseAddress()
    {
        try
        {
            return (int)_javaSocket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress) != 0;
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
    }

    public virtual IDatagramChannelConfig SetReuseAddress(bool reuseAddress)
    {
        try
        {
            _javaSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, reuseAddress);
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
        return this;
    }

    // CLR: Socket.ReceiveBufferSize reads and writes SO_RCVBUF.
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.receivebuffersize
    public int GetReceiveBufferSize()
    {
        try
        {
            return _javaSocket.ReceiveBufferSize;
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
    }

    public virtual IDatagramChannelConfig SetReceiveBufferSize(int receiveBufferSize)
    {
        try
        {
            // CLR: DatagramSocket.setReceiveBufferSize rejects size <= 0 with IllegalArgumentException; the Socket
            // property accepts 0 (https://learn.microsoft.com/dotnet/api/system.net.sockets.socket.receivebuffersize).
            if (receiveBufferSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(receiveBufferSize), receiveBufferSize, "Invalid receive size");
            }
            _javaSocket.ReceiveBufferSize = receiveBufferSize;
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
        return this;
    }

    // CLR: Socket.SendBufferSize reads and writes SO_SNDBUF.
    public int GetSendBufferSize()
    {
        try
        {
            return _javaSocket.SendBufferSize;
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
    }

    public virtual IDatagramChannelConfig SetSendBufferSize(int sendBufferSize)
    {
        try
        {
            // CLR: DatagramSocket.setSendBufferSize rejects size <= 0 with IllegalArgumentException.
            if (sendBufferSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sendBufferSize), sendBufferSize, "Invalid send size");
            }
            _javaSocket.SendBufferSize = sendBufferSize;
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
        return this;
    }

    // CLR: SocketOptionName.MulticastTimeToLive is the multicast TTL (IP_MULTICAST_TTL or
    // IPV6_MULTICAST_HOPS at the socket family level), not the unicast Socket.Ttl.
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketoptionname
    public int GetTimeToLive()
    {
        try
        {
            return (int)_javaSocket.GetSocketOption(MulticastOptionLevel, SocketOptionName.MulticastTimeToLive);
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
    }

    // CLR: the NIO IP_MULTICAST_TTL option rejects values outside 0..255 before the native call.
    public virtual IDatagramChannelConfig SetTimeToLive(int ttl)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ttl);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ttl, 255);
        try
        {
            _javaSocket.SetSocketOption(MulticastOptionLevel, SocketOptionName.MulticastTimeToLive, ttl);
            SetMappedIPv4Option(SocketOptionName.MulticastTimeToLive, ttl);
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
        return this;
    }

    public int GetTrafficClass()
    {
        try
        {
            if (OperatingSystem.IsLinux() && _javaSocket.AddressFamily == AddressFamily.InterNetworkV6)
            {
                Span<byte> value = stackalloc byte[sizeof(int)];
                int written = _javaSocket.GetRawSocketOption((int)SocketOptionLevel.IPv6, LinuxIPv6TrafficClass, value);
                return BitConverter.ToInt32(value[..written]);
            }
            return (int)_javaSocket.GetSocketOption(MulticastOptionLevel, SocketOptionName.TypeOfService);
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
    }

    // CLR: the JDK socket rejects traffic classes outside 0..255 before the native call.
    public virtual IDatagramChannelConfig SetTrafficClass(int trafficClass)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(trafficClass);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(trafficClass, 255);
        try
        {
            if (OperatingSystem.IsLinux() && _javaSocket.AddressFamily == AddressFamily.InterNetworkV6)
            {
                Span<byte> value = stackalloc byte[sizeof(int)];
                BitConverter.TryWriteBytes(value, trafficClass);
                _javaSocket.SetRawSocketOption((int)SocketOptionLevel.IPv6, LinuxIPv6TrafficClass, value);
                SetMappedIPv4Option(SocketOptionName.TypeOfService, trafficClass);
            }
            else
                _javaSocket.SetSocketOption(MulticastOptionLevel, SocketOptionName.TypeOfService, trafficClass);
        }
        catch (SocketException e)
        {
            throw new ChannelException(e);
        }
        return this;
    }

    protected override void AutoReadCleared() => ((AbstractChannel)Channel).ClearReadPending();

    // Linux's IPv6 traffic class is IPV6_TCLASS, which has no SocketOptionName mapping.
    // https://github.com/torvalds/linux/blob/master/include/uapi/linux/in6.h
    private const int LinuxIPv6TrafficClass = 67;

    private void SetMappedIPv4Option(SocketOptionName option, int value)
    {
        if (_linuxDualMode) _javaSocket.SetSocketOption(SocketOptionLevel.IP, option, value);
    }

    private SocketOptionLevel MulticastOptionLevel => _javaSocket.AddressFamily == AddressFamily.InterNetworkV6
        ? SocketOptionLevel.IPv6 : SocketOptionLevel.IP;
}
#pragma warning restore CS0612, CS0618
