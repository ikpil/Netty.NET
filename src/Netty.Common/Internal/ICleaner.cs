using System;

namespace Netty.NET.Common.Internal;

public interface ICleaner
{
    ICleanableDirectBuffer allocate(int capacity);
    void freeDirectBuffer(ByteBuffer buffer);
}
