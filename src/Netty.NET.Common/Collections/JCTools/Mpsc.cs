using System;
using Netty.NET.Common.Internal;
using Netty.NET.Common.Internal.Logging;

namespace Netty.NET.Common.Collections.JCTools;

public static class Mpsc
{
    private static readonly IInternalLogger logger = InternalLoggerFactory.getInstance(typeof(Mpsc));

    private static readonly bool USE_MPSC_CHUNKED_ARRAY_QUEUE;

    static Mpsc()
    {
        // object @unsafe = null;
        // if (hasUnsafe())
        // {
        //     // jctools goes through its own process of initializing unsafe; of
        //     // course, this requires permissions which might not be granted to calling code, so we
        //     // must mark this block as privileged too
        //     @unsafe = AccessController.doPrivileged(new PrivilegedAction<object>()
        //     {
        //         @Override
        //         public object run() {
        //         // force JCTools to initialize unsafe
        //         return UnsafeAccess.UNSAFE;
        //     }
        //     });
        // }
        //
        // if (@unsafe == null) {
        //     logger.debug("org.jctools-core.MpscChunkedArrayQueue: unavailable");
        //     USE_MPSC_CHUNKED_ARRAY_QUEUE = false;
        // } else {
        //     logger.debug("org.jctools-core.MpscChunkedArrayQueue: available");
        //     USE_MPSC_CHUNKED_ARRAY_QUEUE = true;
        // }
    }

    public static IQueue<T> newMpscQueue<T>(int maxCapacity)
    {
        // Calculate the max capacity which can not be bigger than MAX_ALLOWED_MPSC_CAPACITY.
        // This is forced by the MpscChunkedArrayQueue implementation as will try to round it
        // up to the next power of two and so will overflow otherwise.
        int capacity = Math.Max(Math.Min(maxCapacity, PlatformDependent.MAX_ALLOWED_MPSC_CAPACITY), PlatformDependent.MIN_MAX_MPSC_CAPACITY);
        return newChunkedMpscQueue<T>(PlatformDependent.MPSC_CHUNK_SIZE, capacity);
    }

    public static IQueue<T> newChunkedMpscQueue<T>(int chunkSize, int capacity)
    {
        throw new NotImplementedException();
        // return USE_MPSC_CHUNKED_ARRAY_QUEUE
        //     ? new MpscChunkedArrayQueue<T>(chunkSize, capacity)
        //     : new MpscChunkedAtomicArrayQueue<T>(chunkSize, capacity);
    }

    public static IQueue<T> newMpscQueue<T>()
    {
        throw new NotImplementedException();
        // return USE_MPSC_CHUNKED_ARRAY_QUEUE
        //     ? new MpscUnboundedArrayQueue<T>(MPSC_CHUNK_SIZE)
        //     : new MpscUnboundedAtomicArrayQueue<T>(MPSC_CHUNK_SIZE);
    }
}