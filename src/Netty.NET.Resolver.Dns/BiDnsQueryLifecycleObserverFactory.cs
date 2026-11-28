/*
 * Copyright 2017 The Netty Project
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

using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Dns;

namespace Netty.NET.Resolver.Dns;

/**
 * Combines two {@link DnsQueryLifecycleObserverFactory} into a single {@link DnsQueryLifecycleObserverFactory}.
 */
// CLR: DnsQuestion is codec-dns IDnsQuestion.
public sealed class BiDnsQueryLifecycleObserverFactory : IDnsQueryLifecycleObserverFactory
{
    private readonly IDnsQueryLifecycleObserverFactory a;
    private readonly IDnsQueryLifecycleObserverFactory b;

    /**
     * Create a new instance.
     * @param a The {@link DnsQueryLifecycleObserverFactory} that will receive events first.
     * @param b The {@link DnsQueryLifecycleObserverFactory} that will receive events second.
     */
    public BiDnsQueryLifecycleObserverFactory(IDnsQueryLifecycleObserverFactory a, IDnsQueryLifecycleObserverFactory b)
    {
        this.a = ObjectUtil.CheckNotNull(a, "a");
        this.b = ObjectUtil.CheckNotNull(b, "b");
    }

    public IDnsQueryLifecycleObserver NewDnsQueryLifecycleObserver(IDnsQuestion question)
    {
        return new BiDnsQueryLifecycleObserver(a.NewDnsQueryLifecycleObserver(question),
                                               b.NewDnsQueryLifecycleObserver(question));
    }
}
