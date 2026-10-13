/*
 * Copyright 2024 The Netty Project
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
using System.Numerics;
using System.Threading.Tasks;

namespace Netty.NET.Buffer.Tests;

public class AdaptivePoolingAllocatorTest
{
    [Fact]
    public void SizeClassComputations()
    {
        int[] sizeClasses = AdaptiveAllocationPolicy.GetSizeClasses();
        for (int sizeClassIndex = 0; sizeClassIndex < sizeClasses.Length; sizeClassIndex++)
        {
            int previousSizeIncluded = sizeClassIndex == 0 ? 0 : sizeClasses[sizeClassIndex - 1] + 1;
            AssertSizeClassOf(sizeClassIndex, previousSizeIncluded, sizeClasses[sizeClassIndex]);
        }
        // beyond the last size class, we return the size class array's length
        AssertSizeClassOf(sizeClasses.Length, sizeClasses[^1] + 1, sizeClasses[^1] + 1);
    }
    private static void AssertSizeClassOf(int expectedSizeClass, int previousSizeIncluded, int maxSizeIncluded)
    {
        for (int size = previousSizeIncluded; size <= maxSizeIncluded; size++)
            Assert.Equal(expectedSizeClass, AdaptiveAllocationPolicy.SizeClassIndexOf(size));
    }

    private static readonly int[] Segments = [32, 64, 128, 256, 512, 640, 1024, 1152, 2048, 2304, 4096, 4352, 8192, 8704, 16384, 16896];
    private static readonly int[] ChunkSizes = [131072, 131072, 131072, 131072, 131072, 131072, 131072, 131072, 131072, 131072, 131072, 139264, 262144, 278528, 524288, 540672];
    public static IEnumerable<object[]> FixedClassCases()
    {
        for (int index = 0; index < Segments.Length; index++)
            foreach (bool lowMemory in new[] { false, true }) yield return [index, lowMemory];
    }
    [Theory]
    [MemberData(nameof(FixedClassCases))]
    public void FixedControllersPreserveOriginalFastCapacityAndChunkGeometry(int index, bool lowMemory)
    {
        var policy = new AdaptiveAllocationPolicy(lowMemory);
        int requested = index == 0 ? 0 : Segments[index - 1] + 1;
        Assert.True(policy.IsPooled(requested));
        Assert.Equal(Segments[index], policy.ComputeBufferCapacity(requested, int.MaxValue));
        Assert.Equal(requested, policy.ComputeBufferCapacity(requested, requested));
        Assert.Equal(ChunkSizes[index], policy.ComputeChunkSize(requested));
        Assert.Equal(ChunkSizes[index], policy.ComputeChunkSize(Segments[index]));
        Assert.True(ChunkSizes[index] / Segments[index] >= 32);
    }
    [Fact]
    public void SizeClassSnapshotsAreIndependentAndLargeInputCannotOverflowTheLookup()
    {
        int[] first = AdaptiveAllocationPolicy.GetSizeClasses(), second = AdaptiveAllocationPolicy.GetSizeClasses();
        Assert.Equal(Segments, first); Assert.NotSame(first, second);
        first[0] = 999; Assert.Equal(32, second[0]); Assert.Equal(32, AdaptiveAllocationPolicy.GetSizeClasses()[0]);
        Assert.Equal(16, AdaptiveAllocationPolicy.SizeClassIndexOf(int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => AdaptiveAllocationPolicy.SizeClassIndexOf(-1));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LowMemoryAndMaximumPoolCutoffsSelectOriginalDedicatedFallback(bool lowMemory)
    {
        var policy = new AdaptiveAllocationPolicy(lowMemory);
        Assert.Equal(lowMemory ? 2097152 : 8388608, policy.MaxChunkSize);
        Assert.Equal(lowMemory ? 262144 : 1048576, policy.MaxPooledBufferSize);
        Assert.True(policy.IsPooled(16896)); Assert.Equal(!lowMemory, policy.IsPooled(16897));
        Assert.Equal(!lowMemory, policy.IsPooled(policy.MaxPooledBufferSize));
        Assert.False(policy.IsPooled(policy.MaxPooledBufferSize + 1));
        Assert.False(policy.IsPooled(int.MaxValue));
        Assert.Equal(int.MaxValue, policy.ComputeBufferCapacity(int.MaxValue, int.MaxValue));
        Assert.Equal(int.MaxValue, policy.ComputeChunkSize(int.MaxValue));
    }
    [Fact]
    public void BuddyControllersRememberLargerChunksWithoutClampingTheRawReservation()
    {
        var policy = new AdaptiveAllocationPolicy(false);
        int[] requests = [16897, 32768, 32769, 65536, 262144, 1048576, 1048577, 16897];
        int[] chunks = [262144, 262144, 524288, 524288, 2097152, 8388608, 1048577, 8388608];
        int[] reservations = [32768, 32768, 65536, 65536, 262144, 1048576, 1048577, 32768];
        for (int i = 0; i < requests.Length; i++)
        {
            Assert.Equal(chunks[i], policy.ComputeChunkSize(requests[i]));
            Assert.Equal(reservations[i], policy.ComputeBufferCapacity(requests[i], requests[i]));
        }
    }
    [Fact]
    public void LowMemorySkipsBuddyPoolingAndDoesNotAffectOtherPolicyInstances()
    {
        var low = new AdaptiveAllocationPolicy(true); var normal = new AdaptiveAllocationPolicy(false);
        Assert.Equal(32769, low.ComputeChunkSize(32769)); Assert.Equal(32769, low.ComputeBufferCapacity(32769, 40000));
        Assert.Equal(524288, normal.ComputeChunkSize(32769));
        var separate = new AdaptiveAllocationPolicy(false); Assert.Equal(262144, separate.ComputeChunkSize(16897));
    }
    [Fact]
    public void InvalidRequestsFailBeforeMutatingRememberedChunkSizing()
    {
        var p = new AdaptiveAllocationPolicy(false);
        Assert.Throws<ArgumentOutOfRangeException>(() => p.ComputeChunkSize(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.ComputeBufferCapacity(-1, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.ComputeBufferCapacity(100, 99));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.ComputeBufferCapacity(0, -1));
        Assert.Equal(262144, p.ComputeChunkSize(16897));
    }
    [Fact]
    public void ConcurrentControllerDecisionsStayWithinTheirOriginalCapacityBounds()
    {
        var p = new AdaptiveAllocationPolicy(false); int[] requests = [16897, 32769, 131073, 262145, 1048576];
        Parallel.For(0, 128, i =>
        {
            int size = requests[i % requests.Length], chunk = p.ComputeChunkSize(size);
            Assert.True(BitOperations.IsPow2(chunk)); Assert.InRange(chunk, size * 8, 8388608);
        });
        Assert.Equal(8388608, p.ComputeChunkSize(1048576));
    }
}
