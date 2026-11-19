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
using Netty.NET.Handler.Codec;

namespace Netty.NET.Handler.Codec.Stomp;

/**
 * The default {@link StompContentSubframe} implementation.
 */
// CLR: the overrides of the virtual DefaultByteBufHolder members are C# covariant override returns
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override). Inside this class the simple
// name DecoderResult is the decoderResult() method in an expression, so DecoderResult.SUCCESS is reached through
// global:: (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/namespace-alias-qualifier).
public class DefaultStompContentSubframe : DefaultByteBufHolder, IStompContentSubframe
{
    private DecoderResult decoderResult = global::Netty.NET.Handler.Codec.DecoderResult.SUCCESS;

    public DefaultStompContentSubframe(ByteBuf content) : base(content)
    {
    }

    public override IStompContentSubframe Copy()
    {
        return (IStompContentSubframe) base.Copy();
    }

    public override IStompContentSubframe Duplicate()
    {
        return (IStompContentSubframe) base.Duplicate();
    }

    public override IStompContentSubframe RetainedDuplicate()
    {
        return (IStompContentSubframe) base.RetainedDuplicate();
    }

    public override IStompContentSubframe Replace(ByteBuf content)
    {
        return new DefaultStompContentSubframe(content);
    }

    public override IStompContentSubframe Retain()
    {
        base.Retain();
        return this;
    }

    public override IStompContentSubframe Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override IStompContentSubframe Touch()
    {
        base.Touch();
        return this;
    }

    public override IStompContentSubframe Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }

    public DecoderResult DecoderResult()
    {
        return decoderResult;
    }

    public void SetDecoderResult(DecoderResult decoderResult)
    {
        this.decoderResult = decoderResult;
    }

    public override string ToString()
    {
        return "DefaultStompContent{" +
            "decoderResult=" + decoderResult +
            '}';
    }
}
