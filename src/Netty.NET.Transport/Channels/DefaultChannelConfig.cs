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
using System.Threading;
using Netty.NET.Buffer;

namespace Netty.NET.Transport.Channels;

/// <summary>Common channel configuration. Transport-specific configurations extend option dispatch.</summary>
/**
 * The default {@link ChannelConfig} implementation.
 */
public class DefaultChannelConfig : IChannelConfig
{
    protected readonly IChannel Channel;
    private IByteBufAllocator _allocator = IByteBufAllocator.Default;
    private IRecvByteBufAllocator _receiveAllocator;
    private IMessageSizeEstimator _estimator = DefaultMessageSizeEstimator.DEFAULT;
    private int _connectTimeoutMillis = 30000;
    private int _writeSpinCount = 16;
    private int _maxMessagesPerWrite = int.MaxValue;
    private int _autoRead = 1;
    private volatile bool _autoClose = true;
    private volatile bool _pinExecutor = true;
    private WriteBufferWaterMark _waterMark = WriteBufferWaterMark.DEFAULT;
    public DefaultChannelConfig(IChannel channel) : this(channel, new AdaptiveRecvByteBufAllocator()) { }
    protected DefaultChannelConfig(IChannel channel, IRecvByteBufAllocator allocator)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(allocator);
        Channel = channel;
        if (allocator is IMaxMessagesRecvByteBufAllocator messages)
            messages.MaxMessagesPerRead(channel.Metadata.DefaultMaxMessagesPerRead());
        _receiveAllocator = allocator;
    }
