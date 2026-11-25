/*
 * Copyright 2017 The Netty Project
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

using Netty.NET.Common.Internal;

namespace Netty.NET.Resolver.Dns;

/**
 * A {@link DnsServerAddressStreamProvider} which is backed by a single {@link DnsServerAddresses}.
 */
// CLR: the package-private abstract class is a public abstract class with an internal constructor, because its public
// sealed subclasses (SequentialDnsServerAddressStreamProvider, SingletonDnsServerAddressStreamProvider) need a base class
// at least as accessible as themselves (CS0060, the package-access rule of docs/codec-http-porting.md); code outside the
// assembly still cannot derive from it. The final
// nameServerAddressStream is a non-virtual implementation of the interface method.
public abstract class UniSequentialDnsServerAddressStreamProvider : IDnsServerAddressStreamProvider
{
    private readonly DnsServerAddresses addresses;

    internal UniSequentialDnsServerAddressStreamProvider(DnsServerAddresses addresses)
    {
        this.addresses = ObjectUtil.CheckNotNull(addresses, "addresses");
    }

    public IDnsServerAddressStream NameServerAddressStream(string hostname)
    {
        return addresses.Stream();
    }
}
