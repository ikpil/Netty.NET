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

using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Socksx;

/**
 * An abstract {@link SocksMessage}.
 */
// CLR: inside this class the simple name DecoderResult binds to the DecoderResult() method in an expression, so the
// DecoderResult type is qualified as Codec.DecoderResult there (as TestMessage in codec-base MessageAggregatorTest).
// Version() of ISocksMessage is abstract here, as the Java abstract class leaves version() to its subclasses.
public abstract class AbstractSocksMessage : ISocksMessage
{

    private DecoderResult decoderResult = Codec.DecoderResult.SUCCESS;

    public DecoderResult DecoderResult()
    {
        return decoderResult;
    }

    public void SetDecoderResult(DecoderResult decoderResult)
    {
        this.decoderResult = ObjectUtil.CheckNotNull(decoderResult, "decoderResult");
    }

    public abstract SocksVersion Version();
}
