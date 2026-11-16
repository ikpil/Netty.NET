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
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Nio;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Tests.TestsuitePermutation;

namespace Netty.NET.Transport.Tests;

public class SocketTestPermutation
{
    internal static readonly string BAD_HOST = SystemPropertyUtil.Get("io.netty.testsuite.badHost", "198.51.100.254");
    internal static readonly int BAD_PORT = SystemPropertyUtil.GetInt("io.netty.testsuite.badPort", 65535);
    // CLR: INCLUDE_OIO (io.netty.testsuite.includeOio, default false) is not read. Every OIO type the source would
    // add (OioEventLoopGroup, OioServerSocketChannel, OioSocketChannel, OioDatagramChannel) is a deprecated-not-ported
    // entry of the transport manifest (owner decision 2026-11-16), so no OIO permutation exists to include.

    // See /etc/services
    public const int UNASSIGNED_PORT = 4;

    // CLR: C# runs static field initializers before the static constructor body, so INSTANCE is assigned here,
    // after the debug lines, to keep the source's static initialization order.
    static SocketTestPermutation()
    {
        IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(SocketConnectionAttemptSourceTest));
        logger.Debug("-Dio.netty.testsuite.badHost: {}", BAD_HOST);
        logger.Debug("-Dio.netty.testsuite.badPort: {}", BAD_PORT);
        INSTANCE = new SocketTestPermutation();
    }

    internal static readonly SocketTestPermutation INSTANCE;

    protected const int NUM_THREADS = 4;

    // CLR: only the OIO factories used this value; it stays for source parity although no OIO permutation is ported.
    protected const int OIO_SO_TIMEOUT = 10;  // Use short timeout for faster runs.

    // CLR: DefaultThreadFactory(name, daemon: true) creates background threads, so the never-shut-down shared group
    // does not keep the test host alive, as the source's daemon threads do not keep the JVM alive.
    protected readonly IEventLoopGroup NIO_GROUP = new MultiThreadIoEventLoopGroup(
            NUM_THREADS, new DefaultThreadFactory("testsuite-nio", true), NioIoHandler.NewFactory());
    // CLR: OIO_GROUP (new OioEventLoopGroup(Integer.MAX_VALUE, new DefaultThreadFactory("testsuite-oio", true))) is
    // not created: OioEventLoopGroup is deprecated-not-ported.

    protected List<IBootstrapComboFactory<A, B>> Combo<A, B>(List<IBootstrapFactory<A>> sbfs, List<IBootstrapFactory<B>> cbfs)
        where A : class where B : class
    {

        List<IBootstrapComboFactory<A, B>> list = new List<IBootstrapComboFactory<A, B>>();

        // Populate the combinations
        foreach (IBootstrapFactory<A> sbf in sbfs)
        {
            foreach (IBootstrapFactory<B> cbf in cbfs)
            {
                IBootstrapFactory<A> sbf0 = sbf;
                IBootstrapFactory<B> cbf0 = cbf;
                list.Add(new BootstrapComboFactory<A, B>(
                        () => sbf0.NewInstance(),
                        () => cbf0.NewInstance()));
            }
        }

        return list;
    }

    public virtual List<IBootstrapComboFactory<ServerBootstrap, Bootstrap>> Socket()
    {
        // Make the list of ServerBootstrap factories.
        List<IBootstrapFactory<ServerBootstrap>> sbfs = ServerSocket();

        // Make the list of Bootstrap factories.
        List<IBootstrapFactory<Bootstrap>> cbfs = ClientSocket();

        // Populate the combinations
        List<IBootstrapComboFactory<ServerBootstrap, Bootstrap>> list = Combo(sbfs, cbfs);

        // CLR: the source only removes the last combination when INCLUDE_OIO added the OIO factories; no OIO
        // factory is ported, so there is nothing to remove:
        // Remove the OIO-OIO case which often leads to a dead lock by its nature.

        return list;
    }

    public virtual List<IBootstrapComboFactory<ServerBootstrap, Bootstrap>> SocketWithFastOpen()
    {
        // Make the list of ServerBootstrap factories.
        List<IBootstrapFactory<ServerBootstrap>> sbfs = ServerSocket();

        // Make the list of Bootstrap factories.
        List<IBootstrapFactory<Bootstrap>> cbfs = ClientSocketWithFastOpen();

        // Populate the combinations
        List<IBootstrapComboFactory<ServerBootstrap, Bootstrap>> list = Combo(sbfs, cbfs);

        // CLR: as in Socket(), the OIO-OIO combination is never added, so it is never removed:
        // Remove the OIO-OIO case which often leads to a dead lock by its nature.

        return list;
    }

    public virtual List<IBootstrapComboFactory<Bootstrap, Bootstrap>> Datagram(SocketProtocolFamily family)
    {
        // Make the list of Bootstrap factories.
        List<IBootstrapFactory<Bootstrap>> bfs = new List<IBootstrapFactory<Bootstrap>>();

        // CLR: the source's anonymous ChannelFactory also overrides toString() to "NioDatagramChannel.class"; this
        // port's Bootstrap.ChannelFactory takes a Func<IChannel> delegate, which has no toString() override.
        bfs.Add(new BootstrapFactory<Bootstrap>(() =>
                new Bootstrap().Group(NIO_GROUP).ChannelFactory(() => new NioDatagramChannel(family))));
        // CLR: the INCLUDE_OIO OioDatagramChannel factory is not ported (deprecated-not-ported).

        // Populare the combinations.
        return Combo(bfs, bfs);
    }

    public virtual List<IBootstrapFactory<ServerBootstrap>> ServerSocket()
    {
        List<IBootstrapFactory<ServerBootstrap>> factories = new List<IBootstrapFactory<ServerBootstrap>>();
        factories.Add(new BootstrapFactory<ServerBootstrap>(() =>
                new ServerBootstrap().Group(NIO_GROUP)
                        .Channel<NioServerSocketChannel>()));
        // CLR: the INCLUDE_OIO OioServerSocketChannel factory is not ported (deprecated-not-ported).

        return factories;
    }

    public virtual List<IBootstrapFactory<Bootstrap>> ClientSocket()
    {
        List<IBootstrapFactory<Bootstrap>> factories = new List<IBootstrapFactory<Bootstrap>>();
        factories.Add(new BootstrapFactory<Bootstrap>(() =>
                new Bootstrap().Group(NIO_GROUP).Channel<NioSocketChannel>()));
        // CLR: the INCLUDE_OIO OioSocketChannel factory is not ported (deprecated-not-ported).
        return factories;
    }

    public virtual List<IBootstrapFactory<Bootstrap>> ClientSocketWithFastOpen()
    {
        return ClientSocket();
    }

    public virtual List<IBootstrapFactory<Bootstrap>> DatagramSocket()
    {
        List<IBootstrapFactory<Bootstrap>> factories = new List<IBootstrapFactory<Bootstrap>>();
        // CLR: the source's channel(NioDatagramChannel.class) opens SelectorProvider.openDatagramChannel(), whose
        // OpenJDK implementation selects "Net.isIPv6Available() ? StandardProtocolFamily.INET6 :
        // StandardProtocolFamily.INET" (sun.nio.ch.DatagramChannelImpl, https://github.com/openjdk/jdk21u/blob/master/
        // src/java.base/share/classes/sun/nio/ch/DatagramChannelImpl.java). This port's parameterless
        // NioDatagramChannel() opens IPv4, so the factory requests the JDK default family explicitly; the INET6
        // constructor enables IPv4-mapped traffic as OpenJDK does.
        factories.Add(new BootstrapFactory<Bootstrap>(() =>
                new Bootstrap().Group(NIO_GROUP).ChannelFactory(() => new NioDatagramChannel(
                        System.Net.Sockets.Socket.OSSupportsIPv6 ? SocketProtocolFamily.Inet6 : SocketProtocolFamily.Inet))));
        // CLR: the INCLUDE_OIO OioDatagramChannel factory is not ported (deprecated-not-ported).
         return factories;
    }

    // CLR: C# has no anonymous classes that implement an interface, so the source's anonymous BootstrapFactory and
    // BootstrapComboFactory instances are these delegate-backed implementations; the lambdas carry the bodies of the
    // source's newInstance()/newServerInstance()/newClientInstance() overrides
    // (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/delegates/).
    protected sealed class BootstrapFactory<T>(Func<T> newInstance) : IBootstrapFactory<T> where T : class
    {
        public T NewInstance() => newInstance();
    }

    protected sealed class BootstrapComboFactory<A, B>(Func<A> newServerInstance, Func<B> newClientInstance)
        : IBootstrapComboFactory<A, B> where A : class where B : class
    {
        public A NewServerInstance() => newServerInstance();

        public B NewClientInstance() => newClientInstance();
    }
}
