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

using System;
using System.Diagnostics;
using System.Security.Cryptography;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * A utility class mainly for use by web sockets
 */
// CLR: IncrementalHash has no reset() member, so GetHashAndReset() with the result dropped stands for reset().
// CreateHash throws CryptographicException when "hashAlgorithm is not a known hash algorithm"
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.incrementalhash.createhash?view=net-10.0),
// which stands for NoSuchAlgorithmException; Java's InternalError (a JVM error) is InvalidOperationException with
// the Java message. ThreadLocalRandom.current() is Random.Shared, a "thread-safe Random instance that may be used
// concurrently from any thread" (https://learn.microsoft.com/en-us/dotnet/api/system.random.shared?view=net-10.0):
// nextBytes is NextBytes and nextDouble() is NextDouble(), which "Returns a random floating-point number that is
// greater than or equal to 0.0, and less than 1.0"
// (https://learn.microsoft.com/en-us/dotnet/api/system.random.nextdouble?view=net-10.0). assert is Debug.Assert.
internal sealed class WebSocketUtil
{

    private static readonly FastThreadLocal<IncrementalHash> MD5 = new Md5Digest();

    private sealed class Md5Digest : FastThreadLocal<IncrementalHash>
    {
        protected override IncrementalHash InitialValue()
        {
            try
            {
                //Try to get a MessageDigest that uses MD5
                //Suppress a warning about weak hash algorithm
                //since it's defined in draft-ietf-hybi-thewebsocketprotocol-00
                return IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            }
            catch (CryptographicException)
            {
                //This shouldn't happen! How old is the computer?
                throw new InvalidOperationException("MD5 not supported on this platform - Outdated?");
            }
        }
    }

    private static readonly FastThreadLocal<IncrementalHash> SHA1 = new Sha1Digest();

    private sealed class Sha1Digest : FastThreadLocal<IncrementalHash>
    {
        protected override IncrementalHash InitialValue()
        {
            try
            {
                //Try to get a MessageDigest that uses SHA1
                //Suppress a warning about weak hash algorithm
                //since it's defined in draft-ietf-hybi-thewebsocketprotocol-00
                return IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            }
            catch (CryptographicException)
            {
                //This shouldn't happen! How old is the computer?
                throw new InvalidOperationException("SHA-1 not supported on this platform - Outdated?");
            }
        }
    }

    /**
     * Performs a MD5 hash on the specified data
     *
     * @param data The data to hash
     * @return The hashed data
     */
    internal static byte[] Md5(byte[] data)
    {
        // TODO(normanmaurer): Create md5 method that not need MessageDigest.
        return Digest(MD5, data);
    }

    internal static IncrementalHash Sha1()
    {
        IncrementalHash digest = SHA1.Get();
        digest.GetHashAndReset();
        return digest;
    }

    /**
     * Performs a SHA-1 hash on the specified data
     *
     * @param data The data to hash
     * @return The hashed data
     */
    internal static byte[] Sha1(byte[] data)
    {
        // TODO(normanmaurer): Create sha1 method that not need MessageDigest.
        return Digest(SHA1, data);
    }

    private static byte[] Digest(FastThreadLocal<IncrementalHash> digestFastThreadLocal, byte[] data)
    {
        IncrementalHash digest = digestFastThreadLocal.Get();
        digest.GetHashAndReset();
        digest.AppendData(data);

        return digest.GetHashAndReset();
    }

    /**
     * Performs base64 encoding on the specified data
     *
     * @param data The data to encode
     * @return An encoded string containing the data
     */
    internal static string Base64(byte[] data)
    {
        return Convert.ToBase64String(data);
    }

    /**
     * Creates an arbitrary number of random bytes
     *
     * @param size the number of random bytes to create
     * @return An array of random bytes
     */
    internal static byte[] RandomBytes(int size)
    {
        byte[] bytes = new byte[size];
        Random.Shared.NextBytes(bytes);
        return bytes;
    }

    /**
     * Generates a pseudo-random number
     *
     * @param minimum The minimum allowable value
     * @param maximum The maximum allowable value
     * @return A pseudo-random number
     */
    internal static int RandomNumber(int minimum, int maximum)
    {
        Debug.Assert(minimum < maximum);
        double fraction = Random.Shared.NextDouble();

        // the idea here is that nextDouble gives us a random value
        //
        //              0 <= fraction <= 1
        //
        // the distance from min to max declared as
        //
        //              dist = max - min
        //
        // satisfies the following
        //
        //              min + dist = max
        //
        // taking into account
        //
        //         0 <= fraction * dist <= dist
        //
        // we've got
        //
        //       min <= min + fraction * dist <= max
        return (int) (minimum + fraction * (maximum - minimum));
    }

    internal static int ByteAtIndex(int mask, int index)
    {
        return (mask >> 8 * (3 - index)) & 0xFF;
    }

    /**
     * A private constructor to ensure that instances of this class cannot be made
     */
    private WebSocketUtil()
    {
        // Unused
    }
}
