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
using System.Globalization;
using Netty.NET.Common;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http;

/**
 * The request method of HTTP or its derived protocols, such as
 * <a href="https://en.wikipedia.org/wiki/Real_Time_Streaming_Protocol">RTSP</a> and
 * <a href="https://en.wikipedia.org/wiki/Internet_Content_Adaptation_Protocol">ICAP</a>.
 */
// CLR: Comparable<HttpMethod> is IComparable<HttpMethod>; String.compareTo is string.CompareOrdinal (see
// HttpVersion). A switch on a null String throws NullPointerException in Java; the C# switch takes the default
// branch, whose constructor throws ArgumentNullException from checkNotNull. The String passed to
// HttpHeaderValidationUtil.validateToken(CharSequence) is wrapped in the Common StringCharSequence, because a C#
// string is not an ICharSequence. IllegalArgumentException is ArgumentException and Integer.toHexString is the
// invariant lower-case "x" format. Java methods are virtual unless final, so name() and asciiName() are virtual
// (subclasses override asciiName(), which HttpUtil.validateRequestLineTokens re-validates for them).
public class HttpMethod : IComparable<HttpMethod>
{

    /**
     * The OPTIONS method represents a request for information about the communication options
     * available on the request/response chain identified by the Request-URI. This method allows
     * the client to determine the options and/or requirements associated with a resource, or the
     * capabilities of a server, without implying a resource action or initiating a resource
     * retrieval.
     */
    public static readonly HttpMethod OPTIONS = new HttpMethod(AsciiString.Cached("OPTIONS"));

    /**
     * The GET method means retrieve whatever information (in the form of an entity) is identified
     * by the Request-URI.  If the Request-URI refers to a data-producing process, it is the
     * produced data which shall be returned as the entity in the response and not the source text
     * of the process, unless that text happens to be the output of the process.
     */
    public static readonly HttpMethod GET = new HttpMethod(AsciiString.Cached("GET"));

    /**
     * The HEAD method is identical to GET except that the server MUST NOT return a message-body
     * in the response.
     */
    public static readonly HttpMethod HEAD = new HttpMethod(AsciiString.Cached("HEAD"));

    /**
     * The POST method is used to request that the origin server accept the entity enclosed in the
     * request as a new subordinate of the resource identified by the Request-URI in the
     * Request-Line.
     */
    public static readonly HttpMethod POST = new HttpMethod(AsciiString.Cached("POST"));

    /**
     * The PUT method requests that the enclosed entity be stored under the supplied Request-URI.
     */
    public static readonly HttpMethod PUT = new HttpMethod(AsciiString.Cached("PUT"));

    /**
     * The PATCH method requests that a set of changes described in the
     * request entity be applied to the resource identified by the Request-URI.
     */
    public static readonly HttpMethod PATCH = new HttpMethod(AsciiString.Cached("PATCH"));

    /**
     * The DELETE method requests that the origin server delete the resource identified by the
     * Request-URI.
     */
    public static readonly HttpMethod DELETE = new HttpMethod(AsciiString.Cached("DELETE"));

    /**
     * The TRACE method is used to invoke a remote, application-layer loop- back of the request
     * message.
     */
    public static readonly HttpMethod TRACE = new HttpMethod(AsciiString.Cached("TRACE"));

    /**
     * This specification reserves the method name CONNECT for use with a proxy that can dynamically
     * switch to being a tunnel
     */
    public static readonly HttpMethod CONNECT = new HttpMethod(AsciiString.Cached("CONNECT"));

    /**
     * The QUERY method requests that the request target process the enclosed content in a safe and
     * idempotent manner and then respond with the result of that processing.
     */
    public static readonly HttpMethod QUERY = new HttpMethod(AsciiString.Cached("QUERY"));

    /**
     * Returns the {@link HttpMethod} represented by the specified name.
     * If the specified name is a standard HTTP method name, a cached instance
     * will be returned.  Otherwise, a new instance will be returned.
     */
    public static HttpMethod ValueOf(string name)
    {
        switch (name)
        {
            case "OPTIONS":
                return HttpMethod.OPTIONS;
            case "GET":
                return HttpMethod.GET;
            case "HEAD":
                return HttpMethod.HEAD;
            case "POST":
                return HttpMethod.POST;
            case "PUT":
                return HttpMethod.PUT;
            case "PATCH":
                return HttpMethod.PATCH;
            case "DELETE":
                return HttpMethod.DELETE;
            case "TRACE":
                return HttpMethod.TRACE;
            case "CONNECT":
                return HttpMethod.CONNECT;
            case "QUERY":
                return HttpMethod.QUERY;
            default:
                return new HttpMethod(name);
        }
    }

    private readonly AsciiString name;

    /**
     * Private constructor for the built-in constants defined in this class.
     * The names are compiler-controlled literals that are already valid HTTP tokens,
     * so there is no need to validate or trim them at runtime.
     */
    private HttpMethod(AsciiString name)
    {
        this.name = name;
    }

    /**
     * Creates a new HTTP method with the specified name.  You will not need to
     * create a new method unless you are implementing a protocol derived from
     * HTTP, such as
     * <a href="https://en.wikipedia.org/wiki/Real_Time_Streaming_Protocol">RTSP</a> and
     * <a href="https://en.wikipedia.org/wiki/Internet_Content_Adaptation_Protocol">ICAP</a>
     */
    public HttpMethod(string name)
    {
        CheckNotNull(name, "name");
        // The name must be non-empty and contain only valid HTTP token characters.
        if (name.Length == 0)
        {
            throw new ArgumentException("name cannot be empty");
        }

        int index = HttpHeaderValidationUtil.ValidateToken(new StringCharSequence(name));
        if (index != -1)
        {
            throw new ArgumentException(
                    "Illegal character in HTTP Method: 0x" + ((int) name[index]).ToString("x", CultureInfo.InvariantCulture));
        }

        this.name = AsciiString.Cached(name);
    }

    /**
     * Returns the name of this method.
     */
    public virtual string Name()
    {
        return name.ToString();
    }

    /**
     * Returns the name of this method.
     */
    public virtual AsciiString AsciiName()
    {
        return name;
    }

    public override int GetHashCode()
    {
        return Name().GetHashCode();
    }

    public override bool Equals(object o)
    {
        if (this == o)
        {
            return true;
        }
        if (!(o is HttpMethod))
        {
            return false;
        }

        HttpMethod that = (HttpMethod) o;
        return Name().Equals(that.Name());
    }

    public override string ToString()
    {
        return name.ToString();
    }

    public int CompareTo(HttpMethod o)
    {
        if (o == this)
        {
            return 0;
        }

        return string.CompareOrdinal(Name(), o.Name());
    }

}
