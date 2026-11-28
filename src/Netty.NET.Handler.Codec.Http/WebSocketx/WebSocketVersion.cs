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

using System;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * <p>
 * Versions of the web socket specification.
 * </p>
 * <p>
 * A specification is tied to one wire protocol version but a protocol version may have use by more than 1 version of
 * the specification.
 * </p>
 */
// CLR: the enum has a constructor, a field and methods, so it is a sealed class with static readonly instances
// (owner rule; a C# enum cannot define methods,
// https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/enum), as codec-base's Base64Dialect.
// ToString() returns the constant name as Enum.toString() does; the implicit values()/valueOf(String) members are
// not added because nothing uses them. The package-private toAsciiString() is internal, and IllegalStateException
// is InvalidOperationException.
public sealed class WebSocketVersion
{
    public static readonly WebSocketVersion UNKNOWN =
        new WebSocketVersion(nameof(UNKNOWN), AsciiString.Cached(StringUtil.EMPTY_STRING));

    /**
     * <a href= "https://tools.ietf.org/html/draft-ietf-hybi-thewebsocketprotocol-00"
     * >draft-ietf-hybi-thewebsocketprotocol- 00</a>.
     */
    public static readonly WebSocketVersion V00 = new WebSocketVersion(nameof(V00), AsciiString.Cached("0"));

    /**
     * <a href= "https://tools.ietf.org/html/draft-ietf-hybi-thewebsocketprotocol-07"
     * >draft-ietf-hybi-thewebsocketprotocol- 07</a>
     */
    public static readonly WebSocketVersion V07 = new WebSocketVersion(nameof(V07), AsciiString.Cached("7"));

    /**
     * <a href= "https://tools.ietf.org/html/draft-ietf-hybi-thewebsocketprotocol-10"
     * >draft-ietf-hybi-thewebsocketprotocol- 10</a>
     */
    public static readonly WebSocketVersion V08 = new WebSocketVersion(nameof(V08), AsciiString.Cached("8"));

    /**
     * <a href="https://tools.ietf.org/html/rfc6455 ">RFC 6455</a>. This was originally <a href=
     * "https://tools.ietf.org/html/draft-ietf-hybi-thewebsocketprotocol-17" >draft-ietf-hybi-thewebsocketprotocol-
     * 17</a>
     */
    public static readonly WebSocketVersion V13 = new WebSocketVersion(nameof(V13), AsciiString.Cached("13"));

    private readonly string name;
    private readonly AsciiString headerValue;

    private WebSocketVersion(string name, AsciiString headerValue)
    {
        this.name = name;
        this.headerValue = headerValue;
    }

    /**
     * @return Value for HTTP Header 'Sec-WebSocket-Version'
     */
    public string ToHttpHeaderValue()
    {
        return ToAsciiString().ToString();
    }

    internal AsciiString ToAsciiString()
    {
        if (this == UNKNOWN)
        {
            // Let's special case this to preserve behaviour
            throw new InvalidOperationException("Unknown web socket version: " + this);
        }

        return headerValue;
    }

    public override string ToString()
    {
        return name;
    }
}
