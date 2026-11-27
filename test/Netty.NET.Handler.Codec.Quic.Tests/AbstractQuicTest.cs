/*
 * Copyright 2020 The Netty Project
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
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Handler.Codec.Quic.Tests;

// CLR: @Timeout(10) is the Timeout of each test attribute (TIMEOUT, in milliseconds; xUnit applies it to async tests).
// @BeforeAll ensureAvailability() is the static constructor, which runs once before the first test of a fixture.
// Executor is IExecutor: ImmediateExecutor.INSTANCE, and Executors.newSingleThreadExecutor() is a DefaultEventExecutor
// (one daemon thread), shut down with ShutdownGracefullyAsync (ExecutorService.shutdown()). The executors are the rows of the
// [MemberData] of each parameterized test (@MethodSource("newSslTaskExecutors")); they are not serializable, so the
// rows are not enumerated at discovery. System.Net.Quic runs TLS on msquic's threads, so the QUIC codec builders accept
// the executor without using it (docs/codec-classes-quic-porting.md).
public abstract class AbstractQuicTest
{
    internal const int TIMEOUT = 10000;

    static AbstractQuicTest()
    {
        EnsureAvailability();
    }

    public static void EnsureAvailability()
    {
        Quic.EnsureAvailability();
    }

    public static IEnumerable<object[]> NewSslTaskExecutors()
    {
        return new[]
        {
            new object[] { ImmediateExecutor.INSTANCE },
            new object[] { new DefaultEventExecutor(new DefaultThreadFactory("sslTaskExecutor", true)) },
        };
    }

    internal static void Shutdown(IExecutor executor)
    {
        if (executor is IEventExecutorGroup group)
        {
            _ = group.ShutdownGracefullyAsync();
        }
    }
}
