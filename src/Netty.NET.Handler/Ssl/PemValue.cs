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

using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Ssl;

/**
 * A PEM encoded value.
 *
 * @see PemEncoded
 * @see PemPrivateKey#toPEM(ByteBufAllocator, boolean, java.security.PrivateKey)
 * @see PemX509Certificate#toPEM(ByteBufAllocator, boolean, java.security.cert.X509Certificate[])
 */
// CLR: The covariant overrides of retain()/retain(int)/touch()/touch(Object) are C# covariant override returns and
// copy()/duplicate()/retainedDuplicate()/replace(ByteBuf) return PemValue; the IByteBufHolder members are explicit
// implementations that forward to them, as codec-memcache DefaultMemcacheContent does
// (src/Netty.NET.Handler.Codec.Memcache/DefaultMemcacheContent.cs). IllegalReferenceCountException is the Common
// type.
internal class PemValue : AbstractReferenceCounted, IPemEncoded
{

    private readonly ByteBuf content;

    private readonly bool sensitive;

    internal PemValue(ByteBuf content, bool sensitive)
    {
        this.content = ObjectUtil.CheckNotNull(content, "content");
        this.sensitive = sensitive;
    }

    public bool IsSensitive()
    {
        return sensitive;
    }

    public ByteBuf Content
    {
        get
        {
            int count = RefCnt;
            if (count <= 0)
            {
                throw new IllegalReferenceCountException(count);
            }

            return content;
        }
    }

    public PemValue Copy()
    {
        return Replace(content.Copy());
    }

    public PemValue Duplicate()
    {
        return Replace(content.Duplicate());
    }

    public PemValue RetainedDuplicate()
    {
        return Replace(content.RetainedDuplicate());
    }

    public PemValue Replace(ByteBuf content)
    {
        return new PemValue(content, sensitive);
    }

    public override PemValue Touch()
    {
        return (PemValue) base.Touch();
    }

    public override PemValue Touch(object hint)
    {
        content.Touch(hint);
        return this;
    }

    public override PemValue Retain()
    {
        return (PemValue) base.Retain();
    }

    public override PemValue Retain(int increment)
    {
        return (PemValue) base.Retain(increment);
    }

    protected override void Deallocate()
    {
        if (sensitive)
        {
            SslUtils.Zeroout(content);
        }

        content.Release();
    }

    IByteBufHolder IByteBufHolder.Copy()
    {
        return Copy();
    }

    IByteBufHolder IByteBufHolder.Duplicate()
    {
        return Duplicate();
    }

    IByteBufHolder IByteBufHolder.RetainedDuplicate()
    {
        return RetainedDuplicate();
    }

    IByteBufHolder IByteBufHolder.Replace(ByteBuf content)
    {
        return Replace(content);
    }

    IByteBufHolder IByteBufHolder.Retain()
    {
        return Retain();
    }

    IByteBufHolder IByteBufHolder.Retain(int increment)
    {
        return Retain(increment);
    }

    IByteBufHolder IByteBufHolder.Touch()
    {
        return Touch();
    }

    IByteBufHolder IByteBufHolder.Touch(object hint)
    {
        return Touch(hint);
    }
}
