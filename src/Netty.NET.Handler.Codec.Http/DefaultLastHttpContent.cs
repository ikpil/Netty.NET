/*
 * Copyright 2012 The Netty Project
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
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Http.DefaultHttpHeadersFactory;

namespace Netty.NET.Handler.Codec.Http;

/**
 * The default {@link LastHttpContent} implementation.
 */
// CLR: the @Deprecated DefaultLastHttpContent(ByteBuf, boolean), whose @deprecated text names
// DefaultLastHttpContent(ByteBuf), is not ported (owner decision 2026-11-16). The narrowed members are covariant
// overrides of DefaultHttpContent's virtual members returning ILastHttpContent
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override). StringBuilder.setLength is
// the Length setter; Map.Entry is the codec-base IEntry.
public class DefaultLastHttpContent : DefaultHttpContent, ILastHttpContent
{
    private readonly HttpHeaders trailingHeaders;

    /**
     * Create a new empty, last HTTP content message.
     */
    public DefaultLastHttpContent() : this(Unpooled.Buffer(0))
    {
    }

    /**
     * Create a new last HTTP content message with the given contents.
     */
    public DefaultLastHttpContent(ByteBuf content) : this(content, TrailersFactory())
    {
    }

    /**
     * Create a new last HTTP content message with the given contents, and trailing headers from the given factory.
     */
    public DefaultLastHttpContent(ByteBuf content, IHttpHeadersFactory trailersFactory) : base(content)
    {
        trailingHeaders = trailersFactory.NewHeaders();
    }

    /**
     * Create a new last HTTP content message with the given contents, and trailing headers.
     */
    public DefaultLastHttpContent(ByteBuf content, HttpHeaders trailingHeaders) : base(content)
    {
        this.trailingHeaders = CheckNotNull(trailingHeaders, "trailingHeaders");
    }

    public override ILastHttpContent Copy()
    {
        return Replace(Content.Copy());
    }

    public override ILastHttpContent Duplicate()
    {
        return Replace(Content.Duplicate());
    }

    public override ILastHttpContent RetainedDuplicate()
    {
        return Replace(Content.RetainedDuplicate());
    }

    public override ILastHttpContent Replace(ByteBuf content)
    {
        return new DefaultLastHttpContent(content, TrailingHeaders().Copy());
    }

    public override ILastHttpContent Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override ILastHttpContent Retain()
    {
        base.Retain();
        return this;
    }

    public override ILastHttpContent Touch()
    {
        base.Touch();
        return this;
    }

    public override ILastHttpContent Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }

    public virtual HttpHeaders TrailingHeaders()
    {
        return trailingHeaders;
    }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder(base.ToString());
        buf.Append(StringUtil.NEWLINE);
        AppendHeaders(buf);

        // Remove the last newline.
        buf.Length = buf.Length - StringUtil.NEWLINE.Length;
        return buf.ToString();
    }

    private void AppendHeaders(StringBuilder buf)
    {
        foreach (IEntry<string, string> e in TrailingHeaders())
        {
            buf.Append(e.GetKey());
            buf.Append(": ");
            buf.Append(e.GetValue());
            buf.Append(StringUtil.NEWLINE);
        }
    }
}
