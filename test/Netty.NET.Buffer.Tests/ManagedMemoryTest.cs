using System;
using System.Runtime.InteropServices;
using Netty.NET.Common;

namespace Netty.NET.Buffer.Tests;

internal static class ManagedMemoryTest
{
    internal static nint Address(ReadOnlyMemory<byte> memory)
    {
        if (MemoryMarshal.TryGetMemoryManager<byte, AdaptivePoolingAllocator.AdaptiveChunkAllocation>(
                memory, out var adaptive, out int offset, out _))
        {
            Assert.True(adaptive.TryGetAddress(offset, out nint address));
            return address;
        }

        if (MemoryMarshal.TryGetMemoryManager<byte, ThreadLocalDirectAllocation>(
                memory, out var threadLocal, out offset, out _))
        {
            Assert.True(threadLocal.TryGetAddress(offset, out nint address));
            return address;
        }

        Assert.True(NativeMemoryOwner.TryGetAddress(memory, out nint directAddress));
        return directAddress;
    }
}
