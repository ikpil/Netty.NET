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

public partial class PooledByteBufAllocator
{
    /**
     * Returns the status of the allocator (which contains all metrics) as string. Be aware this may be expensive
     * and so should not called too frequently.
     */
    /// <remarks>Reports configured heap/direct arena order, utilization lists and available small subpages.
    /// Each arena is read under its own lock; the entire allocator is not one transactional snapshot.
    /// Diagnostics reserve no buffer storage and stay available after CLR disposal, when live
    /// reservations may remain while their retired lists are already empty.</remarks>
    public string DumpStats()
    {
        var text = new StringBuilder(512);
        text.Append(_heapArenas.Length.ToString(CultureInfo.InvariantCulture)).Append(" heap arena(s):").Append(Environment.NewLine);
        foreach (PoolArena arena in _heapArenas) text.Append(arena);
        text.Append(_directArenas.Length.ToString(CultureInfo.InvariantCulture)).Append(" direct arena(s):").Append(Environment.NewLine);
        foreach (PoolArena arena in _directArenas) text.Append(arena);
        return text.ToString();
    }
}
