/*
 * Copyright 2018 The Netty Project
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

using System.Collections.Generic;
using System.Linq;
using Netty.NET.Handler.Ssl;

namespace Netty.NET.Handler.Tests.Ssl;

public class IdentityCipherSuiteFilterTest
{

    [Fact]
    public void RegularInstanceDefaultsToDefaultCiphers()
    {
        List<string> defaultCiphers = new List<string> { "FOO", "BAR" };
        HashSet<string> supportedCiphers = new HashSet<string>(new[] { "BAZ", "QIX" });
        string[] filtered = IdentityCipherSuiteFilter.INSTANCE
                .FilterCipherSuites(null, defaultCiphers, supportedCiphers);
        Assert.Equal(defaultCiphers.ToArray(), filtered);
    }

    [Fact]
    public void AlternativeInstanceDefaultsToSupportedCiphers()
    {
        List<string> defaultCiphers = new List<string> { "FOO", "BAR" };
        HashSet<string> supportedCiphers = new HashSet<string>(new[] { "BAZ", "QIX" });
        string[] filtered = IdentityCipherSuiteFilter.INSTANCE_DEFAULTING_TO_SUPPORTED_CIPHERS
                .FilterCipherSuites(null, defaultCiphers, supportedCiphers);
        Assert.Equal(supportedCiphers.ToArray(), filtered);
    }
}
