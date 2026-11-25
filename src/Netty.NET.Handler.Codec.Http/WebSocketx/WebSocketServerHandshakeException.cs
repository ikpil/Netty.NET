/*
 * Copyright 2020 The Netty Project
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

namespace Netty.NET.Handler.Codec.Http.WebSocketx;

/**
 * Server exception during handshaking process.
 *
 * <p><b>IMPORTANT</b>: This exception does not contain any {@link ReferenceCounted} fields
 * e.g. {@link FullHttpRequest}, so no special treatment is needed.
 */
// CLR: HttpRequest is IHttpRequest (the core message interfaces); serialVersionUID is JVM serialization metadata and
// has no counterpart.
public sealed class WebSocketServerHandshakeException : WebSocketHandshakeException
{

    private readonly IHttpRequest request;

    public WebSocketServerHandshakeException(string message) : this(message, null)
    {
    }

    public WebSocketServerHandshakeException(string message, IHttpRequest httpRequest) : base(message)
    {
        if (httpRequest != null)
        {
            request = new DefaultHttpRequest(httpRequest.ProtocolVersion(), httpRequest.Method(),
                                             httpRequest.Uri(), httpRequest.Headers());
        }
        else
        {
            request = null;
        }
    }

    /**
     * Returns a {@link HttpRequest request} if exception occurs during request validation otherwise {@code null}.
     */
    public IHttpRequest Request()
    {
        return request;
    }
}
