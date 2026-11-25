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
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;
using static Netty.NET.Handler.Codec.Http.HttpConstants;

namespace Netty.NET.Handler.Codec.Http;

/**
 * The response code and its description of HTTP or its derived protocols, such as
 * <a href="https://en.wikipedia.org/wiki/Real_Time_Streaming_Protocol">RTSP</a> and
 * <a href="https://en.wikipedia.org/wiki/Internet_Content_Adaptation_Protocol">ICAP</a>.
 */
// CLR: Comparable<HttpResponseStatus> is IComparable<HttpResponseStatus>. Integer.parseInt(String), which accepts an
// optional sign and decimal digits only, is int.Parse with NumberStyles.AllowLeadingSign and the invariant culture; the
// AsciiString overloads are the Common AsciiString.ParseInt members. The Java catch (Exception) that rewraps a parse
// failure as IllegalArgumentException is catch (Exception) with ArgumentException (docs/codec-base-porting.md).
// ByteProcessor.FIND_ASCII_SPACE is the Common Func<byte, bool> constant, CharsetUtil.US_ASCII is Encoding.ASCII
// (docs/codec-smtp-porting.md), buf.writeCharSequence(..., US_ASCII) is ByteBufUtil.WriteAscii (AsciiString.c2b per
// char, as Java writes), String.contentEquals(String) is string.Equals, and the package-private encode(ByteBuf) is
// internal.
public class HttpResponseStatus : IComparable<HttpResponseStatus>
{

    /**
     * 100 Continue
     */
    public static readonly HttpResponseStatus CONTINUE = NewStatus(100, "Continue");

    /**
     * 101 Switching Protocols
     */
    public static readonly HttpResponseStatus SWITCHING_PROTOCOLS = NewStatus(101, "Switching Protocols");

    /**
     * 102 Processing (WebDAV, RFC2518)
     */
    public static readonly HttpResponseStatus PROCESSING = NewStatus(102, "Processing");

    /**
     * 103 Early Hints (RFC 8297)
     */
    public static readonly HttpResponseStatus EARLY_HINTS = NewStatus(103, "Early Hints");

    /**
     * 200 OK
     */
    public static readonly HttpResponseStatus OK = NewStatus(200, "OK");

    /**
     * 201 Created
     */
    public static readonly HttpResponseStatus CREATED = NewStatus(201, "Created");

    /**
     * 202 Accepted
     */
    public static readonly HttpResponseStatus ACCEPTED = NewStatus(202, "Accepted");

    /**
     * 203 Non-Authoritative Information (since HTTP/1.1)
     */
    public static readonly HttpResponseStatus NON_AUTHORITATIVE_INFORMATION =
            NewStatus(203, "Non-Authoritative Information");

    /**
     * 204 No Content
     */
    public static readonly HttpResponseStatus NO_CONTENT = NewStatus(204, "No Content");

    /**
     * 205 Reset Content
     */
    public static readonly HttpResponseStatus RESET_CONTENT = NewStatus(205, "Reset Content");

    /**
     * 206 Partial Content
     */
    public static readonly HttpResponseStatus PARTIAL_CONTENT = NewStatus(206, "Partial Content");

    /**
     * 207 Multi-Status (WebDAV, RFC2518)
     */
    public static readonly HttpResponseStatus MULTI_STATUS = NewStatus(207, "Multi-Status");

    /**
     * 300 Multiple Choices
     */
    public static readonly HttpResponseStatus MULTIPLE_CHOICES = NewStatus(300, "Multiple Choices");

    /**
     * 301 Moved Permanently
     */
    public static readonly HttpResponseStatus MOVED_PERMANENTLY = NewStatus(301, "Moved Permanently");

    /**
     * 302 Found
     */
    public static readonly HttpResponseStatus FOUND = NewStatus(302, "Found");

    /**
     * 303 See Other (since HTTP/1.1)
     */
    public static readonly HttpResponseStatus SEE_OTHER = NewStatus(303, "See Other");

    /**
     * 304 Not Modified
     */
    public static readonly HttpResponseStatus NOT_MODIFIED = NewStatus(304, "Not Modified");

    /**
     * 305 Use Proxy (since HTTP/1.1)
     */
    public static readonly HttpResponseStatus USE_PROXY = NewStatus(305, "Use Proxy");

    /**
     * 307 Temporary Redirect (since HTTP/1.1)
     */
    public static readonly HttpResponseStatus TEMPORARY_REDIRECT = NewStatus(307, "Temporary Redirect");

