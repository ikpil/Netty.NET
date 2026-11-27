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

using System.Collections.Generic;
using System.Globalization;
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Http;
using static Netty.NET.Handler.Codec.Http3.IHttp3Headers.PseudoHeaderName;

namespace Netty.NET.Handler.Codec.Http3;

/**
 * {@link BiConsumer} that does add header names and values to
 * {@link Http3Headers} while also validate these.
 */
// CLR: the package-private final class is internal sealed. BiConsumer<CharSequence, CharSequence> has no BCL interface;
// its counterpart is the delegate Action<ICharSequence, ICharSequence>, so the class keeps accept(...) as Accept(...)
// and a caller passes the method group (sink.Accept) where Java passes the sink, as the Common ByteProcessor mapping keeps
// process(byte) (docs/codec-http-porting.md, Shared CLR adaptations). The throws clauses have no counterpart (C# has no
// checked exceptions). String.format("... (%d)", maxHeaderListSize) formats the long with the invariant culture, and
// the %s texts of the header names are their toString() (the names are never null here). "*".contentEquals(path) is
// the private ContentEquals("*", path), which dereferences path as String.contentEquals does. The private enum
// HeaderType is a C# enum (docs/codec-http2-porting.md, Shared CLR adaptations: simple enums). The static imports of
// the PseudoHeaderName constants and methods are a using static of IHttp3Headers.PseudoHeaderName.
internal sealed class Http3HeadersSink
{
    private readonly IHttp3Headers headers;
    private readonly long maxHeaderListSize;
    private readonly bool validate;
    private readonly bool trailer;
    private long headersLength;
    private bool exceededMaxLength;
    private Http3HeadersValidationException validationException;
    private HeaderType? previousType;
    private bool request;
    private int receivedPseudoHeaders;

    internal Http3HeadersSink(IHttp3Headers headers, long maxHeaderListSize, bool validate, bool trailer)
    {
        this.headers = headers;
        this.maxHeaderListSize = maxHeaderListSize;
        this.validate = validate;
        this.trailer = trailer;
    }

    /**
     * This method must be called after the sink is used.
     */
    internal void Finish()
    {
        if (exceededMaxLength)
        {
            throw new Http3Exception(Http3ErrorCode.H3_EXCESSIVE_LOAD,
                    "Header size exceeded max allowed size (" +
                    maxHeaderListSize.ToString(CultureInfo.InvariantCulture) + ")");
        }
        if (validationException != null)
        {
            throw validationException;
        }
        if (validate)
        {
            if (trailer)
            {
                if (receivedPseudoHeaders != 0)
                {
                    // Trailers must not have pseudo headers.
                    throw new Http3HeadersValidationException("Pseudo-header(s) included in trailers.");
                }
                return;
            }

            // Validate that all mandatory pseudo-headers are included.
            if (request)
            {
                ValidateAuthorityAndHost();
                ICharSequence method = headers.Method();
                // fast-path
                if (HttpMethod.CONNECT.AsciiName().ContentEqualsIgnoreCase(method))
                {
                    // Check if this is an Extended CONNECT request (RFC 9220)
                    // Extended CONNECT includes the :protocol pseudo-header
                    if ((receivedPseudoHeaders & PROTOCOL.GetFlag()) != 0)
                    {
                        // Extended CONNECT (RFC 9220) requires:
                        // - :method
                        // - :scheme
                        // - :authority
                        // - :path
                        // - :protocol
                        int requiredPseudoHeaders = METHOD.GetFlag() | SCHEME.GetFlag() |
                                                         AUTHORITY.GetFlag() | PATH.GetFlag() | PROTOCOL.GetFlag();
                        if (receivedPseudoHeaders != requiredPseudoHeaders)
                        {
                            throw new Http3HeadersValidationException(
                                    "Not all mandatory pseudo-headers included for Extended CONNECT.");
                        }
                    }
                    else
                    {
                        // Regular CONNECT (RFC 9114) requires:
                        // - :method
                        // - :authority
                        int requiredPseudoHeaders = METHOD.GetFlag() | AUTHORITY.GetFlag();
                        if (receivedPseudoHeaders != requiredPseudoHeaders)
                        {
                            throw new Http3HeadersValidationException("Not all mandatory pseudo-headers included.");
                        }
                    }
                }
                else if (HttpMethod.OPTIONS.AsciiName().ContentEqualsIgnoreCase(method))
                {
                    // See:
                    //
                    // https://www.rfc-editor.org/rfc/rfc9114.html#section-4.3.1
                    // https://www.rfc-editor.org/rfc/rfc9110#section-7.1
                    // - :method
                    // - :scheme
                    // - :authority
                    // - :path
                    int requiredPseudoHeaders = METHOD.GetFlag() | SCHEME.GetFlag() | PATH.GetFlag();
                    if ((receivedPseudoHeaders & requiredPseudoHeaders) != requiredPseudoHeaders ||
                            (!AuthorityOrHostHeaderReceived() && !ContentEquals("*", headers.Path())))
                    {
                        throw new Http3HeadersValidationException("Not all mandatory pseudo-headers included.");
                    }
                }
                else
                {
                    // For other requests we must include:
                    // - :method
                    // - :scheme
                    // - :authority
                    // - :path
                    int requiredPseudoHeaders = METHOD.GetFlag() | SCHEME.GetFlag() | PATH.GetFlag();
                    if ((receivedPseudoHeaders & requiredPseudoHeaders) != requiredPseudoHeaders ||
                        !AuthorityOrHostHeaderReceived())
                    {
                        throw new Http3HeadersValidationException("Not all mandatory pseudo-headers included.");
                    }
                }
            }
            else
            {
                // For responses we must include:
                // - :status
                if (receivedPseudoHeaders != STATUS.GetFlag())
                {
                    throw new Http3HeadersValidationException("Not all mandatory pseudo-headers included.");
                }
            }
        }
    }

