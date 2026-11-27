/*
 * Copyright 2022 The Netty Project
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

using System.Collections.Generic;

namespace Netty.NET.Buffer;

internal sealed partial class AdaptivePoolingAllocator
{
    // CLR: the BCL has no ConcurrentSkipListMap. A locked sorted multimap replaces
    // ConcurrentSkipListIntObjMultimap under the source's type name. Equal keys put the
    // newest entry first, as the original base-list insertion does.
    internal sealed class ConcurrentSkipListChunkCache : IChunkCache
    {
        private readonly object _sync = new();
        private readonly SortedDictionary<int, List<Chunk>> _chunks = new();
        // CLR: per-instance CHUNK_REUSE_QUEUE.
        private readonly int _chunkReuseQueue;
        private int _count;
        private bool _disposed;

        internal ConcurrentSkipListChunkCache(int chunkReuseQueue)
        {
            _chunkReuseQueue = chunkReuseQueue;
        }

        public bool IsEmpty
        {
            get
            {
                lock (_sync)
                {
                    return _count == 0;
                }
            }
        }
        internal int Count
        {
            get
            {
                lock (_sync)
                {
                    return _count;
                }
            }
        }

        // CLR: multimap put/remove/iteration helpers over the sorted dictionary.
        private void Put(int key, Chunk chunk)
        {
            if (!_chunks.TryGetValue(key, out var list))
            {
                _chunks.Add(key, list = new());
            }
            list.Insert(0, chunk);
            _count++;
        }

        private void Remove(int key, Chunk chunk)
        {
            List<Chunk> list = _chunks[key];
            list.Remove(chunk);
            _count--;
            if (list.Count == 0)
            {
                _chunks.Remove(key);
            }
        }

        private List<(int Key, Chunk Chunk)> Entries()
        {
            var entries = new List<(int, Chunk)>(_count);

            foreach (var pair in _chunks)
            {
                foreach (var chunk in pair.Value)
                {
                    entries.Add((pair.Key, chunk));
                }
            }

            return entries;
        }

        public Chunk PollChunk(int size)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            lock (_sync)
            {
                if (_disposed || _count == 0)
                {
                    return null;
                }
                Chunk ceiling = null;
                int key = 0;
                foreach (var pair in _chunks)
                {
                    if (pair.Key >= size)
                    {
                        key = pair.Key;
                        ceiling = pair.Value[0];
                        break;
                    }
                }
                if (ceiling != null)
                {
                    Remove(key, ceiling);
                    Chunk chunk = ceiling;
                    if (chunk.HasUnprocessedFreelistEntries)
                    {
                        chunk.ProcessFreelistEntries();
                    }
                    return chunk;
                }

                Chunk bestChunk = null;
                int bestRemainingCapacity = 0;
                foreach (var entry in Entries())
                {
                    Chunk chunk = entry.Chunk;
                    if (chunk.HasUnprocessedFreelistEntries)
                    {
                        Remove(entry.Key, chunk);
                        chunk.ProcessFreelistEntries();
                        int remainingCapacity = chunk.RemainingCapacity;
                        if (remainingCapacity >= size &&
                                (bestChunk == null || remainingCapacity > bestRemainingCapacity))
                        {
                            if (bestChunk != null)
                            {
                                Put(bestRemainingCapacity, bestChunk);
                            }
                            bestChunk = chunk;
                            bestRemainingCapacity = remainingCapacity;
                        }
                        else
                        {
                            Put(remainingCapacity, chunk);
                        }
                    }
                }

                return bestChunk;
            }
        }

        public bool OfferChunk(Chunk chunk)
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            lock (_sync)
            {
                if (_disposed)
                {
                    return false;
                }
                Put(chunk.RemainingCapacity, chunk);

                int size = _count;
                while (size > _chunkReuseQueue)
                {
                    int key = -1;
                    Chunk toDeallocate = null;
                    foreach (var entry in _chunks)
                    {
                        foreach (Chunk candidate in entry.Value)
                        {
                            if (candidate.RefCnt == 1)
                            {
                                toDeallocate = candidate;
                                key = entry.Key;
                                break;
                            }
                        }
                        if (toDeallocate != null)
                        {
                            break;
                        }
                    }
                    if (toDeallocate == null)
                    {
                        break;
                    }
                    Remove(key, toDeallocate);
                    toDeallocate.MarkToDeallocate();
                    size = _count;
                }
                return true;
            }
        }

        public void Dispose()
        {
            using var events = BufferAllocatorEventSource.Log.BeginOperation();
            lock (_sync)
            {
                _disposed = true;
                foreach (var entry in Entries())
                {
                    entry.Chunk.MarkToDeallocate();
                }
                _chunks.Clear();
                _count = 0;
            }
        }
    }
}
