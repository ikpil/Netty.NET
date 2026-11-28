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
using System.IO.Hashing;
using System.Runtime.InteropServices;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Compression;

/**
 * {@link Checksum} implementation which can directly act on a {@link ByteBuf}.
 * <p>
 * Implementations may optimize access patterns depending on if the {@link ByteBuf} is backed by a
 * byte array ({@link ByteBuf#hasArray()} is {@code true}) or not.
 */
// CLR: owner decision 2026-11-23: java.util.zip.Checksum is System.IO.Hashing.NonCryptographicHashAlgorithm
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.noncryptographichashalgorithm?view=net-10.0-pp), so a
// ByteBufChecksum can be passed wherever a NonCryptographicHashAlgorithm is expected. The Checksum members keep their
// Java names and shapes: update(int) is Update(int), update(byte[], int, int) is Update(byte[], int, int) and getValue()
// is GetValue(), returning the Java long; reset() is the base class's public abstract Reset(), "When overridden in a
// derived class, resets the hash computation to the initial state"
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.noncryptographichashalgorithm.reset?view=net-10.0-pp).
// The base class constructor takes "The number of bytes produced from this hash algorithm"
// (https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.noncryptographichashalgorithm.-ctor?view=net-10.0-pp),
// so the constructor takes the hash length. Its abstract GetCurrentHashCore(Span<byte>), which "must write exactly
// HashLengthInBytes bytes" (https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.noncryptographichashalgorithm.getcurrenthashcore?view=net-10.0-pp),
// is left to the subclasses. The class is internal as the package-private Java class; its members are public as in Java.
internal abstract class ByteBufChecksum : NonCryptographicHashAlgorithm
{
    // CLR: the anonymous ByteProcessor is a Func<byte, bool> (the Buffer port's forEachByte processor, see
    // docs/codec-redis-porting.md); it is created in the constructor because a C# field initializer cannot reference
    // this. The Buffer port's bytes are unsigned, but update(int) uses only the low eight bits of its argument ("Updates
    // the current checksum with the specified byte", https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/zip/Checksum.html),
    // so the value of a negative Java byte and the same unsigned byte give the same checksum.
    private readonly Func<byte, bool> updateProcessor;

    protected ByteBufChecksum(int hashLengthInBytes) : base(hashLengthInBytes)
    {
        updateProcessor = value =>
        {
            Update(value);
            return true;
        };
    }

    // CLR: checkNotNull's NullPointerException is ArgumentNullException.
    internal static ByteBufChecksum WrapChecksum(NonCryptographicHashAlgorithm checksum)
    {
        ObjectUtil.CheckNotNull(checksum, "checksum");
        if (checksum is ByteBufChecksum)
        {
            return (ByteBufChecksum) checksum;
        }

        return new JdkByteBufChecksum(checksum);
    }

    // CLR: the abstract java.util.zip.Checksum methods that NonCryptographicHashAlgorithm does not declare.
    public abstract void Update(int b);

    public abstract void Update(byte[] b, int off, int len);

    public abstract long GetValue();

    // CLR: Append(ReadOnlySpan<byte>) is abstract in NonCryptographicHashAlgorithm ("When overridden in a derived class,
    // appends the contents of source", https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.noncryptographichashalgorithm.append?view=net-10.0-pp);
    // it is Checksum's default update(ByteBuffer), whose implementation for a buffer "not backed by an accessible byte
    // array" copies at most 4096 bytes at a time into a temporary array and calls update(b, 0, length)
    // (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/zip/Checksum.html). A span does not expose
    // a backing array, so the copying branch is the only one.
    public override void Append(ReadOnlySpan<byte> source)
    {
        byte[] b = new byte[Math.Min(source.Length, 4096)];

        while (!source.IsEmpty)
        {
            int length = Math.Min(source.Length, b.Length);
            source.Slice(0, length).CopyTo(b);
            Update(b, 0, length);
            source = source.Slice(length);
        }
    }

    /**
     * @see #update(byte[], int, int)
     */
    public virtual void Update(ByteBuf b, int off, int len)
    {
        if (TryGetArray(b, off, len, out ArraySegment<byte> array))
        {
            Update(array.Array, array.Offset, len);
        }
        else
        {
            b.ForEachByte(off, len, updateProcessor);
        }
    }

    // CLR: the Buffer port has no hasArray()/array()/arrayOffset(); borrowed Memory and MemoryMarshal.TryGetArray
    // replace them. The range is read through AsReadOnlySequence (nioBuffers(int, int)); a single segment backed
    // by a managed array gives the array and the array index of buffer index off, which is b.arrayOffset() + off.
    // As the Buffer port records, a range that lies in one array of a composite buffer also takes this branch
    // where Java's hasArray() is false; both branches give the same checksum.
    private static bool TryGetArray(ByteBuf b, int off, int len, out ArraySegment<byte> array)
    {
        ReadOnlySequence<byte> range = b.AsReadOnlySequence(off, len);
        if (range.IsSingleSegment && MemoryMarshal.TryGetArray(range.First, out array))
        {
            return true;
        }

        array = default;
        return false;
    }

