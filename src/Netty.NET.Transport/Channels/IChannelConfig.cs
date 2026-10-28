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
using Netty.NET.Buffer;

namespace Netty.NET.Transport.Channels;

/**
 * A set of configuration properties of a {@link Channel}.
 * <p>
 * Please down-cast to more specific configuration type such as
 * {@link SocketChannelConfig} or use {@link #setOptions(Map)} to set the
 * transport-specific properties:
 * <pre>
 * {@link Channel} ch = ...;
 * {@link SocketChannelConfig} cfg = <strong>({@link SocketChannelConfig}) ch.getConfig();</strong>
 * cfg.setTcpNoDelay(false);
 * </pre>
 *
 * <h3>Option map</h3>
 *
 * An option map property is a dynamic write-only property which allows
 * the configuration of a {@link Channel} without down-casting its associated
 * {@link ChannelConfig}.  To update an option map, please call {@link #setOptions(Map)}.
 * <p>
 * All {@link ChannelConfig} has the following options:
 *
 * <table border="1" cellspacing="0" cellpadding="6">
 * <tr>
 * <th>Name</th><th>Associated setter method</th>
 * </tr><tr>
 * <td>{@link ChannelOption#CONNECT_TIMEOUT_MILLIS}</td><td>{@link #setConnectTimeoutMillis(int)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#WRITE_SPIN_COUNT}</td><td>{@link #setWriteSpinCount(int)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#WRITE_BUFFER_WATER_MARK}</td><td>{@link #setWriteBufferWaterMark(WriteBufferWaterMark)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#ALLOCATOR}</td><td>{@link #setAllocator(ByteBufAllocator)}</td>
 * </tr><tr>
 * <td>{@link ChannelOption#AUTO_READ}</td><td>{@link #setAutoRead(boolean)}</td>
 * </tr>
 * </table>
 * <p>
 * More options are available in the sub-types of {@link ChannelConfig}.  For
 * example, you can configure the parameters which are specific to a TCP/IP
 * socket as explained in {@link SocketChannelConfig}.
 */
public interface IChannelConfig
{

    /**
     * Return all set {@link ChannelOption}'s.
     */
    IReadOnlyDictionary<ChannelOption, object> GetOptions();

    /**
     * Sets the configuration properties from the specified {@link Map}.
     */
    bool SetOptions(IEnumerable<KeyValuePair<ChannelOption, object>> options);

    /**
     * Return the value of the given {@link ChannelOption}
     */
    // CLR: TryGet distinguishes an unsupported option from a supported default-valued bool/int.
    bool TryGetOption<T>(ChannelOption<T> option, out T value);

    /**
     * Sets a configuration property with the specified name and value.
     * To override this method properly, you must call the super class:
     * <pre>
     * public boolean setOption(ChannelOption&lt;T&gt; option, T value) {
     *     if (super.setOption(option, value)) {
     *         return true;
     *     }
     *
     *     if (option.equals(additionalOption)) {
     *         ....
     *         return true;
     *     }
     *
     *     return false;
     * }
     * </pre>
     *
     * @return {@code true} if and only if the property has been set
     */
    bool SetOption<T>(ChannelOption<T> option, T value);

    /**
     * Returns the connect timeout of the channel in milliseconds.  If the
     * {@link Channel} does not support connect operation, this property is not
     * used at all, and therefore will be ignored.
     *
     * @return the connect timeout in milliseconds.  {@code 0} if disabled.
     */
    int GetConnectTimeoutMillis();

    /**
     * Sets the connect timeout of the channel in milliseconds.  If the
     * {@link Channel} does not support connect operation, this property is not
     * used at all, and therefore will be ignored.
     *
     * @param connectTimeoutMillis the connect timeout in milliseconds.
     *                             {@code 0} to disable.
     */
    IChannelConfig SetConnectTimeoutMillis(int connectTimeoutMillis);

    /**
     * @deprecated Use {@link MaxMessagesRecvByteBufAllocator} and
     * {@link MaxMessagesRecvByteBufAllocator#maxMessagesPerRead()}.
     * <p>
     * Returns the maximum number of messages to read per read loop.
     * a {@link ChannelInboundHandler#channelRead(ChannelHandlerContext, Object) channelRead()} event.
     * If this value is greater than 1, an event loop might attempt to read multiple times to procure multiple messages.
     */
    [Obsolete]
    int GetMaxMessagesPerRead();

    /**
     * @deprecated Use {@link MaxMessagesRecvByteBufAllocator} and
     * {@link MaxMessagesRecvByteBufAllocator#maxMessagesPerRead(int)}.
     * <p>
     * Sets the maximum number of messages to read per read loop.
     * If this value is greater than 1, an event loop might attempt to read multiple times to procure multiple messages.
     */
    [Obsolete]
    IChannelConfig SetMaxMessagesPerRead(int maxMessagesPerRead);

