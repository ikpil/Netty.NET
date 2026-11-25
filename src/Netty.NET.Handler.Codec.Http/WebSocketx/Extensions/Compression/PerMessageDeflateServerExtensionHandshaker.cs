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

using System;
using System.Collections.Generic;
using System.Globalization;
using Netty.NET.Handler.Codec.Compression;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression;

/**
 * <a href="https://tools.ietf.org/html/draft-ietf-hybi-permessage-compression-18">permessage-deflate</a>
 * handshake implementation.
 */
// CLR: the package-private extension and parameter names are internal constants. The four @Deprecated constructors (),
// (int, boolean, int, boolean, boolean), (int, boolean, int, boolean, boolean, WebSocketExtensionFilterProvider) and
// (int, boolean, int, boolean, boolean, WebSocketExtensionFilterProvider, int) name their replacements and are not
// ported (owner decision 2026-11-16). ZlibCodecFactory.isSupportingWindowSizeAndMemLevel() is false, because jzlib is
// library-not-ported (owner decision 2026-11-23, src/Netty.NET.Handler.Codec.Compression/ZlibCodecFactory.cs), so the
// default configuration never lets the client choose the server window, as Java without jzlib; a configured window size
// below 15 or a memory level other than 8 makes the encoder fail at the first compressed frame (see DeflateEncoder).
// IllegalArgumentException is ArgumentException with the numbers in the invariant culture; checkInRange,
// checkPositiveOrZero and checkNotNull are the Common ObjectUtil checks. HashMap<String, String> is Dictionary<string,
// string>: neither defines an iteration order ("no guarantees as to the order of the map",
// https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/HashMap.html; "The order in which the items
// are returned is undefined",
// https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2?view=net-10.0), and both allow
// null values. The Iterator over parameters().entrySet() is an IEnumerator<KeyValuePair<string, string>>;
// equalsIgnoreCase is string.Equals with StringComparison.OrdinalIgnoreCase, the same for these ASCII names
// (https://learn.microsoft.com/en-us/dotnet/api/system.stringcomparison?view=net-10.0). Integer.parseInt is ParseInt
// (internal, also used by PerMessageDeflateClientExtensionHandshaker through its static import of this class):
// Integer.parseInt accepts an optional ASCII sign and decimal digits and throws NumberFormatException for null, for
// non-digits and for values outside the int range
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Integer.html#parseInt(java.lang.String,int)),
// and int.TryParse with NumberStyles.AllowLeadingSign and the invariant culture fails for null, for non-digits and for
// values outside the Int32 range (https://learn.microsoft.com/en-us/dotnet/api/system.int32.tryparse?view=net-10.0),
// where ParseInt throws FormatException, the counterpart of NumberFormatException, with the messages of OpenJDK 17's
// parseInt and NumberFormatException.forInputString
// (https://raw.githubusercontent.com/openjdk/jdk17u/jdk-17+35/src/java.base/share/classes/java/lang/Integer.java,
// https://raw.githubusercontent.com/openjdk/jdk17u/jdk-17+35/src/java.base/share/classes/java/lang/NumberFormatException.java);
// Java also accepts non-ASCII Unicode decimal digits (Character.digit), which the BCL rejects. Integer.toString is
// ToString with the invariant culture, the private static nested class PermessageDeflateExtension is a private nested
// class, and its RSV1 is IWebSocketExtension.RSV1.
public sealed class PerMessageDeflateServerExtensionHandshaker : IWebSocketServerExtensionHandshaker
{

    public const int MIN_WINDOW_SIZE = 8;
    public const int MAX_WINDOW_SIZE = 15;

    internal const string PERMESSAGE_DEFLATE_EXTENSION = "permessage-deflate";
    internal const string CLIENT_MAX_WINDOW = "client_max_window_bits";
    internal const string SERVER_MAX_WINDOW = "server_max_window_bits";
    internal const string CLIENT_NO_CONTEXT = "client_no_context_takeover";
    internal const string SERVER_NO_CONTEXT = "server_no_context_takeover";

    /**
     * Default memory level used for deflate compression (zlib MAX_MEM_LEVEL).
     */
    public const int DEFAULT_MEM_LEVEL = 8;
    public const int MIN_MEM_LEVEL = 1;
    public const int MAX_MEM_LEVEL = 9;

