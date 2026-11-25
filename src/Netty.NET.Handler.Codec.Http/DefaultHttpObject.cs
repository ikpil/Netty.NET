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

using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http;

// CLR: inside this class the simple name DecoderResult is the decoderResult() method in an expression, so
// DecoderResult.SUCCESS is reached through global::, as codec-stomp's DefaultStompContentSubframe does
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/namespace-alias-qualifier). The
// @Deprecated getDecoderResult() is not ported (see IHttpObject). Java methods are virtual unless final, so the public
// members are virtual; equals/hashCode are Equals/GetHashCode.
public class DefaultHttpObject : IHttpObject
{

    private const int HASH_CODE_PRIME = 31;
    private DecoderResult decoderResult = global::Netty.NET.Handler.Codec.DecoderResult.SUCCESS;

    protected DefaultHttpObject()
    {
        // Disallow direct instantiation
    }

    public virtual DecoderResult DecoderResult()
    {
        return decoderResult;
    }

    public virtual void SetDecoderResult(DecoderResult decoderResult)
    {
        this.decoderResult = ObjectUtil.CheckNotNull(decoderResult, "decoderResult");
    }

    public override int GetHashCode()
    {
        int result = 1;
        result = HASH_CODE_PRIME * result + decoderResult.GetHashCode();
        return result;
    }

    public override bool Equals(object o)
    {
        if (!(o is DefaultHttpObject))
        {
            return false;
        }

        DefaultHttpObject other = (DefaultHttpObject) o;

        return DecoderResult().Equals(other.DecoderResult());
    }
}
