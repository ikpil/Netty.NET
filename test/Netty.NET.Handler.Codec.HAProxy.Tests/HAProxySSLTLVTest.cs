/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.HAProxy.Tests;

// CLR: Collections.<HAProxyTLV>emptyList() is an empty array; the four-argument constructor is internal and reached
// through InternalsVisibleTo as the Java test reaches it through package access.
public class HAProxySSLTLVTest
{

    [Fact]
    public void TestClientBitmask()
    {

        // 0b0000_0111
        const byte allClientsEnabled = 0x7;
        HAProxySSLTLV allClientsEnabledTLV =
                new HAProxySSLTLV(0, allClientsEnabled, Array.Empty<HAProxyTLV>(), Unpooled.Buffer());

        Assert.True(allClientsEnabledTLV.IsPP2ClientCertConn());
        Assert.True(allClientsEnabledTLV.IsPP2ClientSSL());
        Assert.True(allClientsEnabledTLV.IsPP2ClientCertSess());

        Assert.True(allClientsEnabledTLV.Release());

        // 0b0000_0101
        const byte clientSSLandClientCertSessEnabled = 0x5;

        HAProxySSLTLV clientSSLandClientCertSessTLV =
                new HAProxySSLTLV(0, clientSSLandClientCertSessEnabled, Array.Empty<HAProxyTLV>(),
                                  Unpooled.Buffer());

        Assert.False(clientSSLandClientCertSessTLV.IsPP2ClientCertConn());
        Assert.True(clientSSLandClientCertSessTLV.IsPP2ClientSSL());
        Assert.True(clientSSLandClientCertSessTLV.IsPP2ClientCertSess());

        Assert.True(clientSSLandClientCertSessTLV.Release());
        // 0b0000_0000
        const byte noClientEnabled = 0x0;

        HAProxySSLTLV noClientTlv =
                new HAProxySSLTLV(0, noClientEnabled, Array.Empty<HAProxyTLV>(),
                                  Unpooled.Buffer());

        Assert.False(noClientTlv.IsPP2ClientCertConn());
        Assert.False(noClientTlv.IsPP2ClientSSL());
        Assert.False(noClientTlv.IsPP2ClientCertSess());

        Assert.True(noClientTlv.Release());
    }
}
