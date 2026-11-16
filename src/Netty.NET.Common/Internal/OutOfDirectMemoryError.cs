/*
 * Copyright 2016 The Netty Project
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

namespace Netty.NET.Common.Internal;

/**
 * {@link OutOfMemoryError} that is throws if {@link PlatformDependent#allocateDirect(int)} can not allocate
 * a new {@link ByteBuffer} due memory restrictions.
 */
// CLR: OutOfMemoryError maps to System.OutOfMemoryException, which is not sealed, so callers
// that handle any out-of-memory condition still catch it. NativeMemoryAllocator's reservation
// limit (the CLR counterpart of PlatformDependent.incrementMemoryCounter and
// DIRECT_MEMORY_LIMIT) throws it with the source message. serialVersionUID has no CLR purpose.
public sealed class OutOfDirectMemoryError : OutOfMemoryException
{
    internal OutOfDirectMemoryError(string s)
        : base(s)
    {
    }
}
