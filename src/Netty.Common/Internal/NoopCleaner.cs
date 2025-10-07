using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Netty.NET.Common.Internal;

public class NoopCleaner : ICleaner
{
    public ICleanableDirectBuffer allocate(int capacity)
    {
        return new NoopDirectBufferImpl(capacity);
    }

    public void freeDirectBuffer(ByteBuffer buffer)
    {
        // NOOP
    }

    private class NoopDirectBufferImpl : ICleanableDirectBuffer
    {
        private readonly ArraySegment<byte> _byteBuffer;

        internal NoopDirectBufferImpl(int capacity)
        {
            // ByteBuffer.allocateDirect(capacity); 
            IntPtr handle = Marshal.AllocHGlobal(capacity);
            _byteBuffer = new ArraySegment<byte>(handle, 0, capacity);
        }

        public ArraySegment<byte> buffer()
        {
            return _byteBuffer;
        }

        public void clean()
        {
            // NOOP
        }
    }
}