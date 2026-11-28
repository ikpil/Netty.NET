/*
 * Copyright 2019 The Netty Project
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

namespace Netty.NET.Handler.Ssl;

/**
 * This pseudorandom function (PRF) takes as input a secret, a seed, and
 * an identifying label and produces an output of arbitrary length.
 *
 * This is used by the TLS RFC to construct/deconstruct an array of bytes into
 * composite secrets.
 *
 * {@link <a href="https://tools.ietf.org/html/rfc5246">rfc5246</a>}
 */
// CLR: javax.crypto.Mac is IncrementalHash.CreateHMAC, which throws CryptographicException when "hashAlgorithm is
// not a known hash algorithm"
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.incrementalhash.createhmac?view=net-10.0);
// getMacLength() is HashLengthInBytes and doFinal(input) is AppendData(input) and GetHashAndReset(). The JCA Mac
// names are "HmacSHA256" and so on and "Standard names are not case-sensitive"
// (https://docs.oracle.com/en/java/javase/17/docs/specs/security/standard-names.html), while "Hash algorithm names
// are case-sensitive" in the BCL
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.hashalgorithmname?view=net-10.0), so
// the hash name is the part after a case-insensitive "Hmac" prefix, upper-cased ("SHA256", "SHA3-256"); names the
// BCL does not know (HmacSHA224, HmacSHA512/224,...) fail with "Could not find algo" as a missing JCA algorithm
// does. SecretKeySpec throws IllegalArgumentException "if algorithm is null or key is null or empty"
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/crypto/spec/SecretKeySpec.html), while an
// HMAC key may be empty in the BCL, so that check is written out after the algorithm lookup, in the source's
// order. GeneralSecurityException is CryptographicException (as pkitesting Algorithms maps it,
// src/Netty.NET.PkiTesting/Algorithms.cs) and IllegalArgumentException is ArgumentException.
internal sealed class PseudoRandomFunction
{

    /**
     * Constructor never to be called.
     */
    private PseudoRandomFunction()
    {
    }

    /**
     * Use a single hash function to expand a secret and seed into an
     * arbitrary quantity of output.
     *
     * P_hash(secret, seed) = HMAC_hash(secret, A(1) + seed) +
     *                        HMAC_hash(secret, A(2) + seed) +
     *                        HMAC_hash(secret, A(3) + seed) + ...
     * where + indicates concatenation.
     * A() is defined as:
     *       A(0) = seed
     *       A(i) = HMAC_hash(secret, A(i-1))
     * @param secret The starting secret to use for expansion
     * @param label An ascii string without a length byte or trailing null character.
     * @param seed The seed of the hash
     * @param length The number of bytes to return
     * @param algo the hmac algorithm to use
     * @return The expanded secrets
     * @throws IllegalArgumentException if the algo could not be found.
     */
    internal static byte[] Hash(byte[] secret, byte[] label, byte[] seed, int length, string algo)
    {
        ObjectUtil.CheckPositiveOrZero(length, "length");

        try
        {
            using IncrementalHash hmac = IncrementalHash.CreateHMAC(HashName(algo), secret);
            // CLR: new SecretKeySpec(secret, algo) rejects an empty key (see the class comment).
            if (secret.Length == 0)
            {
                throw new ArgumentException("Empty key");
            }
            /*
             * P_hash(secret, seed) = HMAC_hash(secret, A(1) + seed) +
             * HMAC_hash(secret, A(2) + seed) + HMAC_hash(secret, A(3) + seed) + ...
             * where + indicates concatenation. A() is defined as: A(0) = seed, A(i)
             * = HMAC_hash(secret, A(i-1))
             */

            int iterations = (int) Math.Ceiling(length / (double) hmac.HashLengthInBytes);
            byte[] expansion = EmptyArrays.EMPTY_BYTES;
            byte[] data = Concat(label, seed);
            byte[] A = data;

            for (int i = 0; i < iterations; i++)
            {
                A = DoFinal(hmac, A);
                expansion = Concat(expansion, DoFinal(hmac, Concat(A, data)));
            }

            return CopyOf(expansion, length);
        }
        catch (CryptographicException e)
        {
            throw new ArgumentException("Could not find algo: " + algo, e);
        }
    }

    // CLR: the BCL hash name of a JCA Mac name (see the class comment); an unknown name makes CreateHMAC throw.
    private static HashAlgorithmName HashName(string algo)
    {
        const string prefix = "Hmac";
        string hash = algo.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? algo.Substring(prefix.Length) : algo;
        return new HashAlgorithmName(hash.ToUpperInvariant());
    }

    private static byte[] DoFinal(IncrementalHash hmac, byte[] input)
    {
        hmac.AppendData(input);

        return hmac.GetHashAndReset();
    }

    // CLR: Arrays.copyOf(original, newLength), which pads with zeros or truncates.
    private static byte[] CopyOf(byte[] original, int newLength)
    {
        byte[] copy = new byte[newLength];
        Array.Copy(original, copy, Math.Min(original.Length, newLength));
        return copy;
    }

    private static byte[] Concat(byte[] first, byte[] second)
    {
        byte[] result = CopyOf(first, first.Length + second.Length);
        Array.Copy(second, 0, result, first.Length, second.Length);
        return result;
    }
}
