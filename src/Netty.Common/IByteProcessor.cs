/*
 * Copyright 2015 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;

namespace Netty.NET.Common;

/**
 * Provides a mechanism to iterate over a collection of bytes.
 */
public interface IByteProcessor
{
    /**
     * @return {@code true} if the processor wants to continue the loop and handle the next byte in the buffer.
     *         {@code false} if the processor wants to stop handling bytes and abort the loop.
     */
    bool process(byte value);
}

/**
* A {@link IByteProcessor} which finds the first appearance of a specific byte.
*/
public sealed class IndexOfProcessor : IByteProcessor
{
    private readonly byte byteToFind;

    public IndexOfProcessor(byte byteToFind)
    {
        this.byteToFind = byteToFind;
    }

    public bool process(byte value)
    {
        return value != byteToFind;
    }
}

/**
 * A {@link IByteProcessor} which finds the first appearance which is not of a specific byte.
 */
public sealed class IndexNotOfProcessor : IByteProcessor
{
    private readonly byte byteToNotFind;

    public IndexNotOfProcessor(byte byteToNotFind)
    {
        this.byteToNotFind = byteToNotFind;
    }

    public bool process(byte value)
    {
        return value == byteToNotFind;
    }
}

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