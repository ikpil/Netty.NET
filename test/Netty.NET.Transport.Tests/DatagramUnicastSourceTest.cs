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
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable xUnit1069 // The xUnit Timeout bounds the Runner body, which has no other cancellable wait.
#pragma warning disable xUnit1051 // The source's sync()/await() calls and its channel teardown take no cancellation.
#pragma warning disable CS0612 // The source uses the deprecated DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION option too.
public abstract class DatagramUnicastSourceTest : AbstractDatagramTest
{
    private static readonly byte[] BAD_PREFIX = Encoding.UTF8.GetBytes("BAD-PREFIX-");
    private static readonly byte[] EXPECTED = Encoding.UTF8.GetBytes("EXPECTED-direct-reader-index");
    private static readonly byte[] BYTES = {0, 1, 2, 3};
    // UDP delivery is best-effort even on loopback, so allow a few resends before failing the test.
    private const int SEND_ATTEMPTS = 3;
    protected enum WrapType
    {
        NONE, DUP, SLICE, READ_ONLY
    }

    [Fact]
    public async Task TestSimpleSendDirectByteBuf()
    {
        await Run(TestContext.Current, TestSimpleSendDirectByteBuf0);
    }

    // CLR: xUnit1024 forbids overloading the TestSimpleSendDirectByteBuf test name.
    private async Task TestSimpleSendDirectByteBuf0(Bootstrap sb, Bootstrap cb)
    {
        await TestSimpleSend(sb, cb, Unpooled.DirectBuffer().WriteBytes(BYTES), true, BYTES, 1);
        await TestSimpleSend(sb, cb, Unpooled.DirectBuffer().WriteBytes(BYTES), true, BYTES, 4);
    }

    [Fact]
    public async Task TestSimpleSendHeapByteBuf()
    {
        await Run(TestContext.Current, TestSimpleSendHeapByteBuf0);
    }

    // CLR: xUnit1024 forbids overloading the TestSimpleSendHeapByteBuf test name.
    private async Task TestSimpleSendHeapByteBuf0(Bootstrap sb, Bootstrap cb)
    {
        await TestSimpleSend(sb, cb, Unpooled.Buffer().WriteBytes(BYTES), true, BYTES, 1);
        await TestSimpleSend(sb, cb, Unpooled.Buffer().WriteBytes(BYTES), true, BYTES, 4);
    }

    [Fact]
    public async Task TestSimpleSendCompositeDirectByteBuf()
    {
        await Run(TestContext.Current, TestSimpleSendCompositeDirectByteBuf0);
    }

    // CLR: xUnit1024 forbids overloading the TestSimpleSendCompositeDirectByteBuf test name. In the composite tests
    // this port's CompositeByteBuf.AddComponent(ByteBuf, bool increaseWriterIndex) takes the flag after the buffer,
    // and ByteBuf writes a byte range from a ReadOnlySpan<byte> instead of writeBytes(byte[], int, int).
    private async Task TestSimpleSendCompositeDirectByteBuf0(Bootstrap sb, Bootstrap cb)
    {
        CompositeByteBuf buf = Unpooled.CompositeBuffer();
        buf.AddComponent(Unpooled.DirectBuffer().WriteBytes(BYTES.AsSpan(0, 2)), true);
        buf.AddComponent(Unpooled.DirectBuffer().WriteBytes(BYTES.AsSpan(2, 2)), true);
        await TestSimpleSend(sb, cb, buf, true, BYTES, 1);

        CompositeByteBuf buf2 = Unpooled.CompositeBuffer();
        buf2.AddComponent(Unpooled.DirectBuffer().WriteBytes(BYTES.AsSpan(0, 2)), true);
        buf2.AddComponent(Unpooled.DirectBuffer().WriteBytes(BYTES.AsSpan(2, 2)), true);
        await TestSimpleSend(sb, cb, buf2, true, BYTES, 4);
    }

    [Fact]
    public async Task TestSimpleSendCompositeHeapByteBuf()
    {
        await Run(TestContext.Current, TestSimpleSendCompositeHeapByteBuf0);
    }

