/*
 * Copyright 2016 The Netty Project
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

using System.Threading.Tasks;

namespace Netty.NET.Transport.Channels.Sockets;

// CLR: a Task reports completion; caller-supplied ChannelPromise overloads are folded into each operation.
/**
 * A duplex {@link Channel} that has two sides that can be shutdown independently.
 */
public interface IDuplexChannel : IChannel
{
    /**
     * Returns {@code true} if and only if the remote peer shut down its output so that no more
     * data is received from this channel.  Note that the semantic of this method is different from
     * that of {@link Socket#shutdownInput()} and {@link Socket#isInputShutdown()}.
     */
    bool IsInputShutdown { get; }

    /**
     * @see Socket#shutdownInput()
     */
    /**
     * Will shutdown the input and notify {@link ChannelPromise}.
     *
     * @see Socket#shutdownInput()
     */
    Task ShutdownInputAsync();

    /**
     * @see Socket#isOutputShutdown()
     */
    bool IsOutputShutdown { get; }

    /**
     * @see Socket#shutdownOutput()
     */
    /**
     * Will shutdown the output and notify {@link ChannelPromise}.
     *
     * @see Socket#shutdownOutput()
     */
    Task ShutdownOutputAsync();

    /**
     * Determine if both the input and output of this channel have been shutdown.
     */
    bool IsShutdown { get; }

    /**
     * Will shutdown the input and output sides of this channel.
     * @return will be completed when both shutdown operations complete.
     */
    /**
     * Will shutdown the input and output sides of this channel.
     * @param promise will be completed when both shutdown operations complete.
     * @return will be completed when both shutdown operations complete.
     */
    Task ShutdownAsync();

}
