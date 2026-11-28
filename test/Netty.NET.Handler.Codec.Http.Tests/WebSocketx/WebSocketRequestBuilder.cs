/*
 * Copyright 2012 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version
 * 2.0 (the "License"); you may not use this file except in compliance with the
 * License. You may obtain a copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations under
 * the License.
 */

using System.Collections.Generic;
using Netty.NET.Handler.Codec.Http.WebSocketx;
using static Netty.NET.Handler.Codec.Http.HttpVersion;

namespace Netty.NET.Handler.Codec.Http.Tests.WebSocketx;

// CLR: the CharSequence parameters are IEnumerable<char>, so both a string and an AsciiString (the HttpHeaderValues
// constants, an ICharSequence) bind to them, as codec-base's StringEncoder takes CharSequence messages
// (docs/codec-base-porting.md, Text); toString() is ToString(). The static import of HttpVersion.HTTP_1_1 is using
// static.
public class WebSocketRequestBuilder
{

    private HttpVersion httpVersion;
    private HttpMethod method;
    private string uri;
    private string host;
    private string upgrade;
    private string connection;
    private string key;
    private string origin;
    private WebSocketVersion version;

    public WebSocketRequestBuilder HttpVersion(HttpVersion httpVersion)
    {
        this.httpVersion = httpVersion;
        return this;
    }

    public WebSocketRequestBuilder Method(HttpMethod method)
    {
        this.method = method;
        return this;
    }

    public WebSocketRequestBuilder Uri(IEnumerable<char> uri)
    {
        if (uri == null)
        {
            this.uri = null;
        }
        else
        {
            this.uri = uri.ToString();
        }

        return this;
    }

    public WebSocketRequestBuilder Host(IEnumerable<char> host)
    {
        if (host == null)
        {
            this.host = null;
        }
        else
        {
            this.host = host.ToString();
        }

        return this;
    }

    public WebSocketRequestBuilder Upgrade(IEnumerable<char> upgrade)
    {
        if (upgrade == null)
        {
            this.upgrade = null;
        }
        else
        {
            this.upgrade = upgrade.ToString();
        }

        return this;
    }

    public WebSocketRequestBuilder Connection(IEnumerable<char> connection)
    {
        if (connection == null)
        {
            this.connection = null;
        }
        else
        {
            this.connection = connection.ToString();
        }

        return this;
    }

    public WebSocketRequestBuilder Key(IEnumerable<char> key)
    {
        if (key == null)
        {
            this.key = null;
        }
        else
        {
            this.key = key.ToString();
        }

        return this;
    }

    public WebSocketRequestBuilder Origin(IEnumerable<char> origin)
    {
        if (origin == null)
        {
            this.origin = null;
        }
        else
        {
            this.origin = origin.ToString();
        }

        return this;
    }

    public WebSocketRequestBuilder Version13()
    {
        version = WebSocketVersion.V13;
        return this;
    }

    public WebSocketRequestBuilder Version8()
    {
        version = WebSocketVersion.V08;
        return this;
    }

    public WebSocketRequestBuilder Version00()
    {
        version = null;
        return this;
    }

    public WebSocketRequestBuilder NoVersion()
    {
        return this;
    }

    public IFullHttpRequest Build()
    {
        IFullHttpRequest req = new DefaultFullHttpRequest(httpVersion, method, uri);
        HttpHeaders headers = req.Headers();

        if (host != null)
        {
            headers.Set(HttpHeaderNames.HOST, host);
        }

        if (upgrade != null)
        {
            headers.Set(HttpHeaderNames.UPGRADE, upgrade);
        }

        if (connection != null)
        {
            headers.Set(HttpHeaderNames.CONNECTION, connection);
        }

        if (key != null)
        {
            headers.Set(HttpHeaderNames.SEC_WEBSOCKET_KEY, key);
        }

        if (origin != null)
        {
            if (version == WebSocketVersion.V13 || version == WebSocketVersion.V00)
            {
                headers.Set(HttpHeaderNames.ORIGIN, origin);
            }
            else
            {
                headers.Set(HttpHeaderNames.SEC_WEBSOCKET_ORIGIN, origin);
            }
        }

        if (version != null)
        {
            headers.Set(HttpHeaderNames.SEC_WEBSOCKET_VERSION, version.ToHttpHeaderValue());
        }

        return req;
    }

    public static IHttpRequest Successful()
    {
        return new WebSocketRequestBuilder().HttpVersion(HTTP_1_1)
                .Method(HttpMethod.GET)
                .Uri("/test")
                .Host("server.example.com")
                .Upgrade(HttpHeaderValues.WEBSOCKET)
                .Connection(HttpHeaderValues.UPGRADE)
                .Key("dGhlIHNhbXBsZSBub25jZQ==")
                .Origin("http://example.com")
                .Version13()
                .Build();
    }
}
