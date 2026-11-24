/*
 * Copyright 2016 The Netty Project
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
using System.Buffers;
using System.Globalization;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Compression;

// CLR: the package-private final class is an internal sealed class with the private constructor, as the codec modules
// port their package-private utility classes (docs/codec-redis-porting.md). String.format("%d") is string.Format with
// the invariant culture, because Java decimal text does not depend on a culture (docs/codec-base-porting.md).
internal sealed class CompressionUtil
{

    internal static readonly int DEFAULT_MAX_FORWARD_BYTES = SystemPropertyUtil.GetInt(
            "io.netty.compression.defaultMaxForwardBytes", 64 * 1024);

    private CompressionUtil() { }

    internal static void CheckChecksum(ByteBufChecksum checksum, ByteBuf uncompressed, int currentChecksum)
    {
        checksum.Reset();
        checksum.Update(uncompressed,
                uncompressed.ReaderIndex, uncompressed.ReadableBytes);

        int checksumResult = (int) checksum.GetValue();
        if (checksumResult != currentChecksum)
        {
            throw new DecompressionException(string.Format(CultureInfo.InvariantCulture,
                    "stream corrupted: mismatching checksum: {0} (expected: {1})",
                    checksumResult, currentChecksum));
        }
    }

    // CLR: java.nio.ByteBuffer is ReadOnlyMemory<byte>, the Buffer port's nioBuffer(int, int) (AsReadOnlyMemory, see
    // ReplayingDecoderByteBuf in docs/codec-base-porting.md); the callers only read the returned range.
    internal static ReadOnlyMemory<byte> SafeReadableNioBuffer(ByteBuf buffer)
    {
        return SafeNioBuffer(buffer, buffer.ReaderIndex, buffer.ReadableBytes);
    }

    // CLR: ByteBuf.nioBufferCount() has no counterpart. AsReadOnlySequence (the Buffer port's nioBuffers(int, int)) is
    // a single segment exactly when the range lies in one backing buffer, which is then borrowed as
    // internalNioBuffer(index, length) does; a range spanning several components is copied, as nioBuffer(index,
    // length) merges such a range into a new buffer (the same mapping as MqttDecoder.readStrictUtf8 of codec-mqtt).
    internal static ReadOnlyMemory<byte> SafeNioBuffer(ByteBuf buffer, int index, int length)
    {
        ReadOnlySequence<byte> nioBuffers = buffer.AsReadOnlySequence(index, length);
        return nioBuffers.IsSingleSegment ? nioBuffers.First
                : ByteBufUtil.GetBytes(buffer, index, length);
    }
}
