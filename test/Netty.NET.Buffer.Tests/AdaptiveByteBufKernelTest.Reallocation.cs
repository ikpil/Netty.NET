/*
 * Copyright 2022 The Netty Project
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

namespace Netty.NET.Buffer.Tests;

public partial class AdaptiveByteBufKernelTest
{
    public static IEnumerable<object[]> Reallocations()
    {
        foreach (bool direct in new[] { false, true })
        for (int profile = 0; profile < 5; ++profile)
        for (int mode = 0; mode < 3; ++mode) yield return [direct, profile, mode];
    }

    [Theory]
    [MemberData(nameof(Reallocations))]
    public void ReallocationReadsLogicalPrefixThroughOldRootBeforePublishingReplacement(bool direct, int profile, int mode)
    {
        using var s = new Storage(direct, profile);
        AdaptiveByteBuf b = s.Buffer;
        AdaptiveChunkAllocation old = b.Allocation, replacement = null;
        int capacity = b.Capacity, target = old.Length + 1;
        byte[] original = b.AsSpan(0, capacity).ToArray();
        byte[] guard = s.Guard.AsSpan(0, s.Guard.Capacity).ToArray();
        b.SetIndex(2, 7).MarkReaderIndex().MarkWriterIndex().SetIndex(3, 8);
        s.Allocator.AfterReservation = (_, allocation) => replacement = allocation;
        s.Observation.Enabled = true;
        s.Observation.Fail = mode == 0;
        if (mode == 1) s.Observation.ReadFill = 0xA5;
        try
        {
            if (mode == 0)
            {
                Assert.Same(s.Observation.Failure, Assert.Throws<KernelFailure>(() => b.Capacity = target));
                Assert.Same(old, b.Allocation);
                Assert.Equal(capacity, b.Capacity);
                Assert.Equal(original, b.AsSpan(0, capacity).ToArray());
                Assert.NotNull(replacement);
                Assert.Throws<ObjectDisposedException>(() => { _ = replacement.GetSpan().Length; });
            }
            else
            {
                b.Capacity = target;
                Assert.Same(replacement, b.Allocation);
                Assert.NotSame(old, b.Allocation);
                Assert.Equal(target, b.Capacity);
                byte[] expected = mode == 1 ? new byte[capacity] : original;
                if (mode == 1) Array.Fill(expected, (byte)0xA5);
                Assert.Equal(expected, b.AsSpan(0, capacity).ToArray());
                Assert.Throws<ObjectDisposedException>(() => { _ = old.GetSpan().Length; });
            }
            var call = Assert.Single(s.Observation.Calls);
            Assert.Equal(("Span", old.Offset, capacity), (call.Method, call.Index, call.Width));
            Assert.Equal((3, 8), (b.ReaderIndex, b.WriterIndex));
            b.ResetWriterIndex().ResetReaderIndex();
            Assert.Equal((2, 7), (b.ReaderIndex, b.WriterIndex));
            Assert.Equal(guard, s.Guard.AsSpan(0, s.Guard.Capacity).ToArray());
            Assert.Equal(1, b.ReferenceCount);
            Assert.Equal(1, s.Guard.ReferenceCount);
            if (mode == 0)
            {
                // The failed destination has been returned. Retrying must still read the old prefix.
                s.Observation.Enabled = false;
                b.Capacity = target;
                Assert.Equal(original, b.AsSpan(0, capacity).ToArray());
            }
        }
        finally { s.Observation.Enabled = false; }
    }
}
