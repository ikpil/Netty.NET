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
/// <remarks>Predicates return true to continue scanning and false to stop. Stateful visitors use ordinary closures.</remarks>
public static class ByteProcessor
{

    /**
     * Aborts on a {@code NUL (0x00)}.
     */
    public static readonly Func<byte, bool> FIND_NUL = static value => value != 0;

    /**
     * Aborts on a non-{@code NUL (0x00)}.
     */
    public static readonly Func<byte, bool> FIND_NON_NUL = static value => value == 0;

    /**
     * Aborts on a {@code CR ('\r')}.
     */
    public static readonly Func<byte, bool> FIND_CR = static value => value != (byte)'\r';

    /**
     * Aborts on a non-{@code CR ('\r')}.
     */
    public static readonly Func<byte, bool> FIND_NON_CR = static value => value == (byte)'\r';

    /**
     * Aborts on a {@code LF ('\n')}.
     */
    public static readonly Func<byte, bool> FIND_LF = static value => value != (byte)'\n';

    /**
     * Aborts on a non-{@code LF ('\n')}.
     */
    public static readonly Func<byte, bool> FIND_NON_LF = static value => value == (byte)'\n';

    /**
     * Aborts on a semicolon {@code (';')}.
     */
    public static readonly Func<byte, bool> FIND_SEMI_COLON = static value => value != (byte)';';

    /**
     * Aborts on a comma {@code (',')}.
     */
    public static readonly Func<byte, bool> FIND_COMMA = static value => value != (byte)',';

    /**
     * Aborts on a ascii space character ({@code ' '}).
     */
    public static readonly Func<byte, bool> FIND_ASCII_SPACE = static value => value != (byte)' ';

    /**
     * Aborts on a {@code CR ('\r')} or a {@code LF ('\n')}.
     */
    public static readonly Func<byte, bool> FIND_CRLF = static value => value != (byte)'\r' && value != (byte)'\n';

    /**
     * Aborts on a byte which is neither a {@code CR ('\r')} nor a {@code LF ('\n')}.
     */
    public static readonly Func<byte, bool> FIND_NON_CRLF = static value => value == (byte)'\r' || value == (byte)'\n';

    /**
     * Aborts on a linear whitespace (a ({@code ' '} or a {@code '\t'}).
     */
    public static readonly Func<byte, bool> FIND_LINEAR_WHITESPACE = static value => value != (byte)' ' && value != (byte)'\t';

    /**
     * Aborts on a byte which is not a linear whitespace (neither {@code ' '} nor {@code '\t'}).
     */
    public static readonly Func<byte, bool> FIND_NON_LINEAR_WHITESPACE = static value => value == (byte)' ' || value == (byte)'\t';
}
