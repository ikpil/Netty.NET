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

using Netty.NET.Common;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Defines the common schemes used for the HTTP protocol as defined by
 * <a href="https://tools.ietf.org/html/rfc7230">rfc7230</a>.
 */
public sealed class HttpScheme
{

    /**
     * Scheme for non-secure HTTP connection.
     */
    public static readonly HttpScheme HTTP = new HttpScheme(80, "http");

    /**
     * Scheme for secure HTTP connection.
     */
    public static readonly HttpScheme HTTPS = new HttpScheme(443, "https");

    private readonly int port;
    private readonly AsciiString name;

    private HttpScheme(int port, string name)
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
        if (!(o is HttpScheme))
        {
            return false;
        }

        HttpScheme other = (HttpScheme) o;
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
