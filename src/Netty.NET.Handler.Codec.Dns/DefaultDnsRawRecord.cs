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

using System.Globalization;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Dns;

/**
 * The default {@code DnsRawRecord} implementation.
 */
// CLR: content() and refCnt() are the IByteBufHolder.Content and IReferenceCounted.RefCnt properties of the Buffer port
// ("read-only Content/ReferenceCount properties", docs/buffer-porting.md, Buffer holders and conversion). The public
// members keep the Java return type DnsRawRecord; the IByteBufHolder and IReferenceCounted members with the wider
// return types are implemented explicitly, as in Transport's DatagramPacket. Numbers in text use the invariant culture
// (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tostring).
public class DefaultDnsRawRecord : AbstractDnsRecord, IDnsRawRecord
{

    private readonly ByteBuf content;

    /**
     * Creates a new {@link #CLASS_IN IN-class} record.
     *
     * @param name the domain name
     * @param type the type of the record
     * @param timeToLive the TTL value of the record
     */
    public DefaultDnsRawRecord(string name, DnsRecordType type, long timeToLive, ByteBuf content) :
        this(name, type, IDnsRecord.CLASS_IN, timeToLive, content)
    {
    }

    /**
     * Creates a new record.
     *
     * @param name the domain name
     * @param type the type of the record
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
     */
    public DefaultDnsRawRecord(
            string name, DnsRecordType type, int dnsClass, long timeToLive, ByteBuf content) :
        base(name, type, dnsClass, timeToLive)
    {
        this.content = CheckNotNull(content, "content");
    }

    public virtual ByteBuf Content => content;

    public virtual IDnsRawRecord Copy()
    {
        return Replace(Content.Copy());
    }

    public virtual IDnsRawRecord Duplicate()
    {
        return Replace(Content.Duplicate());
    }

    public virtual IDnsRawRecord RetainedDuplicate()
    {
        return Replace(Content.RetainedDuplicate());
    }

    public virtual IDnsRawRecord Replace(ByteBuf content)
    {
        return new DefaultDnsRawRecord(Name(), Type(), DnsClass(), TimeToLive(), content);
    }

    public virtual int RefCnt => Content.RefCnt;

    public virtual IDnsRawRecord Retain()
    {
        Content.Retain();
        return this;
    }

    public virtual IDnsRawRecord Retain(int increment)
    {
        Content.Retain(increment);
        return this;
    }

    public virtual bool Release()
    {
        return Content.Release();
    }

    public virtual bool Release(int decrement)
    {
        return Content.Release(decrement);
    }

    public virtual IDnsRawRecord Touch()
    {
        Content.Touch();
        return this;
    }

    public virtual IDnsRawRecord Touch(object hint)
    {
        Content.Touch(hint);
        return this;
    }

    IByteBufHolder IByteBufHolder.Copy() => Copy();
    IByteBufHolder IByteBufHolder.Duplicate() => Duplicate();
    IByteBufHolder IByteBufHolder.RetainedDuplicate() => RetainedDuplicate();
    IByteBufHolder IByteBufHolder.Replace(ByteBuf content) => Replace(content);
    IByteBufHolder IByteBufHolder.Retain() => Retain();
    IByteBufHolder IByteBufHolder.Retain(int increment) => Retain(increment);
    IByteBufHolder IByteBufHolder.Touch() => Touch();
    IByteBufHolder IByteBufHolder.Touch(object hint) => Touch(hint);
    IReferenceCounted IReferenceCounted.Retain() => Retain();
    IReferenceCounted IReferenceCounted.Retain(int increment) => Retain(increment);
    IReferenceCounted IReferenceCounted.Touch() => Touch();
    IReferenceCounted IReferenceCounted.Touch(object hint) => Touch(hint);

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder(64).Append(StringUtil.SimpleClassName(this)).Append('(');
        DnsRecordType type = Type();
        if (type != DnsRecordType.OPT)
        {
            buf.Append(Name().Length == 0? "<root>" : Name())
               .Append(' ')
               .Append(TimeToLive().ToString(CultureInfo.InvariantCulture))
               .Append(' ');

            DnsMessageUtil.AppendRecordClass(buf, DnsClass())
                          .Append(' ')
                          .Append(type.Name());
        }
        else
        {
            buf.Append("OPT flags:")
               .Append(TimeToLive().ToString(CultureInfo.InvariantCulture))
               .Append(" udp:")
               .Append(DnsClass().ToString(CultureInfo.InvariantCulture));
        }

        buf.Append(' ')
           .Append(Content.ReadableBytes.ToString(CultureInfo.InvariantCulture))
           .Append("B)");

        return buf.ToString();
    }
}
