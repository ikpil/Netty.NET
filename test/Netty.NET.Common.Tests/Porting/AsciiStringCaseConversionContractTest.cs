using System;

namespace Netty.NET.Common.Tests.Porting;

public class AsciiStringCaseConversionContractTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(257)]
    [InlineData(1025)]
    public void EveryWordAndTailMatchesScalarAsciiOracle(int length)
    {
        // A fixed generator exercises mixed high bytes and ASCII boundaries at
        // every alignment without using the production SWAR or case helpers.
        var random = new Random(4219 + length);
        for (int offset = 0; offset < 16; offset++)
        {
            byte[] storage = new byte[offset + length + 8];
            random.NextBytes(storage);
            if (length > 0)
                storage[offset] = (byte)'A';
            if (length > 1)
                storage[offset + length - 1] = (byte)'z';
            Verify(storage, offset, length);
        }
    }

    [Fact]
    public void EveryByteValuePreservesNonAsciiAndPunctuation()
    {
        for (int value = 0; value <= 255; value++)
        {
            byte[] storage = new byte[40];
            Array.Fill(storage, (byte)value);
            // 8-byte words, 4/2/1-byte tails, and an unaligned source slice.
            for (int length = 8; length <= 23; length++)
                Verify(storage, 3, length);
        }
    }

    [Fact]
    public void EveryByteLaneMatchesOracleWithHighBitNeighbors()
    {
        for (int value = 0; value <= 255; value++)
            for (int lane = 0; lane < 8; lane++)
            {
                byte[] storage = { 0xcc, 0xff, 0x80, 0x7f, 0xff, 0, 0x80, 0xff, 0x80, 0xcc };
                storage[lane + 1] = (byte)value;
                Verify(storage, 1, 8);
            }
    }

    private static void Verify(byte[] storage, int offset, int length)
    {
        byte[] original = (byte[])storage.Clone();
        byte[] lower = new byte[length];
        byte[] upper = new byte[length];
        bool hasUpper = false, hasLower = false;
        for (int i = 0; i < length; i++)
        {
            byte value = storage[offset + i];
            bool uppercase = value >= 65 && value <= 90;
            bool lowercase = value >= 97 && value <= 122;
            lower[i] = uppercase ? (byte)(value + 32) : value;
            upper[i] = lowercase ? (byte)(value - 32) : value;
            hasUpper |= uppercase;
            hasLower |= lowercase;
        }
        var source = new AsciiString(storage, offset, length, false);
        AsciiString actualLower = source.ToLowerCase();
        AsciiString actualUpper = source.ToUpperCase();
        Assert.Equal(lower, actualLower.AsSpan().ToArray());
        Assert.Equal(upper, actualUpper.AsSpan().ToArray());
        Assert.Equal(original, storage);
        Assert.Equal(!hasUpper, ReferenceEquals(source, actualLower));
        Assert.Equal(!hasLower, ReferenceEquals(source, actualUpper));
    }
}