#pragma warning disable CS0612, CS0618 // The original config explicitly supports deprecated option aliases.
    private static readonly ChannelOption[] StandardOptions = {
        ChannelOption.CONNECT_TIMEOUT_MILLIS, ChannelOption.MAX_MESSAGES_PER_READ, ChannelOption.WRITE_SPIN_COUNT,
        ChannelOption.ALLOCATOR, ChannelOption.AUTO_READ, ChannelOption.AUTO_CLOSE, ChannelOption.RECVBUF_ALLOCATOR,
        ChannelOption.WRITE_BUFFER_HIGH_WATER_MARK, ChannelOption.WRITE_BUFFER_LOW_WATER_MARK,
        ChannelOption.WRITE_BUFFER_WATER_MARK, ChannelOption.MESSAGE_SIZE_ESTIMATOR,
        ChannelOption.SINGLE_EVENTEXECUTOR_PER_GROUP, ChannelOption.MAX_MESSAGES_PER_WRITE
    };
    public virtual IReadOnlyDictionary<ChannelOption, object> GetOptions()
    {
        var result = new Dictionary<ChannelOption, object>();
        foreach (var option in StandardOptions)
            if (TryGetValue(option, out var value))
                result.Add(option, value);
        return result;
    }
    public virtual bool SetOptions(IEnumerable<KeyValuePair<ChannelOption, object>> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        bool all = true;
        foreach (var pair in options)
        {
            ArgumentNullException.ThrowIfNull(pair.Key);
            pair.Key.ValidateValue(pair.Value);
            if (!SetValue(pair.Key, pair.Value))
                all = false;
        }
        return all;
    }
    public virtual bool TryGetOption<T>(ChannelOption<T> option, out T value)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (TryGetValue(option, out var result))
        { value = (T)result; return true; }
        value = default;
        return false;
    }
    protected virtual bool TryGetValue(ChannelOption option, out object value)
    {
        if (ReferenceEquals(option, ChannelOption.CONNECT_TIMEOUT_MILLIS))
            value = GetConnectTimeoutMillis();
        else if (ReferenceEquals(option, ChannelOption.MAX_MESSAGES_PER_READ))
            value = GetMaxMessagesPerRead();
        else if (ReferenceEquals(option, ChannelOption.WRITE_SPIN_COUNT))
            value = GetWriteSpinCount();
        else if (ReferenceEquals(option, ChannelOption.ALLOCATOR))
            value = GetAllocator();
        else if (ReferenceEquals(option, ChannelOption.RECVBUF_ALLOCATOR))
            value = GetRecvByteBufAllocator();
        else if (ReferenceEquals(option, ChannelOption.AUTO_READ))
            value = IsAutoRead();
        else if (ReferenceEquals(option, ChannelOption.AUTO_CLOSE))
            value = IsAutoClose();
        else if (ReferenceEquals(option, ChannelOption.WRITE_BUFFER_HIGH_WATER_MARK))
            value = GetWriteBufferHighWaterMark();
        else if (ReferenceEquals(option, ChannelOption.WRITE_BUFFER_LOW_WATER_MARK))
            value = GetWriteBufferLowWaterMark();
        else if (ReferenceEquals(option, ChannelOption.WRITE_BUFFER_WATER_MARK))
            value = GetWriteBufferWaterMark();
        else if (ReferenceEquals(option, ChannelOption.MESSAGE_SIZE_ESTIMATOR))
            value = GetMessageSizeEstimator();
        else if (ReferenceEquals(option, ChannelOption.SINGLE_EVENTEXECUTOR_PER_GROUP))
            value = _pinExecutor;
        else if (ReferenceEquals(option, ChannelOption.MAX_MESSAGES_PER_WRITE))
            value = GetMaxMessagesPerWrite();
        else
        { value = null; return false; }
        return true;
    }
    public virtual bool SetOption<T>(ChannelOption<T> option, T value)
    {
        ArgumentNullException.ThrowIfNull(option);
        option.Validate(value);
        return SetValue(option, value);
    }
    protected virtual bool SetValue(ChannelOption option, object value)
    {
        if (ReferenceEquals(option, ChannelOption.CONNECT_TIMEOUT_MILLIS))
            SetConnectTimeoutMillis((int)value);
        else if (ReferenceEquals(option, ChannelOption.MAX_MESSAGES_PER_READ))
            SetMaxMessagesPerRead((int)value);
        else if (ReferenceEquals(option, ChannelOption.WRITE_SPIN_COUNT))
            SetWriteSpinCount((int)value);
        else if (ReferenceEquals(option, ChannelOption.ALLOCATOR))
            SetAllocator((IByteBufAllocator)value);
        else if (ReferenceEquals(option, ChannelOption.RECVBUF_ALLOCATOR))
            SetRecvByteBufAllocator((IRecvByteBufAllocator)value);
        else if (ReferenceEquals(option, ChannelOption.AUTO_READ))
            SetAutoRead((bool)value);
        else if (ReferenceEquals(option, ChannelOption.AUTO_CLOSE))
            SetAutoClose((bool)value);
        else if (ReferenceEquals(option, ChannelOption.WRITE_BUFFER_HIGH_WATER_MARK))
            SetWriteBufferHighWaterMark((int)value);
        else if (ReferenceEquals(option, ChannelOption.WRITE_BUFFER_LOW_WATER_MARK))
            SetWriteBufferLowWaterMark((int)value);
        else if (ReferenceEquals(option, ChannelOption.WRITE_BUFFER_WATER_MARK))
            SetWriteBufferWaterMark((WriteBufferWaterMark)value);
        else if (ReferenceEquals(option, ChannelOption.MESSAGE_SIZE_ESTIMATOR))
            SetMessageSizeEstimator((IMessageSizeEstimator)value);
        else if (ReferenceEquals(option, ChannelOption.SINGLE_EVENTEXECUTOR_PER_GROUP))
            _pinExecutor = (bool)value;
        else if (ReferenceEquals(option, ChannelOption.MAX_MESSAGES_PER_WRITE))
            SetMaxMessagesPerWrite((int)value);
        else
            return false;
        return true;
    }
    public int GetConnectTimeoutMillis() => Volatile.Read(ref _connectTimeoutMillis);
    public virtual IChannelConfig SetConnectTimeoutMillis(int value)
    { ArgumentOutOfRangeException.ThrowIfNegative(value); Volatile.Write(ref _connectTimeoutMillis, value); return this; }
    [Obsolete("Use the receive allocator's MaxMessagesPerRead.")]
    /**
     * {@inheritDoc}
     * <p>
     * @throws IllegalStateException if {@link #getRecvByteBufAllocator()} does not return an object of type
     * {@link MaxMessagesRecvByteBufAllocator}.
     */
    public int GetMaxMessagesPerRead() => _receiveAllocator is IMaxMessagesRecvByteBufAllocator allocator ? allocator.MaxMessagesPerRead() : throw new InvalidOperationException("Receive allocator must support a message budget.");
    [Obsolete("Use the receive allocator's MaxMessagesPerRead.")]
    public IChannelConfig SetMaxMessagesPerRead(int value)
    { if (_receiveAllocator is not IMaxMessagesRecvByteBufAllocator allocator) throw new InvalidOperationException("Receive allocator must support a message budget."); allocator.MaxMessagesPerRead(value); return this; }
