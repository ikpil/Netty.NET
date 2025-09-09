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
 * {@link OutOfMemoryError} that is throws if {@link PlatformDependent#allocateDirectNoCleaner(int)} can not allocate
 * a new {@link ByteBuffer} due memory restrictions.
 */
public class OutOfDirectMemoryErrorException : OutOfMemoryException
{
    public OutOfDirectMemoryErrorException()
    {
    }

    public OutOfDirectMemoryErrorException(string message)
        : base(message)
    {
    }

    public OutOfDirectMemoryErrorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

}