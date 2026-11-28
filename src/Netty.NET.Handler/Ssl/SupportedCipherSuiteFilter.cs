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
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Ssl;

/**
 * This class will filter all requested ciphers out that are not supported by the current {@link SSLEngine}.
 */
// CLR: the collection types are the BCL interfaces of ICipherSuiteFilter; the SSLEngine of the Javadoc is the SslStream
// that SslHandler drives.
public sealed class SupportedCipherSuiteFilter : ICipherSuiteFilter
{
    public static readonly SupportedCipherSuiteFilter INSTANCE = new SupportedCipherSuiteFilter();

    private SupportedCipherSuiteFilter()
    {
    }

    public string[] FilterCipherSuites(IEnumerable<string> ciphers, IList<string> defaultCiphers,
            ISet<string> supportedCiphers)
    {
        ObjectUtil.CheckNotNull(defaultCiphers, "defaultCiphers");
        ObjectUtil.CheckNotNull(supportedCiphers, "supportedCiphers");

        List<string> newCiphers;

        if (ciphers == null)
        {
            newCiphers = new List<string>(defaultCiphers.Count);
            ciphers = defaultCiphers;
        }
        else
        {
            newCiphers = new List<string>(supportedCiphers.Count);
        }

        foreach (string c in ciphers)
        {
            if (c == null)
            {
                break;
            }

            if (supportedCiphers.Contains(c))
            {
                newCiphers.Add(c);
            }
        }

        return newCiphers.ToArray();
    }

}