    /**
     * 308 Permanent Redirect (RFC7538)
     */
    public static readonly HttpResponseStatus PERMANENT_REDIRECT = NewStatus(308, "Permanent Redirect");

    /**
     * 400 Bad Request
     */
    public static readonly HttpResponseStatus BAD_REQUEST = NewStatus(400, "Bad Request");

    /**
     * 401 Unauthorized
     */
    public static readonly HttpResponseStatus UNAUTHORIZED = NewStatus(401, "Unauthorized");

    /**
     * 402 Payment Required
     */
    public static readonly HttpResponseStatus PAYMENT_REQUIRED = NewStatus(402, "Payment Required");

    /**
     * 403 Forbidden
     */
    public static readonly HttpResponseStatus FORBIDDEN = NewStatus(403, "Forbidden");

    /**
     * 404 Not Found
     */
    public static readonly HttpResponseStatus NOT_FOUND = NewStatus(404, "Not Found");

    /**
     * 405 Method Not Allowed
     */
    public static readonly HttpResponseStatus METHOD_NOT_ALLOWED = NewStatus(405, "Method Not Allowed");

    /**
     * 406 Not Acceptable
     */
    public static readonly HttpResponseStatus NOT_ACCEPTABLE = NewStatus(406, "Not Acceptable");

    /**
     * 407 Proxy Authentication Required
     */
    public static readonly HttpResponseStatus PROXY_AUTHENTICATION_REQUIRED =
            NewStatus(407, "Proxy Authentication Required");

    /**
     * 408 Request Timeout
     */
    public static readonly HttpResponseStatus REQUEST_TIMEOUT = NewStatus(408, "Request Timeout");

    /**
     * 409 Conflict
     */
    public static readonly HttpResponseStatus CONFLICT = NewStatus(409, "Conflict");

    /**
     * 410 Gone
     */
    public static readonly HttpResponseStatus GONE = NewStatus(410, "Gone");

    /**
     * 411 Length Required
     */
    public static readonly HttpResponseStatus LENGTH_REQUIRED = NewStatus(411, "Length Required");

    /**
     * 412 Precondition Failed
     */
    public static readonly HttpResponseStatus PRECONDITION_FAILED = NewStatus(412, "Precondition Failed");

    /**
     * 413 Request Entity Too Large
     */
    public static readonly HttpResponseStatus REQUEST_ENTITY_TOO_LARGE =
            NewStatus(413, "Request Entity Too Large");

    /**
     * 414 Request-URI Too Long
     */
    public static readonly HttpResponseStatus REQUEST_URI_TOO_LONG = NewStatus(414, "Request-URI Too Long");

    /**
     * 415 Unsupported Media Type
     */
    public static readonly HttpResponseStatus UNSUPPORTED_MEDIA_TYPE = NewStatus(415, "Unsupported Media Type");

    /**
     * 416 Requested Range Not Satisfiable
     */
    public static readonly HttpResponseStatus REQUESTED_RANGE_NOT_SATISFIABLE =
            NewStatus(416, "Requested Range Not Satisfiable");

    /**
     * 417 Expectation Failed
     */
    public static readonly HttpResponseStatus EXPECTATION_FAILED = NewStatus(417, "Expectation Failed");

    /**
     * 421 Misdirected Request
     *
     * @see <a href="https://tools.ietf.org/html/rfc7540#section-9.1.2">421 (Misdirected Request) Status Code</a>
     */
    public static readonly HttpResponseStatus MISDIRECTED_REQUEST = NewStatus(421, "Misdirected Request");

    /**
     * 422 Unprocessable Entity (WebDAV, RFC4918)
     */
    public static readonly HttpResponseStatus UNPROCESSABLE_ENTITY = NewStatus(422, "Unprocessable Entity");

    /**
     * 423 Locked (WebDAV, RFC4918)
     */
    public static readonly HttpResponseStatus LOCKED = NewStatus(423, "Locked");

    /**
     * 424 Failed Dependency (WebDAV, RFC4918)
     */
    public static readonly HttpResponseStatus FAILED_DEPENDENCY = NewStatus(424, "Failed Dependency");

    /**
     * 425 Unordered Collection (WebDAV, RFC3648)
     */
    public static readonly HttpResponseStatus UNORDERED_COLLECTION = NewStatus(425, "Unordered Collection");

