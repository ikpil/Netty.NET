using System;
using System.Buffers;
using System.Buffers.Binary;

namespace Netty.NET.Common.Tests.Porting;

// Pinned buffer/VarHandleByteBufferAccess uses byte offsets and explicit endian
// views; these consumers use the real common owner/view lifetime and bounds.
public class EndianMemoryConsumerContractTest
{
    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(8, false)]
    [InlineData(8, true)]
    public void WireWordsPreserveSignedBitsAndLogicalBoundsAcrossHeapOwnedAndBorrowedMemory(int width, bool little)
    {
        Check(new byte[24], width, little);
        var allocator = new NativeMemoryAllocator();
        using var owner = allocator.Allocate(24);
        Check(owner.Memory, width, little);
        using var pin = owner.Memory.Pin();
        using var view = new NativeMemoryView(Address(pin), 24);
        Check(view.Memory, width, little);
    }

    private static void Check(Memory<byte> memory, int width, bool little)
    {
        long[] values = { 0, 1, -1, long.MinValue, unchecked((long)0x8070605040302010UL), 0x123456789abcdef0L };
        foreach (long value in values)
        {
            long narrowed = width == 2 ? unchecked((short)value) : width == 4 ? unchecked((int)value) : value;
            for (int offset = 0; offset < 8; offset++)
            {
                memory.Span.Fill(0x5a);
                Memory<byte> region = memory.Slice(offset, width);
                Write(region.Span, width, little, value);
                Assert.Equal(narrowed, Read(region.Span, width, little));
                byte[] expected = new byte[24];
                Array.Fill(expected, (byte)0x5a);
                for (int i = 0; i < width; i++)
                {
                    int shift = 8 * (little ? i : width - i - 1);
                    expected[offset + i] = unchecked((byte)((ulong)value >> shift));
                }
                Assert.Equal(expected, memory.ToArray());

                // The backing allocation is large enough; the logical view is not.
                Memory<byte> tooShort = region.Slice(0, width - 1);
                Assert.Throws<ArgumentOutOfRangeException>(() => Read(tooShort.Span, width, little));
                Assert.Throws<ArgumentOutOfRangeException>(() => Write(tooShort.Span, width, little, ~value));
                Assert.Equal(expected, memory.ToArray());
            }
        }
    }

    private static long Read(ReadOnlySpan<byte> memory, int width, bool little) => width switch
    {
        2 => little ? BinaryPrimitives.ReadInt16LittleEndian(memory) : BinaryPrimitives.ReadInt16BigEndian(memory),
        4 => little ? BinaryPrimitives.ReadInt32LittleEndian(memory) : BinaryPrimitives.ReadInt32BigEndian(memory),
        8 => little ? BinaryPrimitives.ReadInt64LittleEndian(memory) : BinaryPrimitives.ReadInt64BigEndian(memory),
        _ => throw new ArgumentOutOfRangeException(nameof(width))
    };

    private static void Write(Span<byte> memory, int width, bool little, long value)
    {
        switch (width)
        {
            case 2:
                if (little) BinaryPrimitives.WriteInt16LittleEndian(memory, unchecked((short)value));
                else BinaryPrimitives.WriteInt16BigEndian(memory, unchecked((short)value));
                break;
            case 4:
                if (little) BinaryPrimitives.WriteInt32LittleEndian(memory, unchecked((int)value));
                else BinaryPrimitives.WriteInt32BigEndian(memory, unchecked((int)value));
                break;
            case 8:
                if (little) BinaryPrimitives.WriteInt64LittleEndian(memory, value);
                else BinaryPrimitives.WriteInt64BigEndian(memory, value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(width));
        }
    }

    private static unsafe nint Address(MemoryHandle pin) => (nint)pin.Pointer;
}
