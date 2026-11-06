using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common.Concurrent;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1051 // Independent channel/socket cleanup.
public class DatagramSourceAndReadErrorContractTest
{
    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IPv6SourceMembershipUsesTheInterfaceAcrossScopedAddressForms(bool sourceSpecific)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            Assert.Skip("The native IPv6 source option layout is defined for Windows and Linux.");
        if (!Socket.OSSupportsIPv6) Assert.Skip("IPv6 is not supported.");
        var cancellation = TestContext.Current.CancellationToken;
        var iface = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n =>
            n.OperationalStatus == OperationalStatus.Up && n.SupportsMulticast &&
            n.GetIPProperties().UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6));
        if (iface == null) Assert.Skip("An active IPv6 multicast interface is required.");

        int index = iface.GetIPProperties().GetIPv6Properties().Index;
        var source = iface.GetIPProperties().UnicastAddresses.First(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6).Address;
        var unscopedSource = new IPAddress(source.GetAddressBytes());
        var multicast = IPAddress.Parse("ff02::114");
        var scopedGroup = new IPAddress(multicast.GetAddressBytes(), index);
        var group = new DefaultEventLoopGroup(1);
        var channel = new DatagramChannel(AddressFamily.InterNetworkV6);
        try
        {
            await group.RegisterAsync(channel, cancellation);
            await channel.BindAsync(new IPEndPoint(IPAddress.IPv6Any, 0), cancellation);
            if (sourceSpecific)
            {
                await channel.JoinGroupAsync(scopedGroup, iface, source);
                await channel.JoinGroupAsync(multicast, iface, unscopedSource);
                await Assert.ThrowsAsync<InvalidOperationException>(() => channel.JoinGroupAsync(new IPEndPoint(multicast, 0), iface));
                await Assert.ThrowsAsync<InvalidOperationException>(() => channel.BlockAsync(multicast, iface, unscopedSource));
                await channel.LeaveGroupAsync(multicast, iface, unscopedSource);
                await channel.LeaveGroupAsync(scopedGroup, iface, source);
                await channel.JoinGroupAsync(new IPEndPoint(multicast, 0), iface);
                await channel.LeaveGroupAsync(scopedGroup, index);
            }
            else
            {
                await channel.JoinGroupAsync(new IPEndPoint(scopedGroup, 0), iface);
                await Assert.ThrowsAsync<InvalidOperationException>(() => channel.JoinGroupAsync(multicast, iface, unscopedSource));
                await channel.BlockAsync(multicast, iface, unscopedSource);
                await channel.BlockAsync(scopedGroup, iface, source);
                await channel.LeaveGroupAsync(multicast, index);
                await channel.JoinGroupAsync(new IPEndPoint(scopedGroup, 0), iface);
                await channel.BlockAsync(scopedGroup, iface, source);
            }
        }
        finally { await group.StopAsync(); }
    }

    [Theory(Timeout = 10000)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SourceMembershipAndBlockingControlRealPackets(bool sourceSpecific, bool ipv6)
    {
        if (ipv6 && !OperatingSystem.IsLinux())
            Assert.Skip("IPv6 packet delivery is exercised on an IPv6-enabled Linux test network.");
        var family = ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        var iface = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up && n.SupportsMulticast &&
            n.GetIPProperties().UnicastAddresses.Any(a => a.Address.AddressFamily == family));
        if (ipv6 && iface == null) Assert.Skip("An active IPv6 multicast interface is required.");
        var source = iface.GetIPProperties().UnicastAddresses.First(a => a.Address.AddressFamily == family).Address;
        var multicastAddress = IPAddress.Parse(!ipv6 ? "230.0.0.2" : sourceSpecific ? "ff32::a187:613c" : "ff12::a187:613d");
        var messages = Channel.CreateUnbounded<int>();
        DatagramChannel channel = null;
        using var sender = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
        sender.Bind(new IPEndPoint(source, 0));
        if (ipv6)
        {
            int index = iface.GetIPProperties().GetIPv6Properties().Index;
            multicastAddress.ScopeId = index;
            sender.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, index);
        }
        else sender.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, source.GetAddressBytes());
        var group = new DefaultEventLoopGroup(1);
        try
        {
            channel = (DatagramChannel)await new Bootstrap().Group(group).ChannelFactory(() => new DatagramChannel(family)).Handler(new Capture(messages.Writer))
                .Option(ChannelOption.IP_MULTICAST_IF, iface).BindAsync(new IPEndPoint(ipv6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
            var multicast = new IPEndPoint(multicastAddress, ((IPEndPoint)channel.LocalAddress).Port);
            if (sourceSpecific)
            {
                await channel.JoinGroupAsync(multicast.Address, iface, source);
                await channel.JoinGroupAsync(multicast.Address, iface, source);
                await Assert.ThrowsAsync<InvalidOperationException>(() => channel.JoinGroupAsync(multicast, iface));
                await Assert.ThrowsAsync<InvalidOperationException>(() => channel.BlockAsync(multicast.Address, iface, source));
            }
            else
            {
                await channel.BlockAsync(multicast.Address, iface, source);
                await channel.JoinGroupAsync(multicast, iface);
                await Assert.ThrowsAsync<InvalidOperationException>(() => channel.JoinGroupAsync(multicast.Address, iface, source));
            }
            await sender.SendToAsync(new byte[] { 1 }, SocketFlags.None, multicast, TestContext.Current.CancellationToken);
            Assert.Equal(1, await messages.Reader.ReadAsync(TestContext.Current.CancellationToken));
            if (sourceSpecific)
            {
                await channel.LeaveGroupAsync(multicast.Address, iface, source);
                await channel.LeaveGroupAsync(multicast.Address, iface, source);
            }
            else
            {
                await channel.BlockAsync(multicast.Address, iface, source);
                await channel.BlockAsync(multicast.Address, source);
            }
            await sender.SendToAsync(new byte[] { 2 }, SocketFlags.None, multicast, TestContext.Current.CancellationToken);
            await Task.Delay(200, TestContext.Current.CancellationToken);
            // A multicast loopback can duplicate the first packet. Only payload 2 tests the filter.
            while (messages.Reader.TryRead(out int received)) Assert.Equal(1, received);
            if (sourceSpecific) await channel.JoinGroupAsync(multicast.Address, iface, source);
            else { await channel.LeaveGroupAsync(multicast, iface); await channel.JoinGroupAsync(multicast, iface); }
            await sender.SendToAsync(new byte[] { 3 }, SocketFlags.None, multicast, TestContext.Current.CancellationToken);
            int resumed;
            do { resumed = await messages.Reader.ReadAsync(TestContext.Current.CancellationToken); } while (resumed == 1);
            Assert.Equal(3, resumed);
        }
        finally { if (channel != null) await channel.CloseAsync(); await group.StopAsync(); }
    }

    [Theory(Timeout = 10000)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SocketReadErrorCompletesBatchThenPreservesReadPermission(bool ipv6, bool autoRead)
    {
        var family = ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        var loopback = ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        var group = new DefaultEventLoopGroup(1);
        var received = Channel.CreateUnbounded<int>();
        var events = new ConcurrentQueue<string>();
        var error = new SocketException((int)SocketError.ConnectionReset);
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = new FailFirstReceive(family, error);
        channel.Config.SetAutoRead(autoRead);
        channel.Pipeline.AddLast(new Capture(received.Writer, events, failed));
        using var sender = new Socket(family, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            await channel.RegisterAsync(group.Next());
            await channel.BindAsync(new IPEndPoint(loopback, 0));
            if (!autoRead) channel.Read();
            Assert.Same(error, await failed.Task.WaitAsync(TestContext.Current.CancellationToken));
            await sender.SendToAsync(new byte[] { 1 }, SocketFlags.None, channel.LocalAddress, TestContext.Current.CancellationToken);
            Assert.Equal(1, await received.Reader.ReadAsync(TestContext.Current.CancellationToken));
            await channel.EventLoop.SubmitAsync(() => { });
            Assert.Equal(new[] { "complete", "error", "read", "complete" }, events.ToArray());
            Assert.True(channel.IsOpen);
            if (!autoRead)
            {
                await sender.SendToAsync(new byte[] { 2 }, SocketFlags.None, channel.LocalAddress, TestContext.Current.CancellationToken);
                await Task.Delay(100, TestContext.Current.CancellationToken);
                Assert.False(received.Reader.TryRead(out _));
                channel.Read();
                Assert.Equal(2, await received.Reader.ReadAsync(TestContext.Current.CancellationToken));
            }
        }
        finally { await channel.CloseAsync(); await group.StopAsync(); }
    }
    [Fact(Timeout = 5000)]
    public async Task NonSocketReadErrorCompletesBatchAndClosesTheChannel()
    {
        var group = new DefaultEventLoopGroup(1);
        var events = new ConcurrentQueue<string>();
        var error = new IOException("fatal receive");
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = new FailFirstReceive(AddressFamily.InterNetwork, error);
        channel.Pipeline.AddLast(new Capture(Channel.CreateUnbounded<int>().Writer, events, failed));
        try
        {
            await channel.RegisterAsync(group.Next());
            await channel.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
            Assert.Same(error, await failed.Task.WaitAsync(TestContext.Current.CancellationToken));
            await channel.CloseCompletion.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(channel.IsOpen);
            Assert.Equal(new[] { "complete", "error" }, events.ToArray());
        }
        finally { await channel.CloseAsync(); await group.StopAsync(); }
    }
    private sealed class FailFirstReceive(AddressFamily family, Exception error) : DatagramChannel(family)
    {
        private int _attempts;
        protected override Task<SocketReceiveFromResult> ReceiveDatagramAsync(Memory<byte> memory, EndPoint remote)
            => Interlocked.Increment(ref _attempts) == 1 ? Task.FromException<SocketReceiveFromResult>(error) : base.ReceiveDatagramAsync(memory, remote);
    }
    private sealed class Capture(ChannelWriter<int> writer, ConcurrentQueue<string> events = null, TaskCompletionSource<Exception> failure = null) : SimpleChannelInboundHandler<DatagramPacket>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, DatagramPacket message)
        { events?.Enqueue("read"); writer.TryWrite(message.Content.ReadByte()); }
        public override void ChannelReadComplete(ChannelHandlerContext context) => events?.Enqueue("complete");
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause)
        { events?.Enqueue("error"); if (failure != null) failure.TrySetResult(cause); else context.FireExceptionCaught(cause); }
    }
}