#pragma warning restore CS0612, CS0618
    /**
     * Get the maximum number of message to write per eventloop run. Once this limit is
     * reached we will continue to process other events before trying to write the remaining messages.
     */
    public int GetMaxMessagesPerWrite() => Volatile.Read(ref _maxMessagesPerWrite);
    /**
     * Set the maximum number of message to write per eventloop run. Once this limit is
     * reached we will continue to process other events before trying to write the remaining messages.
     */
    public virtual IChannelConfig SetMaxMessagesPerWrite(int value)
    { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value); Volatile.Write(ref _maxMessagesPerWrite, value); return this; }
    public int GetWriteSpinCount() => Volatile.Read(ref _writeSpinCount);
    public virtual IChannelConfig SetWriteSpinCount(int value)
    { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value); Volatile.Write(ref _writeSpinCount, value == int.MaxValue ? value - 1 : value); return this; }
    public IByteBufAllocator GetAllocator() => Volatile.Read(ref _allocator);
    public virtual IChannelConfig SetAllocator(IByteBufAllocator value)
    { ArgumentNullException.ThrowIfNull(value); Volatile.Write(ref _allocator, value); return this; }
    public IRecvByteBufAllocator GetRecvByteBufAllocator() => Volatile.Read(ref _receiveAllocator);
    public virtual IChannelConfig SetRecvByteBufAllocator(IRecvByteBufAllocator value)
    { ArgumentNullException.ThrowIfNull(value); Volatile.Write(ref _receiveAllocator, value); return this; }
    public bool IsAutoRead() => Volatile.Read(ref _autoRead) == 1;
    public virtual IChannelConfig SetAutoRead(bool value)
    {
        bool old = Interlocked.Exchange(ref _autoRead, value ? 1 : 0) == 1;
        if (value && !old)
            Channel.Read();
        else if (!value && old)
            AutoReadCleared();
        return this;
    }
    /**
     * Is called once {@link #setAutoRead(boolean)} is called with {@code false} and {@link #isAutoRead()} was
     * {@code true} before.
     */
    protected virtual void AutoReadCleared() { }
    public bool IsAutoClose() => _autoClose;
    public virtual IChannelConfig SetAutoClose(bool value) { _autoClose = value; return this; }
    public int GetWriteBufferHighWaterMark() => Volatile.Read(ref _waterMark).High();
    public virtual IChannelConfig SetWriteBufferHighWaterMark(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        for (; ; )
        {
            var old = Volatile.Read(ref _waterMark);
            if (value < old.Low())
                throw new ArgumentOutOfRangeException(nameof(value));
            if (Interlocked.CompareExchange(ref _waterMark, new(old.Low(), value, false), old) == old)
                return this;
        }
    }
    public int GetWriteBufferLowWaterMark() => Volatile.Read(ref _waterMark).Low();
    public virtual IChannelConfig SetWriteBufferLowWaterMark(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        for (; ; )
        {
            var old = Volatile.Read(ref _waterMark);
            if (value > old.High())
                throw new ArgumentOutOfRangeException(nameof(value));
            if (Interlocked.CompareExchange(ref _waterMark, new(value, old.High(), false), old) == old)
                return this;
        }
    }
    public WriteBufferWaterMark GetWriteBufferWaterMark() => Volatile.Read(ref _waterMark);
    public virtual IChannelConfig SetWriteBufferWaterMark(WriteBufferWaterMark value)
    { ArgumentNullException.ThrowIfNull(value); Volatile.Write(ref _waterMark, value); return this; }
    public IMessageSizeEstimator GetMessageSizeEstimator() => Volatile.Read(ref _estimator);
    public virtual IChannelConfig SetMessageSizeEstimator(IMessageSizeEstimator value)
    { ArgumentNullException.ThrowIfNull(value); Volatile.Write(ref _estimator, value); return this; }
}