    // CLR: the wrapped Checksum is a NonCryptographicHashAlgorithm. update(int) appends the low eight bits as a one-byte
    // span and update(byte[], int, int) the array range, because NonCryptographicHashAlgorithm only appends spans. The
    // wrapper reports the wrapped algorithm's HashLengthInBytes and writes its GetCurrentHash bytes, in the byte order the
    // wrapped type documents (Crc32: "Little Endian", https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.crc32?view=net-10.0-pp;
    // XxHash32: "Big Endian", https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.xxhash32?view=net-10.0-pp;
    // Adler32: big endian, BinaryPrimitives.WriteUInt32BigEndian in
    // https://raw.githubusercontent.com/dotnet/dotnet/297acf930fb0f5c07fd4e51d78e521b4ad2e5d1f/src/runtime/src/libraries/System.IO.Hashing/src/System/IO/Hashing/Adler32.cs).
    // The private class keeps its Java name although no JDK class is wrapped any more.
    private class JdkByteBufChecksum : ByteBufChecksum
    {
        protected readonly NonCryptographicHashAlgorithm checksum;

        internal JdkByteBufChecksum(NonCryptographicHashAlgorithm checksum) : base(checksum.HashLengthInBytes)
        {
            this.checksum = checksum;
        }

        public override void Update(int b)
        {
            ReadOnlySpan<byte> value = stackalloc byte[] { (byte) b };
            checksum.Append(value);
        }

        // CLR: CRC32.update(ByteBuffer) and Adler32.update(ByteBuffer) are Append(ReadOnlySpan<byte>) of the BCL Crc32 and
        // Adler32 (https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.crc32?view=net-10.0-pp,
        // https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.adler32?view=net-10.0-pp). The type tests keep
        // the source branches; Crc32 is written System.IO.Hashing.Crc32 because codec-compression's own Crc32 (bzip2)
        // shares this namespace.
        public override void Update(ByteBuf b, int off, int len)
        {
            if (TryGetArray(b, off, len, out ArraySegment<byte> array))
            {
                Update(array.Array, array.Offset, len);
            }
            else if (checksum is System.IO.Hashing.Crc32)
            {
                ReadOnlyMemory<byte> byteBuffer = GetSafeBuffer(b, off, len);
                ((System.IO.Hashing.Crc32) checksum).Append(byteBuffer.Span);
            }
            else if (checksum is Adler32)
            {
                ReadOnlyMemory<byte> byteBuffer = GetSafeBuffer(b, off, len);
                ((Adler32) checksum).Append(byteBuffer.Span);
            }
            else
            {
                base.Update(b, off, len);
            }
        }

        private ReadOnlyMemory<byte> GetSafeBuffer(ByteBuf b, int off, int len)
        {
            ReadOnlyMemory<byte> byteBuffer = CompressionUtil.SafeNioBuffer(b, off, len);
            // CLR: the source copies a direct NIO ByteBuffer into the scratchBuffer field when
            // PlatformDependent.javaVersion() is 22 to 24, before CRC32/Adler32.update(ByteBuffer), to avoid the JVM issue
            // the next comment links; the CLR appends the borrowed span directly, so there is no version test, no copy and
            // no scratchBuffer field.
            // Work-around for https://bugs.openjdk.org/browse/JDK-8357145
            return byteBuffer;
        }

        public override void Update(byte[] b, int off, int len)
        {
            checksum.Append(new ReadOnlySpan<byte>(b, off, len));
        }

        // CLR: NonCryptographicHashAlgorithm has no numeric value. CRC32, Adler32 and the XXHash32 that Netty wraps map to
        // System.IO.Hashing.Crc32, Adler32 and XxHash32, whose GetCurrentHashAsUInt32() "Gets the current computed hash
        // value without modifying accumulated state"
        // (https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.crc32.getcurrenthashasuint32?view=net-10.0-pp,
        // https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.adler32.getcurrenthashasuint32?view=net-10.0-pp,
        // https://learn.microsoft.com/en-us/dotnet/api/system.io.hashing.xxhash32.getcurrenthashasuint32?view=net-10.0-pp);
        // the unsigned 32-bit value is the Java long that CRC32/Adler32.getValue() return. Any other
        // NonCryptographicHashAlgorithm can be wrapped, updated and reset, but GetValue() throws NotSupportedException:
        // the base class only exposes the hash as bytes, whose byte order each type defines.
        public override long GetValue()
        {
            switch (checksum)
            {
                case System.IO.Hashing.Crc32 crc32:
                    return crc32.GetCurrentHashAsUInt32();
                case Adler32 adler32:
                    return adler32.GetCurrentHashAsUInt32();
                case XxHash32 xxHash32:
                    return xxHash32.GetCurrentHashAsUInt32();
                default:
                    throw new NotSupportedException("getValue() of " + checksum.GetType().FullName +
                            ": only System.IO.Hashing.Crc32, Adler32 and XxHash32 have a documented numeric value");
            }
        }

        public override void Reset()
        {
            checksum.Reset();
        }

        // CLR: NonCryptographicHashAlgorithm's abstract hash output (see the class comment).
        protected override void GetCurrentHashCore(Span<byte> destination)
        {
            checksum.GetCurrentHash(destination);
        }
    }
}
