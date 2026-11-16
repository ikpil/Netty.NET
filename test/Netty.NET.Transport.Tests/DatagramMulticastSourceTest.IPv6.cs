/*
 * Copyright 2019 The Netty Project
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

using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Transport.Bootstrapping;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public class DatagramMulticastIPv6SourceTest : DatagramMulticastSourceTest
{
    protected override async Task TestMulticast0(Bootstrap sb, Bootstrap cb)
    {
        // Not works on windows atm.
        // See https://github.com/netty/netty/issues/11285
        // CLR: assumeFalse is xUnit v3's Assert.SkipWhen, which "will skip the test if condition is true"
        // (https://xunit.net/docs/getting-started/v3/whats-new).
        Assert.SkipWhen(PlatformDependent.IsWindows(), "assumeFalse(PlatformDependent.isWindows())");
        await base.TestMulticast0(sb, cb);
    }

    protected override SocketProtocolFamily SocketProtocolFamily()
    {
        return Channels.Sockets.SocketProtocolFamily.Inet6;
    }
}
