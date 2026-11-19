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

using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Socks;

/**
 * An unknown socks response.
 *
 * @see SocksInitResponseDecoder
 * @see SocksAuthResponseDecoder
 * @see SocksCmdResponseDecoder
 */
public sealed class UnknownSocksResponse : SocksResponse
{

    public UnknownSocksResponse() : base(SocksResponseType.UNKNOWN)
    {
    }

    // CLR: the Java override of the deprecated encodeAsByteBuf carries no @Deprecated, so the C# override is not marked
    // [Obsolete] and CS0672 is suppressed (see UnknownSocksRequest).
#pragma warning disable CS0672
    public override void EncodeAsByteBuf(ByteBuf byteBuf)
#pragma warning restore CS0672
    {
        // NOOP
    }
}
