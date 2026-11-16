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

using System.Collections.Generic;
using System.Net;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels.Sockets;
using static Netty.NET.Transport.Tests.TestsuitePermutation;

namespace Netty.NET.Transport.Tests;

// CLR: counterpart of the domain-socket part of transport-native-epoll's EpollSocketTestPermutation (domainSocket(),
// serverDomainSocket(), clientDomainSocket() and newDomainSocketAddress()), which the Epoll*DomainSocket*Test fixtures
// return from newFactories()/newSocketAddress(). Epoll/KQueue native transports are not ported; this port's
// AF_UNIX stream channels are NioServerDomainSocketChannel/NioDomainSocketChannel on the NIO group, so the
// DomainSocket*SourceTest fixtures run the same testsuite bodies through these permutations.
internal sealed class DomainSocketTestPermutation : SocketTestPermutation
{
    internal static new readonly DomainSocketTestPermutation INSTANCE = new DomainSocketTestPermutation();

    public List<IBootstrapComboFactory<ServerBootstrap, Bootstrap>> DomainSocket()
    {
        return Combo(ServerDomainSocket(), ClientDomainSocket());
    }

    public List<IBootstrapFactory<ServerBootstrap>> ServerDomainSocket()
    {
        return new List<IBootstrapFactory<ServerBootstrap>>
        {
            new BootstrapFactory<ServerBootstrap>(() => new ServerBootstrap().Group(NIO_GROUP)
                    .Channel<NioServerDomainSocketChannel>()),
        };
    }

    public List<IBootstrapFactory<Bootstrap>> ClientDomainSocket()
    {
        return new List<IBootstrapFactory<Bootstrap>>
        {
            new BootstrapFactory<Bootstrap>(() => new Bootstrap().Group(NIO_GROUP)
                    .Channel<NioDomainSocketChannel>()),
        };
    }

    // CLR: UnixTestUtils.newDomainSocketAddress() creates a unique path in the temporary directory, as
    // DomainSocketSourceTest.NewAddress() does.
    public static EndPoint NewDomainSocketAddress()
    {
        return DomainSocketSourceTest.NewAddress();
    }
}
