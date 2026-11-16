/*
 * Copyright 2012 The Netty Project
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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;

namespace Netty.NET.Transport.Channels.Nio;

/**
 * {@link AbstractNioChannel} base class for {@link Channel}s that operate on messages.
 */
public abstract class AbstractNioMessageChannel : AbstractNioChannel
{
    internal bool InputShutdown;

    /**
     * @see AbstractNioChannel#AbstractNioChannel(Channel, SelectableChannel, int)
     */
    protected AbstractNioMessageChannel(IChannel parent, Socket ch, int readInterestOp, ChannelMetadata metadata)
        : base(parent, ch, readInterestOp, metadata)
    {
    }

    protected AbstractNioMessageChannel(IChannel parent, Socket ch, NioIoOps readOps, ChannelMetadata metadata)
        : base(parent, ch, readOps, metadata)
    {
    }

    protected override AbstractNioUnsafe NewUnsafe()
    {
        return new NioMessageUnsafe(this);
    }

    protected override void DoBeginRead()
    {
        if (InputShutdown)
        {
            return;
        }
        base.DoBeginRead();
    }

    protected virtual bool ContinueReading(IRecvByteBufAllocator.IHandle allocHandle)
    {
        return allocHandle.ContinueReading();
    }

    private sealed class NioMessageUnsafe : AbstractNioUnsafe
    {
        private readonly AbstractNioMessageChannel _channel;
        private readonly List<object> _readBuf = new List<object>();

        internal NioMessageUnsafe(AbstractNioMessageChannel channel) : base(channel)
        {
            _channel = channel;
        }

        public override void Read()
        {
            Debug.Assert(_channel.EventLoop.InEventLoop());
            IChannelConfig config = _channel.Config;
            IChannelPipeline pipeline = _channel.Pipeline;
            IRecvByteBufAllocator.IHandle allocHandle = _channel.RecvBufAllocHandle();
            allocHandle.Reset(config);

            bool closed = false;
            Exception exception = null;
            try
            {
                try
                {
                    do
                    {
                        int localRead = _channel.DoReadMessages(_readBuf);
                        if (localRead == 0)
                        {
                            break;
                        }
                        if (localRead < 0)
                        {
                            closed = true;
                            break;
                        }

                        allocHandle.IncMessagesRead(localRead);
                    } while (_channel.ContinueReading(allocHandle));
                }
                catch (Exception t)
                {
                    exception = t;
                }

                int size = _readBuf.Count;
                for (int i = 0; i < size; i++)
                {
                    _channel.ReadPending = false;
                    pipeline.FireChannelRead(_readBuf[i]);
                }
                _readBuf.Clear();
                allocHandle.ReadComplete();
                pipeline.FireChannelReadComplete();

                if (exception != null)
                {
                    closed = _channel.CloseOnReadError(exception);

                    pipeline.FireExceptionCaught(exception);
                }

                if (closed)
                {
                    _channel.InputShutdown = true;
                    if (_channel.IsOpen)
                    {
                        _ = _channel.Close();
                    }
                }
            }
            finally
            {
                // Check if there is a readPending which was not processed yet.
                // This could be for two reasons:
                // * The user called Channel.read() or ChannelHandlerContext.read() in channelRead(...) method
                // * The user called Channel.read() or ChannelHandlerContext.read() in channelReadComplete(...) method
                //
                // See https://github.com/netty/netty/issues/2254
                if (!_channel.ReadPending && !config.IsAutoRead())
                {
                    RemoveReadOp();
                }
            }
        }
    }

    protected override void DoWrite(ChannelOutboundBuffer @in)
    {
        int maxMessagesPerWrite = MaxMessagesPerWrite();
        while (maxMessagesPerWrite > 0)
        {
            object msg = @in.Current;
            if (msg == null)
            {
                break;
            }
            try
            {
                bool done = false;
                for (int i = Config.GetWriteSpinCount() - 1; i >= 0; i--)
                {
                    if (DoWriteMessage(msg, @in))
                    {
                        done = true;
                        break;
                    }
                }

                if (done)
                {
                    maxMessagesPerWrite--;
                    @in.Remove();
                }
                else
                {
                    break;
                }
            }
            catch (Exception e)
            {
                if (ContinueOnWriteError)
                {
                    maxMessagesPerWrite--;
                    @in.Remove(e);
                }
                else
                {
                    throw;
                }
            }
        }
        if (@in.IsEmpty)
        {
            // Wrote all messages.
            RemoveAndSubmit(NioIoOps.WRITE);
        }
        else
        {
            // Did not write all messages.
            AddAndSubmit(NioIoOps.WRITE);
        }
    }

    // CLR: AbstractChannel.maxMessagesPerWrite().
    protected int MaxMessagesPerWrite()
    {
        IChannelConfig config = Config;
        if (config is DefaultChannelConfig defaultChannelConfig)
        {
            return defaultChannelConfig.GetMaxMessagesPerWrite();
        }
        return config.TryGetOption(ChannelOption.MAX_MESSAGES_PER_WRITE, out int value) ? value : int.MaxValue;
    }

    /**
     * Returns {@code true} if we should continue the write loop on a write error.
     */
    protected override bool ContinueOnWriteError => false;

    protected virtual bool CloseOnReadError(Exception cause)
    {
        if (!IsActive)
        {
            // If the channel is not active anymore for whatever reason we should not try to continue reading.
            return true;
        }
        // CLR: java.net.PortUnreachableException has no CLR type. Winsock reports an ICMP port unreachable for a UDP
        // socket as WSAECONNRESET, which NioDatagramChannel keeps open.
        // https://learn.microsoft.com/windows/win32/api/winsock/nf-winsock-recvfrom
        // CLR: a SocketException is not an IOException, so both are tested.
        if (cause is IOException or SocketException)
        {
            // ServerChannel should not be closed even on IOException because it can often continue
            // accepting incoming connections. (e.g. too many open files)
            return this is not IServerChannel;
        }
        return true;
    }

    /**
     * Read messages into the given array and return the amount which was read.
     */
    protected abstract int DoReadMessages(List<object> buf);

    /**
     * Write a message to the underlying {@link java.nio.channels.Channel}.
     *
     * @return {@code true} if and only if the message has been written
     */
    protected abstract bool DoWriteMessage(object msg, ChannelOutboundBuffer @in);
}
