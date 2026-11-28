/*
 * Copyright 2017 The Netty Project
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

using Netty.NET.Common;

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * Defines the common schemes used for the WebSocket protocol as defined by
 * <a href="https://tools.ietf.org/html/rfc6455">rfc6455</a>.
 */
// CLR: equals(Object) and hashCode() override Equals(object) and GetHashCode(); name() and port() are Name() and
// Port(), as core's HttpScheme.
public sealed class WebSocketScheme
{
    /**
     * Scheme for non-secure WebSocket connection.
     */
    public static readonly WebSocketScheme WS = new WebSocketScheme(80, "ws");

    /**
     * Scheme for secure WebSocket connection.
     */
    public static readonly WebSocketScheme WSS = new WebSocketScheme(443, "wss");

    private readonly int port;
    private readonly AsciiString name;

    private WebSocketScheme(int port, string name)
    {
        this.port = port;
        this.name = AsciiString.Cached(name);
    }

    public AsciiString Name()
    {
        return name;
    }

    public int Port()
    {
        return port;
    }

    public override bool Equals(object o)
    {
        if (!(o is WebSocketScheme))
        {
            return false;
        }

        WebSocketScheme other = (WebSocketScheme) o;
        return other.Port() == port && other.Name().Equals(name);
    }

    public override int GetHashCode()
    {
        return port * 31 + name.GetHashCode();
    }

    public override string ToString()
    {
        return name.ToString();
    }
}
