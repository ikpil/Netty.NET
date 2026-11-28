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

using Netty.NET.Buffer;
using Netty.NET.Transport.Channels;

namespace Netty.NET.Handler.Codec.Protobuf;

/**
 * An encoder that prepends the Google Protocol Buffers
 * <a href="https://developers.google.com/protocol-buffers/docs/encoding?csw=1#varints">Base
 * 128 Varints</a> integer length field. For example:
 * <pre>
 * BEFORE ENCODE (300 bytes)       AFTER ENCODE (302 bytes)
 * +---------------+               +--------+---------------+
 * | Protobuf Data |-------------->| Length | Protobuf Data |
 * |  (300 bytes)  |               | 0xAC02 |  (300 bytes)  |
 * +---------------+               +--------+---------------+
 * </pre> *
 *
 * @see CodedOutputStream
 * @see CodedOutputByteBufferNano
 */
// CLR: the imports com.google.protobuf.CodedOutputStream and com.google.protobuf.nano.CodedOutputByteBufferNano serve
// only the @see links above, which are kept verbatim; the encoder implements the varint itself. protobuf-java and
// protobuf-javanano have no BCL counterpart and are not referenced (owner decision 2026-11-23,
// docs/codec-protobuf-porting.md), so these imports have no C# using. @Sharable is [Sharable], the Class argument of
// the MessageToByteEncoder constructor is a Type, ByteBuf out is ByteBuf @out (a C# keyword) and C# has no throws
// clauses (MessageToByteEncoder in codec-base, docs/codec-base-porting.md).
[Sharable]
public class ProtobufVarint32LengthFieldPrepender : MessageToByteEncoder<ByteBuf>
{

    public ProtobufVarint32LengthFieldPrepender() : base(typeof(ByteBuf))
    {
    }

    protected override void Encode(
            IChannelHandlerContext ctx, ByteBuf msg, ByteBuf @out)
    {
        int bodyLen = msg.ReadableBytes;
        int headerLen = ComputeRawVarint32Size(bodyLen);
        @out.EnsureWritable(headerLen + bodyLen);
        WriteRawVarint32(@out, bodyLen);
        @out.WriteBytes(msg, msg.ReaderIndex, bodyLen);
    }

    /**
     * Writes protobuf varint32 to (@link ByteBuf).
     * @param out to be written to
     * @param value to be written
     */
    // CLR: the package-private method is internal (the tests call it through InternalsVisibleTo). value >>>= 7 is the
    // same logical shift in both languages: Java's >>> shifts "with zero-extension" (JLS 15.19,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-15.html#jls-15.19), and the C# >>> operator "always
    // performs a logical shift" on an int
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/bitwise-and-shift-operators), so a
    // negative value is written as a five-byte varint as in Java.
    internal static void WriteRawVarint32(ByteBuf @out, int value)
    {
        while (true)
        {
            if ((value & ~0x7F) == 0)
            {
                @out.WriteByte(value);
                return;
            }
            else
            {
                @out.WriteByte((value & 0x7F) | 0x80);
                value >>>= 7;
            }
        }
    }

    /**
     * Computes size of protobuf varint32 after encoding.
     * @param value which is to be encoded.
     * @return size of value encoded as protobuf varint32.
     */
    // CLR: the package-private method is internal, and the final modifier of the parameter is dropped (the method does
    // not assign it). The Java literal 0xffffffff is the int -1 (JLS 3.10.1,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-3.html#jls-3.10.1), while the C# literal is the uint
    // 4,294,967,295; "unchecked((int)0xFF_FF_FF_FF) produces -1"
    // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types), so the
    // masks are written unchecked((int) 0xffffffff) << n and keep the Java int values.
    internal static int ComputeRawVarint32Size(int value)
    {
        if ((value & (unchecked((int) 0xffffffff) <<  7)) == 0)
        {
            return 1;
        }
        if ((value & (unchecked((int) 0xffffffff) << 14)) == 0)
        {
            return 2;
        }
        if ((value & (unchecked((int) 0xffffffff) << 21)) == 0)
        {
            return 3;
        }
        if ((value & (unchecked((int) 0xffffffff) << 28)) == 0)
        {
            return 4;
        }

        return 5;
    }
}
