/*
 * Copyright 2015 The Netty Project
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
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Resolver;

/**
 * Resolves a possibility unresolved {@link SocketAddress}.
 */
// CLR: EndPoint replaces SocketAddress; DnsEndPoint and IPEndPoint distinguish unresolved/resolved IP endpoints.
public interface IAddressResolver<T> : IDisposable where T : EndPoint
{
    /**
   * Returns {@code true} if and only if the specified address is supported by this resolved.
   */
    bool IsSupported(EndPoint address);
    /**
   * Returns {@code true} if and only if the specified address has been resolved.
   *
   * @throws UnsupportedAddressTypeException if the specified address is not supported by this resolver
   */
    bool IsResolved(EndPoint address);
    /**
   * Resolves the specified address. If the specified address is resolved already, this method does nothing
   * but returning the original address.
   *
   * @param address the address to resolve
   *
   * @return the {@link SocketAddress} as the result of the resolution
   */
    /**
   * Resolves the specified address. If the specified address is resolved already, this method does nothing
   * but returning the original address.
   *
   * @param address the address to resolve
   * @param promise the {@link Promise} which will be fulfilled when the name resolution is finished
   *
   * @return the {@link SocketAddress} as the result of the resolution
   */
    // CLR: Task owns completion, with cooperative CancellationToken cancellation.
    Task<T> ResolveAsync(EndPoint address, CancellationToken cancellationToken = default);
    /**
   * Resolves the specified address. If the specified address is resolved already, this method does nothing
   * but returning the original address.
   *
   * @param address the address to resolve
   *
   * @return the list of the {@link SocketAddress}es as the result of the resolution
   */
    /**
   * Resolves the specified address. If the specified address is resolved already, this method does nothing
   * but returning the original address.
   *
   * @param address the address to resolve
   * @param promise the {@link Promise} which will be fulfilled when the name resolution is finished
   *
   * @return the list of the {@link SocketAddress}es as the result of the resolution
   */
    Task<IReadOnlyList<T>> ResolveAllAsync(EndPoint address, CancellationToken cancellationToken = default);
    /**
   * Closes all the resources allocated and used by this resolver.
   */
    new void Dispose();
}
