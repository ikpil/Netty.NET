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
using System.Collections.Generic;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Ssl.Util;

/**
 * A builder for creating {@link FingerprintTrustManagerFactory}.
 */
// CLR: CharSequence is string: the builder only keeps each fingerprint's toString(), and the C# string is the text type
// Java call sites pass (fingerprints("1C:53:...")). Arrays.asList is the array itself (an IEnumerable<string>);
// IllegalStateException is InvalidOperationException.
public sealed class FingerprintTrustManagerFactoryBuilder
{

    /**
     * A hash algorithm for fingerprints.
     */
    private readonly string algorithm;

    /**
     * A list of fingerprints.
     */
    private readonly List<string> fingerprints = new List<string>();

    /**
     * Creates a builder.
     *
     * @param algorithm a hash algorithm
     */
    internal FingerprintTrustManagerFactoryBuilder(string algorithm)
    {
        this.algorithm = ObjectUtil.CheckNotNull(algorithm, "algorithm");
    }

    /**
     * Adds fingerprints.
     *
     * @param fingerprints a number of fingerprints
     * @return the same builder
     */
    public FingerprintTrustManagerFactoryBuilder Fingerprints(params string[] fingerprints)
    {
        return Fingerprints((IEnumerable<string>) ObjectUtil.CheckNotNull(fingerprints, "fingerprints"));
    }

    /**
     * Adds fingerprints.
     *
     * @param fingerprints a number of fingerprints
     * @return the same builder
     */
    public FingerprintTrustManagerFactoryBuilder Fingerprints(IEnumerable<string> fingerprints)
    {
        ObjectUtil.CheckNotNull(fingerprints, "fingerprints");
        foreach (string fingerprint in fingerprints)
        {
            ObjectUtil.CheckNotNullWithIAE(fingerprint, "fingerprint");
            this.fingerprints.Add(fingerprint);
        }
        return this;
    }

    /**
     * Creates a {@link FingerprintTrustManagerFactory}.
     *
     * @return a new {@link FingerprintTrustManagerFactory}
     */
    public FingerprintTrustManagerFactory Build()
    {
        if (fingerprints.Count == 0)
        {
            throw new InvalidOperationException("No fingerprints provided");
        }
        return new FingerprintTrustManagerFactory(algorithm,
                                                  FingerprintTrustManagerFactory.ToFingerprintArray(fingerprints));
    }
}
