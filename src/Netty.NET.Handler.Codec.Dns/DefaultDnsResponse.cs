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
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Dns;

/**
 * The default {@link DnsResponse} implementation.
 */
// CLR: the Java overrides narrow the return type to DnsResponse with C# 9 covariant overrides: "an overriding method
// can declare a more derived return type the method it overrides"
// (https://learn.microsoft.com/en-us/dotnet/standard/generics/covariance-and-contravariance). checkNotNull throws
// ArgumentNullException (docs/codec-base-porting.md).
public class DefaultDnsResponse : AbstractDnsMessage, IDnsResponse
{

    private bool authoritativeAnswer;
    private bool truncated;
    private bool recursionAvailable;
    private DnsResponseCode code;

    /**
     * Creates a new instance with the {@link DnsOpCode#QUERY} {@code opCode} and
     * the {@link DnsResponseCode#NOERROR} {@code RCODE}.
     *
     * @param id the {@code ID} of the DNS response
     */
    public DefaultDnsResponse(int id) : this(id, DnsOpCode.QUERY, DnsResponseCode.NOERROR)
    {
    }

    /**
     * Creates a new instance with the {@link DnsResponseCode#NOERROR} {@code RCODE}.
     *
     * @param id the {@code ID} of the DNS response
     * @param opCode the {@code opCode} of the DNS response
     */
    public DefaultDnsResponse(int id, DnsOpCode opCode) : this(id, opCode, DnsResponseCode.NOERROR)
    {
    }

    /**
     * Creates a new instance.
     *
     * @param id the {@code ID} of the DNS response
     * @param opCode the {@code opCode} of the DNS response
     * @param code the {@code RCODE} of the DNS response
     */
    public DefaultDnsResponse(int id, DnsOpCode opCode, DnsResponseCode code) : base(id, opCode)
    {
        SetCode(code);
    }

    public virtual bool IsAuthoritativeAnswer()
    {
        return authoritativeAnswer;
    }

    public virtual IDnsResponse SetAuthoritativeAnswer(bool authoritativeAnswer)
    {
        this.authoritativeAnswer = authoritativeAnswer;
        return this;
    }

    public virtual bool IsTruncated()
    {
        return truncated;
    }

    public virtual IDnsResponse SetTruncated(bool truncated)
    {
        this.truncated = truncated;
        return this;
    }

    public virtual bool IsRecursionAvailable()
    {
        return recursionAvailable;
    }

    public virtual IDnsResponse SetRecursionAvailable(bool recursionAvailable)
    {
        this.recursionAvailable = recursionAvailable;
        return this;
    }

    public virtual DnsResponseCode Code()
    {
        return code;
    }

    public virtual IDnsResponse SetCode(DnsResponseCode code)
    {
        this.code = CheckNotNull(code, "code");
        return this;
    }

    public override IDnsResponse SetId(int id)
    {
        return (IDnsResponse) base.SetId(id);
    }

    public override IDnsResponse SetOpCode(DnsOpCode opCode)
    {
        return (IDnsResponse) base.SetOpCode(opCode);
    }

    public override IDnsResponse SetRecursionDesired(bool recursionDesired)
    {
        return (IDnsResponse) base.SetRecursionDesired(recursionDesired);
    }

    public override IDnsResponse SetZ(int z)
    {
        return (IDnsResponse) base.SetZ(z);
    }

    public override IDnsResponse SetRecord(DnsSection section, IDnsRecord record)
    {
        return (IDnsResponse) base.SetRecord(section, record);
    }

    public override IDnsResponse AddRecord(DnsSection section, IDnsRecord record)
    {
        return (IDnsResponse) base.AddRecord(section, record);
    }

    public override IDnsResponse AddRecord(DnsSection section, int index, IDnsRecord record)
    {
        return (IDnsResponse) base.AddRecord(section, index, record);
    }

    public override IDnsResponse Clear(DnsSection section)
    {
        return (IDnsResponse) base.Clear(section);
    }

    public override IDnsResponse Clear()
    {
        return (IDnsResponse) base.Clear();
    }

    public override IDnsResponse Touch()
    {
        return (IDnsResponse) base.Touch();
    }

    public override IDnsResponse Touch(object hint)
    {
        return (IDnsResponse) base.Touch(hint);
    }

    public override IDnsResponse Retain()
    {
        return (IDnsResponse) base.Retain();
    }

    public override IDnsResponse Retain(int increment)
    {
        return (IDnsResponse) base.Retain(increment);
    }

    public override string ToString()
    {
        return DnsMessageUtil.AppendResponse(new StringBuilder(128), this).ToString();
    }
}
