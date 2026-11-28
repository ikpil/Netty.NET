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

using System.Globalization;
using System.Text;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Dns;

// CLR: hostname() stays a method; checkNotNull throws ArgumentNullException. Numbers in text use the invariant
// culture (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
public class DefaultDnsPtrRecord : AbstractDnsRecord, IDnsPtrRecord
{

    private readonly string hostname;

    /**
     * Creates a new PTR record.
     *
     * @param name the domain name
     * @param dnsClass the class of the record, usually one of the following:
     *                 <ul>
     *                     <li>{@link #CLASS_IN}</li>
     *                     <li>{@link #CLASS_CSNET}</li>
     *                     <li>{@link #CLASS_CHAOS}</li>
     *                     <li>{@link #CLASS_HESIOD}</li>
     *                     <li>{@link #CLASS_NONE}</li>
     *                     <li>{@link #CLASS_ANY}</li>
     *                 </ul>
     * @param timeToLive the TTL value of the record
     * @param hostname the hostname this PTR record resolves to.
     */
    public DefaultDnsPtrRecord(
            string name, int dnsClass, long timeToLive, string hostname) :
        base(name, DnsRecordType.PTR, dnsClass, timeToLive)
    {
        this.hostname = CheckNotNull(hostname, "hostname");
    }

    public virtual string Hostname()
    {
        return hostname;
    }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder(64).Append(StringUtil.SimpleClassName(this)).Append('(');
        DnsRecordType type = Type();
        buf.Append(Name().Length == 0? "<root>" : Name())
           .Append(' ')
           .Append(TimeToLive().ToString(CultureInfo.InvariantCulture))
           .Append(' ');

        DnsMessageUtil.AppendRecordClass(buf, DnsClass())
                      .Append(' ')
                      .Append(type.Name());

        buf.Append(' ')
           .Append(hostname);

        return buf.ToString();
    }
}
