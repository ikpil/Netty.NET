/*
 * Copyright 2013 The Netty Project
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
using Netty.NET.Common;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec;

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * Provides the constants for the standard SPDY HTTP header names and commonly
 * used utility methods that access a {@link SpdyHeadersFrame}.
 */
// CLR: the Java interface takes the I prefix (owner rule). CharSequence is the Common ICharSequence and Headers is the
// codec-base IHeaders<K, V, T>, whose Map.Entry and Iterator are IEntry<K, V> and IIterator<E>
// (docs/codec-base-porting.md, Headers), as codec-stomp's IStompHeaders; List<String> is IList<string>. The nested
// final class HttpNames with a private constructor is a static class nested in the interface (interfaces may declare
// nested types, https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface).
public interface ISpdyHeaders : IHeaders<ICharSequence, ICharSequence, ISpdyHeaders>
{

    /**
     * SPDY HTTP header names
     */
    public static class HttpNames
    {
        /**
         * {@code ":host"}
         */
        public static readonly AsciiString HOST = AsciiString.Cached(":host");
        /**
         * {@code ":method"}
         */
        public static readonly AsciiString METHOD = AsciiString.Cached(":method");
        /**
         * {@code ":path"}
         */
        public static readonly AsciiString PATH = AsciiString.Cached(":path");
        /**
         * {@code ":scheme"}
         */
        public static readonly AsciiString SCHEME = AsciiString.Cached(":scheme");
        /**
         * {@code ":status"}
         */
        public static readonly AsciiString STATUS = AsciiString.Cached(":status");
        /**
         * {@code ":version"}
         */
        public static readonly AsciiString VERSION = AsciiString.Cached(":version");
    }

    /**
     * {@link Headers#get(Object)} and convert the result to a {@link String}.
     * @param name the name of the header to retrieve
     * @return the first header value if the header is found. {@code null} if there's no such header.
     */
    string GetAsString(ICharSequence name);

    /**
     * {@link Headers#getAll(Object)} and convert each element of {@link List} to a {@link String}.
     * @param name the name of the header to retrieve
     * @return a {@link List} of header values or an empty {@link List} if no values are found.
     */
    IList<string> GetAllAsString(ICharSequence name);

    /**
     * {@link #iterator()} that converts each {@link Entry}'s key and value to a {@link String}.
     */
    IIterator<IEntry<string, string>> IteratorAsString();

    /**
     * Returns {@code true} if a header with the {@code name} and {@code value} exists, {@code false} otherwise.
     * <p>
     * If {@code ignoreCase} is {@code true} then a case insensitive compare is done on the value.
     * @param name the name of the header to find
     * @param value the value of the header to find
     * @param ignoreCase {@code true} then a case insensitive compare is run to compare values.
     * otherwise a case sensitive compare is run to compare values.
     */
    bool Contains(ICharSequence name, ICharSequence value, bool ignoreCase);
}
