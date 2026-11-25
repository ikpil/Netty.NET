/*
 * Copyright 2014 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * A collection of headers sent or received via HTTP/2.
 */
// CLR: Headers<CharSequence, CharSequence, Http2Headers> is the codec-base IHeaders<ICharSequence, ICharSequence,
// IHttp2Headers> (docs/codec-base-porting.md, Headers). iterator() is re-declared in Java only to document the
// iteration order; its signature equals the inherited IHeaders member IIterator<IEntry<K, V>> Iterator(), so the
// comment stays here and the member is inherited (a repeated interface member would hide it, CS0108).
// valueIterator(CharSequence) is declared here, as in Java (codec-base IHeaders has no valueIterator; DefaultHeaders
// and EmptyHeaders implement it). method()/method(CharSequence) and the other
// pseudo-header accessors are the overloads Method()/Method(ICharSequence) and so on.
public interface IHttp2Headers : IHeaders<ICharSequence, ICharSequence, IHttp2Headers>
{

    /**
     * HTTP/2 pseudo-headers names.
     */
    // CLR: the Java enum with a constructor, fields and methods is a sealed class with static readonly instances, as
    // codec-base Base64Dialect and codec-http HttpStatusClass (docs/codec-base-porting.md): "You can't define a method
    // inside the definition of an enumeration type"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum). The implicit values()
    // is Values() (a new array per call, as Java's values() clones), and ToString() returns the constant name.
    // String.contentEquals(CharSequence) is the private ContentEquals(string, ICharSequence). The identity checks
    // ":method" == header compare a String literal with the CharSequence; a C# string is never an ICharSequence, so
    // they cannot succeed and are left out, and the contentEquals checks after them decide. isPseudoHeader(String)
    // reaches getPseudoHeader(CharSequence) through a StringCharSequence, as Java passes the String as a CharSequence.
    public sealed class PseudoHeaderName
    {
        /**
         * {@code :method}.
         */
        public static readonly PseudoHeaderName METHOD = new PseudoHeaderName(nameof(METHOD), ":method", true);

        /**
         * {@code :scheme}.
         */
        public static readonly PseudoHeaderName SCHEME = new PseudoHeaderName(nameof(SCHEME), ":scheme", true);

        /**
         * {@code :authority}.
         */
        public static readonly PseudoHeaderName AUTHORITY = new PseudoHeaderName(nameof(AUTHORITY), ":authority", true);

        /**
         * {@code :path}.
         */
        public static readonly PseudoHeaderName PATH = new PseudoHeaderName(nameof(PATH), ":path", true);

        /**
         * {@code :status}.
         */
        public static readonly PseudoHeaderName STATUS = new PseudoHeaderName(nameof(STATUS), ":status", false);

        /**
         * {@code :protocol}, as defined in <a href="https://datatracker.ietf.org/doc/rfc8441/">RFC 8441,
         * Bootstrapping WebSockets with HTTP/2</a>.
         */
        public static readonly PseudoHeaderName PROTOCOL = new PseudoHeaderName(nameof(PROTOCOL), ":protocol", true);

        private const char PSEUDO_HEADER_PREFIX = ':';
        private const byte PSEUDO_HEADER_PREFIX_BYTE = (byte) PSEUDO_HEADER_PREFIX;

        private readonly string name;
        private readonly AsciiString value;
        private readonly bool requestOnly;

        private PseudoHeaderName(string name, string value, bool requestOnly)
        {
            this.name = name;
            this.value = AsciiString.Cached(value);
            this.requestOnly = requestOnly;
        }

        // CLR: Java's implicit values().
        public static PseudoHeaderName[] Values()
        {
            return new[] { METHOD, SCHEME, AUTHORITY, PATH, STATUS, PROTOCOL };
        }

        public AsciiString Value()
        {
            // Return a slice so that the buffer gets its own reader index.
            return value;
        }

        /**
         * Indicates whether the specified header follows the pseudo-header format (begins with ':' character)
         *
         * @return {@code true} if the header follow the pseudo-header format
         */
        public static bool HasPseudoHeaderFormat(ICharSequence headerName)
        {
            if (headerName is AsciiString)
            {
                AsciiString asciiHeaderName = (AsciiString) headerName;
                return asciiHeaderName.Length() > 0 && asciiHeaderName.ByteAt(0) == PSEUDO_HEADER_PREFIX_BYTE;
            }
            else
            {
                return headerName.Length() > 0 && headerName.CharAt(0) == PSEUDO_HEADER_PREFIX;
            }
        }

        /**
         * Indicates whether the given header name is a valid HTTP/2 pseudo header.
         */
        public static bool IsPseudoHeader(ICharSequence header)
        {
            return GetPseudoHeader(header) != null;
        }

        /**
         * Indicates whether the given header name is a valid HTTP/2 pseudo header.
         */
        public static bool IsPseudoHeader(AsciiString header)
        {
            return GetPseudoHeader(header) != null;
        }

        /**
         * Indicates whether the given header name is a valid HTTP/2 pseudo header.
         */
        public static bool IsPseudoHeader(string header)
        {
            return GetPseudoHeader(new StringCharSequence(header)) != null;
        }

