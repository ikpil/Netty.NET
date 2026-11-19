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

using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Memcache.Binary;

/**
 * Represents all Opcodes that can occur in a {@link BinaryMemcacheMessage}.
 * <p/>
 * This class can be extended if a custom application needs to implement a superset of the normally supported
 * operations by a vanilla memcached protocol.
 */
// CLR: the static final byte opcodes are System.Byte constants, as ByteBuf.readByte() returns System.Byte (owner
// decision, docs/codec-memcache-porting.md, Byte values); every opcode is below 0x80, so the values are equal.
[UnstableApi]
public sealed class BinaryMemcacheOpcodes
{

    private BinaryMemcacheOpcodes()
    {
        // disallow construction
    }

    public const byte GET = 0x00;
    public const byte SET = 0x01;
    public const byte ADD = 0x02;
    public const byte REPLACE = 0x03;
    public const byte DELETE = 0x04;
    public const byte INCREMENT = 0x05;
    public const byte DECREMENT = 0x06;
    public const byte QUIT = 0x07;
    public const byte FLUSH = 0x08;
    public const byte GETQ = 0x09;
    public const byte NOOP = 0x0a;
    public const byte VERSION = 0x0b;
    public const byte GETK = 0x0c;
    public const byte GETKQ = 0x0d;
    public const byte APPEND = 0x0e;
    public const byte PREPEND = 0x0f;
    public const byte STAT = 0x10;
    public const byte SETQ = 0x11;
    public const byte ADDQ = 0x12;
    public const byte REPLACEQ = 0x13;
    public const byte DELETEQ = 0x14;
    public const byte INCREMENTQ = 0x15;
    public const byte DECREMENTQ = 0x16;
    public const byte QUITQ = 0x17;
    public const byte FLUSHQ = 0x18;
    public const byte APPENDQ = 0x19;
    public const byte PREPENDQ = 0x1a;
    public const byte TOUCH = 0x1c;
    public const byte GAT = 0x1d;
    public const byte GATQ = 0x1e;
    public const byte GATK = 0x23;
    public const byte GATKQ = 0x24;
    public const byte SASL_LIST_MECHS = 0x20;
    public const byte SASL_AUTH = 0x21;
    public const byte SASL_STEP = 0x22;
}