    /**
     * 426 Upgrade Required (RFC2817)
     */
    public static readonly HttpResponseStatus UPGRADE_REQUIRED = NewStatus(426, "Upgrade Required");

    /**
     * 428 Precondition Required (RFC6585)
     */
    public static readonly HttpResponseStatus PRECONDITION_REQUIRED = NewStatus(428, "Precondition Required");

    /**
     * 429 Too Many Requests (RFC6585)
     */
    public static readonly HttpResponseStatus TOO_MANY_REQUESTS = NewStatus(429, "Too Many Requests");

    /**
     * 431 Request Header Fields Too Large (RFC6585)
     */
    public static readonly HttpResponseStatus REQUEST_HEADER_FIELDS_TOO_LARGE =
            NewStatus(431, "Request Header Fields Too Large");

    /**
     * 500 Internal Server Error
     */
    public static readonly HttpResponseStatus INTERNAL_SERVER_ERROR = NewStatus(500, "Internal Server Error");

    /**
     * 501 Not Implemented
     */
    public static readonly HttpResponseStatus NOT_IMPLEMENTED = NewStatus(501, "Not Implemented");

    /**
     * 502 Bad Gateway
     */
    public static readonly HttpResponseStatus BAD_GATEWAY = NewStatus(502, "Bad Gateway");

    /**
     * 503 Service Unavailable
     */
    public static readonly HttpResponseStatus SERVICE_UNAVAILABLE = NewStatus(503, "Service Unavailable");

    /**
     * 504 Gateway Timeout
     */
    public static readonly HttpResponseStatus GATEWAY_TIMEOUT = NewStatus(504, "Gateway Timeout");

    /**
     * 505 HTTP Version Not Supported
     */
    public static readonly HttpResponseStatus HTTP_VERSION_NOT_SUPPORTED =
            NewStatus(505, "HTTP Version Not Supported");

    /**
     * 506 Variant Also Negotiates (RFC2295)
     */
    public static readonly HttpResponseStatus VARIANT_ALSO_NEGOTIATES = NewStatus(506, "Variant Also Negotiates");

    /**
     * 507 Insufficient Storage (WebDAV, RFC4918)
     */
    public static readonly HttpResponseStatus INSUFFICIENT_STORAGE = NewStatus(507, "Insufficient Storage");

    /**
     * 510 Not Extended (RFC2774)
     */
    public static readonly HttpResponseStatus NOT_EXTENDED = NewStatus(510, "Not Extended");

    /**
     * 511 Network Authentication Required (RFC6585)
     */
    public static readonly HttpResponseStatus NETWORK_AUTHENTICATION_REQUIRED =
            NewStatus(511, "Network Authentication Required");
    private static HttpResponseStatus NewStatus(int statusCode, string reasonPhrase)
    {
        return new HttpResponseStatus(statusCode, reasonPhrase, true);
    }

    /**
     * Returns the {@link HttpResponseStatus} represented by the specified code.
     * If the specified code is a standard HTTP status code, a cached instance
     * will be returned.  Otherwise, a new instance will be returned.
     */
    public static HttpResponseStatus ValueOf(int code)
    {
        HttpResponseStatus status = ValueOf0(code);
        return status != null ? status : new HttpResponseStatus(code);
    }

