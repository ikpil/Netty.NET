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
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

public class DatagramLinuxConfigSourceTest
{
    [Fact]
    public void TestIpFreeBind()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("IP_FREEBIND requires Linux.");
        var channel = new DatagramChannel();
        try
        {
            Assert.True(channel.Config.SetOption(ChannelOption.IP_FREEBIND, true));
            Assert.True(channel.Config.TryGetOption(ChannelOption.IP_FREEBIND, out bool freeBind));
            Assert.True(freeBind);
        }
        finally { channel.CloseForcibly(); }
    }
}
