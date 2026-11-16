/*
 * Copyright 2020 The Netty Project
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

using System.Net;
using Netty.NET.Transport.Channels.Sockets;

namespace Netty.NET.Transport.Tests;

#pragma warning disable CS0618 // The original fixture exercises the deprecated InternetProtocolFamily.
public class InternetProtocolFamilyTest
{
    // CLR: Common replaces NetUtil.LOCALHOST4/LOCALHOST6 with IPAddress.Loopback/IPv6Loopback.
    [Fact]
    public void Ipv4ShouldHaveLocalhostOfIpV4()
    {
        Assert.Equal(IPAddress.Loopback, InternetProtocolFamily.IPv4.Localhost());
    }

    [Fact]
    public void Ipv6ShouldHaveLocalhostOfIpV6()
    {
        Assert.Equal(IPAddress.IPv6Loopback, InternetProtocolFamily.IPv6.Localhost());
    }
}
