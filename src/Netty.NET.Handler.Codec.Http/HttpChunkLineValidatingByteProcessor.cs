/*
 * Copyright 2026 The Netty Project
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
using System.Collections;
using System.Diagnostics;

namespace Netty.NET.Handler.Codec.Http;

/**
 * Validates the chunk start line. That is, the chunk size and chunk extensions, until the CR LF pair.
 * See <a href="https://www.rfc-editor.org/rfc/rfc9112#name-chunked-transfer-coding">RFC 9112 section 7.1</a>.
 *
 * <pre>{@code
 *   chunked-body   = *chunk
 *                    last-chunk
 *                    trailer-section
 *                    CRLF
 *
 *   chunk          = chunk-size [ chunk-ext ] CRLF
 *                    chunk-data CRLF
 *   chunk-size     = 1*HEXDIG
 *   last-chunk     = 1*("0") [ chunk-ext ] CRLF
 *
 *   chunk-data     = 1*OCTET ; a sequence of chunk-size octets
 *   chunk-ext      = *( BWS ";" BWS chunk-ext-name
 *                       [ BWS "=" BWS chunk-ext-val ] )
 *
 *   chunk-ext-name = token
 *   chunk-ext-val  = token / quoted-string
 *   quoted-string  = DQUOTE *( qdtext / quoted-pair ) DQUOTE
 *   qdtext         = HTAB / SP / %x21 / %x23-5B / %x5D-7E / obs-text
 *   quoted-pair    = "\" ( HTAB / SP / VCHAR / obs-text )
 *   obs-text       = %x80-FF
 *   OWS            = *( SP / HTAB )
 *                  ; optional whitespace
 *   BWS            = OWS
 *                  ; "bad" whitespace
 *   VCHAR          =  %x21-7E
 *                  ; visible (printing) characters
 * }</pre>
 */
// CLR: the package-private final class implements ByteProcessor, which is Func<byte, bool> in the Common port, through
// Process(byte) (pass processor.Process), as codec-stomp's line parsers do. Match extends java.util.BitSet, whose C#
// counterpart BitArray is sealed, so Match keeps a 256-bit BitArray (BitSet.set/get are Set/Get). The enum State has a
// constructor, a field and a method, so it is a sealed class with static readonly instances (docs/codec-base-porting.md,
// as Base64Dialect); State.values() is the STATES_BY_ORDINAL array in declaration order, and the switch over the
// constants of finish() is a comparison chain. match(byte value) calls BitSet.get(int) with the sign-extended Java byte,
// so the octets 0x80-0xFF are negative indexes that throw IndexOutOfBoundsException in Java ("if the specified index is
// negative", https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/BitSet.html#get(int)); the octet is
// read as sbyte so BitArray.Get throws ArgumentOutOfRangeException for them, keeping the Java result (Java byte sign
// rule). NumberFormatException is FormatException and assert is Debug.Assert.
internal sealed class HttpChunkLineValidatingByteProcessor
{
    private const int SIZE = 0;
    private const int CHUNK_EXT_NAME = 1;
    private const int CHUNK_EXT_VAL_START = 2;
    private const int CHUNK_EXT_VAL_QUOTED = 3;
    private const int CHUNK_EXT_VAL_QUOTED_ESCAPE = 4;
    private const int CHUNK_EXT_VAL_QUOTED_END = 5;
    private const int CHUNK_EXT_VAL_TOKEN = 6;

    internal sealed class Match
    {
        private readonly BitArray bits = new BitArray(256);
        internal readonly int then;

        internal Match(int then)
        {
            this.then = then;
        }

        internal Match Chars(string chars)
        {
            return Chars(chars, true);
        }

        internal Match Chars(string chars, bool value)
        {
            for (int i = 0, len = chars.Length; i < len; i++)
            {
                bits.Set(chars[i], value);
            }
            return this;
        }

        internal Match Range(int from, int to)
        {
            return Range(from, to, true);
        }

