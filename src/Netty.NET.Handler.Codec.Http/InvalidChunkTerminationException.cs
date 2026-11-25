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

namespace Netty.NET.Handler.Codec.Http;

/**
 * Thrown when HTTP chunks could not be parsed, typically due to incorrect use of CR LF delimiters.
 * <p>
 * <a href="https://datatracker.ietf.org/doc/html/rfc9112#name-chunked-transfer-coding">RFC 9112</a>
 * specifies that chunk bodies must be terminated in a CR LF pair,
 * and that the delimiter must follow the given chunk-size number of octets in chunk-data.
 */
// CLR: serialVersionUID has no counterpart, Throwable is Exception and the cause-only constructor keeps the
// codec-base detail message (docs/codec-base-porting.md, Exceptions).
public sealed class InvalidChunkTerminationException : CorruptedFrameException
{

    public InvalidChunkTerminationException()
        : base("Chunk data sections must be terminated by a CR LF octet pair")
    {
    }

    public InvalidChunkTerminationException(string message, Exception cause) : base(message, cause)
    {
    }

    public InvalidChunkTerminationException(string message) : base(message)
    {
    }

    public InvalidChunkTerminationException(Exception cause) : base(cause)
    {
    }
}
