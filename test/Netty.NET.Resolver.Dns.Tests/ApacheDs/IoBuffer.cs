// CLR: test-only stand-in for org.apache.mina.core.buffer.IoBuffer of Apache MINA 2.2.3, the MINA version that
// apacheds-protocol-dns 2.0.0.AM27 pins (mina.core.version in
// https://github.com/apache/directory-server/blob/2.0.0.AM27/pom.xml):
// https://github.com/apache/mina/blob/2.2.3/mina-core/src/main/java/org/apache/mina/core/buffer/IoBuffer.java
// with AbstractIoBuffer.java and SimpleBufferAllocator.java of the same folder. IoBuffer.allocate(capacity) returns a
// SimpleBuffer over a heap java.nio.ByteBuffer that does not auto-expand; this sealed class folds that hierarchy into
// one buffer over a byte[] with the java.nio.Buffer rules
// (https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/Buffer.html): capacity never changes, limit
// and position stay within it, flip() sets the limit to the position and the position to zero, rewind() sets the
// position to zero, and multi-byte values are big-endian (the ByteBuffer default). A relative put past the limit
// throws InvalidOperationException where Java throws BufferOverflowException, and a relative get past the limit
// InvalidOperationException where Java throws BufferUnderflowException ("in either case, no data is transferred");
// an absolute index outside the limit throws ArgumentOutOfRangeException (IndexOutOfBoundsException) and position(int)
// outside [0, limit] ArgumentException (IllegalArgumentException). Java's signed byte is the C# byte (owner decision
// for ByteBuf); getUnsigned() keeps MINA's short result. Only the members the resolver-dns tests and this stand-in use
// are present; mark/reset, auto-expansion, direct buffers and the other getters have no caller.

using System;
using System.Buffers.Binary;

namespace Netty.NET.Resolver.Dns.Tests.ApacheDs;

public sealed class IoBuffer
{
    private readonly byte[] array;
    private int position;
    private int limit;

    private IoBuffer(byte[] array)
    {
        this.array = array;
        limit = array.Length;
    }

    public static IoBuffer Allocate(int capacity)
    {
        if (capacity < 0)
        {
            throw new ArgumentException("capacity: " + capacity);
        }

        return new IoBuffer(new byte[capacity]);
    }

    public static IoBuffer Wrap(byte[] byteArray)
    {
        return new IoBuffer(byteArray);
    }

    public byte[] Array()
    {
        return array;
    }

    public int Capacity()
    {
        return array.Length;
    }

    public int Position()
    {
        return position;
    }

    public IoBuffer Position(int newPosition)
    {
        if (newPosition < 0 || newPosition > limit)
        {
            throw new ArgumentException("newPosition > limit: (" + newPosition + " > " + limit + ")");
        }

        position = newPosition;
        return this;
    }

    public int Limit()
    {
        return limit;
    }

    public IoBuffer Limit(int newLimit)
    {
        if (newLimit < 0 || newLimit > array.Length)
        {
            throw new ArgumentException("newLimit > capacity: (" + newLimit + " > " + array.Length + ")");
        }

        limit = newLimit;

        if (position > newLimit)
        {
            position = newLimit;
        }

        return this;
    }

    public IoBuffer Flip()
    {
        limit = position;
        position = 0;
        return this;
    }

    public IoBuffer Rewind()
    {
        position = 0;
        return this;
    }

    public int Remaining()
    {
        return limit - position;
    }

    public bool HasRemaining()
    {
        return limit > position;
    }

    public byte Get()
    {
        return array[NextGetIndex(1)];
    }

    public byte Get(int index)
    {
        return array[CheckIndex(index, 1)];
    }

    public IoBuffer Get(byte[] dst)
    {
        int index = NextGetIndex(dst.Length);
        System.Array.Copy(array, index, dst, 0, dst.Length);
        return this;
    }

    public short GetUnsigned()
    {
        return (short)(Get() & 0xff);
    }

    public short GetShort()
    {
        return BinaryPrimitives.ReadInt16BigEndian(array.AsSpan(NextGetIndex(2), 2));
    }

    public int GetUnsignedShort()
    {
        return GetShort() & 0xffff;
    }

    public int GetInt()
    {
        return BinaryPrimitives.ReadInt32BigEndian(array.AsSpan(NextGetIndex(4), 4));
    }

    public IoBuffer Put(byte b)
    {
        array[NextPutIndex(1)] = b;
        return this;
    }

    public IoBuffer Put(byte[] src)
    {
        return Put(src, 0, src.Length);
    }

    public IoBuffer Put(byte[] src, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > src.Length - length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        int index = NextPutIndex(length);
        System.Array.Copy(src, offset, array, index, length);
        return this;
    }

    public IoBuffer PutShort(short value)
    {
        BinaryPrimitives.WriteInt16BigEndian(array.AsSpan(NextPutIndex(2), 2), value);
        return this;
    }

    public IoBuffer PutInt(int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(array.AsSpan(NextPutIndex(4), 4), value);
        return this;
    }

    private int NextGetIndex(int length)
    {
        if (limit - position < length)
        {
            throw new InvalidOperationException("BufferUnderflowException: " + length + " byte(s) requested, " +
                                                Remaining() + " remaining");
        }

        int index = position;
        position += length;
        return index;
    }

    private int NextPutIndex(int length)
    {
        if (limit - position < length)
        {
            throw new InvalidOperationException("BufferOverflowException: " + length + " byte(s) to write, " +
                                                Remaining() + " remaining");
        }

        int index = position;
        position += length;
        return index;
    }

    private int CheckIndex(int index, int length)
    {
        if (index < 0 || length > limit - index)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "index outside the limit " + limit);
        }

        return index;
    }

    public override string ToString()
    {
        return "HeapBuffer[pos=" + position + " lim=" + limit + " cap=" + array.Length + "]";
    }
}
