using System;

namespace Netty.NET.Common.Internal;

public static class RandomExtensions
{
    public static void NextBytes(this Random rnd, byte[] bytes)
    {
        rnd.NextBytes(bytes);
    }
}