    /**
     * https://www.rfc-editor.org/rfc/rfc9114#section-4.3.1
     * the request MUST contain either an :authority pseudo-header field or a Host header field.
     * If both fields are present, they MUST contain the same value.
     * https://datatracker.ietf.org/doc/html/rfc9110#section-5.3
     * a sender MUST NOT generate multiple field lines with the same name ...
     * unless that field's definition allows ... a comma-separated list
     */
    private void ValidateAuthorityAndHost()
    {
        if (!headers.Contains(HttpHeaderNames.HOST))
        {
            return;
        }
        ICharSequence authority = headers.Authority();
        IList<ICharSequence> hosts = headers.GetAll(HttpHeaderNames.HOST);
        ICharSequence expected = authority != null ? authority : hosts[0];
        for (int i = 0; i < hosts.Count; i++)
        {
            if (!AsciiString.ContentEqualsIgnoreCase(expected, hosts[i]))
            {
                throw new Http3HeadersValidationException(authority != null ?
                        "Conflicting ':authority' pseudo-header and 'host' header field." :
                        "Conflicting 'host' header fields.");
            }
        }
    }

    /**
     * Find host header field in case the :authority pseudo header is not specified.
     * See:
     * https://www.rfc-editor.org/rfc/rfc9110#section-7.2
     */
    private bool AuthorityOrHostHeaderReceived()
    {
        return (receivedPseudoHeaders & AUTHORITY.GetFlag()) == AUTHORITY.GetFlag() ||
                headers.Contains(HttpHeaderNames.HOST);
    }

    public void Accept(ICharSequence name, ICharSequence value)
    {
        headersLength += QpackHeaderField.SizeOf(name, value);
        exceededMaxLength |= headersLength > maxHeaderListSize;

        if (exceededMaxLength || validationException != null)
        {
            // We don't store the header since we've already failed validation requirements.
            return;
        }

        if (validate)
        {
            try
            {
                 Validate(headers, name);
            }
            catch (Http3HeadersValidationException ex)
            {
                validationException = ex;
                return;
            }
        }

        headers.Add(name, value);
    }

    private void Validate(IHttp3Headers headers, ICharSequence name)
    {
        if (HasPseudoHeaderFormat(name))
        {
            if (previousType == HeaderType.REGULAR_HEADER)
            {
                throw new Http3HeadersValidationException(
                        "Pseudo-header field '" + name + "' found after regular header.");
            }

            IHttp3Headers.PseudoHeaderName pseudoHeader = GetPseudoHeader(name);
            if (pseudoHeader == null)
            {
                throw new Http3HeadersValidationException(
                        "Invalid HTTP/3 pseudo-header '" + name + "' encountered.");
            }
            if ((receivedPseudoHeaders & pseudoHeader.GetFlag()) != 0)
            {
                // There can't be any duplicates for pseudy header names.
                throw new Http3HeadersValidationException(
                        "Pseudo-header field '" + name + "' exists already.");
            }
            receivedPseudoHeaders |= pseudoHeader.GetFlag();

            HeaderType currentHeaderType = pseudoHeader.IsRequestOnly() ?
                    HeaderType.REQUEST_PSEUDO_HEADER : HeaderType.RESPONSE_PSEUDO_HEADER;
            request = pseudoHeader.IsRequestOnly();
            previousType = currentHeaderType;
        }
        else
        {
            previousType = HeaderType.REGULAR_HEADER;
        }
    }

    // CLR: String.contentEquals(CharSequence) (a null cs throws, as in Java).
    private static bool ContentEquals(string s, ICharSequence cs)
    {
        if (s.Length != cs.Length())
        {
            return false;
        }
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != cs.CharAt(i))
            {
                return false;
            }
        }
        return true;
    }

    // CLR: the private enum; previousType starts as null in Java, so the field is a HeaderType?.
    private enum HeaderType
    {
        REGULAR_HEADER,
        REQUEST_PSEUDO_HEADER,
        RESPONSE_PSEUDO_HEADER
    }
}