    /**
     * Returns the maximum loop count for a write operation until
     * {@link WritableByteChannel#write(ByteBuffer)} returns a non-zero value.
     * It is similar to what a spin lock is used for in concurrency programming.
     * It improves memory utilization and write throughput depending on
     * the platform that JVM runs on.  The default value is {@code 16}.
     */
    int GetWriteSpinCount();

    /**
     * Sets the maximum loop count for a write operation until
     * {@link WritableByteChannel#write(ByteBuffer)} returns a non-zero value.
     * It is similar to what a spin lock is used for in concurrency programming.
     * It improves memory utilization and write throughput depending on
     * the platform that JVM runs on.  The default value is {@code 16}.
     *
     * @throws IllegalArgumentException
     *         if the specified value is {@code 0} or less than {@code 0}
     */
    IChannelConfig SetWriteSpinCount(int writeSpinCount);

    /**
     * Returns {@link ByteBufAllocator} which is used for the channel
     * to allocate buffers.
     */
    IByteBufAllocator GetAllocator();

    /**
     * Set the {@link ByteBufAllocator} which is used for the channel
     * to allocate buffers.
     */
    IChannelConfig SetAllocator(IByteBufAllocator allocator);

    /**
     * Returns {@link RecvByteBufAllocator} which is used for the channel to allocate receive buffers.
     */
    IRecvByteBufAllocator GetRecvByteBufAllocator();

    /**
     * Set the {@link RecvByteBufAllocator} which is used for the channel to allocate receive buffers.
     */
    IChannelConfig SetRecvByteBufAllocator(IRecvByteBufAllocator allocator);

    /**
     * Returns {@code true} if and only if {@link ChannelHandlerContext#read()} will be invoked automatically so that
     * a user application doesn't need to call it at all. The default value is {@code true}.
     */
    bool IsAutoRead();

    /**
     * Sets if {@link ChannelHandlerContext#read()} will be invoked automatically so that a user application doesn't
     * need to call it at all. The default value is {@code true}.
     */
    IChannelConfig SetAutoRead(bool autoRead);

    /**
     * Returns {@code true} if and only if the {@link Channel} will be closed automatically and immediately on
     * write failure. The default is {@code true}.
     */
    bool IsAutoClose();

    /**
     * Sets whether the {@link Channel} should be closed automatically and immediately on write failure.
     * The default is {@code true}.
     */
    IChannelConfig SetAutoClose(bool autoClose);

    /**
     * Returns the high water mark of the write buffer.  If the number of bytes
     * queued in the write buffer exceeds this value, {@link Channel#isWritable()}
     * will start to return {@code false}.
     */
    int GetWriteBufferHighWaterMark();

    /**
     * <p>
     * Sets the high water mark of the write buffer.  If the number of bytes
     * queued in the write buffer exceeds this value, {@link Channel#isWritable()}
     * will start to return {@code false}.
     */
    IChannelConfig SetWriteBufferHighWaterMark(int writeBufferHighWaterMark);

    /**
     * Returns the low water mark of the write buffer.  Once the number of bytes
     * queued in the write buffer exceeded the
     * {@linkplain #setWriteBufferHighWaterMark(int) high water mark} and then
     * dropped down below this value, {@link Channel#isWritable()} will start to return
     * {@code true} again.
     */
    int GetWriteBufferLowWaterMark();

    /**
     * <p>
     * Sets the low water mark of the write buffer.  Once the number of bytes
     * queued in the write buffer exceeded the
     * {@linkplain #setWriteBufferHighWaterMark(int) high water mark} and then
     * dropped down below this value, {@link Channel#isWritable()} will start to return
     * {@code true} again.
     */
    IChannelConfig SetWriteBufferLowWaterMark(int writeBufferLowWaterMark);

    /**
     * Returns {@link MessageSizeEstimator} which is used for the channel
     * to detect the size of a message.
     */
    IMessageSizeEstimator GetMessageSizeEstimator();

    /**
     * Set the {@link MessageSizeEstimator} which is used for the channel
     * to detect the size of a message.
     */
    IChannelConfig SetMessageSizeEstimator(IMessageSizeEstimator estimator);

    /**
     * Returns the {@link WriteBufferWaterMark} which is used for setting the high and low
     * water mark of the write buffer.
     */
    WriteBufferWaterMark GetWriteBufferWaterMark();

    /**
     * Set the {@link WriteBufferWaterMark} which is used for setting the high and low
     * water mark of the write buffer.
     */
    IChannelConfig SetWriteBufferWaterMark(WriteBufferWaterMark writeBufferWaterMark);
}
