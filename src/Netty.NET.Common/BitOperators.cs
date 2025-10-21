namespace Netty.NET.Common;

public static class BitOperators
{
    public static int NumberOfLeadingZeros(int i)
    {
        if (i == 0) return 32;

        int n = 1;
        uint x = (uint)i;

        if ((x >> 16) == 0) { n += 16; x <<= 16; }
        if ((x >> 24) == 0) { n += 8;  x <<= 8; }
        if ((x >> 28) == 0) { n += 4;  x <<= 4; }
        if ((x >> 30) == 0) { n += 2;  x <<= 2; }
        n -= (int)(x >> 31);

        return n;
    } 
    
    public static int NumberOfLeadingZeros(long i)
    {
        if (i == 0) return 64;

        int n = 1;
        ulong x = (ulong)i;

        if ((x >> 32) == 0) { n += 32; x <<= 32; }
        if ((x >> 48) == 0) { n += 16; x <<= 16; }
        if ((x >> 56) == 0) { n += 8;  x <<= 8; }
        if ((x >> 60) == 0) { n += 4;  x <<= 4; }
        if ((x >> 62) == 0) { n += 2;  x <<= 2; }
        n -= (int)(x >> 63);

        return n;
    }
    
    public static int NumberOfTrailingZeros(int i)
    {
        if (i == 0) return 32;

        int n = 31;
        uint x = (uint)i;

        x = x & (uint)(-i); // isolate lowest set bit
        if ((x & 0x0000FFFF) != 0) { n -= 16; }
        if ((x & 0x00FF00FF) != 0) { n -= 8; }
        if ((x & 0x0F0F0F0F) != 0) { n -= 4; }
        if ((x & 0x33333333) != 0) { n -= 2; }
        if ((x & 0x55555555) != 0) { n -= 1; }

        return n;
    }
    
    public static int NumberOfTrailingZeros(long i)
    {
        if (i == 0) return 64;

        int n = 63;
        ulong x = (ulong)i;

        x = x & (ulong)(-i); // isolate lowest set bit
        if ((x & 0x00000000FFFFFFFFUL) != 0) { n -= 32; }
        if ((x & 0x0000FFFF0000FFFFUL) != 0) { n -= 16; }
        if ((x & 0x00FF00FF00FF00FFUL) != 0) { n -= 8; }
        if ((x & 0x0F0F0F0F0F0F0F0FUL) != 0) { n -= 4; }
        if ((x & 0x3333333333333333UL) != 0) { n -= 2; }
        if ((x & 0x5555555555555555UL) != 0) { n -= 1; }

        return n;
    }

}