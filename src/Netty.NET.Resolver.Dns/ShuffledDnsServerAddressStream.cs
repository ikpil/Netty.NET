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

namespace Netty.NET.Resolver.Dns;

// CLR: Collections.shuffle(addresses, ThreadLocalRandom.current()) "traverses the list backwards, from the last element
// up to the second, repeatedly swapping a randomly selected element into the "current position""
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Collections.html); the same swap walk runs over
// Random.Shared, the ThreadLocalRandom mapping of this port (the JDK sequence is not reproduced). The covariant
// duplicate() is the public ShuffledDnsServerAddressStream Duplicate() with an explicit interface implementation.
internal sealed class ShuffledDnsServerAddressStream : IDnsServerAddressStream
{

    private readonly List<EndPoint> addresses;
    private int i;

    /**
     * Create a new instance.
     * @param addresses The addresses are not cloned. It is assumed the caller has cloned this array or otherwise will
     *                  not modify the contents.
     */
    internal ShuffledDnsServerAddressStream(List<EndPoint> addresses)
    {
        this.addresses = addresses;

        Shuffle();
    }

    private ShuffledDnsServerAddressStream(List<EndPoint> addresses, int startIdx)
    {
        this.addresses = addresses;
        i = startIdx;
    }

    private void Shuffle()
    {
        Random rnd = Random.Shared;
        for (int i = addresses.Count; i > 1; i--)
        {
            int j = rnd.Next(i);
            (addresses[i - 1], addresses[j]) = (addresses[j], addresses[i - 1]);
        }
    }

    public EndPoint Next()
    {
        int i = this.i;
        EndPoint next = addresses[i];
        if (++ i < addresses.Count)
        {
            this.i = i;
        }
        else
        {
            this.i = 0;
            Shuffle();
        }
        return next;
    }

    public int Size()
    {
        return addresses.Count;
    }

    public ShuffledDnsServerAddressStream Duplicate()
    {
        return new ShuffledDnsServerAddressStream(addresses, i);
    }

    IDnsServerAddressStream IDnsServerAddressStream.Duplicate()
    {
        return Duplicate();
    }

    public override string ToString()
    {
        return SequentialDnsServerAddressStream.ToString("shuffled", i, addresses);
    }
}
