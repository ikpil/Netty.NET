/*
 * Copyright 2021 The Netty Project
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

using System;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * A {@link QuicConnectionIdGenerator} which creates new connection id by signing the given input
 * using hmac algorithms.
 */
// CLR: the package-private class is internal. java.nio.ByteBuffer is ReadOnlyMemory<byte> (see
// IQuicConnectionIdGenerator), a struct that cannot be null, so checkNotNull(buffer) has no counterpart and remaining()
// is Length. Quiche.QUICHE_MAX_CONN_ID_LEN, a constant read through JNI, is the RFC 9000 maximum Quic.MAX_CONN_ID_LEN
// (20, https://www.rfc-editor.org/rfc/rfc9000.html#section-17.2). UnsupportedOperationException is
// NotSupportedException.
internal sealed class HmacSignQuicConnectionIdGenerator : IQuicConnectionIdGenerator
{
    internal static readonly IQuicConnectionIdGenerator INSTANCE = new HmacSignQuicConnectionIdGenerator();

    private HmacSignQuicConnectionIdGenerator()
    {
    }

    public ReadOnlyMemory<byte> NewId(int length)
    {
        throw new NotSupportedException(
                "HmacSignQuicConnectionIdGenerator should always have an input to sign with");
    }

    public ReadOnlyMemory<byte> NewId(ReadOnlyMemory<byte> buffer, int length)
    {
        ObjectUtil.CheckPositive(buffer.Length, "buffer");
        ObjectUtil.CheckInRange(length, 0, MaxConnectionIdLength(), "length");

        return Hmac.SignCid(buffer, length);
    }

    public int MaxConnectionIdLength()
    {
        return Quic.MAX_CONN_ID_LEN;
    }

    public bool IsIdempotent()
    {
        return true;
    }
}
