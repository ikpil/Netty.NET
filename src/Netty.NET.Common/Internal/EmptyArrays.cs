/*
 * Copyright 2013 The Netty Project
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

namespace Netty.NET.Common.Internal;







public final class EmptyArrays {

    public static readonly int[] EMPTY_INTS = {};
    public static readonly byte[] EMPTY_BYTES = {};
    public static readonly char[] EMPTY_CHARS = {};
    public static readonly object[] EMPTY_OBJECTS = {};
    public static readonly Class<?>[] EMPTY_CLASSES = {};
    public static readonly string[] EMPTY_STRINGS = {};
    public static readonly AsciiString[] EMPTY_ASCII_STRINGS = {};
    public static readonly StackTraceElement[] EMPTY_STACK_TRACE = {};
    public static readonly ByteBuffer[] EMPTY_BYTE_BUFFERS = {};
    public static readonly Certificate[] EMPTY_CERTIFICATES = {};
    public static readonly X509Certificate[] EMPTY_X509_CERTIFICATES = {};
    public static readonly javax.security.cert.X509Certificate[] EMPTY_JAVAX_X509_CERTIFICATES = {};

    public static readonly Exception[] EMPTY_THROWABLES = {};

    private EmptyArrays() { }
}
