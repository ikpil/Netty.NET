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

using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;

namespace Netty.NET.Handler.Codec.Memcache;

/**
 * The default {@link MemcacheObject} implementation.
 */
[UnstableApi]
public abstract class AbstractMemcacheObject : AbstractReferenceCounted, IMemcacheObject
{

    // CLR: inside this class the simple name DecoderResult is the method in an expression, so the DecoderResult type is
    // reached through global:: (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/namespace-alias-qualifier).
    private DecoderResult decoderResult = global::Netty.NET.Handler.Codec.DecoderResult.SUCCESS;

    protected AbstractMemcacheObject()
    {
        // Disallow direct instantiation
    }

    public DecoderResult DecoderResult()
    {
        return decoderResult;
    }

    public void SetDecoderResult(DecoderResult result)
    {
        this.decoderResult = ObjectUtil.CheckNotNull(result, "DecoderResult should not be null.");
    }
}
