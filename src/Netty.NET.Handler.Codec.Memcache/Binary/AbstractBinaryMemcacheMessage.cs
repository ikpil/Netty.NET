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
 * Default implementation of a {@link BinaryMemcacheMessage}.
 */
// CLR: the covariant overrides of retain()/retain(int)/touch()/touch(Object) are C# covariant override returns
// (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override).
// The package-private setExtrasLength/setKeyLength/copyMeta are internal. The literal 0 of the length conditionals is
// cast to short/byte: C# gives `c ? 0 : (short) x` the type int, because an implicit conversion only exists from short
// to int (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/conditional-operator).
[UnstableApi]
public abstract class AbstractBinaryMemcacheMessage
    : AbstractMemcacheObject, IBinaryMemcacheMessage
{

    /**
     * Contains the optional key.
     */
    private ByteBuf key;

    /**
     * Contains the optional extras.
     */
    private ByteBuf extras;

    private byte magic;
    private byte opcode;
    private short keyLength;
    private byte extrasLength;
    private byte dataType;
    private int totalBodyLength;
    private int opaque;
    private long cas;

    /**
     * Create a new instance with all properties set.
     *
     * @param key    the message key.
     * @param extras the message extras.
     */
    protected AbstractBinaryMemcacheMessage(ByteBuf key, ByteBuf extras)
    {
        this.key = key;
        keyLength = key == null ? (short) 0 : (short) key.ReadableBytes;
        this.extras = extras;
        extrasLength = extras == null ? (byte) 0 : (byte) extras.ReadableBytes;
        // CLR: Java promotes the signed byte extrasLength to int by sign extension (JLS 5.6, 5.1.2:
        // https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.6), so an extras length of 128..255 adds
        // a negative value; (sbyte) keeps that sum for the System.Byte field
        // (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
        totalBodyLength = keyLength + (sbyte) extrasLength;
    }

    public ByteBuf Key()
    {
        return key;
    }

    public ByteBuf Extras()
    {
        return extras;
    }

    public IBinaryMemcacheMessage SetKey(ByteBuf key)
    {
        if (this.key != null)
        {
            this.key.Release();
        }

        this.key = key;
        short oldKeyLength = keyLength;
        keyLength = key == null ? (short) 0 : (short) key.ReadableBytes;
        totalBodyLength = totalBodyLength + keyLength - oldKeyLength;
        return this;
    }

    public IBinaryMemcacheMessage SetExtras(ByteBuf extras)
    {
        if (this.extras != null)
        {
            this.extras.Release();
        }

        this.extras = extras;
        // CLR: the old and new extras lengths are sign-extended as Java promotes its signed byte (JLS 5.6, 5.1.2:
        // https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.6); (sbyte) keeps the Java total for the
        // System.Byte field (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/integral-numeric-types).
        short oldExtrasLength = (sbyte) extrasLength;
        extrasLength = extras == null ? (byte) 0 : (byte) extras.ReadableBytes;
        totalBodyLength = totalBodyLength + (sbyte) extrasLength - oldExtrasLength;
        return this;
    }

    public byte Magic()
    {
        return magic;
    }

    public IBinaryMemcacheMessage SetMagic(byte magic)
    {
        this.magic = magic;
        return this;
    }

    public long Cas()
    {
        return cas;
    }

    public IBinaryMemcacheMessage SetCas(long cas)
    {
        this.cas = cas;
        return this;
    }

    public int Opaque()
    {
        return opaque;
    }

    public IBinaryMemcacheMessage SetOpaque(int opaque)
    {
        this.opaque = opaque;
        return this;
    }

    public int TotalBodyLength()
    {
        return totalBodyLength;
    }

    public IBinaryMemcacheMessage SetTotalBodyLength(int totalBodyLength)
    {
        this.totalBodyLength = totalBodyLength;
        return this;
    }

    public byte DataType()
    {
        return dataType;
    }

    public IBinaryMemcacheMessage SetDataType(byte dataType)
    {
        this.dataType = dataType;
        return this;
    }

    public byte ExtrasLength()
    {
        return extrasLength;
    }

    /**
     * Set the extras length of the message.
     * <p/>
     * This may be 0, since the extras content is optional.
     *
     * @param extrasLength the extras length.
     */
    internal IBinaryMemcacheMessage SetExtrasLength(byte extrasLength)
    {
        this.extrasLength = extrasLength;
        return this;
    }

    public short KeyLength()
    {
        return keyLength;
    }

    /**
     * Set the key length of the message.
     * <p/>
     * This may be 0, since the key is optional.
     *
     * @param keyLength the key length to use.
     */
    internal IBinaryMemcacheMessage SetKeyLength(short keyLength)
    {
        this.keyLength = keyLength;
        return this;
    }

    public byte Opcode()
    {
        return opcode;
    }

    public IBinaryMemcacheMessage SetOpcode(byte opcode)
    {
        this.opcode = opcode;
        return this;
    }

    public override IBinaryMemcacheMessage Retain()
    {
        base.Retain();
        return this;
    }

    public override IBinaryMemcacheMessage Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    protected override void Deallocate()
    {
        if (key != null)
        {
            key.Release();
        }

        if (extras != null)
        {
            extras.Release();
        }
    }

    public override IBinaryMemcacheMessage Touch()
    {
        base.Touch();
        return this;
    }

    public override IBinaryMemcacheMessage Touch(object hint)
    {
        if (key != null)
        {
            key.Touch(hint);
        }

        if (extras != null)
        {
            extras.Touch(hint);
        }

        return this;
    }

    /**
     * Copies special metadata hold by this instance to the provided instance
     *
     * @param dst The instance where to copy the metadata of this instance to
     */
    internal void CopyMeta(AbstractBinaryMemcacheMessage dst)
    {
        dst.magic = magic;
        dst.opcode = opcode;
        dst.keyLength = keyLength;
        dst.extrasLength = extrasLength;
        dst.dataType = dataType;
        dst.totalBodyLength = totalBodyLength;
        dst.opaque = opaque;
        dst.cas = cas;
    }
}