        /**
         * Returns the {@link PseudoHeaderName} corresponding to the specified header name.
         *
         * @return corresponding {@link PseudoHeaderName} if any, {@code null} otherwise.
         */
        public static PseudoHeaderName GetPseudoHeader(ICharSequence header)
        {
            if (header is AsciiString)
            {
                return GetPseudoHeader((AsciiString) header);
            }
            return GetPseudoHeaderName(header);
        }

        private static PseudoHeaderName GetPseudoHeaderName(ICharSequence header)
        {
            int length = header.Length();
            if (length > 0 && header.CharAt(0) == PSEUDO_HEADER_PREFIX)
            {
                switch (length)
                {
                case 5:
                    // :path
                    return ContentEquals(":path", header)? PATH : null;
                case 7:
                    // :method, :scheme, :status
                    if (ContentEquals(":method", header))
                    {
                        return METHOD;
                    }
                    if (ContentEquals(":scheme", header))
                    {
                        return SCHEME;
                    }
                    return ContentEquals(":status", header)? STATUS : null;
                case 9:
                    // :protocol
                    return ContentEquals(":protocol", header)? PROTOCOL : null;
                case 10:
                    // :authority
                    return ContentEquals(":authority", header)? AUTHORITY : null;
                }
            }
            return null;
        }

        /**
         * Returns the {@link PseudoHeaderName} corresponding to the specified header name.
         *
         * @return corresponding {@link PseudoHeaderName} if any, {@code null} otherwise.
         */
        public static PseudoHeaderName GetPseudoHeader(AsciiString header)
        {
            int length = header.Length();
            if (length > 0 && header.CharAt(0) == PSEUDO_HEADER_PREFIX)
            {
                switch (length)
                {
                case 5:
                    // :path
                    return PATH.Value().Equals(header) ? PATH : null;
                case 7:
                    if (ReferenceEquals(header, METHOD.Value()))
                    {
                        return METHOD;
                    }
                    if (ReferenceEquals(header, SCHEME.Value()))
                    {
                        return SCHEME;
                    }
                    if (ReferenceEquals(header, STATUS.Value()))
                    {
                        return STATUS;
                    }
                    // :method, :scheme, :status
                    if (METHOD.Value().Equals(header))
                    {
                        return METHOD;
                    }
                    if (SCHEME.Value().Equals(header))
                    {
                        return SCHEME;
                    }
                    return STATUS.Value().Equals(header)? STATUS : null;
                case 9:
                    // :protocol
                    return PROTOCOL.Value().Equals(header)? PROTOCOL : null;
                case 10:
                    // :authority
                    return AUTHORITY.Value().Equals(header)? AUTHORITY : null;
                }
            }
            return null;
        }

        /**
         * Indicates whether the pseudo-header is to be used in a request context.
         *
         * @return {@code true} if the pseudo-header is to be used in a request context
         */
        public bool IsRequestOnly()
        {
            return requestOnly;
        }

        public override string ToString()
        {
            return name;
        }

        // CLR: String.contentEquals(CharSequence).
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
    }

    /**
     * Returns an iterator over all HTTP/2 headers. The iteration order is as follows:
     *   1. All pseudo headers (order not specified).
     *   2. All non-pseudo headers (in insertion order).
     */
    // CLR: inherited IHeaders.Iterator() (see the type comment).

    /**
     * Equivalent to {@link #getAll(Object)} but no intermediate list is generated.
     * @param name the name of the header to retrieve
     * @return an {@link Iterator} of header values corresponding to {@code name}.
     */
    IIterator<ICharSequence> ValueIterator(ICharSequence name);

    /**
     * Sets the {@link PseudoHeaderName#METHOD} header
     */
    IHttp2Headers Method(ICharSequence value);

    /**
     * Sets the {@link PseudoHeaderName#SCHEME} header
     */
    IHttp2Headers Scheme(ICharSequence value);

    /**
     * Sets the {@link PseudoHeaderName#AUTHORITY} header
     */
    IHttp2Headers Authority(ICharSequence value);

    /**
     * Sets the {@link PseudoHeaderName#PATH} header
     */
    IHttp2Headers Path(ICharSequence value);

    /**
     * Sets the {@link PseudoHeaderName#STATUS} header
     */
    IHttp2Headers Status(ICharSequence value);

    /**
     * Gets the {@link PseudoHeaderName#METHOD} header or {@code null} if there is no such header
     */
    ICharSequence Method();

    /**
     * Gets the {@link PseudoHeaderName#SCHEME} header or {@code null} if there is no such header
     */
    ICharSequence Scheme();

    /**
     * Gets the {@link PseudoHeaderName#AUTHORITY} header or {@code null} if there is no such header
     */
    ICharSequence Authority();

    /**
     * Gets the {@link PseudoHeaderName#PATH} header or {@code null} if there is no such header
     */
    ICharSequence Path();

    /**
     * Gets the {@link PseudoHeaderName#STATUS} header or {@code null} if there is no such header
     */
    ICharSequence Status();

    /**
     * Returns {@code true} if a header with the {@code name} and {@code value} exists, {@code false} otherwise.
     * <p>
     * If {@code caseInsensitive} is {@code true} then a case insensitive compare is done on the value.
     *
     * @param name the name of the header to find
     * @param value the value of the header to find
     * @param caseInsensitive {@code true} then a case insensitive compare is run to compare values.
     * otherwise a case sensitive compare is run to compare values.
     */
    bool Contains(ICharSequence name, ICharSequence value, bool caseInsensitive);
}
