/*
 * Copyright 2014 The Netty Project
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

namespace Netty.NET.Handler.Codec.Stomp;

/**
 * The multimap data structure for the STOMP header names and values. It also provides the constants for the standard
 * STOMP header names and values.
 */
// CLR: The interface constants are static readonly fields of the interface
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface), and List<String> is
// IList<string>.
public interface IStompHeaders : IHeaders<ICharSequence, ICharSequence, IStompHeaders>
{

    public static readonly AsciiString ACCEPT_VERSION = AsciiString.Cached("accept-version");
    public static readonly AsciiString HOST = AsciiString.Cached("host");
    public static readonly AsciiString LOGIN = AsciiString.Cached("login");
    public static readonly AsciiString PASSCODE = AsciiString.Cached("passcode");
    public static readonly AsciiString HEART_BEAT = AsciiString.Cached("heart-beat");
    public static readonly AsciiString VERSION = AsciiString.Cached("version");
    public static readonly AsciiString SESSION = AsciiString.Cached("session");
    public static readonly AsciiString SERVER = AsciiString.Cached("server");
    public static readonly AsciiString DESTINATION = AsciiString.Cached("destination");
    public static readonly AsciiString ID = AsciiString.Cached("id");
    public static readonly AsciiString ACK = AsciiString.Cached("ack");
    public static readonly AsciiString TRANSACTION = AsciiString.Cached("transaction");
    public static readonly AsciiString RECEIPT = AsciiString.Cached("receipt");
    public static readonly AsciiString MESSAGE_ID = AsciiString.Cached("message-id");
    public static readonly AsciiString SUBSCRIPTION = AsciiString.Cached("subscription");
    public static readonly AsciiString RECEIPT_ID = AsciiString.Cached("receipt-id");
    public static readonly AsciiString MESSAGE = AsciiString.Cached("message");
    public static readonly AsciiString CONTENT_LENGTH = AsciiString.Cached("content-length");
    public static readonly AsciiString CONTENT_TYPE = AsciiString.Cached("content-type");

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
