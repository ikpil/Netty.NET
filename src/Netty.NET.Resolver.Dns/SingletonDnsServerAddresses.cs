/*
 * Copyright 2015 The Netty Project
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

namespace Netty.NET.Resolver.Dns;

// CLR: InetSocketAddress is EndPoint and prints as EndPoint.ToString() (docs/handler-porting.md Addresses). The
// anonymous DnsServerAddressStream is the named nested SingletonDnsServerAddressStream, which takes the enclosing
// instance (SingletonDnsServerAddresses.this); a C# field initializer cannot refer to this, so the constructor creates it.
internal sealed class SingletonDnsServerAddresses : DnsServerAddresses
{

    private readonly EndPoint address;

    private readonly IDnsServerAddressStream stream;

    private sealed class SingletonDnsServerAddressStream : IDnsServerAddressStream
    {
        private readonly SingletonDnsServerAddresses parent;

        internal SingletonDnsServerAddressStream(SingletonDnsServerAddresses parent)
        {
            this.parent = parent;
        }

        public EndPoint Next()
        {
            return parent.address;
        }

        public int Size()
        {
            return 1;
        }

        public IDnsServerAddressStream Duplicate()
        {
            return this;
        }

        public override string ToString()
        {
            return parent.ToString();
        }
    }

    internal SingletonDnsServerAddresses(EndPoint address)
    {
        stream = new SingletonDnsServerAddressStream(this);
        this.address = address;
    }

    public override IDnsServerAddressStream Stream()
    {
        return stream;
    }

    public override string ToString()
    {
        return "singleton(" + address + ")";
    }
}
