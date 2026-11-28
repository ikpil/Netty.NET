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
using Netty.NET.Handler.Codec.Dns;
using DnsResponseCode = Netty.NET.Handler.Codec.Dns.DnsResponseCode;

namespace Netty.NET.Resolver.Dns;

// CLR: the parameter types follow IDnsQueryLifecycleObserver (EndPoint, Task, IReadOnlyList<EndPoint>, IDnsQuestion,
// Exception); the package-private singleton is internal.
internal sealed class NoopDnsQueryLifecycleObserver : IDnsQueryLifecycleObserver
{
    internal static readonly NoopDnsQueryLifecycleObserver INSTANCE = new NoopDnsQueryLifecycleObserver();

    private NoopDnsQueryLifecycleObserver()
    {
    }

    public void QueryWritten(EndPoint dnsServerAddress, Task future)
    {
    }

    public void QueryCancelled(int queriesRemaining)
    {
    }

    public IDnsQueryLifecycleObserver QueryRedirected(IReadOnlyList<EndPoint> nameServers)
    {
        return this;
    }

    public IDnsQueryLifecycleObserver QueryCNAMEd(IDnsQuestion cnameQuestion)
    {
        return this;
    }

    public IDnsQueryLifecycleObserver QueryNoAnswer(DnsResponseCode code)
    {
        return this;
    }

    public void QueryFailed(Exception cause)
    {
    }

    public void QuerySucceed()
    {
    }
}
