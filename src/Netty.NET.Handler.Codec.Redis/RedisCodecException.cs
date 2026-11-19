/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;

namespace Netty.NET.Handler.Codec.Redis;

/**
 * An {@link Exception} which is thrown by {@link RedisEncoder} or {@link RedisDecoder}.
 */
// CLR: serialVersionUID is JVM serialization metadata and has no counterpart; Throwable is Exception, and the
// cause-only constructor keeps the Java detail message through CodecException (docs/codec-base-porting.md, Shared CLR
// adaptations).
[UnstableApi]
public sealed class RedisCodecException : CodecException
{

    /**
     * Creates a new instance.
     */
    public RedisCodecException(string message) : base(message)
    {
    }

    /**
     * Creates a new instance.
     */
    public RedisCodecException(Exception cause) : base(cause)
    {
    }
}
