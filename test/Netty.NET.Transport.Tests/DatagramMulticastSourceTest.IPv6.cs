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

using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Netty.NET.Transport.Tests;

public partial class DatagramMulticastSourceTest
{
    private static Task RunIPv6MulticastAsync(bool adaptive)
    {
        // Not works on windows atm.
        // See https://github.com/netty/netty/issues/11285
        if (OperatingSystem.IsWindows()) Assert.Skip("The original IPv6 multicast fixture excludes Windows.");
        return RunMulticastAsync(adaptive, AddressFamily.InterNetworkV6);
    }
}
