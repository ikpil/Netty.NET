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

using System.Net;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Resolver;

/**
 * A skeletal {@link NameResolver} implementation that resolves {@link InetAddress}.
 */
public abstract class InetNameResolver : SimpleNameResolver<IPAddress>
{
    private readonly object _gate = new();
    private IAddressResolver<IPEndPoint> _addressResolver;

    /**
     * @param executor the {@link EventExecutor} which is used to notify the listeners of the {@link Future} returned
     *                 by {@link #resolve(String)}
     */
    protected InetNameResolver(IEventExecutor executor) : base(executor) { }

    /**
     * Return a {@link AddressResolver} that will use this name resolver underneath.
     * It's cached internally, so the same instance is always returned.
     */
    public IAddressResolver<IPEndPoint> AsAddressResolver()
    {
        lock (_gate)
            return _addressResolver ??= new InetSocketAddressResolver(Executor, this);
    }
}