    // CLR: xUnit1024 forbids overloading the TestSimpleSendCompositeHeapByteBuf test name.
    private async Task TestSimpleSendCompositeHeapByteBuf0(Bootstrap sb, Bootstrap cb)
    {
        CompositeByteBuf buf = Unpooled.CompositeBuffer();
        buf.AddComponent(Unpooled.Buffer().WriteBytes(BYTES.AsSpan(0, 2)), true);
        buf.AddComponent(Unpooled.Buffer().WriteBytes(BYTES.AsSpan(2, 2)), true);
        await TestSimpleSend(sb, cb, buf, true, BYTES, 1);

        CompositeByteBuf buf2 = Unpooled.CompositeBuffer();
        buf2.AddComponent(Unpooled.Buffer().WriteBytes(BYTES.AsSpan(0, 2)), true);
        buf2.AddComponent(Unpooled.Buffer().WriteBytes(BYTES.AsSpan(2, 2)), true);
        await TestSimpleSend(sb, cb, buf2, true, BYTES, 4);
    }

    [Fact]
    public async Task TestSimpleSendCompositeMixedByteBuf()
    {
        await Run(TestContext.Current, TestSimpleSendCompositeMixedByteBuf0);
    }

    // CLR: xUnit1024 forbids overloading the TestSimpleSendCompositeMixedByteBuf test name.
    private async Task TestSimpleSendCompositeMixedByteBuf0(Bootstrap sb, Bootstrap cb)
    {
        CompositeByteBuf buf = Unpooled.CompositeBuffer();
        buf.AddComponent(Unpooled.DirectBuffer().WriteBytes(BYTES.AsSpan(0, 2)), true);
        buf.AddComponent(Unpooled.Buffer().WriteBytes(BYTES.AsSpan(2, 2)), true);
        await TestSimpleSend(sb, cb, buf, true, BYTES, 1);

        CompositeByteBuf buf2 = Unpooled.CompositeBuffer();
        buf2.AddComponent(Unpooled.DirectBuffer().WriteBytes(BYTES.AsSpan(0, 2)), true);
        buf2.AddComponent(Unpooled.Buffer().WriteBytes(BYTES.AsSpan(2, 2)), true);
        await TestSimpleSend(sb, cb, buf2, true, BYTES, 4);
    }

    [Fact]
    public async Task TestSimpleSendWithoutBind()
    {
        await Run(TestContext.Current, TestSimpleSendWithoutBind0);
    }

    // CLR: xUnit1024 forbids overloading the TestSimpleSendWithoutBind test name.
    private async Task TestSimpleSendWithoutBind0(Bootstrap sb, Bootstrap cb)
    {
        await TestSimpleSend(sb, cb, Unpooled.DirectBuffer().WriteBytes(BYTES), false, BYTES, 1);
        await TestSimpleSend(sb, cb, Unpooled.DirectBuffer().WriteBytes(BYTES), false, BYTES, 4);
    }

    private async Task TestSimpleSend(Bootstrap sb, Bootstrap cb, ByteBuf buf, bool bindClient,
                                byte[] bytes, int count)
    {
        try
        {
            foreach (WrapType type in Enum.GetValues<WrapType>())
            {
                await TestSimpleSend0(sb, cb, buf.Retain(), bindClient, bytes, count, type);
            }
        }
        finally
        {
            Assert.True(buf.Release());
        }
    }

    [Fact]
    public async Task TestSimpleSendWithConnect()
    {
        await Run(TestContext.Current, TestSimpleSendWithConnect0);
    }

    // CLR: xUnit1024 forbids overloading the TestSimpleSendWithConnect test name, so this body and the source's
    // testSimpleSendWithConnect(sb, cb, buf, bytes, count) helper overload TestSimpleSendWithConnect0 instead.
    // EpollDatagramUnicastTest overrides this body; see TestSimpleSendWithConnectAndOriginalDestination.
    protected virtual async Task TestSimpleSendWithConnect0(Bootstrap sb, Bootstrap cb)
    {
        await TestSimpleSendWithConnect0(sb, cb, Unpooled.DirectBuffer().WriteBytes(BYTES), BYTES, 1);
        await TestSimpleSendWithConnect0(sb, cb, Unpooled.DirectBuffer().WriteBytes(BYTES), BYTES, 4);
    }

