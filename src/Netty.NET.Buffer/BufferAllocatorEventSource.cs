/*
 * Copyright 2025 The Netty Project
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
using System.Diagnostics.Tracing;
using System.Runtime.Loader;

namespace Netty.NET.Buffer;

/**
 * An abstract memory allocator event.
 */
/// <summary>Optional allocator diagnostics through the CLR tracing infrastructure.</summary>
/// <remarks>Enable individual keywords using EventListener, EventPipe or ETW. Payloads are
/// immutable snapshots; addresses are observations, never borrowed memory handles. Buffer
/// events currently cover PooledByteBufAllocator. Chunk and adaptive events remain pending.
/// CLR callbacks run after allocator/capacity/storage-return locks have been released.
/// The default load context uses Netty-NET-Buffer. Each separately loaded library copy
/// uses a unique Netty-NET-Buffer-{identifier} name, available through Log.Name.</remarks>
[EventSource(Name = "Netty-NET-Buffer")]
public sealed class BufferAllocatorEventSource : EventSource
{
    public static readonly BufferAllocatorEventSource Log = new();
    private BufferAllocatorEventSource() : base(SourceName(), EventSourceSettings.EtwManifestEventFormat) { }

    [NonEvent]
    private static string SourceName()
        // EventSource identities are process-wide, while library statics belong to
        // their load context. Separately loaded copies must not register the same GUID.
        => AssemblyLoadContext.GetLoadContext(typeof(BufferAllocatorEventSource).Assembly) == AssemblyLoadContext.Default
            ? "Netty-NET-Buffer" : "Netty-NET-Buffer-" + Guid.NewGuid().ToString("N");

    public static class Keywords
    {
        public const EventKeywords AllocateBuffer = (EventKeywords)1;
        public const EventKeywords FreeBuffer = (EventKeywords)2;
        public const EventKeywords ReallocateBuffer = (EventKeywords)4;
    }

    // CLR: the original AbstractBufferEvent fields become a value snapshot shared by
    // the three event payloads. No JFR Event objects or JVM class metadata are needed.
    internal readonly record struct Snapshot(int Size, int MaxFastCapacity, int MaxCapacity,
        bool Direct, long Address, bool ChunkPooled, bool ChunkThreadLocal);

    /**
     * Statically check if this event is enabled.
     */
    [NonEvent]
    internal bool Enabled(EventKeywords keyword) => IsEnabled(EventLevel.Informational, keyword);

    [NonEvent]
    internal unsafe Snapshot? Capture(ByteBuf buffer, EventKeywords keyword, ReadOnlyMemory<byte> memory,
        bool chunkPooled, bool chunkThreadLocal)
    {
        if (!Enabled(keyword)) return null;
        long address = 0;
        if (buffer.IsDirect)
        {
            // A pin only observes live native storage while capturing the address. The
            // emitted integer grants no access and does not extend the storage lifetime.
            using MemoryHandle pin = memory.Pin();
            address = unchecked((long)(nint)pin.Pointer);
        }
        return new Snapshot(buffer.Capacity, buffer.MaxFastWritableBytes + buffer.WriterIndex,
            buffer.MaxCapacity, buffer.IsDirect, address, chunkPooled, chunkThreadLocal);
    }

    [NonEvent]
    internal void Allocated(ByteBuf buffer)
    {
        if (!Enabled(Keywords.AllocateBuffer)) return;
        var pooled = buffer as PooledByteBuf;
        Snapshot? snapshot = Capture(buffer, Keywords.AllocateBuffer,
            buffer.IsDirect ? buffer.AsReadOnlyMemory(0, buffer.Capacity) : default,
            pooled?.Allocation.IsPooled ?? false, pooled?.Allocation.FromThreadCache ?? false);
        Allocated(snapshot);
    }

    [NonEvent]
    internal void Allocated(Snapshot? snapshot)
    {
        if (snapshot is not { } value) return;
        AllocateBuffer(typeof(PooledByteBufAllocator).FullName, value.Size, value.MaxFastCapacity,
            value.MaxCapacity, value.Direct, value.Address, value.ChunkPooled, value.ChunkThreadLocal);
    }

    [NonEvent]
    internal void Freed(Snapshot? snapshot)
    {
        if (snapshot is not { } value) return;
        FreeBuffer(typeof(PooledByteBufAllocator).FullName, value.Size, value.MaxFastCapacity,
            value.MaxCapacity, value.Direct, value.Address);
    }

    [NonEvent]
    internal void Reallocated(Snapshot? snapshot, int newCapacity)
    {
        if (snapshot is not { } value) return;
        ReallocateBuffer(typeof(PooledByteBufAllocator).FullName, value.Size, value.MaxFastCapacity,
            value.MaxCapacity, value.Direct, value.Address, newCapacity);
    }

    // Original descriptions retained from the JFR annotations:
    // Allocator type: The type of allocator this event is for
    // size: Configured buffer capacity
    // maxFastCapacity: Actual allocated buffer capacity
    // maxCapacity: Maximum buffer capacity
    // direct: Is this buffer referencing off-heap memory?
    // address: The memory address of the off-heap memory, if available
    // chunkPooled: Is this chunk pooled, or is it a one-off allocation for this buffer?
    // chunkThreadLocal: Is this buffer's chunk part of a thread-local magazine or arena?
    // newCapacity: Targeted buffer capacity
    [Event(1, Level = EventLevel.Informational, Keywords = Keywords.AllocateBuffer,
        Message = "Triggered when a buffer is allocated (or reallocated) from an allocator")]
    private void AllocateBuffer(string allocatorType, int size, int maxFastCapacity, int maxCapacity,
        bool direct, long address, bool chunkPooled, bool chunkThreadLocal)
    {
        if (Enabled(Keywords.AllocateBuffer))
            WriteEvent(1, new object[] { allocatorType, size, maxFastCapacity, maxCapacity, direct, address, chunkPooled, chunkThreadLocal });
    }

    [Event(2, Level = EventLevel.Informational, Keywords = Keywords.FreeBuffer,
        Message = "Triggered when a buffer is freed from an allocator")]
    private void FreeBuffer(string allocatorType, int size, int maxFastCapacity, int maxCapacity, bool direct, long address)
    {
        if (Enabled(Keywords.FreeBuffer))
            WriteEvent(2, new object[] { allocatorType, size, maxFastCapacity, maxCapacity, direct, address });
    }

    [Event(3, Level = EventLevel.Informational, Keywords = Keywords.ReallocateBuffer,
        Message = "Triggered when a buffer is reallocated for resizing in an allocator. Will be followed by an AllocateBufferEvent")]
    private void ReallocateBuffer(string allocatorType, int size, int maxFastCapacity, int maxCapacity,
        bool direct, long address, int newCapacity)
    {
        if (Enabled(Keywords.ReallocateBuffer))
            WriteEvent(3, new object[] { allocatorType, size, maxFastCapacity, maxCapacity, direct, address, newCapacity });
    }
}
