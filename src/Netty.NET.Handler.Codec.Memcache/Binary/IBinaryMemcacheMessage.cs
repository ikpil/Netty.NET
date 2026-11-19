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

using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using Netty.NET.Handler.Codec.Memcache;

namespace Netty.NET.Handler.Codec.Memcache.Binary;

/**
 * An interface that defines a binary Memcache message, providing common properties for
 * {@link BinaryMemcacheRequest} and {@link BinaryMemcacheResponse}.
 * <p/>
 * A {@link BinaryMemcacheMessage} always consists of a header and optional extras or/and
 * a key.
 *
 * @see BinaryMemcacheRequest
 * @see BinaryMemcacheResponse
 */
// CLR: the Java byte fields (magic, opcode, extras length, data type) are System.Byte, as ByteBuf.readByte()
// (owner decision, docs/codec-memcache-porting.md, Byte values).
[UnstableApi]
public interface IBinaryMemcacheMessage : IMemcacheMessage
{

    /**
     * Returns the magic byte for the message.
     *
     * @return the magic byte.
     */
    byte Magic();

    /**
     * Sets the magic byte.
     *
     * @param magic the magic byte to use.
     * @see BinaryMemcacheOpcodes for typesafe opcodes.
     */
    IBinaryMemcacheMessage SetMagic(byte magic);

    /**
     * Returns the opcode for the message.
     *
     * @return the opcode.
     */
    byte Opcode();

    /**
     * Sets the opcode for the message.
     *
     * @param code the opcode to use.
     */
    IBinaryMemcacheMessage SetOpcode(byte code);

    /**
     * Returns the key length of the message.
     * <p/>
     * This may return 0, since the key is optional.
     *
     * @return the key length.
     */
    short KeyLength();

    /**
     * Return the extras length of the message.
     * <p/>
     * This may be 0, since the extras content is optional.
     *
     * @return the extras length.
     */
    byte ExtrasLength();

    /**
     * Returns the data type of the message.
     *
     * @return the data type of the message.
     */
    byte DataType();

    /**
     * Sets the data type of the message.
     *
     * @param dataType the data type of the message.
     */
    IBinaryMemcacheMessage SetDataType(byte dataType);

    /**
     * Returns the total body length.
     * <p/>
     * Note that this may be 0, since the body is optional.
     *
     * @return the total body length.
     */
    int TotalBodyLength();

    /**
     * Sets the total body length.
     * <p/>
     * Note that this may be 0, since the body length is optional.
     *
     * @param totalBodyLength the total body length.
     */
    IBinaryMemcacheMessage SetTotalBodyLength(int totalBodyLength);

    /**
     * Returns the opaque value.
     *
     * @return the opaque value.
     */
    int Opaque();

    /**
     * Sets the opaque value.
     *
     * @param opaque the opaque value to use.
     */
    IBinaryMemcacheMessage SetOpaque(int opaque);

    /**
     * Returns the CAS identifier.
     *
     * @return the CAS identifier.
     */
    long Cas();

    /**
     * Sets the CAS identifier.
     *
     * @param cas the CAS identifier to use.
     */
    IBinaryMemcacheMessage SetCas(long cas);

    /**
     * Returns the optional key of the document.
     *
     * @return the key of the document.
     */
    ByteBuf Key();

    /**
     * Sets the key of the document. {@link ByteBuf#release()} ownership of {@code key}
     * is transferred to this {@link BinaryMemcacheMessage}.
     *
     * @param key the key of the message. {@link ByteBuf#release()} ownership is transferred
     *            to this {@link BinaryMemcacheMessage}.
     */
    IBinaryMemcacheMessage SetKey(ByteBuf key);

    /**
     * Returns a {@link ByteBuf} representation of the optional extras.
     *
     * @return the optional extras.
     */
    ByteBuf Extras();

    /**
     * Sets the extras buffer on the message. {@link ByteBuf#release()} ownership of {@code extras}
     * is transferred to this {@link BinaryMemcacheMessage}.
     *
     * @param extras the extras buffer of the document. {@link ByteBuf#release()} ownership is transferred
     *               to this {@link BinaryMemcacheMessage}.
     */
    IBinaryMemcacheMessage SetExtras(ByteBuf extras);

    /**
     * Increases the reference count by {@code 1}.
     */
    // CLR: retain() is re-declared only to narrow the return type, so it is inherited from IReferenceCounted, not
    // repeated (owner decision 2026-11-19; docs/transport-porting.md, Decisions).

    /**
     * Increases the reference count by the specified {@code increment}.
     */
    // CLR: retain(int), touch() and touch(Object) are inherited from IReferenceCounted for the same reason.
}
