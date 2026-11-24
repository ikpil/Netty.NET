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

using System.Collections.Generic;
using System.Linq;

namespace Netty.NET.Handler.Ssl;

/**
 * This class will not do any filtering of ciphers suites.
 */
// CLR: toArray(EmptyArrays.EMPTY_STRINGS) of a List or Set is Enumerable.ToArray(), which "returns an array that
// contains the query results" (https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.toarray?view=net-10.0);
// the collection types are the BCL interfaces of ICipherSuiteFilter.
public sealed class IdentityCipherSuiteFilter : ICipherSuiteFilter
{

    /**
     * Defaults to default ciphers when provided ciphers are null
     */
    public static readonly IdentityCipherSuiteFilter INSTANCE = new IdentityCipherSuiteFilter(true);

    /**
     * Defaults to supported ciphers when provided ciphers are null
     */
    public static readonly IdentityCipherSuiteFilter INSTANCE_DEFAULTING_TO_SUPPORTED_CIPHERS =
            new IdentityCipherSuiteFilter(false);

    private readonly bool defaultToDefaultCiphers;

    private IdentityCipherSuiteFilter(bool defaultToDefaultCiphers)
    {
        this.defaultToDefaultCiphers = defaultToDefaultCiphers;
    }

    public string[] FilterCipherSuites(IEnumerable<string> ciphers, IList<string> defaultCiphers,
            ISet<string> supportedCiphers)
    {
        if (ciphers == null)
        {
            return defaultToDefaultCiphers ?
                    defaultCiphers.ToArray() :
                    supportedCiphers.ToArray();
        }
        else
        {
            List<string> newCiphers = new List<string>(supportedCiphers.Count);
            foreach (string c in ciphers)
            {
                if (c == null)
                {
                    break;
                }
                newCiphers.Add(c);
            }
            return newCiphers.ToArray();
        }
    }
}
