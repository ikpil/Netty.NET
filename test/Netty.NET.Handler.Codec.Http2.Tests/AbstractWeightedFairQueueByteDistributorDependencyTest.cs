/*
 * Copyright 2017 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Collections.Generic;
using Moq;
using TestStreamByteDistributorStreamState = Netty.NET.Handler.Codec.Http2.Tests.Http2TestUtil.TestStreamByteDistributorStreamState;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: the package-private abstract fixture is a public abstract class (xUnit test classes and their bases must be
// public) whose package-private members are internal. IntObjectMap/IntObjectHashMap are Dictionary<int, ...> (owner
// decision 2026-11-19). The @Mock writer, which the subclasses initialize with MockitoAnnotations.initMocks, is a Moq
// Mock<IStreamByteDistributor.IWriter> field; Answer<Void> writeAnswer(boolean) returns the Action<IHttp2Stream, int>
// that the subclasses pass to the Moq Write setup's Callback, whose parameters are the invocation's arguments
// (in.getArgument(0)/(1); Moq Quickstart, https://github.com/devlooped/moq/wiki/Quickstart).
public abstract class AbstractWeightedFairQueueByteDistributorDependencyTest
{
    internal IHttp2Connection connection;
    internal WeightedFairQueueByteDistributor distributor;
    private readonly Dictionary<int, TestStreamByteDistributorStreamState> stateMap =
            new Dictionary<int, TestStreamByteDistributorStreamState>();

    internal readonly Mock<IStreamByteDistributor.IWriter> writer = new Mock<IStreamByteDistributor.IWriter>();

    internal IHttp2Stream Stream(int streamId)
    {
        return connection.Stream(streamId);
    }

    internal Action<IHttp2Stream, int> WriteAnswer(bool closeIfNoFrame)
    {
        return (stream, numBytes) =>
        {
            TestStreamByteDistributorStreamState state = stateMap[stream.Id()];
            state.pendingBytes -= numBytes;
            state.hasFrame = state.pendingBytes > 0;
            state.isWriteAllowed = state.hasFrame;

            if (closeIfNoFrame && !state.hasFrame)
            {
                stream.Close();
            }

            distributor.UpdateStreamableBytes(state);
        };
    }

    internal void InitState(int streamId, long streamableBytes, bool hasFrame)
    {
        InitState(streamId, streamableBytes, hasFrame, hasFrame);
    }

    internal void InitState(int streamId, long pendingBytes, bool hasFrame,
                              bool isWriteAllowed)
    {
        IHttp2Stream stream = Stream(streamId);
        TestStreamByteDistributorStreamState state = new TestStreamByteDistributorStreamState(stream, pendingBytes,
                hasFrame, isWriteAllowed);
        stateMap[streamId] = state;
        distributor.UpdateStreamableBytes(state);
    }

    internal void SetPriority(int streamId, int parent, int weight, bool exclusive)
    {
        distributor.UpdateDependencyTree(streamId, parent, (short) weight, exclusive);
    }
}
