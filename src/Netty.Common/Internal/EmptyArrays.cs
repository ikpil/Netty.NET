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

using System;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace Netty.NET.Common.Internal;

public static class EmptyArrays
{
    public static readonly int[] EMPTY_INTS = Array.Empty<int>();
    public static readonly byte[] EMPTY_BYTES = Array.Empty<byte>();
    public static readonly char[] EMPTY_CHARS = Array.Empty<char>();
    public static readonly object[] EMPTY_OBJECTS = Array.Empty<object>();
    public static readonly Type[] EMPTY_CLASSES = Array.Empty<Type>();
    public static readonly string[] EMPTY_STRINGS = Array.Empty<string>();
    public static readonly AsciiString[] EMPTY_ASCII_STRINGS = Array.Empty<AsciiString>();
    public static readonly StackFrame[] EMPTY_STACK_TRACE = Array.Empty<StackFrame>();
    //public static readonly ByteBuffer[] EMPTY_BYTE_BUFFERS = Array.Empty<ByteBuffer>();
    public static readonly X509Certificate[] EMPTY_CERTIFICATES = Array.Empty<X509Certificate>();
    public static readonly X509Certificate[] EMPTY_X509_CERTIFICATES = Array.Empty<X509Certificate>();
    public static readonly X509Certificate2[] EMPTY_JAVAX_X509_CERTIFICATES = Array.Empty<X509Certificate2>();

    public static readonly Exception[] EMPTY_THROWABLES = Array.Empty<Exception>();
}