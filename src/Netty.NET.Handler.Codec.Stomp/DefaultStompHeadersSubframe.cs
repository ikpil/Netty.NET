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

using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;

namespace Netty.NET.Handler.Codec.Stomp;

/**
 * Default implementation of {@link StompHeadersSubframe}.
 */
// CLR: the package-private constructor is internal. checkNotNull(command, "command") is gone because StompCommand is a
// C# enum, a value type that cannot be null (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum). Inside this class the simple name DecoderResult is the
// decoderResult() method in an expression, so DecoderResult.SUCCESS is reached through global::
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/namespace-alias-qualifier). The enum
// command prints its constant name in toString(), as Enum.toString() does
// (https://learn.microsoft.com/en-us/dotnet/api/system.enum.tostring).
public class DefaultStompHeadersSubframe : IStompHeadersSubframe
{

    protected readonly StompCommand command;
    protected DecoderResult decoderResult = global::Netty.NET.Handler.Codec.DecoderResult.SUCCESS;
    protected readonly DefaultStompHeaders headers;

    public DefaultStompHeadersSubframe(StompCommand command) : this(command, null)
    {
    }

    internal DefaultStompHeadersSubframe(StompCommand command, DefaultStompHeaders headers)
    {
        this.command = command;
        this.headers = headers == null ? new DefaultStompHeaders() : headers;
    }

    public StompCommand Command()
    {
        return command;
    }

    public IStompHeaders Headers()
    {
        return headers;
    }

    public DecoderResult DecoderResult()
    {
        return decoderResult;
    }

    public void SetDecoderResult(DecoderResult decoderResult)
    {
        this.decoderResult = decoderResult;
    }

    public override string ToString()
    {
        return "StompFrame{" +
            "command=" + command +
            ", headers=" + headers +
            '}';
    }
}
