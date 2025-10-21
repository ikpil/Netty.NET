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
using System.Collections.Generic;
using System.Text;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Common;

/**
 * A utility class that provides various common operations and constants
 * related with {@link Encoding} and its relevant classes.
 */
public static class CharsetUtil
{
    /**
     * 16-bit UTF (UCS Transformation Format) whose byte order is identified by
     * an optional byte-order mark
     */
    public static readonly Encoding UTF_16 = Encoding.Unicode; //StandardCharsets.UTF_16;

    /**
     * 16-bit UTF (UCS Transformation Format) whose byte order is big-endian
     */
    public static readonly Encoding UTF_16BE = Encoding.BigEndianUnicode; //StandardCharsets.UTF_16BE;

    /**
     * 16-bit UTF (UCS Transformation Format) whose byte order is little-endian
     */
    public static readonly Encoding UTF_16LE = Encoding.Unicode; // StandardCharsets.UTF_16LE;

    /**
     * 8-bit UTF (UCS Transformation Format)
     */
    public static readonly Encoding UTF_8 = Encoding.UTF8; //StandardCharsets.UTF_8;

    /**
     * ISO Latin Alphabet No. 1, as known as <tt>ISO-LATIN-1</tt>
     */
    public static readonly Encoding ISO_8859_1 = Encoding.Latin1; // StandardCharsets.ISO_8859_1;

    /**
     * 7-bit ASCII, as known as ISO646-US or the Basic Latin block of the
     * Unicode character set
     */
    public static readonly Encoding US_ASCII = Encoding.ASCII; //StandardCharsets.US_ASCII;

    private static readonly Encoding[] CHARSETS = new Encoding[]
    {
        UTF_16, UTF_16BE, UTF_16LE, UTF_8, ISO_8859_1, US_ASCII
    };

    public static Encoding[] values()
    {
        return CHARSETS;
    }

    /**
     * @deprecated Use {@link #encoder(Encoding)}.
     */
    [Obsolete]
    public static Encoder getEncoder(Encoding charset)
    {
        return encoder(charset);
    }

    /**
     * Returns a new {@link Encoder} for the {@link Encoding} with specified error actions.
     *
     * @param charset The specified charset
     * @param malformedInputAction The encoder's action for malformed-input errors
     * @param unmappableCharacterAction The encoder's action for unmappable-character errors
     * @return The encoder for the specified {@code charset}
     */
    public static Encoder encoder(Encoding charset, EncoderFallback malformedInputAction, EncoderFallback unmappableCharacterAction)
    {
        checkNotNull(charset, "charset");
        Encoder e = charset.GetEncoder();
        //e.onMalformedInput(malformedInputAction).onUnmappableCharacter(unmappableCharacterAction);
        e.Fallback = malformedInputAction ?? EncoderFallback.ExceptionFallback;
        return e;
    }

    /**
     * Returns a new {@link Encoder} for the {@link Encoding} with the specified error action.
     *
     * @param charset The specified charset
     * @param codingErrorAction The encoder's action for malformed-input and unmappable-character errors
     * @return The encoder for the specified {@code charset}
     */
    public static Encoder encoder(Encoding charset, EncoderFallback codingErrorAction)
    {
        return encoder(charset, codingErrorAction, codingErrorAction);
    }

    /**
     * Returns a cached thread-local {@link Encoder} for the specified {@link Encoding}.
     *
     * @param charset The specified charset
     * @return The encoder for the specified {@code charset}
     */
    public static Encoder encoder(Encoding charset)
    {
        checkNotNull(charset, "charset");

        IDictionary<Encoding, Encoder> map = InternalThreadLocalMap.get().charsetEncoderCache();
        map.TryGetValue(charset, out Encoder e);
        if (e != null)
        {
            e.Reset();
            e.Fallback = EncoderFallback.ReplacementFallback;
            return e;
        }

        e = encoder(charset, EncoderFallback.ReplacementFallback, EncoderFallback.ReplacementFallback);
        map[charset] = e;
        return e;
    }

    /**
     * @deprecated Use {@link #decoder(Encoding)}.
     */
    [Obsolete]
    public static Decoder getDecoder(Encoding charset)
    {
        return decoder(charset);
    }

    /**
     * Returns a new {@link Decoder} for the {@link Encoding} with specified error actions.
     *
     * @param charset The specified charset
     * @param malformedInputAction The decoder's action for malformed-input errors
     * @param unmappableCharacterAction The decoder's action for unmappable-character errors
     * @return The decoder for the specified {@code charset}
     */
    public static Decoder decoder(Encoding charset, DecoderFallback malformedInputAction, DecoderFallback unmappableCharacterAction)
    {
        checkNotNull(charset, "charset");
        Decoder d = charset.GetDecoder();
        d.Fallback = malformedInputAction ?? DecoderFallback.ExceptionFallback;
        return d;
    }

    /**
     * Returns a new {@link Decoder} for the {@link Encoding} with the specified error action.
     *
     * @param charset The specified charset
     * @param codingErrorAction The decoder's action for malformed-input and unmappable-character errors
     * @return The decoder for the specified {@code charset}
     */
    public static Decoder decoder(Encoding charset, DecoderFallback codingErrorAction)
    {
        return decoder(charset, codingErrorAction, codingErrorAction);
    }

    /**
     * Returns a cached thread-local {@link Decoder} for the specified {@link Encoding}.
     *
     * @param charset The specified charset
     * @return The decoder for the specified {@code charset}
     */
    public static Decoder decoder(Encoding charset)
    {
        checkNotNull(charset, "charset");

        IDictionary<Encoding, Decoder> map = InternalThreadLocalMap.get().charsetDecoderCache();
        map.TryGetValue(charset, out Decoder d);
        if (d != null)
        {
            d.Reset();
            d.Fallback = DecoderFallback.ReplacementFallback;
            return d;
        }

        d = decoder(charset, DecoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
        map[charset] = d;
        return d;
    }
}