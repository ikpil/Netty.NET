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

using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http;

/**
 * The default {@link HttpMessage} implementation.
 */
// CLR: the @Deprecated DefaultHttpMessage(HttpVersion, boolean, boolean), whose @deprecated text names
// DefaultHttpMessage(HttpVersion, HttpHeadersFactory), and the @Deprecated getProtocolVersion() are not ported.
// setProtocolVersion is virtual so that the subclasses narrow its return type with covariant override returns
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override); equals and hashCode are
// Equals/GetHashCode.
public abstract class DefaultHttpMessage : DefaultHttpObject, IHttpMessage
{
    private const int HASH_CODE_PRIME = 31;
    private HttpVersion version;
    private readonly HttpHeaders headers;

    /**
     * Creates a new instance.
     */
    protected DefaultHttpMessage(HttpVersion version) : this(version, DefaultHttpHeadersFactory.HeadersFactory())
    {
    }

    /**
     * Creates a new instance.
     */
    protected DefaultHttpMessage(HttpVersion version, IHttpHeadersFactory headersFactory)
        : this(version, headersFactory.NewHeaders())
    {
    }

    /**
     * Creates a new instance.
     */
    protected DefaultHttpMessage(HttpVersion version, HttpHeaders headers)
    {
        this.version = CheckNotNull(version, "version");
        this.headers = CheckNotNull(headers, "headers");
    }

    public virtual HttpHeaders Headers()
    {
        return headers;
    }

    public virtual HttpVersion ProtocolVersion()
    {
        return version;
    }

    public override int GetHashCode()
    {
        int result = 1;
        result = HASH_CODE_PRIME * result + headers.GetHashCode();
        result = HASH_CODE_PRIME * result + version.GetHashCode();
        result = HASH_CODE_PRIME * result + base.GetHashCode();
        return result;
    }

    public override bool Equals(object o)
    {
        if (!(o is DefaultHttpMessage))
        {
            return false;
        }

        DefaultHttpMessage other = (DefaultHttpMessage) o;

        return Headers().Equals(other.Headers()) &&
               ProtocolVersion().Equals(other.ProtocolVersion()) &&
               base.Equals(o);
    }

    public virtual IHttpMessage SetProtocolVersion(HttpVersion version)
    {
        this.version = CheckNotNull(version, "version");
        return this;
    }
}
