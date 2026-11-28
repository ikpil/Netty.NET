/*
 * Copyright 2020 The Netty Project
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
using System.Security.Cryptography;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Quic;

// CLR: SecureRandom.nextBytes is RandomNumberGenerator.Fill (a cryptographically strong generator that is safe to
// share between threads, so the RANDOM field has no counterpart), and ByteBuffer.wrap the returned
// ReadOnlyMemory<byte> (see IQuicConnectionIdGenerator). Quiche.QUICHE_MAX_CONN_ID_LEN, a constant read through
// JNI, is the RFC 9000 maximum Quic.MAX_CONN_ID_LEN (20,
// https://www.rfc-editor.org/rfc/rfc9000.html#section-17.2).
internal sealed class SecureRandomQuicConnectionIdGenerator : IQuicConnectionIdGenerator
{

    internal static readonly IQuicConnectionIdGenerator INSTANCE = new SecureRandomQuicConnectionIdGenerator();

    private SecureRandomQuicConnectionIdGenerator()
    {
    }

    public ReadOnlyMemory<byte> NewId(int length)
    {
        ObjectUtil.CheckInRange(length, 0, MaxConnectionIdLength(), "length");

        byte[] bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    public ReadOnlyMemory<byte> NewId(ReadOnlyMemory<byte> buffer, int length)
    {
        return NewId(length);
    }

    public int MaxConnectionIdLength()
    {
        return Quic.MAX_CONN_ID_LEN;
    }

    public bool IsIdempotent()
    {
        return false;
    }
}
