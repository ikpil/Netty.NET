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
using static Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression.PerMessageDeflateServerExtensionHandshaker;

namespace Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression;

/**
 * <a href="https://tools.ietf.org/html/draft-ietf-hybi-permessage-compression-18">permessage-deflate</a>
 * handshake implementation.
 */
// CLR: the three @Deprecated constructors (), (int, boolean, int, boolean, boolean) and (int, boolean, int, boolean,
// boolean, WebSocketExtensionFilterProvider) name their replacements and are not ported (owner decision 2026-11-16).
// ZlibCodecFactory.isSupportingWindowSizeAndMemLevel() is false because jzlib is library-not-ported, so the default
// configuration does not offer client_max_window_bits, as Java without jzlib (see
// PerMessageDeflateServerExtensionHandshaker). The constants and Integer.parseInt (ParseInt) come from the static
// import of PerMessageDeflateServerExtensionHandshaker; HashMap is Dictionary, equalsIgnoreCase is
// StringComparison.OrdinalIgnoreCase and the entry iterator is an IEnumerator<KeyValuePair<string, string>> (see that
// class). The inner (non-static) class PermessageDeflateExtension reads compressionLevel of its enclosing instance ("An
// instance i of a direct inner class C ... is associated with an instance of O, known as the immediately enclosing
// instance of i", https://docs.oracle.com/javase/specs/jls/se17/html/jls-8.html#jls-8.1.3); a C# nested class has no
// enclosing instance, so the handshaker is passed to its constructor as the first argument. IllegalArgumentException is
// ArgumentException with the numbers in the invariant culture; checkPositiveOrZero and checkNotNull are the Common
// ObjectUtil checks.
public sealed class PerMessageDeflateClientExtensionHandshaker : IWebSocketClientExtensionHandshaker
{

    private readonly int compressionLevel;
    private readonly bool allowClientWindowSize;
    private readonly int requestedServerWindowSize;
    private readonly bool allowClientNoContext;
    private readonly bool requestedServerNoContext;
    private readonly IWebSocketExtensionFilterProvider extensionFilterProvider;
    private readonly int maxAllocation;

    /**
     * Constructor with default configuration.
     * @param maxAllocation
     *            Maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    public PerMessageDeflateClientExtensionHandshaker(int maxAllocation)
        : this(6, ZlibCodecFactory.IsSupportingWindowSizeAndMemLevel(), MAX_WINDOW_SIZE, false, false, maxAllocation)
    {
    }

    /**
     * Constructor with custom configuration.
     *
     * @param compressionLevel
     *            Compression level between 0 and 9 (default is 6).
     * @param allowClientWindowSize
     *            allows WebSocket server to customize the client inflater window size
     *            (default is false).
     * @param requestedServerWindowSize
     *            indicates the requested sever window size to use if server inflater is customizable.
     * @param allowClientNoContext
     *            allows WebSocket server to activate client_no_context_takeover
     *            (default is false).
     * @param requestedServerNoContext
     *            indicates if client needs to activate server_no_context_takeover
     *            if server is compatible with (default is false).
     * @param maxAllocation
     *            Maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    public PerMessageDeflateClientExtensionHandshaker(int compressionLevel,
            bool allowClientWindowSize, int requestedServerWindowSize,
            bool allowClientNoContext, bool requestedServerNoContext,
            int maxAllocation)
        : this(compressionLevel, allowClientWindowSize, requestedServerWindowSize,
             allowClientNoContext, requestedServerNoContext, IWebSocketExtensionFilterProvider.DEFAULT, maxAllocation)
    {
    }

    /**
     * Constructor with custom configuration.
     *
     * @param compressionLevel
     *            Compression level between 0 and 9 (default is 6).
     * @param allowClientWindowSize
     *            allows WebSocket server to customize the client inflater window size
     *            (default is false).
     * @param requestedServerWindowSize
     *            indicates the requested sever window size to use if server inflater is customizable.
     * @param allowClientNoContext
     *            allows WebSocket server to activate client_no_context_takeover
     *            (default is false).
     * @param requestedServerNoContext
     *            indicates if client needs to activate server_no_context_takeover
     *            if server is compatible with (default is false).
     * @param extensionFilterProvider
     *            provides client extension filters for per message deflate encoder and decoder.
     * @param maxAllocation
     *            Maximum size of the decompression buffer. Must be &gt;= 0. If zero, maximum size is not limited.
     */
    public PerMessageDeflateClientExtensionHandshaker(int compressionLevel,
            bool allowClientWindowSize, int requestedServerWindowSize,
            bool allowClientNoContext, bool requestedServerNoContext,
            IWebSocketExtensionFilterProvider extensionFilterProvider,
            int maxAllocation)
    {

        if (requestedServerWindowSize > MAX_WINDOW_SIZE || requestedServerWindowSize < MIN_WINDOW_SIZE)
        {
            throw new ArgumentException(
                    "requestedServerWindowSize: " + requestedServerWindowSize.ToString(CultureInfo.InvariantCulture) +
                    " (expected: 8-15)");
        }
        if (compressionLevel < 0 || compressionLevel > 9)
        {
            throw new ArgumentException(
                    "compressionLevel: " + compressionLevel.ToString(CultureInfo.InvariantCulture) +
                    " (expected: 0-9)");
        }

        this.compressionLevel = compressionLevel;
        this.allowClientWindowSize = allowClientWindowSize;
        this.requestedServerWindowSize = requestedServerWindowSize;
        this.allowClientNoContext = allowClientNoContext;
        this.requestedServerNoContext = requestedServerNoContext;
        this.extensionFilterProvider = CheckNotNull(extensionFilterProvider, "extensionFilterProvider");
        this.maxAllocation = CheckPositiveOrZero(maxAllocation, "maxAllocation");
    }

