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

namespace Netty.NET.Buffer;

public partial class CompositeByteBuf
{
    internal override int ForEachByteAscending(int start, int end, Func<byte, bool> processor)
    {
        if (end <= start) return -1;
        int componentIndex = FindComponentIndex(start);
        while (start < end)
        {
            CheckIndex(start, end - start);
            ComponentEntry component = _components[componentIndex++];
            if (component.Length == 0) continue; // empty
            int count = Math.Min(end - start, component.EndOffset - start);
            int local = component.SourceIndex + (start - component.Offset);
            component.Source.CheckIndex(local, count);
            ByteBuf storage = component.Source.UnwrapIterationStorage(ref local);
            // avoid additional checks in AbstractByteBuf case
            // CLR: all storage providers expose this internal loop, independently of public iteration overrides.
            int remaining = count;
            bool Process(byte value)
            {
                if (!processor(value)) return false;
                if (--remaining > 0) { EnsureAccessible(); component.Source.CheckIndex(0, 0); }
                return true;
            }
            int result = storage.ForEachByteAscending(local, local + count, Process);
            if (result != -1) return start + (result - local);
            start += count;
        }
        return -1;
    }

    internal override int ForEachByteDescending(int start, int end, Func<byte, bool> processor)
    {
        // rStart *and* rEnd are inclusive
        if (end > start) return -1;
        int componentIndex = FindComponentIndex(start);
        while (start >= end)
        {
            CheckIndex(end, start - end + 1);
            ComponentEntry component = _components[componentIndex--];
            if (component.Length == 0) continue; // empty
            // CLR: clamp at the captured component boundary. The original localRStart-based
            // length can include bytes before a component's captured readable start.
            int count = Math.Min(start - end + 1, start - component.Offset + 1);
            int componentStart = start - count + 1;
            int local = component.SourceIndex + (componentStart - component.Offset);
            component.Source.CheckIndex(local, count);
            ByteBuf storage = component.Source.UnwrapIterationStorage(ref local);
            // avoid additional checks in AbstractByteBuf case
            int remaining = count;
            bool Process(byte value)
            {
                if (!processor(value)) return false;
                if (--remaining > 0) { EnsureAccessible(); component.Source.CheckIndex(0, 0); }
                return true;
            }
            int result = storage.ForEachByteDescending(local + count - 1, local, Process);
            if (result != -1) return componentStart + (result - local);
            start -= count;
        }
        return -1;
    }
}
