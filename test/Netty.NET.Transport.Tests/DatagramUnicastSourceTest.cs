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
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable CS0612, CS0618, xUnit1051 // Original deprecated option and independently owned teardown.
public class DatagramUnicastSourceTest
{
    private static readonly byte[] BadPrefix = Encoding.UTF8.GetBytes("BAD-PREFIX-");
    private static readonly byte[] Expected = Encoding.UTF8.GetBytes("EXPECTED-direct-reader-index");
    private static readonly byte[] Bytes = [0, 1, 2, 3];
    // UDP delivery is best-effort even on loopback, so allow a few resends before failing the test.
    private const int SendAttempts = 3;
    private enum WrapType { None, Dup, Slice, ReadOnly }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TestSimpleSendDirectByteBuf(bool ipv6, bool adaptive)
    {
        await using var fixture = new Fixture(ipv6, adaptive);
        foreach (int count in new[] { 1, 4 })
        {
            var buffer = NewBuffer("direct");
            try
            {
                foreach (WrapType wrap in Enum.GetValues<WrapType>())
                {
                    buffer.Retain();
                    await SimpleSend(fixture, buffer, true, count, wrap);
                }
            }
            finally { Assert.True(buffer.Release()); }
        }
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TestSimpleSendHeapByteBuf(bool ipv6, bool adaptive)
    {
        await using var fixture = new Fixture(ipv6, adaptive);
        foreach (int count in new[] { 1, 4 })
        {
            var buffer = NewBuffer("heap");
            try
            {
                foreach (WrapType wrap in Enum.GetValues<WrapType>())
                {
                    buffer.Retain();
                    await SimpleSend(fixture, buffer, true, count, wrap);
                }
            }
            finally { Assert.True(buffer.Release()); }
        }
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TestSimpleSendCompositeDirectByteBuf(bool ipv6, bool adaptive)
    {
        await using var fixture = new Fixture(ipv6, adaptive);
        foreach (int count in new[] { 1, 4 })
        {
            var buffer = NewBuffer("composite-direct");
            try
            {
                foreach (WrapType wrap in Enum.GetValues<WrapType>())
                {
                    buffer.Retain();
                    await SimpleSend(fixture, buffer, true, count, wrap);
                }
            }
            finally { Assert.True(buffer.Release()); }
        }
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TestSimpleSendCompositeHeapByteBuf(bool ipv6, bool adaptive)
    {
        await using var fixture = new Fixture(ipv6, adaptive);
        foreach (int count in new[] { 1, 4 })
        {
            var buffer = NewBuffer("composite-heap");
            try
            {
                foreach (WrapType wrap in Enum.GetValues<WrapType>())
                {
                    buffer.Retain();
                    await SimpleSend(fixture, buffer, true, count, wrap);
                }
            }
            finally { Assert.True(buffer.Release()); }
        }
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TestSimpleSendCompositeMixedByteBuf(bool ipv6, bool adaptive)
    {
        await using var fixture = new Fixture(ipv6, adaptive);
        foreach (int count in new[] { 1, 4 })
        {
            var buffer = NewBuffer("composite-mixed");
            try
            {
                foreach (WrapType wrap in Enum.GetValues<WrapType>())
                {
                    buffer.Retain();
                    await SimpleSend(fixture, buffer, true, count, wrap);
                }
            }
            finally { Assert.True(buffer.Release()); }
        }
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TestSimpleSendWithoutBind(bool ipv6, bool adaptive)
    {
        await using var fixture = new Fixture(ipv6, adaptive);
        foreach (int count in new[] { 1, 4 })
        {
            var buffer = NewBuffer("direct");
            try
            {
                foreach (WrapType wrap in Enum.GetValues<WrapType>())
                {
                    buffer.Retain();
                    await SimpleSend(fixture, buffer, false, count, wrap);
                }
            }
            finally { Assert.True(buffer.Release()); }
        }
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TestSimpleSendWithConnect(bool ipv6, bool adaptive)
    {
        await using var fixture = new Fixture(ipv6, adaptive);
        foreach (int count in new[] { 1, 4 })
        {
            var buffer = Unpooled.DirectBuffer().WriteBytes(Bytes);
            try
            {
                foreach (WrapType wrap in Enum.GetValues<WrapType>())
                {
                    buffer.Retain();
                    await ConnectedSend(fixture, buffer, count, wrap);
                }
            }
            finally { Assert.True(buffer.Release()); }
        }
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TestReceiveEmptyDatagrams(bool ipv6, bool adaptive)
    {
        await using var fixture = new Fixture(ipv6, adaptive);
        using var semaphore = new SemaphoreSlim(0);
        var server = await fixture.Bootstrap(new PacketHandler((_, _) => semaphore.Release())).BindAsync(fixture.Address());
        try
        {
            using var client = new Socket(fixture.Family, SocketType.Dgram, ProtocolType.Udp);
            client.Bind(fixture.Address());
            for (int i = 0; i < 100; i++)
            {
                await client.SendToAsync(ReadOnlyMemory<byte>.Empty, SocketFlags.None, server.LocalAddress, TestContext.Current.CancellationToken);
                await semaphore.WaitAsync(TestContext.Current.CancellationToken);
            }
        }
        finally { await server.CloseAsync(); }
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TestSendToUnresolvableAddress(bool ipv6, bool adaptive)
    {
        await using var fixture = new Fixture(ipv6, adaptive);
        // Just drop
        var server = await fixture.Bootstrap(new PacketHandler((_, _) => { })).BindAsync(fixture.Address());
        var client = await fixture.Bootstrap(new ChannelInboundHandlerAdapter())
            .Option(ChannelOption.DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION, true).RegisterAsync();
        try
        {
            var goodHost = (IPEndPoint)server.LocalAddress;
            var unresolvedHost = new DnsEndPoint("NOT_A_REAL_ADDRESS", goodHost.Port);
            Assert.IsType<IPEndPoint>(goodHost);
            Assert.IsType<DnsEndPoint>(unresolvedHost);
            for (int i = 0; i < 2; i++)
            {
                await client.WriteAndFlushAsync(new DatagramPacket(Unpooled.CopiedBuffer("hello world!", Encoding.ASCII), goodHost));
                var failure = await Assert.ThrowsAsync<SocketException>(() => client.WriteAndFlushAsync(
                    new DefaultAddressedEnvelope<ByteBuf, EndPoint>(Unpooled.CopiedBuffer("hello world!", Encoding.ASCII), unresolvedHost)));
                Assert.Equal(SocketError.HostNotFound, failure.SocketErrorCode);
                // DatagramChannel should still be open after sending to unresolved address
                Assert.True(client.IsOpen);
                // DatagramChannel should still be able to send messages outbound
            }
        }
        finally { await client.CloseAsync(); await server.CloseAsync(); }
    }
    [Theory(Timeout = 10000)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TestWriteOffsetBytebuf(bool ipv6, bool adaptive)
    {
        await using var fixture = new Fixture(ipv6, adaptive);
        var buffer = Unpooled.DirectBuffer(BadPrefix.Length + Expected.Length).WriteBytes(BadPrefix).WriteBytes(Expected);
        buffer.ReaderIndex = BadPrefix.Length;
        try
        {
            foreach (WrapType wrap in Enum.GetValues<WrapType>())
            {
                buffer.Retain();
                var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                IChannel server = null, client = null;
                try
                {
                    server = await fixture.Bootstrap(new PacketHandler((_, packet) =>
                    {
                        var bytes = new byte[packet.Content.ReadableBytes];
                        packet.Content.GetBytes(packet.Content.ReaderIndex, bytes);
                        received.TrySetResult(bytes);
                    })).BindAsync(fixture.Address());
                    // Correctly ha
                    var serverAddress = server.LocalAddress;
                    // no-op
                    client = await fixture.Bootstrap(new PacketHandler((_, _) => { })).ConnectAsync(serverAddress);
                    var written = client.WriteAsync(Wrap(buffer, wrap));
                    client.Flush();
                    await written;
                    Assert.Equal(Expected, await received.Task.WaitAsync(TestContext.Current.CancellationToken));
                }
                finally
                {
                    // release as we used buf.retain() before
                    buffer.Release();
                    if (client != null) await client.CloseAsync();
                    if (server != null) await server.CloseAsync();
                }
            }
            Assert.Equal(1, buffer.ReferenceCount);
        }
        finally { buffer.Release(); }
    }
    private static ByteBuf NewBuffer(string kind)
    {
        if (kind == "direct") return Unpooled.DirectBuffer().WriteBytes(Bytes);
        if (kind == "heap") return Unpooled.Buffer().WriteBytes(Bytes);
        var buffer = Unpooled.CompositeBuffer();
        var first = kind == "composite-heap" ? Unpooled.Buffer() : Unpooled.DirectBuffer();
        var second = kind == "composite-direct" ? Unpooled.DirectBuffer() : Unpooled.Buffer();
        buffer.AddComponent(first.WriteBytes(Bytes.AsSpan(0, 2)), true);
        buffer.AddComponent(second.WriteBytes(Bytes.AsSpan(2, 2)), true);
        return buffer;
    }
    private static ByteBuf Wrap(ByteBuf buffer, WrapType type)
    {
        switch (type)
        {
            case WrapType.Dup: return buffer.RetainedDuplicate();
            case WrapType.Slice: return buffer.RetainedSlice();
            case WrapType.ReadOnly: buffer.Retain(); return buffer.AsReadOnly();
            case WrapType.None: buffer.Retain(); return buffer;
            default: throw new InvalidOperationException("Unexpected wrap type.");
        }
    }
    private static async Task SimpleSend(Fixture fixture, ByteBuf buffer, bool bindClient, int count, WrapType wrap)
    {
        IChannel server = null, client = null;
        try
        {
            // Nothing will be sent.
            var bootstrap = fixture.Bootstrap(new PacketHandler((_, _) => { }));
            client = bindClient ? await bootstrap.BindAsync(fixture.Address())
                : await bootstrap.Option(ChannelOption.DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION, true).RegisterAsync();
            var latch = new Countdown(count);
            var error = new ErrorHolder();
            server = await fixture.Bootstrap(Receiver(Bytes, bindClient ? client.LocalAddress : null, latch, error, false, false)).BindAsync(fixture.Address());
            bool received = false;
            for (int attempt = 1; attempt <= SendAttempts && !received; attempt++)
            {
                var futures = new List<Task>();
                for (int i = 0; i < count; i++)
                    futures.Add(client.WriteAsync(new DatagramPacket(Wrap(buffer, wrap), (IPEndPoint)server.LocalAddress)));
                // release as we used buf.retain() before
                client.Flush();
                await Task.WhenAll(futures);
                // UDP datagrams can be lost even on loopback, so retry a few times before giving up.
                received = await latch.Await();
            }
            if (!received) { if (error.Cause != null) throw error.Cause; Assert.Fail(); }
        }
        finally
        {
            // release as we used buf.retain() before
            buffer.Release();
            if (client != null) await client.CloseAsync();
            if (server != null) await server.CloseAsync();
        }
    }
    private static async Task ConnectedSend(Fixture fixture, ByteBuf buffer, int count, WrapType wrap)
    {
        IChannel server = null, client = null;
        try
        {
            var latch = new Countdown(count);
            var error = new ErrorHolder();
            var clientLatch = new Countdown(count);
            var clientError = new ErrorHolder();
            client = await fixture.Bootstrap(Receiver(Bytes, null, clientLatch, clientError, false, true)).BindAsync(fixture.Address());
            server = await fixture.Bootstrap(Receiver(Bytes, client.LocalAddress, latch, error, true, false)).BindAsync(fixture.Address());
            await client.ConnectAsync(server.LocalAddress);
            bool received = false;
            for (int attempt = 1; attempt <= SendAttempts && !received; attempt++)
            {
                var futures = new List<Task>();
                for (int i = 0; i < count; i++) futures.Add(client.WriteAsync(Wrap(buffer, wrap)));
                client.Flush();
                await Task.WhenAll(futures);
                // UDP datagrams (including the echo) can be lost even on loopback, so retry a few times
                // before giving up.
                received = await latch.Await() && await clientLatch.Await();
            }
            if (!received)
            {
                if (latch.Remaining != 0) { if (error.Cause != null) throw error.Cause; Assert.Fail(); }
                if (clientError.Cause != null) throw clientError.Cause;
                Assert.Fail();
            }
            Assert.True(((DatagramChannel)client).IsConnected);
            Assert.NotNull(client.LocalAddress);
            Assert.NotNull(client.RemoteAddress);
            // Test what happens when we call disconnect()
            await client.DisconnectAsync();
            Assert.False(((DatagramChannel)client).IsConnected);
            Assert.NotNull(client.LocalAddress);
            Assert.Null(client.RemoteAddress);
            var disconnectedWrite = client.WriteAndFlushAsync(buffer.RetainedDuplicate());
            Assert.Equal(SocketError.NotConnected, (await Assert.ThrowsAsync<SocketException>(() => disconnectedWrite)).SocketErrorCode);
            // Connect again and try to write.
            await client.ConnectAsync(server.LocalAddress);
            var finalWrite = client.WriteAsync(Wrap(buffer, wrap));
            client.Flush();
            await finalWrite;
        }
        finally
        {
            // release as we used buf.retain() before
            buffer.Release();
            if (client != null) await client.CloseAsync();
            if (server != null) await server.CloseAsync();
        }
    }
    private static PacketHandler Receiver(byte[] bytes, EndPoint sender, Countdown latch, ErrorHolder error, bool echo, bool client)
        => new((context, packet) =>
        {
            try
            {
                if (!client)
                {
                    if (sender == null) Assert.NotNull(packet.Sender);
                    else if (((IPEndPoint)sender).Address.Equals(IPAddress.Any) || ((IPEndPoint)sender).Address.Equals(IPAddress.IPv6Any))
                        Assert.Equal(((IPEndPoint)sender).Port, packet.Sender.Port);
                    else Assert.Equal(sender, packet.Sender);
                }
                var buffer = packet.Content;
                Assert.Equal(bytes.Length, buffer.ReadableBytes);
                for (int i = 0; i < bytes.Length; i++) Assert.Equal(bytes[i], buffer.GetByte(buffer.ReaderIndex + i));
                var local = (IPEndPoint)context.Channel.LocalAddress;
                if (client && (local.Address.Equals(IPAddress.Any) || local.Address.Equals(IPAddress.IPv6Any)))
                    Assert.Equal(local.Port, packet.Recipient.Port);
                else
                {
                    // Test that the channel's localAddress is equal to the message's recipient
                    Assert.Equal(local, packet.Recipient);
                }
                if (echo) _ = context.WriteAndFlushAsync(new DatagramPacket(buffer.RetainedDuplicate(), packet.Sender));
            }
            finally { latch.CountDown(); }
        }, error);
    private sealed class PacketHandler(Action<ChannelHandlerContext, DatagramPacket> read, ErrorHolder error = null) : SimpleChannelInboundHandler<DatagramPacket>
    {
        protected override void ChannelRead0(ChannelHandlerContext context, DatagramPacket packet) => read(context, packet);
        public override void ExceptionCaught(ChannelHandlerContext context, Exception cause)
        { if (error != null) error.Set(cause); else context.FireExceptionCaught(cause); }
    }
    private sealed class ErrorHolder
    {
        internal Exception Cause;
        internal void Set(Exception cause) => Interlocked.CompareExchange(ref Cause, cause, null);
    }
    private sealed class Countdown(int count)
    {
        private int _remaining = count;
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Remaining => Volatile.Read(ref _remaining);
        internal void CountDown()
        {
            int remaining;
            do { remaining = Volatile.Read(ref _remaining); if (remaining == 0) return; }
            while (Interlocked.CompareExchange(ref _remaining, remaining - 1, remaining) != remaining);
            if (remaining == 1) _done.TrySetResult();
        }
        internal async Task<bool> Await()
        {
            try { await _done.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); return true; }
            catch (TimeoutException) { return false; }
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly DefaultEventLoopGroup Group = new(1);
        internal readonly AddressFamily Family;
        private readonly IByteBufAllocator _allocator;
        internal Fixture(bool ipv6, bool adaptive)
        { Family = ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork; _allocator = adaptive ? new AdaptiveByteBufAllocator() : PooledByteBufAllocator.Default; }
        internal IPEndPoint Address() => new(Family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, 0);
        internal Bootstrap Bootstrap(IChannelHandler handler) => new Bootstrap().Group(Group).ChannelFactory(() => new DatagramChannel(Family)).Option(ChannelOption.ALLOCATOR, _allocator).Handler(handler);
        public async ValueTask DisposeAsync()
        { await Group.StopAsync(); if (_allocator is AdaptiveByteBufAllocator adaptive) adaptive.Dispose(); }
    }
}
