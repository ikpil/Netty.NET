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
    // Raw addresses have no managed view. The compatibility overload rejects
    // every address without taking ownership or reading it.
    [Fact]
    public void TestNewDirectBufferNegativeMemoryAddress()
    {
        AssertUnsupported(-1);
    }

    [Fact]
    public void TestNewDirectBufferNonNegativeMemoryAddress()
    {
        AssertUnsupported(10);
    }

    [Fact]
    public void TestNewDirectBufferZeroMemoryAddress()
    {
        AssertUnsupported(0);
    }

    private static void AssertUnsupported(nint address)
    {
#pragma warning disable CS0618
        Assert.Throws<PlatformNotSupportedException>(() => new NativeMemoryView(address, 10));
#pragma warning restore CS0618
    }

    // The two JDK-version/SecurityManager scenarios and their original comments
    // are mapped in docs/common-platform-runtime.md. CLR consumers use
    // Environment.Version directly; runtime versions do not select JDK features.
}
