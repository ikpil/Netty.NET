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

using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Buffer;

public static partial class ByteBufUtil
{
    // CLR: separate holders avoid a cycle when an allocator singleton initializes before
    // the global selector. Utility calls alone need not construct any allocator.
    internal static IByteBufAllocator DefaultAllocator => DefaultAllocatorHolder.Instance;
    internal static bool PreferDirect => DirectPreference.Value;

    private static class DirectPreference
    {
        // We should always prefer direct buffers by default if we can use a Cleaner to release direct buffers.
        // CLR: SafeHandle-backed native owners provide reliable release without JVM Cleaner detection.
        internal static readonly bool Value = !SystemPropertyUtil.GetBoolean("io.netty.noPreferDirect", false);
        static DirectPreference() { }
    }

    private static class DefaultAllocatorHolder
    {
        internal static readonly IByteBufAllocator Instance = Create();
        static DefaultAllocatorHolder() { }

        private static IByteBufAllocator Create()
        {
            string allocType = SystemPropertyUtil.Get("io.netty.allocator.type", "adaptive");
            IInternalLogger logger = InternalLoggerFactory.GetInstance(typeof(ByteBufUtil));
            IByteBufAllocator allocator;
            if (allocType == "unpooled")
            {
                allocator = UnpooledByteBufAllocator.Default;
                logger.Debug("-Dio.netty.allocator.type: {}", allocType);
            }
            else if (allocType == "pooled")
            {
                allocator = PooledByteBufAllocator.Default;
                logger.Debug("-Dio.netty.allocator.type: {}", allocType);
            }
            else if (allocType == "adaptive")
            {
                allocator = new AdaptiveByteBufAllocator();
                logger.Debug("-Dio.netty.allocator.type: {}", allocType);
            }
            else
            {
                allocator = PooledByteBufAllocator.Default;
                logger.Debug("-Dio.netty.allocator.type: pooled (unknown: {})", allocType);
            }
            return allocator;
        }
    }
}
