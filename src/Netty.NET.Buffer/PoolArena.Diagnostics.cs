/*
 * Copyright 2012 The Netty Project
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
using System.Globalization;
using System.Text;

namespace Netty.NET.Buffer;

internal abstract partial class PoolArena
{
    public override string ToString()
    {
        lock (_sync)
        {
            var text = new StringBuilder();
            text.Append("Chunk(s) at 0~25%:").Append(Environment.NewLine).Append(_qInit).Append(Environment.NewLine)
                .Append("Chunk(s) at 0~50%:").Append(Environment.NewLine).Append(_q000).Append(Environment.NewLine)
                .Append("Chunk(s) at 25~75%:").Append(Environment.NewLine).Append(_q025).Append(Environment.NewLine)
                .Append("Chunk(s) at 50~100%:").Append(Environment.NewLine).Append(_q050).Append(Environment.NewLine)
                .Append("Chunk(s) at 75~100%:").Append(Environment.NewLine).Append(_q075).Append(Environment.NewLine)
                .Append("Chunk(s) at 100%:").Append(Environment.NewLine).Append(_q100).Append(Environment.NewLine)
                .Append("small subpages:");
            for (int i = 0; i < _smallSubpagePools.Length; i++)
            {
                PoolSubpage head = _smallSubpagePools[i];
                if (head == null) continue; // CLR disposal removes heads; live reservations may still exist.
                // CLR mutations use the head lock even while holding the arena lock.
                lock (head.SyncRoot)
                {
                    if (head.Next == head || head.Next == null) continue;
                    text.Append(Environment.NewLine).Append(i.ToString(CultureInfo.InvariantCulture)).Append(": ");
                    for (PoolSubpage subpage = head.Next; subpage != null && subpage != head; subpage = subpage.Next)
                        text.Append(subpage);
                }
            }
            return text.Append(Environment.NewLine).ToString();
        }
    }
}
