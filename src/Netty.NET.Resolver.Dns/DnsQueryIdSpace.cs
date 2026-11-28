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
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Netty.NET.Common.Internal;

namespace Netty.NET.Resolver.Dns;

/**
 * Special data-structure that will allow to retrieve the next query id to use, while still guarantee some sort
 * of randomness.
 * The query id will be between 0 (inclusive) and 65535 (inclusive) as defined by the RFC.
 */
// CLR: java.security.SecureRandom is RandomNumberGenerator : nextInt(bound) is the static
// RandomNumberGenerator.GetInt32(toExclusive), which uses "a cryptographically strong random number generator"
// (https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.randomnumbergenerator.getint32?view=net-10.0),
// and nextBoolean() is GetInt32(2) != 0. The static generator needs no instance, so the shared SecureRandom field
// and the random parameters of newBucket and DnsQueryIdRange have no counterpart. In nextId() the locals bucket
// and id after the loop are newBucket and nextId, because C# rejects a local that a nested scope of the same
// method also declares (CS0136, docs/codec-compression-porting.md). Java assert is Debug.Assert and
// IllegalArgumentException/IllegalStateException are ArgumentException/InvalidOperationException.
internal sealed class DnsQueryIdSpace
{
    private const int MAX_ID = 65535;
    private const int BUCKETS = 4;
    // Each bucket is 16kb of size.
    private const int BUCKET_SIZE = (MAX_ID + 1) / BUCKETS;

    // If there are other buckets left that have at least 500 usable ids we will drop an unused bucket.
    private const int BUCKET_DROP_THRESHOLD = 500;
    private readonly DnsQueryIdRange[] idBuckets = new DnsQueryIdRange[BUCKETS];

    internal DnsQueryIdSpace()
    {
        Debug.Assert(idBuckets.Length == MathUtil.FindNextPositivePowerOfTwo(idBuckets.Length));
        // We start with 1 bucket.
        idBuckets[0] = NewBucket(0);
    }

    private static DnsQueryIdRange NewBucket(int idBucketsIdx)
    {
        return new DnsQueryIdRange(BUCKET_SIZE, idBucketsIdx * BUCKET_SIZE);
    }

    /**
     * Returns the next ID to use for a query or {@code -1} if there is none left to use.
     *
     * @return next id to use.
     */
    internal int NextId()
    {
        int freeIdx = -1;

        for (int bucketIdx = 0; bucketIdx < idBuckets.Length; bucketIdx++)
        {
            DnsQueryIdRange bucket = idBuckets[bucketIdx];
            if (bucket != null)
            {
                int id = bucket.NextId();
                if (id != -1)
                {
                    return id;
                }
            }
            else if (freeIdx == -1 ||
                    // Let's make it somehow random which free slot is used.
                    RandomNumberGenerator.GetInt32(2) != 0)
            {
                // We have a slot that we can use to create a new bucket if we need to.
                freeIdx = bucketIdx;
            }
        }

        if (freeIdx == -1)
        {
            // No ids left and no slot left to create a new bucket.
            return -1;
        }

        // We still have some slots free to store a new bucket. Let's do this now and use it to generate the next id.
        DnsQueryIdRange newBucket = NewBucket(freeIdx);
        idBuckets[freeIdx] = newBucket;
        int nextId = newBucket.NextId();
        Debug.Assert(nextId >= 0);
        return nextId;
    }

    /**
     * Push back the id, so it can be used again for the next query.
     *
     * @param id the id.
     */
    internal void PushId(int id)
    {
        int bucketIdx = id / BUCKET_SIZE;
        if (bucketIdx >= idBuckets.Length)
        {
            throw new ArgumentException("id too large: " + id.ToString(CultureInfo.InvariantCulture));
        }

        DnsQueryIdRange bucket = idBuckets[bucketIdx];
        Debug.Assert(bucket != null);
        bucket.PushId(id);

        if (bucket.UsableIds() == bucket.MaxUsableIds())
        {
            // All ids are usable in this bucket. Let's check if there are other buckets left that have still
            // some space left and if so drop this bucket.
            for (int idx = 0; idx < idBuckets.Length; idx++)
            {
                if (idx != bucketIdx)
                {
                    DnsQueryIdRange otherBucket = idBuckets[idx];
                    if (otherBucket != null && otherBucket.UsableIds() > BUCKET_DROP_THRESHOLD)
                    {
                        // Drop bucket on the floor to reduce memory usage, there is another bucket left we can
                        // use that still has enough ids to use.
                        idBuckets[bucketIdx] = null;
                        return;
                    }
                }
            }
        }
    }

    /**
     * Return how much more usable ids are left.
     *
     * @return the number of ids that are left for usage.
     */
    internal int UsableIds()
    {
        int usableIds = 0;

        foreach (DnsQueryIdRange bucket in idBuckets)
        {
            // If there is nothing stored in the index yet we can assume the whole bucket is usable
            usableIds += bucket == null ? BUCKET_SIZE : bucket.UsableIds();
        }

        return usableIds;
    }

    /**
     * Return the maximum number of ids that are supported.
     *
     * @return the maximum number of ids.
     */
    internal int MaxUsableIds()
    {
        return BUCKET_SIZE * idBuckets.Length;
    }

    /**
     * Provides a query if from a range of possible ids.
     */
    // CLR: the ids stay a short[] as in the source: (short) id wraps the ids 32768..65535 to negative values (unchecked),
    // and id & 0xFFFF restores the unsigned id, the Java byte/short sign rule (porting rule 6).
    private sealed class DnsQueryIdRange
    {

        // Holds all possible ids which are stored as unsigned shorts
        private readonly short[] ids;
        private readonly int startId;
        private int count;

        internal DnsQueryIdRange(int bucketSize, int startId)
        {
            this.ids = new short[bucketSize];
            this.startId = startId;

            for (int v = startId; v < bucketSize + startId; v++)
            {
                PushId(v);
            }
        }

        /**
         * Returns the next ID to use for a query or {@code -1} if there is none left to use.
         *
         * @return next id to use.
         */
        internal int NextId()
        {
            Debug.Assert(count >= 0);
            if (count == 0)
            {
                return -1;
            }

            short id = ids[count - 1];
            count--;

            return id & 0xFFFF;
        }

        /**
         * Push back the id, so it can be used again for the next query.
         *
         * @param id the id.
         */
        internal void PushId(int id)
        {
            if (count == ids.Length)
            {
                throw new InvalidOperationException("overflow");
            }

            Debug.Assert(id <= startId + ids.Length && id >= startId);
            // pick a slot for our index, and whatever was in that slot before will get moved to the tail.
            int insertionPosition = RandomNumberGenerator.GetInt32(count + 1);
            short moveId = ids[insertionPosition];
            short insertId = unchecked((short) id);

            // Assert that the ids are different or its the same index.
            Debug.Assert(moveId != insertId || insertionPosition == count);

            ids[count] = moveId;
            ids[insertionPosition] = insertId;
            count++;
        }

        /**
         * Return how much more usable ids are left.
         *
         * @return the number of ids that are left for usage.
         */
        internal int UsableIds()
        {
            return count;
        }

        /**
         * Return the maximum number of ids that are supported.
         *
         * @return the maximum number of ids.
         */
        internal int MaxUsableIds()
        {
            return ids.Length;
        }
    }
}
