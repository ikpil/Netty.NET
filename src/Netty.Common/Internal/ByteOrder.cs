using System;

namespace Netty.NET.Common.Internal;

public class ByteOrder
{
    public readonly string _name;

    private ByteOrder(string name)
    {
        _name = name;
    }

    /**
     * Constant denoting big-endian byte order.  In this order, the bytes of a
     * multibyte value are ordered from most significant to least significant.
     */
    public static readonly ByteOrder BIG_ENDIAN = new ByteOrder(nameof(BIG_ENDIAN));

    /**
     * Constant denoting little-endian byte order.  In this order, the bytes of
     * a multibyte value are ordered from least significant to most
     * significant.
     */
    public static readonly ByteOrder LITTLE_ENDIAN = new ByteOrder(nameof(LITTLE_ENDIAN));

    // Retrieve the native byte order. It's used early during bootstrap, and
    // must be initialized after BIG_ENDIAN and LITTLE_ENDIAN.
    private static readonly ByteOrder NATIVE_ORDER = BitConverter.IsLittleEndian
        ? LITTLE_ENDIAN
        : BIG_ENDIAN;

    /**
     * Retrieves the native byte order of the underlying platform.
     *
     * <p> This method is defined so that performance-sensitive Java code can
     * allocate direct buffers with the same byte order as the hardware.
     * Native code libraries are often more efficient when such buffers are
     * used.  </p>
     *
     * @return  The native byte order of the hardware upon which this Java
     *          virtual machine is running
     */
    public static ByteOrder nativeOrder()
    {
        return NATIVE_ORDER;
    }

    /**
     * Constructs a string describing this object.
     *
     * <p> This method returns the string
     * {@code "BIG_ENDIAN"} for {@link #BIG_ENDIAN} and
     * {@code "LITTLE_ENDIAN"} for {@link #LITTLE_ENDIAN}.
     *
     * @return  The specified string
     */
    public override string ToString()
    {
        return _name;
    }
}