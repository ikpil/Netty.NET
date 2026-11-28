/*
 * Copyright 2015 The Netty Project
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

namespace Netty.NET.Handler.Codec;

/**
 * Result of detecting a protocol.
 *
 * @param <T> the type of the protocol
 */
// CLR: T is unconstrained so that a value-type protocol (for example a ported Java enum) can be detected; the
// non-detected results then hold default(T), which is null for a reference type as in the source.
public sealed class ProtocolDetectionResult<T>
{
    // CLR: the source shares one raw instance per state through an unchecked cast; a static field of a generic class
    // exists once per constructed type, so each ProtocolDetectionResult<T> holds its own NEEDS_MORE_DATA and INVALID.
    private static readonly ProtocolDetectionResult<T> NEEDS_MORE_DATA =
            new ProtocolDetectionResult<T>(ProtocolDetectionState.NEEDS_MORE_DATA, default);
    private static readonly ProtocolDetectionResult<T> INVALID =
            new ProtocolDetectionResult<T>(ProtocolDetectionState.INVALID, default);

    private readonly ProtocolDetectionState state;
    private readonly T result;

    /**
     * Returns a {@link ProtocolDetectionResult} that signals that more data is needed to detect the protocol.
     */
    public static ProtocolDetectionResult<T> NeedsMoreData()
    {
        return NEEDS_MORE_DATA;
    }

    /**
     * Returns a {@link ProtocolDetectionResult} that signals the data was invalid for the protocol.
     */
    public static ProtocolDetectionResult<T> Invalid()
    {
        return INVALID;
    }

    /**
     * Returns a {@link ProtocolDetectionResult} which holds the detected protocol.
     */
    // CLR: ObjectUtil.CheckNotNull requires a class type argument, so checkNotNull(protocol, "protocol") is written out
    // with the same ArgumentNullException.
    public static ProtocolDetectionResult<T> Detected(T protocol)
    {
        if (protocol == null)
        {
            throw new ArgumentNullException("protocol");
        }

        return new ProtocolDetectionResult<T>(ProtocolDetectionState.DETECTED, protocol);
    }

    private ProtocolDetectionResult(ProtocolDetectionState state, T result)
    {
        this.state = state;
        this.result = result;
    }

    /**
     * Return the {@link ProtocolDetectionState}. If the state is {@link ProtocolDetectionState#DETECTED} you
     * can retrieve the protocol via {@link #detectedProtocol()}.
     */
    public ProtocolDetectionState State()
    {
        return state;
    }

    /**
     * Returns the protocol if {@link #state()} returns {@link ProtocolDetectionState#DETECTED}, otherwise {@code null}.
     */
    public T DetectedProtocol()
    {
        return result;
    }
}
