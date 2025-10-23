using System;

namespace Netty.NET.Common;

public sealed class ByteProcessor : IByteProcessor
{
    /**
     * Aborts on a {@code NUL (0x00)}.
     */
    public static readonly IndexOfProcessor FIND_NUL = new IndexOfProcessor((byte)0);

    /**
     * Aborts on a non-{@code NUL (0x00)}.
     */
    public static readonly IndexNotOfProcessor FIND_NON_NUL = new IndexNotOfProcessor((byte)0);

    /**
     * Aborts on a {@code CR ('\r')}.
     */
    public static readonly IndexOfProcessor FIND_CR = new IndexOfProcessor(ByteProcessorUtils.CARRIAGE_RETURN);

    /**
     * Aborts on a non-{@code CR ('\r')}.
     */
    public static readonly IndexNotOfProcessor FIND_NON_CR = new IndexNotOfProcessor(ByteProcessorUtils.CARRIAGE_RETURN);

    /**
     * Aborts on a {@code LF ('\n')}.
     */
    public static readonly IndexOfProcessor FIND_LF = new IndexOfProcessor(ByteProcessorUtils.LINE_FEED);

    /**
     * Aborts on a non-{@code LF ('\n')}.
     */
    public static readonly IndexNotOfProcessor FIND_NON_LF = new IndexNotOfProcessor(ByteProcessorUtils.LINE_FEED);

    /**
     * Aborts on a semicolon {@code (';')}.
     */
    public static readonly IndexOfProcessor FIND_SEMI_COLON = new IndexOfProcessor((byte)';');

    /**
     * Aborts on a comma {@code (',')}.
     */
    public static readonly IndexOfProcessor FIND_COMMA = new IndexOfProcessor((byte)',');

    /**
     * Aborts on a ascii space character ({@code ' '}).
     */
    public static readonly IndexOfProcessor FIND_ASCII_SPACE = new IndexOfProcessor(ByteProcessorUtils.SPACE);

    /**
     * Aborts on a {@code CR ('\r')} or a {@code LF ('\n')}.
     */
    public static readonly ByteProcessor FIND_CRLF
        = new ByteProcessor(value => value != ByteProcessorUtils.CARRIAGE_RETURN && value != ByteProcessorUtils.LINE_FEED);

    /**
     * Aborts on a byte which is neither a {@code CR ('\r')} nor a {@code LF ('\n')}.
     */
    public static readonly ByteProcessor FIND_NON_CRLF
        = new ByteProcessor(value => value == ByteProcessorUtils.CARRIAGE_RETURN || value == ByteProcessorUtils.LINE_FEED);

    /**
     * Aborts on a linear whitespace (a ({@code ' '} or a {@code '\t'}).
     */
    public static readonly ByteProcessor FIND_LINEAR_WHITESPACE
        = new ByteProcessor(value => value != ByteProcessorUtils.SPACE && value != ByteProcessorUtils.HTAB);

    /**
     * Aborts on a byte which is not a linear whitespace (neither {@code ' '} nor {@code '\t'}).
     */
    public static readonly ByteProcessor FIND_NON_LINEAR_WHITESPACE
        = new ByteProcessor(value => value == ByteProcessorUtils.SPACE || value == ByteProcessorUtils.HTAB);

    private readonly Func<byte, bool> _handler;

    public ByteProcessor(Func<byte, bool> handler)
    {
        _handler = handler;
    }

    public bool process(byte value)
    {
        return _handler.Invoke(value);
    }
}