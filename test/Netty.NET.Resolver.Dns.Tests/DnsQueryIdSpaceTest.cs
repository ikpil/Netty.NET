/*
 * Copyright 2024 The Netty Project
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

namespace Netty.NET.Resolver.Dns.Tests;

// CLR: IllegalStateException is InvalidOperationException and the anonymous Executable a lambda; LinkedHashSet<Integer>
// is a HashSet<int> whose insertion order is kept by a List<int> (a HashSet enumerates in no defined order).
public class DnsQueryIdSpaceTest
{

    [Fact]
    public void TestOverflow()
    {
        DnsQueryIdSpace ids = new DnsQueryIdSpace();
        Assert.Throws<InvalidOperationException>(() => ids.PushId(1));
    }

    [Fact]
    public void TestConsumeAndProduceAll()
    {
        DnsQueryIdSpace ids = new DnsQueryIdSpace();
        Assert.Equal(ids.MaxUsableIds(), ids.UsableIds());
        LinkedHashSet producedIdRound1 = new LinkedHashSet();
        LinkedHashSet producedIdRound2 = new LinkedHashSet();

        for (int i = ids.MaxUsableIds(); i > 0; i--)
        {
            int id = ids.NextId();
            Assert.True(id >= 0);
            Assert.True(producedIdRound1.Add(id));
        }
        Assert.Equal(0, ids.UsableIds());
        Assert.Equal(-1, ids.NextId());

        foreach (int v in  producedIdRound1)
        {
            ids.PushId(v);
        }

        for (int i = ids.MaxUsableIds(); i > 0; i--)
        {
            int id = ids.NextId();
            Assert.True(id >= 0);
            Assert.True(producedIdRound2.Add(id));
        }

        Assert.Equal(producedIdRound1.Count, producedIdRound2.Count);

        using IEnumerator<int> producedIdRoundIt = producedIdRound1.GetEnumerator();
        using IEnumerator<int> producedIdRound2It = producedIdRound2.GetEnumerator();

        bool notSame = false;
        while (producedIdRoundIt.MoveNext())
        {
            producedIdRound2It.MoveNext();
            if (producedIdRoundIt.Current != producedIdRound2It.Current)
            {
                notSame = true;
                break;
            }
        }
        Assert.True(notSame);
    }

    // CLR: java.util.LinkedHashSet<Integer> (see the class comment).
    private sealed class LinkedHashSet
    {
        private readonly HashSet<int> set = new HashSet<int>();
        private readonly List<int> order = new List<int>();

        public int Count => order.Count;

        public bool Add(int value)
        {
            if (!set.Add(value))
            {
                return false;
            }
            order.Add(value);
            return true;
        }

        public IEnumerator<int> GetEnumerator() => order.GetEnumerator();
    }
}
