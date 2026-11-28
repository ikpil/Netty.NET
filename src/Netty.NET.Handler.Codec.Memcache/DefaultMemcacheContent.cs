/*
 * Copyright 2013 The Netty Project
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

namespace Netty.NET.Handler.Codec.Memcache;

/**
 * The default {@link MemcacheContent} implementation.
 */
// CLR: The covariant overrides of retain()/retain(int)/touch()/touch(Object) and
// copy()/duplicate()/retainedDuplicate()/ replace(ByteBuf) are C# covariant override returns
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override). The IByteBufHolder
// members, which the shared interface re-declares with its own return type, are explicit implementations that
// forward to the public members, as Transport DatagramPacket does
// (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/interfaces/explicit-interface-implementation).
[UnstableApi]
public class DefaultMemcacheContent : AbstractMemcacheObject, IMemcacheContent
{

    private readonly ByteBuf content;

    /**
     * Creates a new instance with the specified content.
     */
    public DefaultMemcacheContent(ByteBuf content)
    {
        this.content = ObjectUtil.CheckNotNull(content, "content");
    }

    public ByteBuf Content => content;

    public virtual IMemcacheContent Copy()
    {
        return Replace(content.Copy());
    }

    public virtual IMemcacheContent Duplicate()
    {
        return Replace(content.Duplicate());
    }

    public virtual IMemcacheContent RetainedDuplicate()
    {
        return Replace(content.RetainedDuplicate());
    }

    public virtual IMemcacheContent Replace(ByteBuf content)
    {
        return new DefaultMemcacheContent(content);
    }

    public override IMemcacheContent Retain()
    {
        base.Retain();
        return this;
    }

    public override IMemcacheContent Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override IMemcacheContent Touch()
    {
        base.Touch();
        return this;
    }

    public override IMemcacheContent Touch(object hint)
    {
        content.Touch(hint);
        return this;
    }

    protected override void Deallocate()
    {
        content.Release();
    }

    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) +
               "(data: " + Content + ", decoderResult: " + DecoderResult() + ')';
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
