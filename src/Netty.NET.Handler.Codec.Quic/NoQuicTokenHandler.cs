/*
 * Copyright 2023 The Netty Project
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

using System.Net;
using Netty.NET.Buffer;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * {@link QuicTokenHandler} which will not use any token and so also fail to validate any given token.
 */
// CLR: InetSocketAddress is IPEndPoint.
internal sealed class NoQuicTokenHandler : IQuicTokenHandler
{

    public static readonly IQuicTokenHandler INSTANCE = new NoQuicTokenHandler();

    private NoQuicTokenHandler()
    {
    }

    public bool WriteToken(ByteBuf @out, ByteBuf dcid, IPEndPoint address)
    {
        return false;
    }

    public int ValidateToken(ByteBuf token, IPEndPoint address)
    {
        return -1;
    }

    public int MaxTokenLength()
    {
        return 0;
    }
}
