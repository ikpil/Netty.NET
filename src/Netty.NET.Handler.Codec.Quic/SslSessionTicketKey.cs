/*
 * Copyright 2023 The Netty Project
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
using System.Globalization;
using System.Text;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Session Ticket Key
 */
// CLR: the package-private fields are internal (BoringSSLSessionTicketCallback, which read them, is not ported; see
// docs/codec-classes-quic-porting.md). byte[].clone() is Array.Clone(), IllegalArgumentException ArgumentException and
// getClass() GetType(). Arrays.equals is SequenceEqual; Arrays.hashCode and Arrays.toString of byte[] are the private
// helpers below, which keep the Java values and text (signed bytes). System.Net.Quic manages session tickets inside
// msquic (docs/quic-design.md, No counterpart in System.Net.Quic): the type keeps its Java API (owner decision
// 2026-11-27).
public sealed class SslSessionTicketKey
{
    /**
     * Size of session ticket key name
     */
    public const int NAME_SIZE = 16;
    /**
     * Size of session ticket key HMAC key
     */
    public const int HMAC_KEY_SIZE = 16;
    /**
     * Size of session ticket key AES key
     */
    public const int AES_KEY_SIZE = 16;
    /**
     * Size of session ticket key
     */
    public const int TICKET_KEY_SIZE = NAME_SIZE + HMAC_KEY_SIZE + AES_KEY_SIZE;

    // package private so we can access these in BoringSSLSessionTicketCallback without calling clone() on the byte[].
    internal readonly byte[] name;
    internal readonly byte[] hmacKey;
    internal readonly byte[] aesKey;

    /**
     * Construct SessionTicketKey.
     * @param name the name of the session ticket key
     * @param hmacKey the HMAC key of the session ticket key
     * @param aesKey the AES key of the session ticket key
     */
    public SslSessionTicketKey(byte[] name, byte[] hmacKey, byte[] aesKey)
    {
        if (name == null || name.Length != NAME_SIZE)
        {
            throw new ArgumentException("Length of name must be " + NAME_SIZE);
        }
        if (hmacKey == null || hmacKey.Length != HMAC_KEY_SIZE)
        {
            throw new ArgumentException("Length of hmacKey must be " + HMAC_KEY_SIZE);
        }
        if (aesKey == null || aesKey.Length != AES_KEY_SIZE)
        {
            throw new ArgumentException("Length of aesKey must be " + AES_KEY_SIZE);
        }

        this.name = (byte[]) name.Clone();
        this.hmacKey = (byte[]) hmacKey.Clone();
        this.aesKey = (byte[]) aesKey.Clone();
    }

    /**
     * Get name.
     *
     * @return the name of the session ticket key
     */
    public byte[] Name()
    {
        return (byte[]) name.Clone();
    }

    /**
     * Get HMAC key.
     * @return the HMAC key of the session ticket key
     */
    public byte[] HmacKey()
    {
        return (byte[]) hmacKey.Clone();
    }

    /**
     * Get AES Key.
     * @return the AES key of the session ticket key
     */
    public byte[] AesKey()
    {
        return (byte[]) aesKey.Clone();
    }

    public override bool Equals(object o)
    {
        if (ReferenceEquals(this, o))
        {
            return true;
        }
        if (o == null || GetType() != o.GetType())
        {
            return false;
        }

        SslSessionTicketKey that = (SslSessionTicketKey) o;

        if (!name.AsSpan().SequenceEqual(that.name))
        {
            return false;
        }
        if (!hmacKey.AsSpan().SequenceEqual(that.hmacKey))
        {
            return false;
        }

        return aesKey.AsSpan().SequenceEqual(that.aesKey);
    }

    public override int GetHashCode()
    {
        int result = ArraysHashCode(name);
        result = unchecked(31 * result + ArraysHashCode(hmacKey));
        result = unchecked(31 * result + ArraysHashCode(aesKey));
        return result;
    }

    public override string ToString()
    {
        return "SessionTicketKey{" +
                "name=" + ArraysToString(name) +
                ", hmacKey=" + ArraysToString(hmacKey) +
                ", aesKey=" + ArraysToString(aesKey) +
                '}';
    }

    // CLR-only helper: Arrays.hashCode(byte[]), 31 * h + each signed byte from 1 (0 for null), as Transport's
    // DefaultChannelId spells it out.
    private static int ArraysHashCode(byte[] a)
    {
        if (a == null)
        {
            return 0;
        }

        int h = 1;

        foreach (byte b in a)
        {
            h = unchecked(31 * h + (sbyte) b);
        }

        return h;
    }

    // CLR-only helper: Arrays.toString(byte[]), "[" + the signed bytes joined with ", " + "]" ("null" for null), as
    // codec-mqtt MqttConnectPayload maps it.
    private static string ArraysToString(byte[] a)
    {
        if (a == null)
        {
            return "null";
        }

        StringBuilder b = new StringBuilder("[");

        for (int i = 0; i < a.Length; i++)
        {
            if (i > 0)
            {
                b.Append(", ");
            }

            b.Append(((sbyte) a[i]).ToString(CultureInfo.InvariantCulture));
        }

        return b.Append(']').ToString();
    }
}