    private static HttpResponseStatus ValueOf0(int code)
    {
        switch (code)
        {
        case 100:
            return CONTINUE;
        case 101:
            return SWITCHING_PROTOCOLS;
        case 102:
            return PROCESSING;
        case 103:
            return EARLY_HINTS;
        case 200:
            return OK;
        case 201:
            return CREATED;
        case 202:
            return ACCEPTED;
        case 203:
            return NON_AUTHORITATIVE_INFORMATION;
        case 204:
            return NO_CONTENT;
        case 205:
            return RESET_CONTENT;
        case 206:
            return PARTIAL_CONTENT;
        case 207:
            return MULTI_STATUS;
        case 300:
            return MULTIPLE_CHOICES;
        case 301:
            return MOVED_PERMANENTLY;
        case 302:
            return FOUND;
        case 303:
            return SEE_OTHER;
        case 304:
            return NOT_MODIFIED;
        case 305:
            return USE_PROXY;
        case 307:
            return TEMPORARY_REDIRECT;
        case 308:
            return PERMANENT_REDIRECT;
        case 400:
            return BAD_REQUEST;
        case 401:
            return UNAUTHORIZED;
        case 402:
            return PAYMENT_REQUIRED;
        case 403:
            return FORBIDDEN;
        case 404:
            return NOT_FOUND;
        case 405:
            return METHOD_NOT_ALLOWED;
        case 406:
            return NOT_ACCEPTABLE;
        case 407:
            return PROXY_AUTHENTICATION_REQUIRED;
        case 408:
            return REQUEST_TIMEOUT;
        case 409:
            return CONFLICT;
        case 410:
            return GONE;
        case 411:
            return LENGTH_REQUIRED;
        case 412:
            return PRECONDITION_FAILED;
        case 413:
            return REQUEST_ENTITY_TOO_LARGE;
        case 414:
            return REQUEST_URI_TOO_LONG;
        case 415:
            return UNSUPPORTED_MEDIA_TYPE;
        case 416:
            return REQUESTED_RANGE_NOT_SATISFIABLE;
        case 417:
            return EXPECTATION_FAILED;
        case 421:
            return MISDIRECTED_REQUEST;
        case 422:
            return UNPROCESSABLE_ENTITY;
        case 423:
            return LOCKED;
        case 424:
            return FAILED_DEPENDENCY;
        case 425:
            return UNORDERED_COLLECTION;
        case 426:
            return UPGRADE_REQUIRED;
        case 428:
            return PRECONDITION_REQUIRED;
        case 429:
            return TOO_MANY_REQUESTS;
        case 431:
            return REQUEST_HEADER_FIELDS_TOO_LARGE;
        case 500:
            return INTERNAL_SERVER_ERROR;
        case 501:
            return NOT_IMPLEMENTED;
        case 502:
            return BAD_GATEWAY;
        case 503:
            return SERVICE_UNAVAILABLE;
        case 504:
            return GATEWAY_TIMEOUT;
        case 505:
            return HTTP_VERSION_NOT_SUPPORTED;
        case 506:
            return VARIANT_ALSO_NEGOTIATES;
        case 507:
            return INSUFFICIENT_STORAGE;
        case 510:
            return NOT_EXTENDED;
        case 511:
            return NETWORK_AUTHENTICATION_REQUIRED;
        }
        return null;
    }

    /**
     * Returns the {@link HttpResponseStatus} represented by the specified {@code code} and {@code reasonPhrase}.
     * If the specified code is a standard HTTP status {@code code} and {@code reasonPhrase}, a cached instance
     * will be returned. Otherwise, a new instance will be returned.
     * @param code The response code value.
     * @param reasonPhrase The response code reason phrase.
     * @return the {@link HttpResponseStatus} represented by the specified {@code code} and {@code reasonPhrase}.
     */
    public static HttpResponseStatus ValueOf(int code, string reasonPhrase)
    {
        HttpResponseStatus responseStatus = ValueOf0(code);
        return responseStatus != null && responseStatus.ReasonPhrase().Equals(reasonPhrase) ? responseStatus :
                new HttpResponseStatus(code, reasonPhrase);
    }

    /**
     * Parses the specified HTTP status line into a {@link HttpResponseStatus}. The expected formats of the line are:
     * <ul>
     * <li>{@code statusCode} (e.g. 200)</li>
     * <li>{@code statusCode} {@code reasonPhrase} (e.g. 404 Not Found)</li>
     * </ul>
     *
     * @throws IllegalArgumentException if the specified status line is malformed
     */
    public static HttpResponseStatus ParseLine(ICharSequence line)
    {
        return (line is AsciiString) ? ParseLine((AsciiString) line) : ParseLine(line.ToString());
    }

    /**
     * Parses the specified HTTP status line into a {@link HttpResponseStatus}. The expected formats of the line are:
     * <ul>
     * <li>{@code statusCode} (e.g. 200)</li>
     * <li>{@code statusCode} {@code reasonPhrase} (e.g. 404 Not Found)</li>
     * </ul>
     *
     * @throws IllegalArgumentException if the specified status line is malformed
     */
    public static HttpResponseStatus ParseLine(string line)
    {
        try
        {
            int space = line.IndexOf(' ');
            return space == -1 ? ValueOf(ParseInt(line)) :
                    ValueOf(ParseInt(line.Substring(0, space)), line.Substring(space + 1));
        }
        catch (Exception e)
        {
            throw new ArgumentException("malformed status line: " + line, e);
        }
    }

