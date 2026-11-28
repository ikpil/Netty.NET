/*
 * Copyright 2014 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Stomp;

/**
 * Default implementation of {@link StompFrame}.
 */
// CLR: content() and refCnt() are the IByteBufHolder/IReferenceCounted Content and RefCnt properties. The module
// interfaces inherit the holder methods instead of narrowing them, so only the IByteBufHolder and
// IReferenceCounted members, which those shared interfaces re-declare with other return types, are explicit
// implementations that forward to the public IStompFrame members, as Transport DatagramPacket does
// (https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/interfaces/explicit-interface-implementation).
// The package-private constructor is internal; CharsetUtil.UTF_8 is Encoding.UTF8.
public class DefaultStompFrame : DefaultStompHeadersSubframe, IStompFrame
{

    private readonly ByteBuf content;

    public DefaultStompFrame(StompCommand command) : this(command, Unpooled.Buffer(0))
    {
    }

    public DefaultStompFrame(StompCommand command, ByteBuf content) : this(command, content, null)
    {
    }

    internal DefaultStompFrame(StompCommand command, ByteBuf content, DefaultStompHeaders headers)
        : base(command, headers)
    {
        this.content = ObjectUtil.CheckNotNull(content, "content");
    }

    public ByteBuf Content => content;

    public IStompFrame Copy()
    {
        return Replace(content.Copy());
    }

    public IStompFrame Duplicate()
    {
        return Replace(content.Duplicate());
    }

    public IStompFrame RetainedDuplicate()
    {
        return Replace(content.RetainedDuplicate());
    }

    public IStompFrame Replace(ByteBuf content)
    {
        return new DefaultStompFrame(command, content, headers.Copy());
    }

    public int RefCnt => content.RefCnt;

    public IStompFrame Retain()
    {
        content.Retain();
        return this;
    }

    public IStompFrame Retain(int increment)
    {
        content.Retain(increment);
        return this;
    }

    public IStompFrame Touch()
    {
        content.Touch();
        return this;
    }

    public IStompFrame Touch(object hint)
    {
        content.Touch(hint);
        return this;
    }

    public bool Release()
    {
        return content.Release();
    }

    public bool Release(int decrement)
    {
        return content.Release(decrement);
    }

    public override string ToString()
    {
        return "DefaultStompFrame{" +
            "command=" + command +
            ", headers=" + headers +
            ", content=" + content.ToString(Encoding.UTF8) +
            '}';
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

    IReferenceCounted IReferenceCounted.Retain()
    {
        return Retain();
    }

    IReferenceCounted IReferenceCounted.Retain(int increment)
    {
        return Retain(increment);
    }

    IReferenceCounted IReferenceCounted.Touch()
    {
        return Touch();
    }

    IReferenceCounted IReferenceCounted.Touch(object hint)
    {
        return Touch(hint);
    }
}
