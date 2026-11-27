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

using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http3;

// CLR: Headers<CharSequence, CharSequence, Http3Headers> is the codec-base IHeaders<ICharSequence, ICharSequence,
// IHttp3Headers> (docs/codec-base-porting.md, Headers), and the interface takes the I prefix, as codec-http2's
// IHttp2Headers. iterator() is re-declared in Java only to document the iteration order; its signature equals the
// inherited IHeaders member IIterator<IEntry<K, V>> Iterator(), so the comment stays here and the member is inherited
// (a repeated interface member would hide it, CS0108). valueIterator(CharSequence) is declared here, as in Java
// (codec-base IHeaders has no valueIterator; DefaultHeaders implements it). method()/method(CharSequence) and the other
// pseudo-header accessors are the overloads Method()/Method(ICharSequence) and so on; the default methods
// protocol(CharSequence) and protocol() are default interface members. The @Nullable (org.jetbrains.annotations) return
// annotations have no counterpart: the project does not enable the nullable annotation context.
public interface IHttp3Headers : IHeaders<ICharSequence, ICharSequence, IHttp3Headers>
{

    /**
     * HTTP/2 (and HTTP/3) pseudo-headers names.
     */
    // CLR: the Java enum with a constructor, fields and methods is a sealed class with static readonly instances, as
    // codec-http2's IHttp2Headers.PseudoHeaderName (docs/codec-http2-porting.md, Shared CLR adaptations): "You can't
    // define a method inside the definition of an enumeration type"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum). The implicit values() is
    // Values() (a new array per call, as Java's values() clones), ToString() returns the constant name, and the static
    // initializer is the static constructor, which runs after the static field initializers (the constants and
    // PSEUDO_HEADERS), as Java runs the enum constants and static fields before the static block. CharSequenceMap
    // requires a reference value type, which this class is.
    public sealed class PseudoHeaderName
    {
        /**
         * {@code :method}.
         */
        public static readonly PseudoHeaderName METHOD = new PseudoHeaderName(nameof(METHOD), ":method", true, 0x1);

        /**
         * {@code :scheme}.
         */
        public static readonly PseudoHeaderName SCHEME = new PseudoHeaderName(nameof(SCHEME), ":scheme", true, 0x2);

        /**
         * {@code :authority}.
         */
        public static readonly PseudoHeaderName AUTHORITY =
            new PseudoHeaderName(nameof(AUTHORITY), ":authority", true, 0x4);

        /**
         * {@code :path}.
         */
        public static readonly PseudoHeaderName PATH = new PseudoHeaderName(nameof(PATH), ":path", true, 0x8);

        /**
         * {@code :status}.
         */
        public static readonly PseudoHeaderName STATUS = new PseudoHeaderName(nameof(STATUS), ":status", false, 0x10);

        /**
         * {@code :protocol}.
         * <p>
         * Used for Extended CONNECT requests as defined in RFC 9220.
         * This pseudo-header is only valid for CONNECT requests and indicates
         * the desired protocol for the connection (e.g., "webtransport", "websocket").
         *
         * @see <a href="https://www.rfc-editor.org/rfc/rfc9220.html">RFC 9220: Bootstrapping WebSockets with HTTP/3</a>
         */
        public static readonly PseudoHeaderName PROTOCOL =
            new PseudoHeaderName(nameof(PROTOCOL), ":protocol", true, 0x20);

        private const char PSEUDO_HEADER_PREFIX = ':';
        private const byte PSEUDO_HEADER_PREFIX_BYTE = (byte) PSEUDO_HEADER_PREFIX;

        private readonly string name;
        private readonly AsciiString value;
        private readonly bool requestOnly;
        // The position of the bit in the flag indicates the type of the header field
        private readonly int flag;
        private static readonly CharSequenceMap<PseudoHeaderName> PSEUDO_HEADERS = new CharSequenceMap<PseudoHeaderName>();

        static PseudoHeaderName()
        {
            foreach (PseudoHeaderName pseudoHeader in PseudoHeaderName.Values())
            {
                PSEUDO_HEADERS.Add(pseudoHeader.Value(), pseudoHeader);
            }
        }

