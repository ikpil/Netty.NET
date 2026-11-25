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

using Netty.NET.Common;

namespace Netty.NET.Handler.Codec.Spdy;

/**
 * Provides the constants for the header names and the utility methods
 * used by the {@link SpdyHttpDecoder} and {@link SpdyHttpEncoder}.
 */
// CLR: the final classes with private constructors are static classes (the nested Names too) of static readonly
// AsciiString constants (AsciiString.cached is the Common AsciiString.Cached).
public static class SpdyHttpHeaders
{

    /**
     * SPDY HTTP header names
     */
    public static class Names
    {

        /**
         * {@code "x-spdy-stream-id"}
         */
        public static readonly AsciiString STREAM_ID = AsciiString.Cached("x-spdy-stream-id");
        /**
         * {@code "x-spdy-associated-to-stream-id"}
         */
        public static readonly AsciiString ASSOCIATED_TO_STREAM_ID =
                AsciiString.Cached("x-spdy-associated-to-stream-id");
        /**
         * {@code "x-spdy-priority"}
         */
        public static readonly AsciiString PRIORITY = AsciiString.Cached("x-spdy-priority");
        /**
         * {@code "x-spdy-scheme"}
         */
        public static readonly AsciiString SCHEME = AsciiString.Cached("x-spdy-scheme");
    }
}
