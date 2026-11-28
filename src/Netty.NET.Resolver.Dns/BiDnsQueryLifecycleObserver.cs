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

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Dns;

namespace Netty.NET.Resolver.Dns;

/**
 * Combines two {@link DnsQueryLifecycleObserver} into a single {@link DnsQueryLifecycleObserver}.
 */
// CLR: the parameter types follow IDnsQueryLifecycleObserver (EndPoint, Task, IReadOnlyList<EndPoint>, IDnsQuestion,
// Exception); checkNotNull is Common's ObjectUtil.CheckNotNull.
public sealed class BiDnsQueryLifecycleObserver : IDnsQueryLifecycleObserver
{
    private readonly IDnsQueryLifecycleObserver a;
    private readonly IDnsQueryLifecycleObserver b;

    /**
     * Create a new instance.
     * @param a The {@link DnsQueryLifecycleObserver} that will receive events first.
     * @param b The {@link DnsQueryLifecycleObserver} that will receive events second.
     */
    public BiDnsQueryLifecycleObserver(IDnsQueryLifecycleObserver a, IDnsQueryLifecycleObserver b)
    {
        this.a = ObjectUtil.CheckNotNull(a, "a");
        this.b = ObjectUtil.CheckNotNull(b, "b");
    }

    public void QueryWritten(EndPoint dnsServerAddress, Task future)
    {
        try
        {
            a.QueryWritten(dnsServerAddress, future);
        }
        finally
        {
            b.QueryWritten(dnsServerAddress, future);
        }
    }

    public void QueryCancelled(int queriesRemaining)
    {
        try
        {
            a.QueryCancelled(queriesRemaining);
        }
        finally
        {
            b.QueryCancelled(queriesRemaining);
        }
    }

    public IDnsQueryLifecycleObserver QueryRedirected(IReadOnlyList<EndPoint> nameServers)
    {
        try
        {
            a.QueryRedirected(nameServers);
        }
        finally
        {
            b.QueryRedirected(nameServers);
        }

        return this;
    }

    public IDnsQueryLifecycleObserver QueryCNAMEd(IDnsQuestion cnameQuestion)
    {
        try
        {
            a.QueryCNAMEd(cnameQuestion);
        }
        finally
        {
            b.QueryCNAMEd(cnameQuestion);
        }

        return this;
    }

    public IDnsQueryLifecycleObserver QueryNoAnswer(DnsResponseCode code)
    {
        try
        {
            a.QueryNoAnswer(code);
        }
        finally
        {
            b.QueryNoAnswer(code);
        }

        return this;
    }

    public void QueryFailed(Exception cause)
    {
        try
        {
            a.QueryFailed(cause);
        }
        finally
        {
            b.QueryFailed(cause);
        }
    }

    public void QuerySucceed()
    {
        try
        {
            a.QuerySucceed();
        }
        finally
        {
            b.QuerySucceed();
        }
    }
}
