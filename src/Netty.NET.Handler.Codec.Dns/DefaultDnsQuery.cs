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

using System.Text;

namespace Netty.NET.Handler.Codec.Dns;

/**
 * The default {@link DnsQuery} implementation.
 */
// CLR: the Java overrides narrow the return type to DnsQuery with C# 9 covariant overrides: "an overriding method can
// declare a more derived return type the method it overrides"
// (https://learn.microsoft.com/en-us/dotnet/standard/generics/covariance-and-contravariance).
public class DefaultDnsQuery : AbstractDnsMessage, IDnsQuery
{

    /**
     * Creates a new instance with the {@link DnsOpCode#QUERY} {@code opCode}.
     *
     * @param id the {@code ID} of the DNS query
     */
    public DefaultDnsQuery(int id) : base(id)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param id the {@code ID} of the DNS query
     * @param opCode the {@code opCode} of the DNS query
     */
    public DefaultDnsQuery(int id, DnsOpCode opCode) : base(id, opCode)
    {
    }

    public override IDnsQuery SetId(int id)
    {
        return (IDnsQuery) base.SetId(id);
    }

    public override IDnsQuery SetOpCode(DnsOpCode opCode)
    {
        return (IDnsQuery) base.SetOpCode(opCode);
    }

    public override IDnsQuery SetRecursionDesired(bool recursionDesired)
    {
        return (IDnsQuery) base.SetRecursionDesired(recursionDesired);
    }

    public override IDnsQuery SetZ(int z)
    {
        return (IDnsQuery) base.SetZ(z);
    }

    public override IDnsQuery SetRecord(DnsSection section, IDnsRecord record)
    {
        return (IDnsQuery) base.SetRecord(section, record);
    }

    public override IDnsQuery AddRecord(DnsSection section, IDnsRecord record)
    {
        return (IDnsQuery) base.AddRecord(section, record);
    }

    public override IDnsQuery AddRecord(DnsSection section, int index, IDnsRecord record)
    {
        return (IDnsQuery) base.AddRecord(section, index, record);
    }

    public override IDnsQuery Clear(DnsSection section)
    {
        return (IDnsQuery) base.Clear(section);
    }

    public override IDnsQuery Clear()
    {
        return (IDnsQuery) base.Clear();
    }

    public override IDnsQuery Touch()
    {
        return (IDnsQuery) base.Touch();
    }

    public override IDnsQuery Touch(object hint)
    {
        return (IDnsQuery) base.Touch(hint);
    }

    public override IDnsQuery Retain()
    {
        return (IDnsQuery) base.Retain();
    }

    public override IDnsQuery Retain(int increment)
    {
        return (IDnsQuery) base.Retain(increment);
    }

    public override string ToString()
    {
        return DnsMessageUtil.AppendQuery(new StringBuilder(128), this).ToString();
    }
}
