/*
 * Copyright 2014 The Netty Project
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

namespace Netty.NET.Handler.Codec.Mqtt;

/**
 * A {@link MqttIdentifierRejectedException} which is thrown when a CONNECT request contains invalid client identifier,
 * will topic name, or username, or when a PUBLISH message contains an invalid topic name.
 */
// CLR: serialVersionUID is JVM serialization metadata and has no counterpart (as DecoderException in codec-base);
// Throwable is Exception (docs/codec-base-porting.md).
public sealed class MqttIdentifierRejectedException : DecoderException
{
    /**
     * Creates a new instance
     */
    public MqttIdentifierRejectedException()
    {
    }

    /**
     * Creates a new instance
     */
    public MqttIdentifierRejectedException(string message, Exception cause) : base(message, cause)
    {
    }

    /**
     * Creates a new instance
     */
    public MqttIdentifierRejectedException(string message) : base(message)
    {
    }

    /**
     * Creates a new instance
     */
    public MqttIdentifierRejectedException(Exception cause) : base(cause)
    {
    }

}