    private readonly int compressionLevel;
    private readonly bool allowServerWindowSize;
    private readonly int preferredClientWindowSize;
    private readonly bool allowServerNoContext;
    private readonly bool preferredClientNoContext;
    private readonly int serverWindowSize;
    private readonly int memLevel;
    private readonly IWebSocketExtensionFilterProvider extensionFilterProvider;
    private readonly int maxAllocation;

    /**
     * Constructor with default configuration.
     *
     * @param maxAllocation
     *            Maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    public PerMessageDeflateServerExtensionHandshaker(int maxAllocation)
        : this(6, ZlibCodecFactory.IsSupportingWindowSizeAndMemLevel(), MAX_WINDOW_SIZE, false, false,
                MAX_WINDOW_SIZE, DEFAULT_MEM_LEVEL, maxAllocation)
    {
    }

    /**
     * Constructor with custom configuration.
     *
     * @param compressionLevel
     *            Compression level between 0 and 9 (default is 6).
     * @param allowServerWindowSize
     *            allows WebSocket client to customize the server inflater window size
     *            (default is false).
     * @param preferredClientWindowSize
     *            indicates the preferred client window size to use if client inflater is customizable.
     * @param allowServerNoContext
     *            allows WebSocket client to activate server_no_context_takeover
     *            (default is false).
     * @param preferredClientNoContext
     *            indicates if server prefers to activate client_no_context_takeover
     *            if client is compatible with (default is false).
     * @param maxAllocation
     *            Maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    public PerMessageDeflateServerExtensionHandshaker(int compressionLevel, bool allowServerWindowSize,
            int preferredClientWindowSize,
            bool allowServerNoContext, bool preferredClientNoContext, int maxAllocation)
        : this(compressionLevel, allowServerWindowSize, preferredClientWindowSize, allowServerNoContext,
             preferredClientNoContext, MAX_WINDOW_SIZE, DEFAULT_MEM_LEVEL, maxAllocation)
    {
    }

    /**
     * Constructor with custom configuration including server-side compressor memory limits.
     *
     * @param compressionLevel
     *            Compression level between 0 and 9 (default is 6).
     * @param allowServerWindowSize
     *            allows WebSocket client to customize the server inflater window size
     *            (default is false).
     * @param preferredClientWindowSize
     *            indicates the preferred client window size to use if client inflater is customizable.
     * @param allowServerNoContext
     *            allows WebSocket client to activate server_no_context_takeover
     *            (default is false).
     * @param preferredClientNoContext
     *            indicates if server prefers to activate client_no_context_takeover
     *            if client is compatible with (default is false).
     * @param serverWindowSize
     *            upper bound (in bits, 8-15) on the server-side deflate window. If the client offers
     *            {@code server_max_window_bits=N}, the negotiated value is {@code min(N, serverWindowSize)}.
     *            Set lower than {@link #MAX_WINDOW_SIZE} to reduce per-connection memory.
     * @param memLevel
     *            zlib memory level for the server-side deflater (1-9). Lower values reduce per-connection
     *            memory at the cost of compression ratio.
     * @param maxAllocation
     *            Maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    public PerMessageDeflateServerExtensionHandshaker(int compressionLevel, bool allowServerWindowSize,
            int preferredClientWindowSize,
            bool allowServerNoContext, bool preferredClientNoContext,
            int serverWindowSize, int memLevel, int maxAllocation)
        : this(compressionLevel, allowServerWindowSize, preferredClientWindowSize, allowServerNoContext,
             preferredClientNoContext, serverWindowSize, memLevel,
             IWebSocketExtensionFilterProvider.DEFAULT, maxAllocation)
    {
    }

    /**
     * Constructor with full custom configuration.
     *
     * @param compressionLevel
     *            Compression level between 0 and 9 (default is 6).
     * @param allowServerWindowSize
     *            allows WebSocket client to customize the server inflater window size
     *            (default is false).
     * @param preferredClientWindowSize
     *            indicates the preferred client window size to use if client inflater is customizable.
     * @param allowServerNoContext
     *            allows WebSocket client to activate server_no_context_takeover
     *            (default is false).
     * @param preferredClientNoContext
     *            indicates if server prefers to activate client_no_context_takeover
     *            if client is compatible with (default is false).
     * @param serverWindowSize
     *            upper bound (in bits, 8-15) on the server-side deflate window. If the client offers
     *            {@code server_max_window_bits=N}, the negotiated value is {@code min(N, serverWindowSize)}.
     *            Per <a href="https://tools.ietf.org/html/rfc7692#section-7.1.2.1">RFC 7692 §7.1.2.1</a>
     *            the server may include {@code server_max_window_bits} with the same or smaller value than the
     *            offer; this handshaker also advertises it unilaterally when {@code serverWindowSize} is less
     *            than {@link #MAX_WINDOW_SIZE}.
     * @param memLevel
     *            zlib memory level for the server-side deflater (1-9). Lower values reduce per-connection
     *            memory at the cost of compression ratio.
     * @param extensionFilterProvider
     *            provides server extension filters for per message deflate encoder and decoder.
     * @param maxAllocation
     *            Maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    public PerMessageDeflateServerExtensionHandshaker(int compressionLevel, bool allowServerWindowSize,
            int preferredClientWindowSize,
            bool allowServerNoContext, bool preferredClientNoContext,
            int serverWindowSize, int memLevel,
            IWebSocketExtensionFilterProvider extensionFilterProvider,
            int maxAllocation)
    {
        if (preferredClientWindowSize > MAX_WINDOW_SIZE || preferredClientWindowSize < MIN_WINDOW_SIZE)
        {
            throw new ArgumentException(
                    "preferredServerWindowSize: " + preferredClientWindowSize.ToString(CultureInfo.InvariantCulture) +
                    " (expected: 8-15)");
        }
        if (compressionLevel < 0 || compressionLevel > 9)
        {
            throw new ArgumentException(
                    "compressionLevel: " + compressionLevel.ToString(CultureInfo.InvariantCulture) +
                    " (expected: 0-9)");
        }
        this.compressionLevel = compressionLevel;
        this.allowServerWindowSize = allowServerWindowSize;
        this.preferredClientWindowSize = preferredClientWindowSize;
        this.allowServerNoContext = allowServerNoContext;
        this.preferredClientNoContext = preferredClientNoContext;
        this.serverWindowSize = CheckInRange(serverWindowSize, MIN_WINDOW_SIZE, MAX_WINDOW_SIZE, "serverWindowSize");
        this.memLevel = CheckInRange(memLevel, MIN_MEM_LEVEL, MAX_MEM_LEVEL, "memLevel");
        this.extensionFilterProvider = CheckNotNull(extensionFilterProvider, "extensionFilterProvider");
        this.maxAllocation = CheckPositiveOrZero(maxAllocation, "maxAllocation");
    }

    public IWebSocketServerExtension HandshakeExtension(WebSocketExtensionData extensionData)
    {
        if (!PERMESSAGE_DEFLATE_EXTENSION.Equals(extensionData.Name()))
        {
            return null;
        }

        bool deflateEnabled = true;
        int clientWindowSize = MAX_WINDOW_SIZE;
        int negotiatedServerWindowSize = this.serverWindowSize;
        bool serverNoContext = false;
        bool clientNoContext = false;

        IEnumerator<KeyValuePair<string, string>> parametersIterator =
                extensionData.Parameters().GetEnumerator();
        while (deflateEnabled && parametersIterator.MoveNext())
        {
            KeyValuePair<string, string> parameter = parametersIterator.Current;

            if (CLIENT_MAX_WINDOW.Equals(parameter.Key, StringComparison.OrdinalIgnoreCase))
            {
                // RFC 7692: client_max_window_bits may have a value or no value
                string value = parameter.Value;
                if (value != null)
                {
                    // Let NumberFormatException bubble up if value is invalid
                    clientWindowSize = ParseInt(value);
                    if (clientWindowSize > MAX_WINDOW_SIZE || clientWindowSize < MIN_WINDOW_SIZE)
                    {
                        deflateEnabled = false;
                    }
                }
                else
                {
                    // No value specified, use preferred client window size
                    clientWindowSize = preferredClientWindowSize;
                }
            }
            else if (SERVER_MAX_WINDOW.Equals(parameter.Key, StringComparison.OrdinalIgnoreCase))
            {
                // use provided windowSize if it is allowed
                if (allowServerWindowSize)
                {
                    int clientOfferedServerWindowSize = ParseInt(parameter.Value);
                    if (clientOfferedServerWindowSize > MAX_WINDOW_SIZE
                            || clientOfferedServerWindowSize < MIN_WINDOW_SIZE)
                    {
                        deflateEnabled = false;
                    }
                    else
                    {
                        // RFC 7692 §7.1.2.1: server accepts with the same or smaller value than the offer.
                        // Cap at the configured serverWindowSize so the server's memory bound is respected.
                        negotiatedServerWindowSize = Math.Min(clientOfferedServerWindowSize, this.serverWindowSize);
                    }
                }
                else
                {
                    deflateEnabled = false;
                }
            }
            else if (CLIENT_NO_CONTEXT.Equals(parameter.Key, StringComparison.OrdinalIgnoreCase))
            {
                // use preferred clientNoContext because client is compatible with customization
                clientNoContext = preferredClientNoContext;
            }
            else if (SERVER_NO_CONTEXT.Equals(parameter.Key, StringComparison.OrdinalIgnoreCase))
            {
                // use server no context if allowed
                if (allowServerNoContext)
                {
                    serverNoContext = true;
                }
                else
                {
                    deflateEnabled = false;
                }
            }
            else
            {
                // unknown parameter
                deflateEnabled = false;
            }
        }

        if (deflateEnabled)
        {
            return new PermessageDeflateExtension(compressionLevel, serverNoContext,
                    negotiatedServerWindowSize, memLevel, clientNoContext, clientWindowSize,
                    extensionFilterProvider, maxAllocation);
        }
        else
        {
            return null;
        }
    }

    internal static int ParseInt(string s)
    {
        if (s == null)
        {
            throw new FormatException("Cannot parse null string");
        }
        if (int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
        {
            return value;
        }
        throw new FormatException("For input string: \"" + s + "\"");
    }

    private class PermessageDeflateExtension : IWebSocketServerExtension
    {

        private readonly int compressionLevel;
        private readonly bool serverNoContext;
        private readonly int serverWindowSize;
        private readonly int memLevel;
        private readonly bool clientNoContext;
        private readonly int clientWindowSize;
        private readonly IWebSocketExtensionFilterProvider extensionFilterProvider;
        private readonly int maxAllocation;

        internal PermessageDeflateExtension(int compressionLevel, bool serverNoContext,
                int serverWindowSize, int memLevel, bool clientNoContext, int clientWindowSize,
                IWebSocketExtensionFilterProvider extensionFilterProvider, int maxAllocation)
        {
            this.compressionLevel = compressionLevel;
            this.serverNoContext = serverNoContext;
            this.serverWindowSize = serverWindowSize;
            this.memLevel = memLevel;
            this.clientNoContext = clientNoContext;
            this.clientWindowSize = clientWindowSize;
            this.extensionFilterProvider = extensionFilterProvider;
            this.maxAllocation = maxAllocation;
        }

        public int Rsv()
        {
            return IWebSocketExtension.RSV1;
        }

        public WebSocketExtensionEncoder NewExtensionEncoder()
        {
            return new PerMessageDeflateEncoder(compressionLevel, serverWindowSize, memLevel, serverNoContext,
                                                extensionFilterProvider.EncoderFilter());
        }

        public WebSocketExtensionDecoder NewExtensionDecoder()
        {
            return new PerMessageDeflateDecoder(clientNoContext, extensionFilterProvider.DecoderFilter(),
                                                maxAllocation);
        }

        public WebSocketExtensionData NewReponseData()
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>(4);
            if (serverNoContext)
            {
                parameters[SERVER_NO_CONTEXT] = null;
            }
            if (clientNoContext)
            {
                parameters[CLIENT_NO_CONTEXT] = null;
            }
            if (serverWindowSize != MAX_WINDOW_SIZE)
            {
                parameters[SERVER_MAX_WINDOW] = serverWindowSize.ToString(CultureInfo.InvariantCulture);
            }
            if (clientWindowSize != MAX_WINDOW_SIZE)
            {
                parameters[CLIENT_MAX_WINDOW] = clientWindowSize.ToString(CultureInfo.InvariantCulture);
            }
            return new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters);
        }
    }

}
