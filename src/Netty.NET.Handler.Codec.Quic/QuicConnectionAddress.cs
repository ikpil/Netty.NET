/*
 * Copyright 2020 The Netty Project
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
using System.Net;
using System.Runtime.CompilerServices;
using Netty.NET.Buffer;
using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * A {@link QuicConnectionAddress} that can be used to connect too.
 */
// CLR: SocketAddress is EndPoint (as Transport's LocalAddress). java.nio.ByteBuffer is ReadOnlyMemory<byte> (see
// IQuicConnectionIdGenerator); the id is held as a private copy of its bytes (Java keeps a read-only view of the caller's
// buffer, so later writes by the caller would show through; a copy does not), and id() returns a read-only view of it.
// Quiche.QUICHE_MAX_CONN_ID_LEN, a constant read through JNI, is the RFC 9000 maximum Quic.MAX_CONN_ID_LEN (20, "In QUIC
// version 1, this value MUST NOT exceed 20 bytes", https://www.rfc-editor.org/rfc/rfc9000.html#section-17.2), and
// Quic.ensureAvailability() stays at the top of the constructor. equals compares the bytes as ByteBuffer.equals does;
// EPHEMERAL has no id (Java's equals dereferences the null id and throws NullPointerException), so it is equal only to
// itself here. hashCode keeps the Java value: Objects.hash of ByteBuffer.hashCode, which folds the remaining bytes from
// the last to the first as signed Java bytes; System.identityHashCode is RuntimeHelpers.GetHashCode.
// IllegalArgumentException is ArgumentException.
public sealed class QuicConnectionAddress : EndPoint
{

    internal static readonly QuicConnectionAddress NULL_LEN = new QuicConnectionAddress(EmptyArrays.EMPTY_BYTES);

    /**
     * Special {@link QuicConnectionAddress} that should be used when the connection address should be generated
     * and chosen on the fly.
     */
    public static readonly QuicConnectionAddress EPHEMERAL = new QuicConnectionAddress(null, false);

    private readonly string toStr;

    private readonly byte[] connId;

    /**
     * Create a new instance
     *
     * @param connId the connection id to use.
     */
    public QuicConnectionAddress(byte[] connId) : this((byte[]) connId.Clone(), true)
    {
    }

    /**
     * Create a new instance
     *
     * @param connId the connection id to use.
     */
    public QuicConnectionAddress(ReadOnlyMemory<byte> connId) : this(connId.ToArray(), true)
    {
    }

    private QuicConnectionAddress(byte[] connId, bool validate)
    {
        Quic.EnsureAvailability();
        if (validate && connId.Length > Quic.MAX_CONN_ID_LEN)
        {
            throw new ArgumentException("Connection ID can only be of max length "
                    + Quic.MAX_CONN_ID_LEN);
        }

        if (connId == null)
        {
            this.connId = null;
            toStr = "QuicConnectionAddress{EPHEMERAL}";
        }
        else
        {
            this.connId = connId;
            ByteBuf buffer = Unpooled.WrappedBuffer(connId);

            try
            {
                toStr = "QuicConnectionAddress{" +
                        "connId=" + ByteBufUtil.HexDump(buffer) + '}';
            }
            finally
            {
                buffer.Release();
            }
        }
    }

    public override string ToString()
    {
        return toStr;
    }

    public override int GetHashCode()
    {
        if (this == EPHEMERAL)
        {
            return RuntimeHelpers.GetHashCode(EPHEMERAL);
        }

        int h = 1;

        for (int i = connId.Length - 1; i >= 0; i--)
        {
            h = unchecked(31 * h + (sbyte) connId[i]);
        }

        return unchecked(31 + h);
    }

    public override bool Equals(object obj)
    {
        if (!(obj is QuicConnectionAddress))
        {
            return false;
        }

        QuicConnectionAddress address = (QuicConnectionAddress) obj;
        if (obj == this)
        {
            return true;
        }

        return connId != null && address.connId != null && connId.AsSpan().SequenceEqual(address.connId);
    }

    internal ReadOnlyMemory<byte> Id()
    {
        if (connId == null)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        return connId;
    }

    /**
     * Return a random generated {@link QuicConnectionAddress} of a given length
     * that can be used to connect a {@link QuicChannel}
     *
     * @param length    the length of the {@link QuicConnectionAddress} to generate.
     * @return          the generated address.
     */
    public static QuicConnectionAddress Random(int length)
    {
        return new QuicConnectionAddress(IQuicConnectionIdGenerator.RandomGenerator().NewId(length));
    }

    /**
     * Return a random generated {@link QuicConnectionAddress} of maximum size
     * that can be used to connect a {@link QuicChannel}
     *
     * @return the generated address.
     */
    public static QuicConnectionAddress Random()
    {
        return Random(Quic.MAX_CONN_ID_LEN);
    }
}
