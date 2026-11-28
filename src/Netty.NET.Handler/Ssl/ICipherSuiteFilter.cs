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

namespace Netty.NET.Handler.Ssl;

/**
 * Provides a means to filter the supplied cipher suite based upon the supported and default cipher suites.
 */
// CLR: Iterable, List and Set are IEnumerable, IList and ISet; the SSLEngine of the Javadoc is the SslStream that
// SslHandler drives.
public interface ICipherSuiteFilter
{
    /**
     * Filter the requested {@code ciphers} based upon other cipher characteristics.
     * @param ciphers The requested ciphers
     * @param defaultCiphers The default recommended ciphers for the current {@link SSLEngine} as determined by Netty
     * @param supportedCiphers The supported ciphers for the current {@link SSLEngine}
     * @return The filter list of ciphers. Must not return {@code null}.
     */
    string[] FilterCipherSuites(IEnumerable<string> ciphers, IList<string> defaultCiphers, ISet<string> supportedCiphers);
}