    public WebSocketExtensionData NewRequestData()
    {
        Dictionary<string, string> parameters = new Dictionary<string, string>(4);

        if (requestedServerNoContext)
        {
            parameters[SERVER_NO_CONTEXT] = null;
        }

        if (allowClientNoContext)
        {
            parameters[CLIENT_NO_CONTEXT] = null;
        }

        if (requestedServerWindowSize != MAX_WINDOW_SIZE)
        {
            parameters[SERVER_MAX_WINDOW] = requestedServerWindowSize.ToString(CultureInfo.InvariantCulture);
        }

        if (allowClientWindowSize)
        {
            parameters[CLIENT_MAX_WINDOW] = null;
        }

        return new WebSocketExtensionData(PERMESSAGE_DEFLATE_EXTENSION, parameters);
    }

    public IWebSocketClientExtension HandshakeExtension(WebSocketExtensionData extensionData)
    {
        if (!PERMESSAGE_DEFLATE_EXTENSION.Equals(extensionData.Name()))
        {
            return null;
        }

        bool succeed = true;
        int clientWindowSize = MAX_WINDOW_SIZE;
        int serverWindowSize = MAX_WINDOW_SIZE;
        bool serverNoContext = false;
        bool clientNoContext = false;

        IEnumerator<KeyValuePair<string, string>> parametersIterator =
                extensionData.Parameters().GetEnumerator();

        while (succeed && parametersIterator.MoveNext())
        {
            KeyValuePair<string, string> parameter = parametersIterator.Current;

            if (CLIENT_MAX_WINDOW.Equals(parameter.Key, StringComparison.OrdinalIgnoreCase))
            {
                // allowed client_window_size_bits
                if (allowClientWindowSize)
                {
                    // RFC 7692: client_max_window_bits may have a value or no value
                    string value = parameter.Value;
                    if (value != null)
                    {
                        // Let NumberFormatException bubble up if value is invalid
                        clientWindowSize = ParseInt(value);
                        if (clientWindowSize > MAX_WINDOW_SIZE || clientWindowSize < MIN_WINDOW_SIZE)
                        {
                            succeed = false;
                        }
                    }
                    // If value is null, keep MAX_WINDOW_SIZE (default)
                }
                else
                {
                    succeed = false;
                }
            }
            else if (SERVER_MAX_WINDOW.Equals(parameter.Key, StringComparison.OrdinalIgnoreCase))
            {
                // acknowledged server_window_size_bits
                serverWindowSize = ParseInt(parameter.Value);
                if (serverWindowSize > MAX_WINDOW_SIZE || serverWindowSize < MIN_WINDOW_SIZE)
                {
                    succeed = false;
                }
            }
            else if (CLIENT_NO_CONTEXT.Equals(parameter.Key, StringComparison.OrdinalIgnoreCase))
            {
                // allowed client_no_context_takeover
                if (allowClientNoContext)
                {
                    clientNoContext = true;
                }
                else
                {
                    succeed = false;
                }
            }
            else if (SERVER_NO_CONTEXT.Equals(parameter.Key, StringComparison.OrdinalIgnoreCase))
            {
                // acknowledged server_no_context_takeover
                serverNoContext = true;
            }
            else
            {
                // unknown parameter
                succeed = false;
            }
        }

        if ((requestedServerNoContext && !serverNoContext) ||
                requestedServerWindowSize < serverWindowSize)
        {
            succeed = false;
        }

        if (succeed)
        {
            return new PermessageDeflateExtension(this, serverNoContext, serverWindowSize,
                    clientNoContext, clientWindowSize, extensionFilterProvider, maxAllocation);
        }
        else
        {
            return null;
        }
    }

    private sealed class PermessageDeflateExtension : IWebSocketClientExtension
    {

        private readonly PerMessageDeflateClientExtensionHandshaker outer;
        private readonly bool serverNoContext;
        private readonly int serverWindowSize;
        private readonly bool clientNoContext;
        private readonly int clientWindowSize;
        private readonly IWebSocketExtensionFilterProvider extensionFilterProvider;
        private readonly int maxAllocation;

        public int Rsv()
        {
            return IWebSocketExtension.RSV1;
        }

        internal PermessageDeflateExtension(PerMessageDeflateClientExtensionHandshaker outer,
                bool serverNoContext, int serverWindowSize,
                bool clientNoContext, int clientWindowSize,
                IWebSocketExtensionFilterProvider extensionFilterProvider, int maxAllocation)
        {
            this.outer = outer;
            this.serverNoContext = serverNoContext;
            this.serverWindowSize = serverWindowSize;
            this.clientNoContext = clientNoContext;
            this.clientWindowSize = clientWindowSize;
            this.extensionFilterProvider = extensionFilterProvider;
            this.maxAllocation = maxAllocation;
        }

        public WebSocketExtensionEncoder NewExtensionEncoder()
        {
            return new PerMessageDeflateEncoder(outer.compressionLevel, clientWindowSize, clientNoContext,
                                                extensionFilterProvider.EncoderFilter());
        }

        public WebSocketExtensionDecoder NewExtensionDecoder()
        {
            return new PerMessageDeflateDecoder(serverNoContext, extensionFilterProvider.DecoderFilter(),
                                                maxAllocation);
        }
    }

}
