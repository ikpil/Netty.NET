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

using System;
using System.Net;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Dns;

/**
 * A {@link DnsQuery} implementation for UDP/IP.
 */
// CLR: AddressedEnvelope<DatagramDnsQuery, InetSocketAddress> is IAddressedEnvelope<DatagramDnsQuery, IPEndPoint>, whose
// content(), sender() and recipient() are the Content, Sender and Recipient properties of the Transport port
// (src/Netty.NET.Transport/Channels/IAddressedEnvelope.cs; InetSocketAddress is IPEndPoint as in DatagramPacket). The
// Java overrides narrow the return type with C# 9 covariant overrides: "an overriding method can declare a more
// derived return type the method it overrides"
// (https://learn.microsoft.com/en-us/dotnet/standard/generics/covariance-and-contravariance). The NullPointerException
// for a missing address is ArgumentNullException, as in DefaultAddressedEnvelope.
public class DatagramDnsQuery : DefaultDnsQuery, IAddressedEnvelope<DatagramDnsQuery, IPEndPoint>
{

    private readonly IPEndPoint sender;
    private readonly IPEndPoint recipient;

    /**
     * Creates a new instance with the {@link DnsOpCode#QUERY} {@code opCode}.
     *
     * @param sender the address of the sender
     * @param recipient the address of the recipient
     * @param id the {@code ID} of the DNS query
     */
    public DatagramDnsQuery(
            IPEndPoint sender, IPEndPoint recipient, int id) : this(sender, recipient, id, DnsOpCode.QUERY)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param sender the address of the sender
     * @param recipient the address of the recipient
     * @param id the {@code ID} of the DNS query
     * @param opCode the {@code opCode} of the DNS query
     */
    public DatagramDnsQuery(
            IPEndPoint sender, IPEndPoint recipient, int id, DnsOpCode opCode) : base(id, opCode)
    {

        if (recipient == null && sender == null)
        {
            throw new ArgumentNullException("recipient and sender");
        }

        this.sender = sender;
        this.recipient = recipient;
    }

    public virtual DatagramDnsQuery Content => this;

    public virtual IPEndPoint Sender => sender;

    public virtual IPEndPoint Recipient => recipient;

    public override DatagramDnsQuery SetId(int id)
    {
        return (DatagramDnsQuery) base.SetId(id);
    }

    public override DatagramDnsQuery SetOpCode(DnsOpCode opCode)
    {
        return (DatagramDnsQuery) base.SetOpCode(opCode);
    }

    public override DatagramDnsQuery SetRecursionDesired(bool recursionDesired)
    {
        return (DatagramDnsQuery) base.SetRecursionDesired(recursionDesired);
    }

    public override DatagramDnsQuery SetZ(int z)
    {
        return (DatagramDnsQuery) base.SetZ(z);
    }

    public override DatagramDnsQuery SetRecord(DnsSection section, IDnsRecord record)
    {
        return (DatagramDnsQuery) base.SetRecord(section, record);
    }

    public override DatagramDnsQuery AddRecord(DnsSection section, IDnsRecord record)
    {
        return (DatagramDnsQuery) base.AddRecord(section, record);
    }

    public override DatagramDnsQuery AddRecord(DnsSection section, int index, IDnsRecord record)
    {
        return (DatagramDnsQuery) base.AddRecord(section, index, record);
    }

    public override DatagramDnsQuery Clear(DnsSection section)
    {
        return (DatagramDnsQuery) base.Clear(section);
    }

    public override DatagramDnsQuery Clear()
    {
        return (DatagramDnsQuery) base.Clear();
    }

    public override DatagramDnsQuery Touch()
    {
        return (DatagramDnsQuery) base.Touch();
    }

    public override DatagramDnsQuery Touch(object hint)
    {
        return (DatagramDnsQuery) base.Touch(hint);
    }

    public override DatagramDnsQuery Retain()
    {
        return (DatagramDnsQuery) base.Retain();
    }

    public override DatagramDnsQuery Retain(int increment)
    {
        return (DatagramDnsQuery) base.Retain(increment);
    }

    // CLR: AddressedEnvelope<?, SocketAddress> is IAddressedEnvelope<object, EndPoint>, which the covariant
    // IAddressedEnvelope<out T, out TAddress> matches for every reference content type
    // (https://learn.microsoft.com/en-us/dotnet/standard/generics/covariance-and-contravariance).
    public override bool Equals(object obj)
    {
        if (this == obj)
        {
            return true;
        }

        if (!base.Equals(obj))
        {
            return false;
        }

        if (!(obj is IAddressedEnvelope<object, EndPoint>))
        {
            return false;
        }

        IAddressedEnvelope<object, EndPoint> that = (IAddressedEnvelope<object, EndPoint>) obj;
        if (Sender == null)
        {
            if (that.Sender != null)
            {
                return false;
            }
        }
        else if (!Sender.Equals(that.Sender))
        {
            return false;
        }

        if (Recipient == null)
        {
            if (that.Recipient != null)
            {
                return false;
            }
        }
        else if (!Recipient.Equals(that.Recipient))
        {
            return false;
        }

        return true;
    }

    public override int GetHashCode()
    {
        int hashCode = base.GetHashCode();
        if (Sender != null)
        {
            hashCode = hashCode * 31 + Sender.GetHashCode();
        }
        if (Recipient != null)
        {
            hashCode = hashCode * 31 + Recipient.GetHashCode();
        }
        return hashCode;
    }
}
