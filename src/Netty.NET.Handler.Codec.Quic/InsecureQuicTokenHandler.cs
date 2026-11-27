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

using System.Net;
using System.Text;
using Netty.NET.Buffer;
using Netty.NET.Common;

namespace Netty.NET.Handler.Codec.Quic;

/**
 * Insecure {@link QuicTokenHandler} which only does basic token generation / validation without any
 * crypto.
 *
 * <strong>This shouldn't be used in production.</strong>
 */
// CLR: InetSocketAddress is IPEndPoint and InetAddress.getAddress() IPAddress.GetAddressBytes(); getBytes(US_ASCII) is
// Encoding.ASCII.GetBytes. ByteBuf.equals compares the readable bytes as in Java (ByteBuf.Equals). The private
// constructor still calls Quic.ensureAvailability(), so INSTANCE cannot be created where QUIC is unavailable, as in Java.
// The static fields keep their Java order, which the C# static field initializers follow.
public sealed class InsecureQuicTokenHandler : IQuicTokenHandler
{

    private const string SERVER_NAME = "netty";
    private static readonly byte[] SERVER_NAME_BYTES = Encoding.ASCII.GetBytes(SERVER_NAME);
    private static readonly ByteBuf SERVER_NAME_BUFFER = Unpooled.UnreleasableBuffer(
            Unpooled.WrappedBuffer(SERVER_NAME_BYTES)).AsReadOnly();

    // Just package-private for unit tests
    internal static readonly int MAX_TOKEN_LEN = Quic.MAX_CONN_ID_LEN +
            NetUtil.LOCALHOST6.GetAddressBytes().Length + SERVER_NAME_BYTES.Length;

    private InsecureQuicTokenHandler()
    {
        Quic.EnsureAvailability();
    }

    public static readonly InsecureQuicTokenHandler INSTANCE = new InsecureQuicTokenHandler();

    public bool WriteToken(ByteBuf @out, ByteBuf dcid, IPEndPoint address)
    {
        byte[] addr = address.Address.GetAddressBytes();
        @out.WriteBytes(SERVER_NAME_BYTES)
                .WriteBytes(addr)
                .WriteBytes(dcid, dcid.ReaderIndex, dcid.ReadableBytes);
        return true;
    }

    public int ValidateToken(ByteBuf token, IPEndPoint address)
    {
        byte[] addr = address.Address.GetAddressBytes();

        int minLength = SERVER_NAME_BYTES.Length + address.Address.GetAddressBytes().Length;
        if (token.ReadableBytes <= SERVER_NAME_BYTES.Length + addr.Length)
        {
            return -1;
        }

        if (!SERVER_NAME_BUFFER.Equals(token.Slice(0, SERVER_NAME_BYTES.Length)))
        {
            return -1;
        }
        ByteBuf addressBuffer = Unpooled.WrappedBuffer(addr);
        try
        {
            if (!addressBuffer.Equals(token.Slice(SERVER_NAME_BYTES.Length, addr.Length)))
            {
                return -1;
            }
        }
        finally
        {
            addressBuffer.Release();
        }
        return minLength;
    }

    public int MaxTokenLength()
    {
        return MAX_TOKEN_LEN;
    }
}
