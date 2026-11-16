/*
 * Copyright 2024 The Netty Project
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
using System.Net.Sockets;
using Netty.NET.Transport.Channels.Nio;

namespace Netty.NET.Transport.Channels.Sockets;

/**
 * {@link DuplexChannel} which uses NIO selector based implementation to support
 * UNIX Domain Sockets. This is only supported when using Java 16+.
 */
/// <summary>A stream channel for Unix domain sockets provided by the CLR.</summary>
// CLR: a System.Net.Sockets.Socket of AddressFamily.Unix replaces the java.nio SocketChannel opened with
// StandardProtocolFamily.UNIX; the stream operations live in AbstractNioByteChannel, shared with NioSocketChannel.
// https://learn.microsoft.com/dotnet/api/system.net.sockets.unixdomainsocketendpoint
public sealed class NioDomainSocketChannel : AbstractNioByteChannel, IDuplexChannel
{
    /**
     * Create a new instance
     */
    public NioDomainSocketChannel() : this(new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)) { }

    /**
     * Create a new instance using the given {@link SocketChannel}.
     */
    public NioDomainSocketChannel(Socket socket) : this(null, socket) { }

    /**
     * Create a new instance
     *
     * @param parent    the {@link Channel} which created this instance or {@code null} if it was created by the user
     * @param socket    the {@link SocketChannel} which will be used
     */
    public NioDomainSocketChannel(IChannel parent, Socket socket) : base(parent, Validate(socket)) { }

    protected override EndPoint LocalAddress0()
    {
        try
        {
            return JavaChannel().LocalEndPoint;
        }
        catch (Exception)
        {
            // ignore
        }
        return null;
    }

    protected override EndPoint RemoteAddress0()
    {
        try
        {
            return JavaChannel().RemoteEndPoint;
        }
        catch (Exception)
        {
            // ignore
        }
        return null;
    }

    // CLR: NioDomainSocketChannel repeats the gathering doWrite of NioSocketChannel, as in Java.
    protected override void DoWrite(ChannelOutboundBuffer @in)
    {
        int writeSpinCount = Config.GetWriteSpinCount();
        do
        {
            if (@in.IsEmpty)
            {
                // All written so clear OP_WRITE
                ClearOpWrite();
                // Directly return here so incompleteWrite(...) is not called.
                return;
            }

            // Ensure the pending writes are made of ByteBufs only.
            int maxBytesPerGatheringWrite = GetMaxBytesPerGatheringWrite();
            ReadOnlyMemory<byte>[] nioBuffers = @in.NioBuffers(1024, maxBytesPerGatheringWrite);
            int nioBufferCnt = @in.NioBufferCount;

            // Always use nioBuffers() to workaround data-corruption.
            // See https://github.com/netty/netty/issues/2761
            switch (nioBufferCnt)
            {
                case 0:
                    // We have something else beside ByteBuffers to write so fallback to normal writes.
                    writeSpinCount -= DoWrite0(@in);
                    break;
                case 1:
                {
                    // Only one ByteBuf so use non-gathering write
                    // Zero length buffers are not added to nioBuffers by ChannelOutboundBuffer, so there is no need
                    // to check if the total size of all the buffers is non-zero.
                    ReadOnlyMemory<byte> buffer = nioBuffers[0];
                    int attemptedBytes = buffer.Length;
                    int localWrittenBytes = WriteSocket(buffer);
                    if (localWrittenBytes <= 0)
                    {
                        IncompleteWrite(true);
                        return;
                    }
                    AdjustMaxBytesPerGatheringWrite(attemptedBytes, localWrittenBytes, maxBytesPerGatheringWrite);
                    @in.RemoveBytes(localWrittenBytes);
                    --writeSpinCount;
                    break;
                }
                default:
                {
                    // Zero length buffers are not added to nioBuffers by ChannelOutboundBuffer, so there is no need
                    // to check if the total size of all the buffers is non-zero.
                    // We limit the max amount to int above so cast is safe
                    long attemptedBytes = @in.NioBufferSize;
                    long localWrittenBytes = WriteSocket(nioBuffers, 0, nioBufferCnt);
                    if (localWrittenBytes <= 0)
                    {
                        IncompleteWrite(true);
                        return;
                    }
                    // Casting to int is safe because we limit the total amount of data in the nioBuffers to int above.
                    AdjustMaxBytesPerGatheringWrite((int)attemptedBytes, (int)localWrittenBytes,
                            maxBytesPerGatheringWrite);
                    @in.RemoveBytes(localWrittenBytes);
                    --writeSpinCount;
                    break;
                }
            }
        } while (writeSpinCount > 0);

        IncompleteWrite(writeSpinCount < 0);
    }

    protected override AbstractNioUnsafe NewUnsafe()
    {
        return new NioSocketChannelUnsafe(this);
    }

    private sealed class NioSocketChannelUnsafe : NioByteUnsafe
    {
        // Only extending it so we create a new instance in newUnsafe() and return it.
        internal NioSocketChannelUnsafe(NioDomainSocketChannel channel) : base(channel)
        {
        }
    }

    private static Socket Validate(Socket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        if (socket.AddressFamily != AddressFamily.Unix || socket.SocketType != SocketType.Stream)
            throw new ArgumentException("A Unix domain stream socket is required.", nameof(socket));
        return socket;
    }
}
