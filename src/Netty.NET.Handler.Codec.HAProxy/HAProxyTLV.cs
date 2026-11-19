/*
 * Copyright 2016 The Netty Project
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
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.HAProxy;

/**
 * A Type-Length Value (TLV vector) that can be added to the PROXY protocol
 * to include additional information like SSL information.
 *
 * @see HAProxySSLTLV
 */
// CLR: the overrides of copy()/duplicate()/retainedDuplicate()/replace()/retain()/touch() narrow the IByteBufHolder
// results of DefaultByteBufHolder with covariant overrides (https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/override);
// content() is the Content property and contentToString() is ContentToString() of the Buffer port. The package-private
// totalNumBytes()/contentNumBytes() are internal, contentNumBytes() virtual for HAProxySSLTLV.
public class HAProxyTLV : DefaultByteBufHolder
{

    private readonly Type type;
    private readonly byte typeByteValue;

    /**
     * The size of this tlv in bytes.
     * @return the number of bytes.
     */
    internal int TotalNumBytes()
    {
        return 3 + ContentNumBytes(); // type(1) + length(2) + content
    }

    internal virtual int ContentNumBytes()
    {
        return Content.ReadableBytes;
    }

    /**
     * The registered types a TLV can have regarding the PROXY protocol 1.5 spec
     */
    // CLR: the enum declares methods, so it is a sealed class with static readonly constants (owner rule; a C# enum
    // cannot define methods, https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/enum); Name(),
    // ToString() and Values() stand for the java.lang.Enum members. The switch over the constants in byteValueForType
    // uses case guards, because a C# case label is a pattern
    // (https://learn.microsoft.com/dotnet/csharp/language-reference/statements/selection-statements); a null type reaches
    // default there instead of the NullPointerException of a Java switch.
    public sealed class Type
    {
        public static readonly Type PP2_TYPE_ALPN = new Type(nameof(PP2_TYPE_ALPN));
        public static readonly Type PP2_TYPE_AUTHORITY = new Type(nameof(PP2_TYPE_AUTHORITY));
        public static readonly Type PP2_TYPE_SSL = new Type(nameof(PP2_TYPE_SSL));
        public static readonly Type PP2_TYPE_SSL_VERSION = new Type(nameof(PP2_TYPE_SSL_VERSION));
        public static readonly Type PP2_TYPE_SSL_CN = new Type(nameof(PP2_TYPE_SSL_CN));
        public static readonly Type PP2_TYPE_NETNS = new Type(nameof(PP2_TYPE_NETNS));
        /**
         * A TLV type that is not officially defined in the spec. May be used for nonstandard TLVs
         */
        public static readonly Type OTHER = new Type(nameof(OTHER));

        private readonly string name;

        private Type(string name)
        {
            this.name = name;
        }

        /**
         * Returns the {@link Type} for a specific byte value as defined in the PROXY protocol 1.5 spec
         * <p>
         * If the byte value is not an official one, it will return {@link Type#OTHER}.
         *
         * @param byteValue the byte for a type
         *
         * @return the {@link Type} of a TLV
         */
        public static Type TypeForByteValue(byte byteValue)
        {
            switch (byteValue)
            {
            case 0x01:
                return PP2_TYPE_ALPN;
            case 0x02:
                return PP2_TYPE_AUTHORITY;
            case 0x20:
                return PP2_TYPE_SSL;
            case 0x21:
                return PP2_TYPE_SSL_VERSION;
            case 0x22:
                return PP2_TYPE_SSL_CN;
            case 0x30:
                return PP2_TYPE_NETNS;
            default:
                return OTHER;
            }
        }

