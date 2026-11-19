/*
 * Copyright 2025 The Netty Project
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
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Socksx.V5;

/**
 * The default {@link Socks5PrivateAuthResponse} implementation.
 */
// CLR: StringUtil.simpleClassName is StringUtil.SimpleClassName.
public sealed class DefaultSocks5PrivateAuthResponse : AbstractSocks5Message
    , ISocks5PrivateAuthResponse
{

    /**
     * The authentication status.
     */
    private readonly Socks5PrivateAuthStatus status;

    /**
     * Creates a new instance with the specified status.
     *
     * @param authStatus the authentication status
     */
    public DefaultSocks5PrivateAuthResponse(Socks5PrivateAuthStatus authStatus)
    {
        this.status = ObjectUtil.CheckNotNull(authStatus, "authStatus");
    }

    public Socks5PrivateAuthStatus Status()
    {
        return status;
    }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder(StringUtil.SimpleClassName(this));

        DecoderResult decoderResult = DecoderResult();
        if (!decoderResult.IsSuccess())
        {
            buf.Append("(decoderResult: ");
            buf.Append(decoderResult);
            buf.Append(", status: ");
            buf.Append(status);
            buf.Append(')');
        }
        else
        {
            buf.Append("(status: ");
            buf.Append(status);
            buf.Append(')');
        }

        return buf.ToString();
    }
}
