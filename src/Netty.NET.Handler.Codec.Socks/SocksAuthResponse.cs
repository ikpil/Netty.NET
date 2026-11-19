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
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Socks;

/**
 * An socks auth response.
 *
 * @see SocksAuthRequest
 * @see SocksAuthResponseDecoder
 */
public sealed class SocksAuthResponse : SocksResponse
{
    private static readonly SocksSubnegotiationVersion SUBNEGOTIATION_VERSION = SocksSubnegotiationVersion.AUTH_PASSWORD;
    private readonly SocksAuthStatus authStatus;

    public SocksAuthResponse(SocksAuthStatus authStatus) : base(SocksResponseType.AUTH)
    {
        this.authStatus = ObjectUtil.CheckNotNull(authStatus, "authStatus");
    }

    /**
     * Returns the {@link SocksAuthStatus} of this {@link SocksAuthResponse}
     *
     * @return The {@link SocksAuthStatus} of this {@link SocksAuthResponse}
     */
    public SocksAuthStatus AuthStatus()
    {
        return authStatus;
    }

    // CLR: the override of the deprecated encodeAsByteBuf is not marked [Obsolete] (see UnknownSocksRequest).
#pragma warning disable CS0672
    public override void EncodeAsByteBuf(ByteBuf byteBuf)
#pragma warning restore CS0672
    {
        byteBuf.WriteByte(SUBNEGOTIATION_VERSION.ByteValue());
        byteBuf.WriteByte(authStatus.ByteValue());
    }
}