    // CLR: java.lang.Integer.parseInt(String) (static import).
    private static int ParseInt(string s)
    {
        return int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    /**
     * Parses the specified HTTP status line into a {@link HttpResponseStatus}. The expected formats of the line are:
     * <ul>
     * <li>{@code statusCode} (e.g. 200)</li>
     * <li>{@code statusCode} {@code reasonPhrase} (e.g. 404 Not Found)</li>
     * </ul>
     *
     * @throws IllegalArgumentException if the specified status line is malformed
     */
    public static HttpResponseStatus ParseLine(AsciiString line)
    {
        try
        {
            int space = line.ForEachByte(ByteProcessor.FIND_ASCII_SPACE);
            return space == -1 ? ValueOf(line.ParseInt()) : ValueOf(line.ParseInt(0, space), line.ToString(space + 1));
        }
        catch (Exception e)
        {
            throw new ArgumentException("malformed status line: " + line, e);
        }
    }

    private readonly int code;
    private readonly AsciiString codeAsText;
    private readonly HttpStatusClass codeClass;

    private readonly string reasonPhrase;
    private readonly byte[] bytes;

    /**
     * Creates a new instance with the specified {@code code} and the auto-generated default reason phrase.
     */
    private HttpResponseStatus(int code)
        : this(code, HttpStatusClass.ValueOf(code).DefaultReasonPhrase() + " (" +
               code.ToString(CultureInfo.InvariantCulture) + ')', false)
    {
    }

    /**
     * Creates a new instance with the specified {@code code} and its {@code reasonPhrase}.
     */
    public HttpResponseStatus(int code, string reasonPhrase) : this(code, reasonPhrase, false)
    {
    }

    private HttpResponseStatus(int code, string reasonPhrase, bool bytes)
    {
        CheckPositiveOrZero(code, "code");
        ObjectUtil.CheckNotNull(reasonPhrase, "reasonPhrase");

        for (int i = 0; i < reasonPhrase.Length; i ++)
        {
            char c = reasonPhrase[i];
            // Check prohibited characters.
            switch (c)
            {
                case '\n': case '\r':
                    throw new ArgumentException(
                            "reasonPhrase contains one of the following prohibited characters: " +
                                    "\\r\\n: " + reasonPhrase);
            }
        }

        this.code = code;
        this.codeClass = HttpStatusClass.ValueOf(code);
        string codeString = code.ToString(CultureInfo.InvariantCulture);
        codeAsText = new AsciiString(codeString);
        this.reasonPhrase = reasonPhrase;
        if (bytes)
        {
            this.bytes = Encoding.ASCII.GetBytes(codeString + ' ' + reasonPhrase);
        }
        else
        {
            this.bytes = null;
        }
    }

    /**
     * Returns the code of this {@link HttpResponseStatus}.
     */
    public int Code()
    {
        return code;
    }

    /**
     * Returns the status code as {@link AsciiString}.
     */
    public AsciiString CodeAsText()
    {
        return codeAsText;
    }

    /**
     * Returns the reason phrase of this {@link HttpResponseStatus}.
     */
    public string ReasonPhrase()
    {
        return reasonPhrase;
    }

    /**
     * Returns the class of this {@link HttpResponseStatus}
     */
    public HttpStatusClass CodeClass()
    {
        return this.codeClass;
    }

    public override int GetHashCode()
    {
        return Code();
    }

    /**
     * Equality of {@link HttpResponseStatus} only depends on {@link #code()}. The
     * reason phrase is not considered for equality.
     */
    public override bool Equals(object o)
    {
        if (!(o is HttpResponseStatus))
        {
            return false;
        }

        return Code() == ((HttpResponseStatus) o).Code();
    }

    /**
     * Equality of {@link HttpResponseStatus} only depends on {@link #code()}. The
     * reason phrase is not considered for equality.
     */
    public int CompareTo(HttpResponseStatus o)
    {
        return Code() - o.Code();
    }

    public override string ToString()
    {
        return new StringBuilder(reasonPhrase.Length + 4)
            .Append(codeAsText)
            .Append(' ')
            .Append(reasonPhrase)
            .ToString();
    }

    internal void Encode(ByteBuf buf)
    {
        if (bytes == null)
        {
            ByteBufUtil.Copy(codeAsText, buf);
            buf.WriteByte(SP);
            ByteBufUtil.WriteAscii(buf, reasonPhrase);
        }
        else
        {
            buf.WriteBytes(bytes);
        }
    }
}