    // CLR: the native epoll variant needs original destination ancillary data.
    // Run this test with IP_RECVORIGDSTADDR option enabled
    [Fact(Skip = "IP_RECVORIGDSTADDR is unavailable on the managed transport.")]
    public Task TestSimpleSendWithConnectAndOriginalDestination() => Task.CompletedTask;

    [Fact]
    public async Task TestReceiveEmptyDatagrams()
    {
        await Run(TestContext.Current, TestReceiveEmptyDatagrams0);
    }

    // CLR: xUnit1024 forbids overloading the TestReceiveEmptyDatagrams test name.
    private async Task TestReceiveEmptyDatagrams0(Bootstrap sb, Bootstrap cb)
    {
        // CLR: java.util.concurrent.Semaphore is SemaphoreSlim, "a lightweight alternative to Semaphore that limits
        // the number of threads that can access a resource or pool of resources concurrently"
        // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim); WaitAsync replaces the
        // blocking acquire(). Like the source, the server channel and the client socket stay open.
        SemaphoreSlim semaphore = new SemaphoreSlim(0);
        IChannel server = await sb.Handler(new ActionChannelInitializer<IChannel>(ch =>
        {
            ch.Pipeline.AddLast(new ReleaseSemaphoreHandler(semaphore));
        })).BindAsync(NewSocketAddress());

        EndPoint address = server.LocalAddress;
        // CLR: java.net.DatagramSocket(SocketAddress) is a BCL UDP Socket bound to the same address. The BCL socket is
        // opened for the family of the address it binds, so the source's IllegalArgumentException handler for an
        // address type the JDK socket does not support (its "unsupported address type" assumption) has no
        // counterpart.
        EndPoint bindaddr = NewSocketAddress();
        Socket client = new Socket(bindaddr.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        client.Bind(bindaddr);
        EndPoint sendAddress = address is IPEndPoint ?
                ConvertAnyAddress((IPEndPoint) address) : address;
        for (int i = 0; i < 100; i++)
        {
            try
            {
                client.SendTo(EmptyArrays.EMPTY_BYTES, 0, SocketFlags.None, sendAddress);
            }
            // CLR: java.net.BindException "Signals that an error occurred while attempting to bind a socket to a
            // local address and port. Typically, the port is in use, or the requested local address could not be
            // assigned" (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/net/BindException.html);
            // the BCL reports those conditions as SocketError.AddressAlreadyInUse and AddressNotAvailable
            // (https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketerror). TestAbortedException is
            // xUnit v3's Assert.Skip (https://xunit.net/docs/getting-started/v3/whats-new).
            catch (SocketException e) when (e.SocketErrorCode is SocketError.AddressAlreadyInUse
                    or SocketError.AddressNotAvailable)
            {
                Assert.Skip("JDK sockets do not support binding to these addresses.");
            }
            await semaphore.WaitAsync();
        }
    }

    [Fact]
    public async Task TestSendToUnresolvableAddress()
    {
        await Run(TestContext.Current, TestSendToUnresolvableAddress0);
    }

    // CLR: xUnit1024 forbids overloading the TestSendToUnresolvableAddress test name.
    private async Task TestSendToUnresolvableAddress0(Bootstrap sb, Bootstrap cb)
    {
        EndPoint serverAddress = NewSocketAddress();
        if (!(serverAddress is IPEndPoint))
        {
            return;
        }
        IChannel sc = await sb.Handler(new ActionChannelInitializer<IChannel>(ch =>
        {
            ch.Pipeline.AddLast(new DropHandler());
        })).BindAsync(serverAddress);

        IChannel cc = await cb.Option(ChannelOption.DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION, true).
                Handler(new ChannelInboundHandlerAdapter()).RegisterAsync();
        try
        {
            IPEndPoint goodHost = ConvertAnyAddress((IPEndPoint) sc.LocalAddress);
            // CLR: InetSocketAddress.isUnresolved() is the endpoint type here: an IPEndPoint holds a resolved address,
            // while DnsEndPoint "Represents a network endpoint as a host name or a string representation of an IP
            // address and a port number" (https://learn.microsoft.com/en-us/dotnet/api/system.net.dnsendpoint) and is
            // never resolved by its constructor, unlike the source's InetSocketAddress(String, int).
            EndPoint unresolvedHost = new DnsEndPoint("NOT_A_REAL_ADDRESS", goodHost.Port);

            Assert.IsType<IPEndPoint>(goodHost);
            Assert.IsType<DnsEndPoint>(unresolvedHost);

            // CLR: CharsetUtil.US_ASCII is Encoding.ASCII, "an encoding for the ASCII (7-bit) character set"
            // (https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.ascii). DatagramPacket's recipient is
            // an IPEndPoint, so the DnsEndPoint destination travels in a DefaultAddressedEnvelope<ByteBuf, EndPoint>,
            // which NioDatagramChannel.FilterOutboundMessage fails with SocketError.HostNotFound ("No such host is
            // known", https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socketerror) in place of
            // java.nio.channels.UnresolvedAddressException.
            string message = "hello world!";
            await cc.WriteAndFlushAsync(new DatagramPacket(Unpooled.CopiedBuffer(message, Encoding.ASCII), goodHost));
            Assert.Equal(SocketError.HostNotFound, (await Assert.ThrowsAsync<SocketException>(() => cc.WriteAndFlushAsync(
                    new DefaultAddressedEnvelope<ByteBuf, EndPoint>(
                            Unpooled.CopiedBuffer(message, Encoding.ASCII), unresolvedHost)))).SocketErrorCode);

            // DatagramChannel should still be open after sending to unresolved address
            Assert.True(cc.IsOpen);

            // DatagramChannel should still be able to send messages outbound
            await cc.WriteAndFlushAsync(new DatagramPacket(Unpooled.CopiedBuffer(message, Encoding.ASCII), goodHost));
            Assert.Equal(SocketError.HostNotFound, (await Assert.ThrowsAsync<SocketException>(() => cc.WriteAndFlushAsync(
                    new DefaultAddressedEnvelope<ByteBuf, EndPoint>(
                            Unpooled.CopiedBuffer(message, Encoding.ASCII), unresolvedHost)))).SocketErrorCode);
            Assert.True(cc.IsOpen);
        }
        finally
        {
            await CloseChannel(cc);
            await CloseChannel(sc);
        }
    }

    private async Task TestSimpleSend0(Bootstrap sb, Bootstrap cb, ByteBuf buf, bool bindClient,
                                byte[] bytes, int count, WrapType wrapType)
    {
        IChannel sc = null;
        IChannel cc = null;

        try
        {
            cb.Handler(new NothingSentHandler());

            EndPoint sender;
            if (bindClient)
            {
                cc = await cb.BindAsync(NewSocketAddress());
                sender = cc.LocalAddress;
            }
            else
            {
                cb.Option(ChannelOption.DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION, true);
                cc = await cb.RegisterAsync();
                sender = null;
            }

            CountDownLatch latch = new CountDownLatch(count);
            StrongBox<Exception> errorRef = new StrongBox<Exception>();
            sc = await SetupServerChannel(sb, bytes, sender, latch, errorRef, false);

            EndPoint localAddr = sc.LocalAddress;
            EndPoint addr = localAddr is IPEndPoint ?
                    ConvertAnyAddress((IPEndPoint) sc.LocalAddress) : localAddr;

            bool received = false;
            for (int attempt = 1; attempt <= SEND_ATTEMPTS && !received; attempt++)
            {
                List<Task> futures = new List<Task>(count);
                for (int i = 0; i < count; i++)
                {
                    futures.Add(Write(cc, buf, addr, wrapType));
                }
                // release as we used buf.retain() before
                cc.Flush();

                foreach (Task future in futures)
                {
                    await future;
                }
                // UDP datagrams can be lost even on loopback, so retry a few times before giving up.
                received = await latch.Await(TimeSpan.FromSeconds(10));
            }
            if (!received)
            {
                Exception error = Volatile.Read(ref errorRef.Value);
                if (error != null)
                {
                    throw error;
                }
                Assert.Fail();
            }
        }
        finally
        {
            // release as we used buf.retain() before
            buf.Release();

            await CloseChannel(cc);
            await CloseChannel(sc);
        }
    }

    // CLR: the source's testSimpleSendWithConnect(sb, cb, buf, bytes, count) helper; see TestSimpleSendWithConnect0.
    private async Task TestSimpleSendWithConnect0(Bootstrap sb, Bootstrap cb, ByteBuf buf, byte[] bytes, int count)
    {
        try
        {
            foreach (WrapType type in Enum.GetValues<WrapType>())
            {
                await TestSimpleSendWithConnect0(sb, cb, buf.Retain(), bytes, count, type);
            }
        }
        finally
        {
            Assert.True(buf.Release());
        }
    }

    private async Task TestSimpleSendWithConnect0(Bootstrap sb, Bootstrap cb, ByteBuf buf, byte[] bytes, int count,
                                            WrapType wrapType)
    {
        IChannel sc = null;
        IChannel cc = null;
        try
        {
            CountDownLatch latch = new CountDownLatch(count);
            StrongBox<Exception> errorRef = new StrongBox<Exception>();
            CountDownLatch clientLatch = new CountDownLatch(count);
            StrongBox<Exception> clientErrorRef = new StrongBox<Exception>();
            cc = await SetupClientChannel(cb, bytes, clientLatch, clientErrorRef);
            sc = await SetupServerChannel(sb, bytes, cc.LocalAddress, latch, errorRef, true);

            EndPoint localAddr = sc.LocalAddress;
            EndPoint addr = localAddr is IPEndPoint ?
                    ConvertAnyAddress((IPEndPoint) sc.LocalAddress) : localAddr;
            await cc.ConnectAsync(addr);

            List<Task> futures = new List<Task>();
            bool received = false;
            for (int attempt = 1; attempt <= SEND_ATTEMPTS && !received; attempt++)
            {
                futures.Clear();
                for (int i = 0; i < count; i++)
                {
                    futures.Add(Write(cc, buf, wrapType));
                }
                cc.Flush();

                foreach (Task future in futures)
                {
                    await future;
                }

                // UDP datagrams (including the echo) can be lost even on loopback, so retry a few times
                // before giving up.
                received = await latch.Await(TimeSpan.FromSeconds(10)) &&
                        await clientLatch.Await(TimeSpan.FromSeconds(10));
            }
            if (!received)
            {
                if (latch.GetCount() != 0)
                {
                    Exception cause = Volatile.Read(ref errorRef.Value);
                    if (cause != null)
                    {
                        throw cause;
                    }
                    Assert.Fail();
                }
                // CLR: the braces scope this second cause local; C# rejects reusing the if block's local name in the
                // enclosing block (CS0136).
                {
                    Exception cause = Volatile.Read(ref clientErrorRef.Value);
                    if (cause != null)
                    {
                        throw cause;
                    }
                }
                Assert.Fail();
            }
            // CLR: supplemental check that neither handler reported an assertion failure for a delivered datagram.
            if (Volatile.Read(ref errorRef.Value) is { } serverError)
            {
                throw serverError;
            }
            if (Volatile.Read(ref clientErrorRef.Value) is { } clientError)
            {
                throw clientError;
            }
            Assert.True(IsConnected(cc));

            Assert.NotNull(cc.LocalAddress);
            Assert.NotNull(cc.RemoteAddress);
            // CLR: supplemental check that disconnect() keeps the bound local port.
            int localPort = ((IPEndPoint) cc.LocalAddress).Port;

            if (SupportDisconnect())
            {
                try
                {
                    // Test what happens when we call disconnect()
                    await cc.DisconnectAsync();
                }
                catch (Exception e)
                {
                    if (e is SocketException)
                    {
                        if (DisconnectMightFail((IDatagramChannel) cc))
                        {
                            return;
                        }
                    }
                    throw;
                }
                Assert.False(IsConnected(cc));
                Assert.NotNull(cc.LocalAddress);
                Assert.Equal(localPort, ((IPEndPoint) cc.LocalAddress).Port);
                Assert.Null(cc.RemoteAddress);

                // CLR: NioDatagramChannel fails a write without a recipient on a disconnected channel with
                // SocketError.NotConnected in place of java.nio.channels.NotYetConnectedException; the awaited failure
                // is the future's cause.
                Task future = cc.WriteAndFlushAsync(buf.Retain().Duplicate());
                Assert.Equal(SocketError.NotConnected,
                        (await Assert.ThrowsAsync<SocketException>(() => future)).SocketErrorCode);

                // Connect again and try to write.
                await cc.ConnectAsync(addr);

                Task f = Write(cc, buf, wrapType);
                cc.Flush();
                await f;
            }
        }
        finally
        {
            // release as we used buf.retain() before
            buf.Release();

            await CloseChannel(cc);
            await CloseChannel(sc);
        }
    }

    private static Task Write(IChannel cc, ByteBuf buf, WrapType wrapType)
    {
        switch (wrapType)
        {
            case WrapType.DUP:
                return cc.WriteAsync(buf.RetainedDuplicate());
            case WrapType.SLICE:
                return cc.WriteAsync(buf.RetainedSlice());
            case WrapType.READ_ONLY:
                return cc.WriteAsync(buf.Retain().AsReadOnly());
            case WrapType.NONE:
                return cc.WriteAsync(buf.Retain());
            default:
                // CLR: java.lang.Error has no BCL type; UnreachableException is "thrown when the program executes an
                // instruction that was thought to be unreachable"
                // (https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.unreachableexception).
                throw new UnreachableException("Unexpected wrap type: " + wrapType);
        }
    }

    [Fact(Timeout = 10000)]
    public async Task TestWriteOffsetBytebuf()
    {
        await Run(TestContext.Current, TestWriteOffsetBytebuf0);
    }

    // CLR: xUnit1024 forbids overloading the TestWriteOffsetBytebuf test name.
    private async Task TestWriteOffsetBytebuf0(Bootstrap sb, Bootstrap cb)
    {
        ByteBuf buf = Unpooled.DirectBuffer(BAD_PREFIX.Length + EXPECTED.Length);
        buf.WriteBytes(BAD_PREFIX);
        buf.WriteBytes(EXPECTED);
        buf.ReaderIndex = BAD_PREFIX.Length;
        try
        {
            foreach (WrapType type in Enum.GetValues<WrapType>())
            {
                await TestWriteOffsetBytebuf0(sb, cb, buf.Retain(), EXPECTED, type);
            }
            Assert.Equal(1, buf.RefCnt);
        }
        finally
        {
            buf.Release();
        }
    }

    private async Task TestWriteOffsetBytebuf0(Bootstrap sb, Bootstrap cb, ByteBuf buf, byte[] expected,
                                         WrapType wrapType)
    {
        // CLR: CompletableFuture<byte[]> is a TaskCompletionSource<byte[]>; complete() is TrySetResult and join() awaits
        // its Task.
        TaskCompletionSource<byte[]> received =
                new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        sb.Handler(new ReceivedBytesHandler(received));

        IChannel sc = null;
        IChannel cc = null;
        try
        {
            sc = await sb.BindAsync(NewSocketAddress());
            EndPoint serverAddress = sc.LocalAddress;
            if (serverAddress is IPEndPoint)
            {
                // Correctly ha
                serverAddress = ConvertAnyAddress((IPEndPoint) serverAddress);
            }
            cb.Handler(new NoOpHolderHandler());

            cc = await cb.ConnectAsync(serverAddress);
            Task wf = Write(cc, buf, wrapType);
            cc.Flush();
            await wf;
            byte[] actual = await received.Task;
            Assert.Equal(expected, actual);
        }
        finally
        {
            // release as we used buf.retain() before
            buf.Release();

            if (cc != null)
            {
                await cc.CloseAsync();
            }
            if (sc != null)
            {
                await sc.CloseAsync();
            }
        }
    }

    protected abstract bool IsConnected(IChannel channel);

    // CLR: the Channel-returning setup methods return the Task of the bind; AtomicReference<Throwable> is a
    // StrongBox<Exception>, which "Holds a reference to a value"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.strongbox-1): compareAndSet(null,
    // cause) is Interlocked.CompareExchange on its Value field and get() is Volatile.Read.
    protected abstract Task<IChannel> SetupClientChannel(Bootstrap cb, byte[] bytes, CountDownLatch latch,
                                                  StrongBox<Exception> errorRef);

    protected abstract Task<IChannel> SetupServerChannel(Bootstrap sb, byte[] bytes, EndPoint sender,
                                                  CountDownLatch latch, StrongBox<Exception> errorRef,
                                                  bool echo);

    protected abstract bool SupportDisconnect();

    protected virtual bool DisconnectMightFail(IDatagramChannel channel)
    {
        return false;
    }

    protected abstract Task Write(IChannel cc, ByteBuf buf, EndPoint remote, WrapType wrapType);

    protected static async Task CloseChannel(IChannel channel)
    {
        if (channel != null)
        {
            await channel.CloseAsync();
        }
    }

    // CLR: java.net.InetSocketAddress is IPEndPoint, and an Inet6Address is an IPAddress whose AddressFamily is
    // InterNetworkV6.
    protected virtual IPEndPoint ConvertAnyAddress(IPEndPoint serverAddress)
    {
        IPAddress addr = serverAddress.Address;
        if (IsAnyLocalAddress(addr))
        {
            if (addr.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return new IPEndPoint(NetUtil.LOCALHOST6, serverAddress.Port);
            }
            return new IPEndPoint(NetUtil.LOCALHOST4, serverAddress.Port);
        }
        return serverAddress;
    }

    // CLR: InetAddress.isAnyLocalAddress() checks "if the InetAddress is a wildcard address"
    // (https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/net/InetAddress.html); the BCL wildcard
    // addresses are IPAddress.Any and IPAddress.IPv6Any, which "indicate that a Socket must listen for client activity
    // on all network interfaces" (https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.ipv6any).
    protected static bool IsAnyLocalAddress(IPAddress addr)
    {
        return addr.Equals(IPAddress.Any) || addr.Equals(IPAddress.IPv6Any);
    }

    // CLR: java.util.concurrent.CountDownLatch. Its countDown() does nothing at zero ("If the current count equals zero
    // then nothing happens",
    // https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/concurrent/CountDownLatch.html), while
    // CountdownEvent.Signal() throws InvalidOperationException when "The current instance is already set"
    // (https://learn.microsoft.com/en-us/dotnet/api/system.threading.countdownevent.signal), and a resend after a lost
    // datagram can count past zero. This latch keeps the JDK semantics; await(timeout, unit) returns a Task<bool>, true
    // "if the count reached zero and false if the waiting time elapsed", so the asynchronous body does not block.
    // It is protected internal because DatagramConnectedWriteExceptionSourceTest uses it too.
    protected internal sealed class CountDownLatch
    {
        private readonly TaskCompletionSource zero = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int count;

        public CountDownLatch(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            this.count = count;
            if (count == 0)
            {
                zero.TrySetResult();
            }
        }

        public void CountDown()
        {
            int current;
            do
            {
                current = Volatile.Read(ref count);
                if (current == 0)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref count, current - 1, current) != current);
            if (current == 1)
            {
                zero.TrySetResult();
            }
        }

        public long GetCount()
        {
            return Volatile.Read(ref count);
        }

        public async Task<bool> Await(TimeSpan timeout)
        {
            try
            {
                await zero.Task.WaitAsync(timeout, TestContext.Current.CancellationToken);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }
    }

    // CLR: C# has no anonymous classes, so the source's anonymous handlers are the private classes below, and its
    // anonymous ChannelInitializer<Channel> is an ActionChannelInitializer<IChannel> whose delegate is the initChannel
    // body ("Delegates are used to pass methods as arguments to other methods",
    // https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/delegates/).
    private sealed class ReleaseSemaphoreHandler(SemaphoreSlim semaphore) : SimpleChannelInboundHandler<DatagramPacket>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, DatagramPacket msg)
        {
            semaphore.Release();
        }
    }

    private sealed class DropHandler : SimpleChannelInboundHandler<DatagramPacket>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, DatagramPacket msg)
        {
            // Just drop
        }
    }

    private sealed class NothingSentHandler : SimpleChannelInboundHandler<object>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, object msgs)
        {
            // Nothing will be sent.
        }
    }

    private sealed class ReceivedBytesHandler(TaskCompletionSource<byte[]> received)
            : SimpleChannelInboundHandler<IByteBufHolder>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, IByteBufHolder packet)
        {
            ByteBuf content = packet.Content;
            byte[] bytes = new byte[content.ReadableBytes];
            content.GetBytes(content.ReaderIndex, bytes);
            received.TrySetResult(bytes);
        }
    }

    private sealed class NoOpHolderHandler : SimpleChannelInboundHandler<IByteBufHolder>
    {
        protected override void ChannelRead0(IChannelHandlerContext ctx, IByteBufHolder msg)
        {
            // no-op
        }
    }
}
#pragma warning restore CS0612
#pragma warning restore xUnit1051
#pragma warning restore xUnit1069
