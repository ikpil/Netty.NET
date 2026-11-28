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

using System;
using System.Security.Cryptography;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Handler.Codec.Quic;

// CLR: javax.crypto.Mac with HmacSHA256 is System.Security.Cryptography.HMACSHA256 (an instance is not
// thread-safe, so each thread keeps its own, as the FastThreadLocal of Java does). SecureRandom.nextBytes is
// RandomNumberGenerator.Fill. The static block runs in the static constructor, after the field initializers; the
// MAC functions read the keys only when a thread first signs, after the keys are filled. mac.reset() is
// Initialize(), update(input) + doFinal() one TryComputeHash over the input bytes, Arrays.copyOf(bytes, n) is
// Array.Resize (it truncates or pads with zeros) and ByteBuffer.wrap is the returned ReadOnlyMemory<byte> (see
// IQuicConnectionIdGenerator). The JCA exceptions of Mac.getInstance/init are CryptographicException, and
// IllegalStateException is InvalidOperationException.
internal sealed class Hmac
{

    private const string ALGORITHM = "HmacSHA256";

    // Two independent keys so that CID signing and reset-token signing are
    // cryptographically decoupled: an observer cannot derive one output from
    // the other even when both use the same input value (RFC 9000 §21.11).
    private static readonly byte[] CID_KEY   = new byte[32];
    private static readonly byte[] TOKEN_KEY = new byte[32];

    static Hmac()
    {
        RandomNumberGenerator.Fill(CID_KEY);
        RandomNumberGenerator.Fill(TOKEN_KEY);
    }

    private static readonly FastThreadLocal<HMACSHA256> CID_MACS =
            new FastThreadLocalFunc<HMACSHA256>(() => NewMac(CID_KEY));

    private static readonly FastThreadLocal<HMACSHA256> TOKEN_MACS =
            new FastThreadLocalFunc<HMACSHA256>(() => NewMac(TOKEN_KEY));

    // CLR: ALGORITHM names the JCA algorithm that HMACSHA256 implements.
    private static HMACSHA256 NewMac(byte[] key)
    {
        try
        {
            return new HMACSHA256(key);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(ALGORITHM + ": " + exception.Message, exception);
        }
    }

    private static ReadOnlyMemory<byte> Sign(HMACSHA256 mac, ReadOnlyMemory<byte> input, int outLength)
    {
        mac.Initialize();
        byte[] signBytes = new byte[mac.HashSize / 8];
        mac.TryComputeHash(input.Span, signBytes, out _);

        if (signBytes.Length != outLength)
        {
            Array.Resize(ref signBytes, outLength);
        }

        return signBytes;
    }

    internal static ReadOnlyMemory<byte> SignCid(ReadOnlyMemory<byte> input, int outLength)
    {
        return Sign(CID_MACS.Get(), input, outLength);
    }

    internal static ReadOnlyMemory<byte> SignToken(ReadOnlyMemory<byte> input, int outLength)
    {
        return Sign(TOKEN_MACS.Get(), input, outLength);
    }

    private Hmac()
    {
    }
}
