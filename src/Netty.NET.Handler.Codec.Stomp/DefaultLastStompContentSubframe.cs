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

using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Stomp;

/**
 * The default implementation for the {@link LastStompContentSubframe}.
 */
// CLR: covariant override returns as in DefaultStompContentSubframe
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override); retain() narrows to
// DefaultLastStompContentSubframe as in the source.
public class DefaultLastStompContentSubframe : DefaultStompContentSubframe, ILastStompContentSubframe
{

    public DefaultLastStompContentSubframe(ByteBuf content) : base(content)
    {
    }

    public override ILastStompContentSubframe Copy()
    {
        return (ILastStompContentSubframe) base.Copy();
    }

    public override ILastStompContentSubframe Duplicate()
    {
        return (ILastStompContentSubframe) base.Duplicate();
    }

    public override ILastStompContentSubframe RetainedDuplicate()
    {
        return (ILastStompContentSubframe) base.RetainedDuplicate();
    }

    public override ILastStompContentSubframe Replace(ByteBuf content)
    {
        return new DefaultLastStompContentSubframe(content);
    }

    public override DefaultLastStompContentSubframe Retain()
    {
        base.Retain();
        return this;
    }

    public override ILastStompContentSubframe Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override ILastStompContentSubframe Touch()
    {
        base.Touch();
        return this;
    }

    public override ILastStompContentSubframe Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }

    public override string ToString()
    {
        return "DefaultLastStompContent{" +
                "decoderResult=" + DecoderResult() +
                '}';
    }
}
