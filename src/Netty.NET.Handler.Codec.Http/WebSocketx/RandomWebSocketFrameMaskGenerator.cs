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

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * {@link WebSocketFrameMaskGenerator} implementation which returns a random int for masking.
 */
// CLR: ThreadLocalRandom.current().nextInt() (any of the 2^32 int values) is the low 32 bits of
// Random.Shared.NextInt64(0, 1L << 32): Random.Shared is a "thread-safe Random instance that may be used concurrently
// from any thread" (https://learn.microsoft.com/en-us/dotnet/api/system.random.shared?view=net-10.0), as
// ThreadLocalRandom serves every thread, and NextInt64(minValue, maxValue) returns an integer "greater than or equal to
// minValue and less than maxValue"
// (https://learn.microsoft.com/en-us/dotnet/api/system.random.nextint64?view=net-10.0), so every uint value is
// possible.
public sealed class RandomWebSocketFrameMaskGenerator : IWebSocketFrameMaskGenerator
{
    public static readonly RandomWebSocketFrameMaskGenerator INSTANCE = new RandomWebSocketFrameMaskGenerator();

    private RandomWebSocketFrameMaskGenerator()
    {
    }

    public int NextMask()
    {
        return unchecked((int) Random.Shared.NextInt64(0, 1L << 32));
    }
}
