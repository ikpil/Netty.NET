/*
 * Copyright 2015 The Netty Project
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

/*
 * Copyright 2014 Twitter, Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Http2;

// CLR: ByteProcessor is the Common Func<byte, bool> (docs/codec-http-porting.md, Shared CLR adaptations:
// "ByteProcessor | Process(byte) as the Common Func<byte, bool>"): the processors keep process(byte) as
// Process(byte), and AsciiString.forEachByte receives a delegate to it that is created once with the processor.
// The inner (non-static) processor classes read codes and lengths of their encoder, so they receive it in their
// constructor. The catch (Exception) blocks that rethrew with PlatformDependent.throwException only satisfied
// Java's checked ByteProcessor.process(...) throws Exception and are left out (the process methods throw nothing;
// handler's SslHandler maps throwException to a plain rethrow). AsciiString.c2b is AsciiString.C2b. The Java byte
// value & 0xFF and the System.Byte value are the same index. The codes (below 2^30) and the EOS padding bits are
// non-negative, so OR-ing them into the long as uint (zero extension, which avoids the C# sign-extension warning
// CS0675) gives the value of Java's int to long widening.
internal sealed class HpackHuffmanEncoder
{

    private readonly int[] codes;
    private readonly byte[] lengths;
    private readonly EncodedLengthProcessor encodedLengthProcessor;
    private readonly EncodeProcessor encodeProcessor;

    internal HpackHuffmanEncoder() : this(HpackUtil.HUFFMAN_CODES, HpackUtil.HUFFMAN_CODE_LENGTHS)
    {
    }

    /**
     * Creates a new Huffman encoder with the specified Huffman coding.
     *
     * @param codes the Huffman codes indexed by symbol
     * @param lengths the length of each Huffman code
     */
    private HpackHuffmanEncoder(int[] codes, byte[] lengths)
    {
        this.codes = codes;
        this.lengths = lengths;
        encodedLengthProcessor = new EncodedLengthProcessor(this);
        encodeProcessor = new EncodeProcessor(this);
    }

    /**
     * Compresses the input string literal using the Huffman coding.
     *
     * @param out the output stream for the compressed data
     * @param data the string literal to be Huffman encoded
     */
    public void Encode(ByteBuf @out, ICharSequence data)
    {
        ObjectUtil.CheckNotNull(@out, "out");

        if (data is AsciiString)
        {
            AsciiString @string = (AsciiString) data;

            try
            {
                encodeProcessor.@out = @out;
                @string.ForEachByte(encodeProcessor.processor);
            }
            finally
            {
                encodeProcessor.End();
            }
        }
        else
        {
            EncodeSlowPath(@out, data);
        }
    }

    private void EncodeSlowPath(ByteBuf @out, ICharSequence data)
    {
        long current = 0;
        int n = 0;

        for (int i = 0; i < data.Length(); i++)
        {
            int b = AsciiString.C2b(data.CharAt(i)) & 0xFF;
            int code = codes[b];
            int nbits = lengths[b];

            current <<= nbits;
            current |= (uint) code;
            n += nbits;

            while (n >= 8)
            {
                n -= 8;
                @out.WriteByte((int) (current >> n));
            }
        }

        if (n > 0)
        {
            current <<= 8 - n;
            current |= (uint) (0xFF >>> n); // this should be EOS symbol
            @out.WriteByte((int) current);
        }
    }

    /**
     * Returns the number of bytes required to Huffman encode the input string literal.
     *
     * @param data the string literal to be Huffman encoded
     * @return the number of bytes required to Huffman encode {@code data}
     */
    internal int GetEncodedLength(ICharSequence data)
    {
        if (data is AsciiString)
        {
            AsciiString @string = (AsciiString) data;
            encodedLengthProcessor.Reset();
            @string.ForEachByte(encodedLengthProcessor.processor);

            return encodedLengthProcessor.Length();
        }
        else
        {
            return GetEncodedLengthSlowPath(data);
        }
    }

    private int GetEncodedLengthSlowPath(ICharSequence data)
    {
        long len = 0;

        for (int i = 0; i < data.Length(); i++)
        {
            len += lengths[AsciiString.C2b(data.CharAt(i)) & 0xFF];
        }

        return (int) (len + 7 >> 3);
    }

    private sealed class EncodeProcessor
    {
        private readonly HpackHuffmanEncoder encoder;
        internal readonly Func<byte, bool> processor;
        internal ByteBuf @out;
        private long current;
        private int n;

        internal EncodeProcessor(HpackHuffmanEncoder encoder)
        {
            this.encoder = encoder;
            processor = Process;
        }

        public bool Process(byte value)
        {
            int b = value & 0xFF;
            int nbits = encoder.lengths[b];

            current <<= nbits;
            current |= (uint) encoder.codes[b];
            n += nbits;

            while (n >= 8)
            {
                n -= 8;
                @out.WriteByte((int) (current >> n));
            }

            return true;
        }

        internal void End()
        {
            try
            {
                if (n > 0)
                {
                    current <<= 8 - n;
                    current |= (uint) (0xFF >>> n); // this should be EOS symbol
                    @out.WriteByte((int) current);
                }
            }
            finally
            {
                @out = null;
                current = 0;
                n = 0;
            }
        }
    }

    private sealed class EncodedLengthProcessor
    {
        private readonly HpackHuffmanEncoder encoder;
        internal readonly Func<byte, bool> processor;
        private long len;

        internal EncodedLengthProcessor(HpackHuffmanEncoder encoder)
        {
            this.encoder = encoder;
            processor = Process;
        }

        public bool Process(byte value)
        {
            len += encoder.lengths[value & 0xFF];
            return true;
        }

        internal void Reset()
        {
            len = 0;
        }

        internal int Length()
        {
            return (int) ((len + 7) >> 3);
        }
    }
}
