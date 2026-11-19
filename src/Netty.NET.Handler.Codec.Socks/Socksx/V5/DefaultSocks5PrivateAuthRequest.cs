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
 * The default {@link Socks5PrivateAuthRequest} implementation.
 * <p>
 * For custom private authentication protocols, you should implement the {@link Socks5PrivateAuthRequest}
 * interface directly. Custom protocols should also implement their own encoder/decoder to handle the wire format.
 * </p>
 */
// CLR: byte[].clone() is Array.Clone(), "a shallow copy", which copies the byte elements
// (https://learn.microsoft.com/en-us/dotnet/api/system.array.clone?view=net-10.0); StringUtil.simpleClassName is
// StringUtil.SimpleClassName.
public sealed class DefaultSocks5PrivateAuthRequest : AbstractSocks5Message
    , ISocks5PrivateAuthRequest
{

    /**
     * The private authentication token.
     */
    private readonly byte[] privateToken;

    /**
     * Creates a new instance with the specified token.
     *
     * @param privateAuthToken the private authentication token
     */
    public DefaultSocks5PrivateAuthRequest(byte[] privateAuthToken)
    {
        this.privateToken = (byte[]) ObjectUtil.CheckNotNull(privateAuthToken, "privateToken").Clone();
    }

    public byte[] PrivateToken()
    {
        return (byte[]) privateToken.Clone();
    }

    public override string ToString()
    {
        StringBuilder buf = new StringBuilder(StringUtil.SimpleClassName(this));

        DecoderResult decoderResult = DecoderResult();
        if (!decoderResult.IsSuccess())
        {
            buf.Append("(decoderResult: ");
            buf.Append(decoderResult);
            buf.Append(", privateToken: ****)");
        }
        else
        {
            buf.Append("(privateToken: ****)");
        }

        return buf.ToString();
    }
}
