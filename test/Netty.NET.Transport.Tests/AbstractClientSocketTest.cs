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

using System.Collections.Generic;
using System.Net;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Transport.Tests;

public abstract class AbstractClientSocketTest : AbstractTestsuiteTest<Bootstrap>
{
    protected override List<TestsuitePermutation.IBootstrapFactory<Bootstrap>> NewFactories()
    {
        return SocketTestPermutation.INSTANCE.ClientSocket();
    }

    protected override void Configure(Bootstrap bootstrap, IByteBufAllocator allocator)
    {
        bootstrap.Option(ChannelOption.ALLOCATOR, allocator);
    }

    // CLR: java.net.SocketAddress/InetSocketAddress are System.Net.EndPoint/IPEndPoint.
    protected virtual EndPoint NewSocketAddress()
    {
        return new IPEndPoint(NetUtil.LOCALHOST, 0);
    }
}
