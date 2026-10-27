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

using System.Threading;
using System.Threading.Tasks;

namespace Netty.NET.Buffer.Tests;

// The original single-NIO-region assumption selects these six inherited providers.
// Composite has a different testInternalNioBuffer override, ported in CompositeByteBufGatheringTest.
public abstract class AbstractContiguousByteBufTest : AbstractByteBufTest
{
    private static readonly SemaphoreSlim InternalBorrowGrowthGate = new(1, 1);

    [Fact]
    public void TestNioBuffer1() => AssertOriginalNioBuffer1();

    [Fact]
    public void TestToByteBuffer2() => AssertOriginalToByteBuffer2();

    [Fact]
    public async Task TestInternalNioBuffer()
    {
        // Keep every original 32/64-MiB input, but bound simultaneous test allocations.
        await InternalBorrowGrowthGate.WaitAsync(TestContext.Current.CancellationToken);
        try { AssertOriginalInternalNioBufferGrowth(); }
        finally { InternalBorrowGrowthGate.Release(); }
    }
}
