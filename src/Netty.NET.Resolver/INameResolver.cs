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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Resolver;

/**
 * Resolves an arbitrary string that represents the name of an endpoint into an address.
 */
public interface INameResolver<T> : IDisposable
{
    /**
     * Resolves the specified name into an address.
     *
     * @param inetHost the name to resolve
     *
     * @return the address as the result of the resolution
     */
    /**
     * Resolves the specified name into an address.
     *
     * @param inetHost the name to resolve
     * @param promise the {@link Promise} which will be fulfilled when the name resolution is finished
     *
     * @return the address as the result of the resolution
     */
    // CLR: the returned Task owns completion; no caller-supplied Promise is required.
    // Await uses normal CLR scheduling. Consumers needing executor affinity dispatch explicitly.
    Task<T> ResolveAsync(string inetHost, CancellationToken cancellationToken = default);

    /**
     * Resolves the specified host name and port into a list of address.
     *
     * @param inetHost the name to resolve
     *
     * @return the list of the address as the result of the resolution
     */
    /**
     * Resolves the specified host name and port into a list of address.
     *
     * @param inetHost the name to resolve
     * @param promise the {@link Promise} which will be fulfilled when the name resolution is finished
     *
     * @return the list of the address as the result of the resolution
     */
    Task<IReadOnlyList<T>> ResolveAllAsync(string inetHost, CancellationToken cancellationToken = default);

    /**
     * Closes all the resources allocated and used by this resolver.
     */
    new void Dispose();
}
