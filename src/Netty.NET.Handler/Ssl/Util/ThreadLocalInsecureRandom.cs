/*
 * Copyright 2014 The Netty Project
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

namespace Netty.NET.Handler.Ssl.Util;

/**
 * Insecure {@link SecureRandom} which relies on {@link PlatformDependent#threadLocalRandom()} for random number
 * generation.
 */
// CLR: java.security.SecureRandom is the BCL random generator abstraction RandomNumberGenerator, an abstract class whose
// GetBytes(byte[]) is to be "overridden in a derived class"
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.randomnumbergenerator?view=net-10.0), and
// ThreadLocalRandom.current() is Random.Shared, "a thread-safe Random instance that may be used concurrently from any
// thread" (https://learn.microsoft.com/en-us/dotnet/api/system.random.shared?view=net-10.0), as Common's random
// consumers use it (docs/common-clr-design.md, "Native random consumers and scalar counters"). nextBytes(byte[]) is
// GetBytes, also for the span and range overloads. getAlgorithm(), setSeed(byte[]), setSeed(long), generateSeed(int)
// and the java.util.Random members nextInt(), nextInt(int), nextBoolean(), nextLong(), nextFloat(), nextDouble() and
// nextGaussian() have no RandomNumberGenerator member to override and no caller in this port (SelfSignedCertificate, the
// only user, is not ported), so they are not ported. serialVersionUID is JVM serialization metadata.
internal sealed class ThreadLocalInsecureRandom : RandomNumberGenerator
{

    private static readonly RandomNumberGenerator INSTANCE = new ThreadLocalInsecureRandom();

    internal static RandomNumberGenerator Current()
    {
        return INSTANCE;
    }

    private ThreadLocalInsecureRandom() { }

    public override void GetBytes(byte[] bytes)
    {
        Random().NextBytes(bytes);
    }

    public override void GetBytes(byte[] data, int offset, int count)
    {
        Random().NextBytes(data.AsSpan(offset, count));
    }

    public override void GetBytes(Span<byte> data)
    {
        Random().NextBytes(data);
    }

    private static System.Random Random()
    {
        return System.Random.Shared;
    }
}
