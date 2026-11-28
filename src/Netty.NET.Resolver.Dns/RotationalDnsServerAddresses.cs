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

using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace Netty.NET.Resolver.Dns;

// CLR: the AtomicIntegerFieldUpdater startIdxUpdater is Interlocked.CompareExchange on the startIdx field, which
// is read with Volatile.Read in place of the volatile modifier.
internal sealed class RotationalDnsServerAddresses : DefaultDnsServerAddresses
{

    private int startIdx;

    internal RotationalDnsServerAddresses(List<EndPoint> addresses) : base("rotational", addresses)
    {
    }

    public override IDnsServerAddressStream Stream()
    {
        for (;;)
        {
            int curStartIdx = Volatile.Read(ref startIdx);
            int nextStartIdx = curStartIdx + 1;
            if (nextStartIdx >= addresses.Count)
            {
                nextStartIdx = 0;
            }

            if (Interlocked.CompareExchange(ref startIdx, nextStartIdx, curStartIdx) == curStartIdx)
            {
                return new SequentialDnsServerAddressStream(addresses, curStartIdx);
            }
        }
    }
}