        internal Match Range(int from, int to, bool value)
        {
            for (int i = from; i <= to; i++)
            {
                bits.Set(i, value);
            }
            return this;
        }

        internal bool Get(int bitIndex)
        {
            return bits.Get(bitIndex);
        }
    }

    private sealed class State
    {
        internal static readonly State Size = new State(
                new Match(SIZE).Chars("0123456789abcdefABCDEF \t"),
                new Match(CHUNK_EXT_NAME).Chars(";"));
        internal static readonly State ChunkExtName = new State(
                new Match(CHUNK_EXT_NAME)
                        .Range(0x21, 0x7E)
                        .Chars(" \t")
                        .Chars("(),/:<=>?@[\\]{}", false),
                new Match(CHUNK_EXT_VAL_START).Chars("="));
        internal static readonly State ChunkExtValStart = new State(
                new Match(CHUNK_EXT_VAL_START).Chars(" \t"),
                new Match(CHUNK_EXT_VAL_QUOTED).Chars("\""),
                new Match(CHUNK_EXT_VAL_TOKEN)
                        .Range(0x21, 0x7E)
                        .Chars("(),/:<=>?@[\\]{}\"", false));
        internal static readonly State ChunkExtValQuoted = new State(
                new Match(CHUNK_EXT_VAL_QUOTED_ESCAPE).Chars("\\"),
                new Match(CHUNK_EXT_VAL_QUOTED_END).Chars("\""),
                new Match(CHUNK_EXT_VAL_QUOTED)
                        .Chars("\t !")
                        .Range(0x23, 0x5B)
                        .Range(0x5D, 0x7E)
                        .Range(0x80, 0xFF));
        internal static readonly State ChunkExtValQuotedEscape = new State(
                new Match(CHUNK_EXT_VAL_QUOTED)
                        .Chars("\t ")
                        .Range(0x21, 0x7E)
                        .Range(0x80, 0xFF));
        internal static readonly State ChunkExtValQuotedEnd = new State(
                new Match(CHUNK_EXT_VAL_QUOTED_END).Chars("\t "),
                new Match(CHUNK_EXT_NAME).Chars(";"));
        internal static readonly State ChunkExtValToken = new State(
                new Match(CHUNK_EXT_VAL_TOKEN)
                        .Range(0x21, 0x7E, true)
                        .Chars("(),/:<=>?@[\\]{};", false),
                new Match(CHUNK_EXT_NAME).Chars(";"));

        private readonly Match[] matches;

        private State(params Match[] matches)
        {
            this.matches = matches;
        }

        internal State DoMatch(byte value)
        {
            foreach (Match match in matches)
            {
                if (match.Get(unchecked((sbyte) value)))
                {
                    return STATES_BY_ORDINAL[match.then];
                }
            }
            if (this == Size)
            {
                throw new FormatException("Invalid chunk size");
            }
            else
            {
                throw new InvalidChunkExtensionException("Invalid chunk extension");
            }
        }
    }

    private static readonly State[] STATES_BY_ORDINAL =
    {
        State.Size, State.ChunkExtName, State.ChunkExtValStart, State.ChunkExtValQuoted,
        State.ChunkExtValQuotedEscape, State.ChunkExtValQuotedEnd, State.ChunkExtValToken
    };

    private State state = State.Size;

    public bool Process(byte value)
    {
        state = state.DoMatch(value);
        return true;
    }

    public void Finish()
    {
        if (state == State.ChunkExtValQuoted || state == State.ChunkExtValQuotedEscape ||
                state == State.ChunkExtValStart)
        {
            throw new InvalidChunkExtensionException("Invalid chunk extension");
        }
        // Exhaustiveness check
        Debug.Assert(state == State.Size ||
                state == State.ChunkExtName ||
                state == State.ChunkExtValQuotedEnd ||
                state == State.ChunkExtValToken);
    }
}
