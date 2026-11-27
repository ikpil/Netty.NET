/*
 * Copyright 2024 The Netty Project
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

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: QuicTokenHandler is IQuicTokenHandler and InetSocketAddress IPEndPoint. The checks
// assertEquals(ByteOrder.BIG_ENDIAN, out.order()) and assertEquals(ByteOrder.BIG_ENDIAN, token.order()) read
// ByteBuf.order(), which is @Deprecated ("use the Little Endian accessors, e.g. {@code getShortLE}, {@code getIntLE}
// instead of creating a buffer with swapped {@code endianness}") and not ported: a Netty.NET ByteBuf has no byte order of
// its own (big-endian accessors with explicit LE variants, docs/buffer-porting.md), so every buffer the handler receives
// is big-endian and the checks have no counterpart; @SuppressWarnings("deprecation") goes with them. The msquic server
// codec builder reports a token handler of its own as unsupported (msquic validates addresses with its own retry
// tokens, docs/codec-classes-quic-porting.md), so only QuicChannelConnectTest's CLR-inapplicable token validation test
// would use this handler.
internal sealed class TestQuicTokenHandler : IQuicTokenHandler
{
    public static readonly IQuicTokenHandler INSTANCE = new TestQuicTokenHandler();

    public bool WriteToken(ByteBuf @out, ByteBuf dcid, IPEndPoint address)
    {
        return InsecureQuicTokenHandler.INSTANCE.WriteToken(@out, dcid, address);
    }

    public int ValidateToken(ByteBuf token, IPEndPoint address)
    {
        return InsecureQuicTokenHandler.INSTANCE.ValidateToken(token, address);
    }

    public int MaxTokenLength()
    {
        return InsecureQuicTokenHandler.INSTANCE.MaxTokenLength();
    }

    private TestQuicTokenHandler() { }
}
