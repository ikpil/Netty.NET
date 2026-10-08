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

// CLR: preserve every native allocation while a growing composite write may
// consolidate/release components whose memory aliases the caller's span.
// A root lease is a value type; only a composite allocates a group of leases.
internal readonly struct BufferMemoryLease : IDisposable
{
    private readonly MemoryHandle _handle;
    private readonly BufferMemoryLease[] _children;

    internal BufferMemoryLease(MemoryHandle handle) => _handle = handle;
    internal BufferMemoryLease(BufferMemoryLease[] children) => _children = children;

    public void Dispose()
    {
        if (_children != null)
            for (int i = _children.Length - 1; i >= 0; --i) _children[i].Dispose();
        _handle.Dispose();
    }
}
