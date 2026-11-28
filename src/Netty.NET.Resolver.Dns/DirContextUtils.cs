/*
 * Copyright 2018 The Netty Project
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
using System.Net;
using System.Net.NetworkInformation;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Resolver.Dns;

// CLR: the final class with a private constructor is a static class. JNDI (javax.naming, the java.naming module that
// module-info.yml requires) has no CLR counterpart. The source asks the JNDI DNS provider for "dns://", for which "the
// provider will attempt to determine and use the server(s) configured for the underlying platform" and sets
// java.naming.provider.url to "a space-separated list of URLs constructed using the server(s)"
// (https://docs.oracle.com/javase/8/docs/technotes/guides/jndi/jndi-dns.html). The CLR reads the platform's servers from
// the BCL network interface API: NetworkInterface.GetAllNetworkInterfaces() "Returns objects that describe the network
// interfaces on the local computer"
// (https://learn.microsoft.com/en-us/dotnet/api/system.net.networkinformation.networkinterface.getallnetworkinterfaces?view=net-10.0),
// and IPInterfaceProperties.DnsAddresses "Gets the addresses of Domain Name System (DNS) servers for this interface"
// (https://learn.microsoft.com/en-us/dotnet/api/system.net.networkinformation.ipinterfaceproperties.dnsaddresses?view=net-10.0).
// Only interfaces whose OperationalStatus is Up ("it can transmit data packets",
// https://learn.microsoft.com/en-us/dotnet/api/system.net.networkinformation.operationalstatus?view=net-10.0) and that are
// not a loopback adapter ("no traffic is sent over the wire",
// https://learn.microsoft.com/en-us/dotnet/api/system.net.networkinformation.networkinterfacetype?view=net-10.0) are read
// (measured on this Windows host: the loopback pseudo-interface reports the site-local placeholders
// fec0:0:0:ffff::1..3), and a server configured on several interfaces is added once, as one platform list.
// Each address is an IPEndPoint on defaultPort, the port of a URL without one, so the URI parsing of the JNDI URLs has no
// counterpart; its comments are recorded in the manifest (jvm-only-replaced). The failure log names the BCL API instead
// of InitialDirContext.
internal static class DirContextUtils
{
    private static readonly IInternalLogger logger =
            InternalLoggerFactory.GetInstance(typeof(DirContextUtils));

    internal static void AddNameServers(List<EndPoint> defaultNameServers, int defaultPort)
    {
        try
        {
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                        networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (IPAddress server in networkInterface.GetIPProperties().DnsAddresses)
                {
                    EndPoint address = new IPEndPoint(server, defaultPort);
                    if (!defaultNameServers.Contains(address))
                    {
                        defaultNameServers.Add(address);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.Debug("Unable to obtain nameservers via NetworkInterface", ex);
        }
    }
}
