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
 * A {@link DnsResponse} implementation for UDP/IP.
 */
// CLR: AddressedEnvelope<DatagramDnsResponse, InetSocketAddress> is IAddressedEnvelope<DatagramDnsResponse, IPEndPoint>,
// whose content(), sender() and recipient() are the Content, Sender and Recipient properties of the Transport port
// (src/Netty.NET.Transport/Channels/IAddressedEnvelope.cs; InetSocketAddress is IPEndPoint as in DatagramPacket). The
// Java overrides narrow the return type with C# 9 covariant overrides: "an overriding method can declare a more
// derived return type the method it overrides"
// (https://learn.microsoft.com/en-us/dotnet/standard/generics/covariance-and-contravariance). The NullPointerException
// for a missing address is ArgumentNullException, as in DefaultAddressedEnvelope.
public class DatagramDnsResponse : DefaultDnsResponse, IAddressedEnvelope<DatagramDnsResponse, IPEndPoint>
{

    private readonly IPEndPoint sender;
    private readonly IPEndPoint recipient;

    /**
     * Creates a new instance with the {@link DnsOpCode#QUERY} {@code opCode} and
     * the {@link DnsResponseCode#NOERROR} {@code RCODE}.
     *
     * @param sender the address of the sender
     * @param recipient the address of the recipient
     * @param id the {@code ID} of the DNS response
     */
    public DatagramDnsResponse(IPEndPoint sender, IPEndPoint recipient, int id) :
        this(sender, recipient, id, DnsOpCode.QUERY, DnsResponseCode.NOERROR)
    {
    }

    /**
     * Creates a new instance with the {@link DnsResponseCode#NOERROR} responseCode.
     *
     * @param sender the address of the sender
     * @param recipient the address of the recipient
     * @param id the {@code ID} of the DNS response
     * @param opCode the {@code opCode} of the DNS response
     */
    public DatagramDnsResponse(IPEndPoint sender, IPEndPoint recipient, int id, DnsOpCode opCode) :
        this(sender, recipient, id, opCode, DnsResponseCode.NOERROR)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param sender the address of the sender
     * @param recipient the address of the recipient
     * @param id the {@code ID} of the DNS response
     * @param opCode the {@code opCode} of the DNS response
     * @param responseCode the {@code RCODE} of the DNS response
     */
    public DatagramDnsResponse(
            IPEndPoint sender, IPEndPoint recipient,
            int id, DnsOpCode opCode, DnsResponseCode responseCode) : base(id, opCode, responseCode)
    {

        if (recipient == null && sender == null)
        {
            throw new ArgumentNullException("recipient and sender");
        }

        this.sender = sender;
        this.recipient = recipient;
    }

    public virtual DatagramDnsResponse Content => this;

    public virtual IPEndPoint Sender => sender;

    public virtual IPEndPoint Recipient => recipient;

    public override DatagramDnsResponse SetAuthoritativeAnswer(bool authoritativeAnswer)
    {
        return (DatagramDnsResponse) base.SetAuthoritativeAnswer(authoritativeAnswer);
    }

    public override DatagramDnsResponse SetTruncated(bool truncated)
    {
        return (DatagramDnsResponse) base.SetTruncated(truncated);
    }

    public override DatagramDnsResponse SetRecursionAvailable(bool recursionAvailable)
    {
        return (DatagramDnsResponse) base.SetRecursionAvailable(recursionAvailable);
    }

    public override DatagramDnsResponse SetCode(DnsResponseCode code)
    {
        return (DatagramDnsResponse) base.SetCode(code);
    }

    public override DatagramDnsResponse SetId(int id)
    {
        return (DatagramDnsResponse) base.SetId(id);
    }

    public override DatagramDnsResponse SetOpCode(DnsOpCode opCode)
    {
        return (DatagramDnsResponse) base.SetOpCode(opCode);
    }

    public override DatagramDnsResponse SetRecursionDesired(bool recursionDesired)
    {
        return (DatagramDnsResponse) base.SetRecursionDesired(recursionDesired);
    }

    public override DatagramDnsResponse SetZ(int z)
    {
        return (DatagramDnsResponse) base.SetZ(z);
    }

    public override DatagramDnsResponse SetRecord(DnsSection section, IDnsRecord record)
    {
        return (DatagramDnsResponse) base.SetRecord(section, record);
    }

    public override DatagramDnsResponse AddRecord(DnsSection section, IDnsRecord record)
    {
        return (DatagramDnsResponse) base.AddRecord(section, record);
    }

    public override DatagramDnsResponse AddRecord(DnsSection section, int index, IDnsRecord record)
    {
        return (DatagramDnsResponse) base.AddRecord(section, index, record);
    }

    public override DatagramDnsResponse Clear(DnsSection section)
    {
        return (DatagramDnsResponse) base.Clear(section);
    }

    public override DatagramDnsResponse Clear()
    {
        return (DatagramDnsResponse) base.Clear();
    }

    public override DatagramDnsResponse Touch()
    {
        return (DatagramDnsResponse) base.Touch();
    }

    public override DatagramDnsResponse Touch(object hint)
    {
        return (DatagramDnsResponse) base.Touch(hint);
    }

    public override DatagramDnsResponse Retain()
    {
        return (DatagramDnsResponse) base.Retain();
    }

    public override DatagramDnsResponse Retain(int increment)
    {
        return (DatagramDnsResponse) base.Retain(increment);
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
            return that.Recipient == null;
        }
        else
        {
            return Recipient.Equals(that.Recipient);
        }
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
