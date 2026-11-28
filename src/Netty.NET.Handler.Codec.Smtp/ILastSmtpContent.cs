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

namespace Netty.NET.Handler.Codec.Smtp;

/**
 * The last part of a sequence of {@link SmtpContent}s that are sent after a {@code DATA} request.
 * Be aware that a {@link SmtpContent} / {@link LastSmtpContent} sequence must always use CRLF as line delimiter
 * and the lines that start with a DOT must be escaped with an extra DOT as
 * specified by <a href="https://www.ietf.org/rfc/rfc2821.txt">RFC2821</a>.
 */
// CLR: The Java members that re-declare ISmtpContent members only to narrow the return type to LastSmtpContent are
// inherited unchanged, as for the Transport interfaces: C# 9 covariant returns cover class overrides only. The
// interface field EMPTY_LAST_CONTENT is a static readonly interface field (C# interfaces may declare static fields
// and nested types, https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface); its
// anonymous class is the named nested class EmptyLastSmtpContent, whose content()/refCnt() are the Content/RefCnt
// properties of the Buffer port.
[UnstableApi]
public interface ILastSmtpContent : ISmtpContent
{

    /**
     * Empty {@link LastSmtpContent}.
     */
    static readonly ILastSmtpContent EMPTY_LAST_CONTENT = new EmptyLastSmtpContent();

    private sealed class EmptyLastSmtpContent : ILastSmtpContent
    {
        public ILastSmtpContent Copy()
        {
            return this;
        }

        public ILastSmtpContent Duplicate()
        {
            return this;
        }

        public ILastSmtpContent RetainedDuplicate()
        {
            return this;
        }

        public ILastSmtpContent Replace(ByteBuf content)
        {
            return new DefaultLastSmtpContent(content);
        }

        public ILastSmtpContent Retain()
        {
            return this;
        }

        public ILastSmtpContent Retain(int increment)
        {
            return this;
        }

        public ILastSmtpContent Touch()
        {
            return this;
        }

        public ILastSmtpContent Touch(object hint)
        {
            return this;
        }

        public ByteBuf Content => Unpooled.EmptyBuffer;

        public int RefCnt => 1;

        public bool Release()
        {
            return false;
        }

        public bool Release(int decrement)
        {
            return false;
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
}