        /**
         * Returns the byte value for the {@link Type} as defined in the PROXY protocol 1.5 spec.
         *
         * @param type the {@link Type}
         *
         * @return the byte value of the {@link Type}.
         */
        public static byte ByteValueForType(Type type)
        {
            switch (type)
            {
            case var _ when type == PP2_TYPE_ALPN:
                return 0x01;
            case var _ when type == PP2_TYPE_AUTHORITY:
                return 0x02;
            case var _ when type == PP2_TYPE_SSL:
                return 0x20;
            case var _ when type == PP2_TYPE_SSL_VERSION:
                return 0x21;
            case var _ when type == PP2_TYPE_SSL_CN:
                return 0x22;
            case var _ when type == PP2_TYPE_NETNS:
                return 0x30;
            default:
                // CLR: Java string conversion writes null as "null"
                // (https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11).
                throw new ArgumentException("unknown type: " + (type?.ToString() ?? "null"));
            }
        }

        public string Name()
        {
            return name;
        }

        public static Type[] Values()
        {
            return new[]
            {
                PP2_TYPE_ALPN, PP2_TYPE_AUTHORITY, PP2_TYPE_SSL, PP2_TYPE_SSL_VERSION, PP2_TYPE_SSL_CN, PP2_TYPE_NETNS,
                OTHER,
            };
        }

        public override string ToString()
        {
            return name;
        }
    }

    /**
     * Creates a new HAProxyTLV
     *
     * @param typeByteValue the byteValue of the TLV. This is especially important if non-standard TLVs are used
     * @param content the raw content of the TLV
     */
    public HAProxyTLV(byte typeByteValue, ByteBuf content) : this(Type.TypeForByteValue(typeByteValue), typeByteValue, content)
    {
    }

    /**
     * Creates a new HAProxyTLV
     *
     * @param type the {@link Type} of the TLV
     * @param content the raw content of the TLV
     */
    public HAProxyTLV(Type type, ByteBuf content) : this(type, Type.ByteValueForType(type), content)
    {
    }

    /**
     * Creates a new HAProxyTLV
     *
     * @param type the {@link Type} of the TLV
     * @param typeByteValue the byteValue of the TLV. This is especially important if non-standard TLVs are used
     * @param content the raw content of the TLV
     */
    internal HAProxyTLV(Type type, byte typeByteValue, ByteBuf content) : base(content)
    {
        this.type = CheckNotNull(type, "type");
        this.typeByteValue = typeByteValue;
    }

    /**
     * Returns the {@link Type} of this TLV
     */
    // CLR: type() would be a method Type(), which cannot be declared beside the nested Type class ("The type 'type
    // name' already contains a definition for 'identifier'", https://learn.microsoft.com/dotnet/csharp/misc/cs0102; the
    // Transport port meets the same clash for EmbeddedChannel.builder()). The nested type keeps the Java name and the
    // method takes a trailing underscore, the only spelling difference besides case that the naming rule allows.
    public Type Type_()
    {
        return type;
    }

    /**
     * Returns the type of the TLV as byte
     */
    public byte TypeByteValue()
    {
        return typeByteValue;
    }

    public override HAProxyTLV Copy()
    {
        return Replace(Content.Copy());
    }

    public override HAProxyTLV Duplicate()
    {
        return Replace(Content.Duplicate());
    }

    public override HAProxyTLV RetainedDuplicate()
    {
        return Replace(Content.RetainedDuplicate());
    }

    public override HAProxyTLV Replace(ByteBuf content)
    {
        return new HAProxyTLV(type, typeByteValue, content);
    }

    public override HAProxyTLV Retain()
    {
        base.Retain();
        return this;
    }

    public override HAProxyTLV Retain(int increment)
    {
        base.Retain(increment);
        return this;
    }

    public override HAProxyTLV Touch()
    {
        base.Touch();
        return this;
    }

    public override HAProxyTLV Touch(object hint)
    {
        base.Touch(hint);
        return this;
    }

    // CLR: the type byte is a Java byte, which string conversion prints signed (-128..127,
    // https://docs.oracle.com/javase/specs/jls/se17/html/jls-4.html#jls-4.2.1); the C# byte is unsigned, so it is printed
    // through sbyte with the invariant culture to keep the Java text (0xE0 prints -32).
    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) +
               "(type: " + Type_() +
               ", typeByteValue: " + ((sbyte) TypeByteValue()).ToString(System.Globalization.CultureInfo.InvariantCulture) +
               ", content: " + ContentToString() + ')';
    }
}
