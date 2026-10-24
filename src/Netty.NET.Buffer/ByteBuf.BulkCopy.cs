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
using System.Buffers;

namespace Netty.NET.Buffer;

public abstract partial class ByteBuf
{
    // A composite's physical contiguous borrow does not substitute for its logical bulk kernel.
    internal virtual bool SupportsDirectBulkCopy => true;
    internal virtual ReadOnlySequence<byte> BorrowCopySequence(int index, int length)
    { CheckIndex(index, length); return GetReadOnlySequenceCore(index, length); }

    internal static bool BulkRangesOverlap(ByteBuf source, int sourceIndex, ByteBuf destination, int destinationIndex, int length)
    {
        if (length == 0) return false;
        if (ReferenceEquals(source, destination)) return true;
        // Inspect storage without public access records. No span survives a component callback.
        ReadOnlySequence<byte> input = source.BorrowCopySequence(sourceIndex, length);
        ReadOnlySequence<byte> output = destination.BorrowCopySequence(destinationIndex, length);
        foreach (ReadOnlyMemory<byte> a in input)
        foreach (ReadOnlyMemory<byte> b in output)
            if (a.Span.Overlaps(b.Span)) return true;
        return false;
    }
}
