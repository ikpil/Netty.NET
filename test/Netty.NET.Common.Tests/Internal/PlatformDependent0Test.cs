/*
 * Copyright 2017 The Netty Project
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

namespace Netty.NET.Common.Tests.Internal;

public class PlatformDependent0Test
{
    // CLR adaptation: address metadata uses a borrowed native view. No JVM
    // constructor/Unsafe assumption controls CLR memory support.
    [Fact]
    public void testNewDirectBufferNegativeMemoryAddress()
    {
        testNewDirectBufferMemoryAddress(-1);
    }

    [Fact]
    public void testNewDirectBufferNonNegativeMemoryAddress()
    {
        testNewDirectBufferMemoryAddress(10);
    }

    [Fact]
    public void testNewDirectBufferZeroMemoryAddress()
    {
        // The original ByteBuffer constructor permits metadata for a null
        // nonempty address. CLR Memory must reject that unusable span boundary.
        Assert.Throws<ArgumentException>(() => new NativeMemoryView(0, 10));
        using var empty = new NativeMemoryView(0, 0);
        Assert.Equal(0, empty.Memory.Length);
    }

    private static void testNewDirectBufferMemoryAddress(long address)
    {
        int capacity = 10;
        using var buffer = new NativeMemoryView(checked((nint)address), capacity);
        Assert.Equal(address, (long)buffer.Address);
        Assert.Equal(capacity, buffer.Memory.Length);
    }

    // The two JDK-version/SecurityManager scenarios and their original comments
    // are mapped in docs/common-platform-runtime.md. CLR consumers use
    // Environment.Version directly; runtime versions do not select JDK features.
}
