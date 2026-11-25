/*
 * Copyright 2012 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System.Text;

namespace Netty.NET.Handler.Codec.Http;

// CLR: the final class with a private constructor is a static class. The byte constants are below 0x80, so the Java
// byte and System.Byte hold the same values; CharsetUtil.UTF_8 is Encoding.UTF8 (docs/codec-smtp-porting.md).
public static class HttpConstants
{

    /**
     * Horizontal space
     */
    public const byte SP = 32;

    /**
     * Horizontal tab
     */
    public const byte HT = 9;

    /**
     * Carriage return
     */
    public const byte CR = 13;

    /**
     * Equals '='
     */
    public const byte EQUALS = 61;

    /**
     * Line feed character
     */
    public const byte LF = 10;

    /**
     * Colon ':'
     */
    public const byte COLON = 58;

    /**
     * Semicolon ';'
     */
    public const byte SEMICOLON = 59;

    /**
     * Comma ','
     */
    public const byte COMMA = 44;

    /**
     * Double quote '"'
     */
    public const byte DOUBLE_QUOTE = (byte) '"';

    /**
     * Backslash '\'
     */
    public const byte BACKSLASH = (byte) '\\';

    /**
     * ASCII DEL character code
     */
    public const byte DEL = 0x7F;

    /**
     * Default character set (UTF-8)
     */
    public static readonly Encoding DEFAULT_CHARSET = Encoding.UTF8;

    /**
     * Horizontal space
     */
    public const char SP_CHAR = (char) SP;

    // CLR: the private constructor and its comment; a static class has no constructor.
    // Unused
}