        private PseudoHeaderName(string name, string value, bool requestOnly, int flag)
        {
            this.name = name;
            this.value = AsciiString.Cached(value);
            this.requestOnly = requestOnly;
            this.flag = flag;
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
         * @param headerName    the header name to check.
         * @return              {@code true} if the header follow the pseudo-header format
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
         * Indicates whether the given header name is a valid HTTP/3 pseudo header.
         *
         * @param name  the header name.
         * @return      {@code true} if the given header name is a valid HTTP/3 pseudo header, {@code false} otherwise.
         */
        public static bool IsPseudoHeader(ICharSequence name)
        {
            return PSEUDO_HEADERS.Contains(name);
        }

        /**
         * Returns the {@link PseudoHeaderName} corresponding to the specified header name.
         *
         * @param name  the header name.
         * @return corresponding {@link PseudoHeaderName} if any, {@code null} otherwise.
         */
        public static PseudoHeaderName GetPseudoHeader(ICharSequence name)
        {
            return PSEUDO_HEADERS.Get(name);
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

        public int GetFlag()
        {
             return flag;
        }

        // CLR: Enum.toString() is the constant name.
        public override string ToString()
        {
            return name;
        }
    }

    /**
     * Returns an iterator over all HTTP/3 headers. The iteration order is as follows:
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
     *
     * @param value the value for the header.
     * @return      this instance itself.
     */
    IHttp3Headers Method(ICharSequence value);

    /**
     * Sets the {@link PseudoHeaderName#SCHEME} header
     *
     * @param value the value for the header.
     * @return      this instance itself.
     */
    IHttp3Headers Scheme(ICharSequence value);

    /**
     * Sets the {@link PseudoHeaderName#AUTHORITY} header
     *
     * @param value the value for the header.
     * @return      this instance itself.
     */
    IHttp3Headers Authority(ICharSequence value);

    /**
     * Sets the {@link PseudoHeaderName#PATH} header
     *
     * @param value the value for the header.
     * @return      this instance itself.
     */
    IHttp3Headers Path(ICharSequence value);

    /**
     * Sets the {@link PseudoHeaderName#STATUS} header
     *
     * @param value the value for the header.
     * @return      this instance itself.
     */
    IHttp3Headers Status(ICharSequence value);

    /**
     * Sets the {@link PseudoHeaderName#PROTOCOL} header
     * <p>
     * This pseudo-header is used for Extended CONNECT requests as defined in RFC 9220.
     * Common values include "webtransport" and "websocket".
     *
     * @param value the value for the header.
     * @return      this instance itself.
     * @see <a href="https://www.rfc-editor.org/rfc/rfc9220.html">RFC 9220: Bootstrapping WebSockets with HTTP/3</a>
     */
    IHttp3Headers Protocol(ICharSequence value)
    {
        Set(PseudoHeaderName.PROTOCOL.Value(), value);
        return this;
    }

    /**
     * Gets the {@link PseudoHeaderName#METHOD} header or {@code null} if there is no such header
     *
     * @return the value of the header.
     */
    ICharSequence Method();

    /**
     * Gets the {@link PseudoHeaderName#SCHEME} header or {@code null} if there is no such header
     *
     * @return the value of the header.
     */
    ICharSequence Scheme();

    /**
     * Gets the {@link PseudoHeaderName#AUTHORITY} header or {@code null} if there is no such header
     *
     * @return the value of the header.
     */
    ICharSequence Authority();

    /**
     * Gets the {@link PseudoHeaderName#PATH} header or {@code null} if there is no such header
     *
     * @return the value of the header.
     */
    ICharSequence Path();

    /**
     * Gets the {@link PseudoHeaderName#STATUS} header or {@code null} if there is no such header
     *
     * @return the value of the header.
     */
    ICharSequence Status();

    /**
     * Gets the {@link PseudoHeaderName#PROTOCOL} header or {@code null} if there is no such header
     * <p>
     * This pseudo-header is used for Extended CONNECT requests as defined in RFC 9220.
     *
     * @return the value of the header.
     * @see <a href="https://www.rfc-editor.org/rfc/rfc9220.html">RFC 9220: Bootstrapping WebSockets with HTTP/3</a>
     */
    ICharSequence Protocol()
    {
        return Get(PseudoHeaderName.PROTOCOL.Value());
    }

    /**
     * Returns {@code true} if a header with the {@code name} and {@code value} exists, {@code false} otherwise.
     * <p>
     * If {@code caseInsensitive} is {@code true} then a case insensitive compare is done on the value.
     *
     * @param name              the name of the header to find
     * @param value             the value of the header to find
     * @param caseInsensitive   {@code true} then a case insensitive compare is run to compare values.
     * otherwise a case sensitive compare is run to compare values.
     * @return                  {@code true} if its contained, {@code false} otherwise.
     */
    bool Contains(ICharSequence name, ICharSequence value, bool caseInsensitive);
}
