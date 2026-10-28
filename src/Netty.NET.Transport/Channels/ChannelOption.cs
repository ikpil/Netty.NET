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
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using Netty.NET.Buffer;
using Netty.NET.Common;
using Netty.NET.Common.Internal;

namespace Netty.NET.Transport.Channels;

/**
 * A {@link ChannelOption} allows to configure a {@link ChannelConfig} in a type-safe
 * way. Which {@link ChannelOption} is supported depends on the actual implementation
 * of {@link ChannelConfig} and may depend on the nature of the transport it belongs
 * to.
 *
 * @param <T>   the type of the value which is valid for the {@link ChannelOption}
 */
// CLR: the registry and built-in fields live on a non-generic base. A name cannot be rebound to a different T.
public abstract class ChannelOption : AbstractConstant<ChannelOption>
{
    private static readonly ConcurrentDictionary<string, ChannelOption> Pool = new();
    private static int _nextId;
    protected ChannelOption(int id, string name) : base(id, name) { }
    internal static int NextId() => Interlocked.Increment(ref _nextId);
    /**
     * Returns the {@link ChannelOption} of the specified name.
     */
    public static ChannelOption<T> ValueOf<T>(string name)
    {
        ObjectUtil.CheckNonEmpty(name, nameof(name));
        var option = Pool.GetOrAdd(name, n => new ChannelOption<T>(NextId(), n));
        return option as ChannelOption<T> ?? throw new ArgumentException("Option is already registered with a different value type.", nameof(name));
    }
    /**
     * Shortcut of {@link #valueOf(String) valueOf(firstNameComponent.getName() + "#" + secondNameComponent)}.
     */
    public static ChannelOption<T> ValueOf<T>(Type firstNameComponent, string secondNameComponent)
    {
        ArgumentNullException.ThrowIfNull(firstNameComponent);
        ArgumentNullException.ThrowIfNull(secondNameComponent);
        string name = firstNameComponent.FullName ?? throw new ArgumentException("A named CLR type is required.", nameof(firstNameComponent));
        return ValueOf<T>(name + '#' + secondNameComponent);
    }
    /**
     * Returns {@code true} if a {@link ChannelOption} exists for the given {@code name}.
     */
    public static bool Exists(string name) => Pool.ContainsKey(ObjectUtil.CheckNonEmpty(name, nameof(name)));
    /**
     * Creates a new {@link ChannelOption} for the given {@code name} or fail with an
     * {@link IllegalArgumentException} if a {@link ChannelOption} for the given {@code name} exists.
     *
     * @deprecated use {@link #valueOf(String)}.
     */
    [Obsolete("Use ValueOf<T>.")]
    public static ChannelOption<T> NewInstance<T>(string name)
    {
        ObjectUtil.CheckNonEmpty(name, nameof(name));
        var option = new ChannelOption<T>(NextId(), name);
        if (!Pool.TryAdd(name, option))
            throw new ArgumentException("Option name is already in use.", nameof(name));
        return option;
    }
    public static readonly ChannelOption<IByteBufAllocator> ALLOCATOR = ValueOf<IByteBufAllocator>("ALLOCATOR");

    public static readonly ChannelOption<IRecvByteBufAllocator> RECVBUF_ALLOCATOR = ValueOf<IRecvByteBufAllocator>("RECVBUF_ALLOCATOR");

    /**
     * @deprecated use {@link #RECVBUF_ALLOCATOR}.
     */
    [Obsolete]
    public static readonly ChannelOption<IRecvByteBufAllocator> RCVBUF_ALLOCATOR = RECVBUF_ALLOCATOR;

    public static readonly ChannelOption<IMessageSizeEstimator> MESSAGE_SIZE_ESTIMATOR = ValueOf<IMessageSizeEstimator>("MESSAGE_SIZE_ESTIMATOR");

    public static readonly ChannelOption<int> CONNECT_TIMEOUT_MILLIS = ValueOf<int>("CONNECT_TIMEOUT_MILLIS");
    /**
     * @deprecated Use {@link MaxMessagesRecvByteBufAllocator}
     * and {@link MaxMessagesRecvByteBufAllocator#maxMessagesPerRead(int)}.
     */
    [Obsolete]
    public static readonly ChannelOption<int> MAX_MESSAGES_PER_READ = ValueOf<int>("MAX_MESSAGES_PER_READ");
    public static readonly ChannelOption<int> MAX_MESSAGES_PER_WRITE = ValueOf<int>("MAX_MESSAGES_PER_WRITE");

    public static readonly ChannelOption<int> WRITE_SPIN_COUNT = ValueOf<int>("WRITE_SPIN_COUNT");
    /**
     * @deprecated Use {@link #WRITE_BUFFER_WATER_MARK}
     */
    [Obsolete]
    public static readonly ChannelOption<int> WRITE_BUFFER_HIGH_WATER_MARK = ValueOf<int>("WRITE_BUFFER_HIGH_WATER_MARK");
    /**
     * @deprecated Use {@link #WRITE_BUFFER_WATER_MARK}
     */
    [Obsolete]
    public static readonly ChannelOption<int> WRITE_BUFFER_LOW_WATER_MARK = ValueOf<int>("WRITE_BUFFER_LOW_WATER_MARK");
    public static readonly ChannelOption<WriteBufferWaterMark> WRITE_BUFFER_WATER_MARK =
            ValueOf<WriteBufferWaterMark>("WRITE_BUFFER_WATER_MARK");

    public static readonly ChannelOption<bool> ALLOW_HALF_CLOSURE = ValueOf<bool>("ALLOW_HALF_CLOSURE");
    public static readonly ChannelOption<bool> AUTO_READ = ValueOf<bool>("AUTO_READ");

    /**
     * If {@code true} then the {@link Channel} is closed automatically and immediately on write failure.
     * The default value is {@code true}.
     */
    public static readonly ChannelOption<bool> AUTO_CLOSE = ValueOf<bool>("AUTO_CLOSE");

    public static readonly ChannelOption<bool> SO_BROADCAST = ValueOf<bool>("SO_BROADCAST");
    public static readonly ChannelOption<bool> SO_KEEPALIVE = ValueOf<bool>("SO_KEEPALIVE");
    public static readonly ChannelOption<int> SO_SNDBUF = ValueOf<int>("SO_SNDBUF");
    public static readonly ChannelOption<int> SO_RCVBUF = ValueOf<int>("SO_RCVBUF");
    public static readonly ChannelOption<bool> SO_REUSEADDR = ValueOf<bool>("SO_REUSEADDR");
    public static readonly ChannelOption<int> SO_LINGER = ValueOf<int>("SO_LINGER");
    public static readonly ChannelOption<int> SO_BACKLOG = ValueOf<int>("SO_BACKLOG");
    public static readonly ChannelOption<int> SO_TIMEOUT = ValueOf<int>("SO_TIMEOUT");

    public static readonly ChannelOption<int> IP_TOS = ValueOf<int>("IP_TOS");
    public static readonly ChannelOption<IPAddress> IP_MULTICAST_ADDR = ValueOf<IPAddress>("IP_MULTICAST_ADDR");
    public static readonly ChannelOption<NetworkInterface> IP_MULTICAST_IF = ValueOf<NetworkInterface>("IP_MULTICAST_IF");
    public static readonly ChannelOption<int> IP_MULTICAST_TTL = ValueOf<int>("IP_MULTICAST_TTL");
    public static readonly ChannelOption<bool> IP_MULTICAST_LOOP_DISABLED = ValueOf<bool>("IP_MULTICAST_LOOP_DISABLED");

    public static readonly ChannelOption<bool> TCP_NODELAY = ValueOf<bool>("TCP_NODELAY");
    /**
     * Client-side TCP FastOpen. Sending data with the initial TCP handshake.
     */
    public static readonly ChannelOption<bool> TCP_FASTOPEN_CONNECT = ValueOf<bool>("TCP_FASTOPEN_CONNECT");

    /**
     * Server-side TCP FastOpen. Configures the maximum number of outstanding (waiting to be accepted) TFO connections.
     */
    public static readonly ChannelOption<int> TCP_FASTOPEN = ValueOf<int>(typeof(ChannelOption), "TCP_FASTOPEN");

    [Obsolete]
    public static readonly ChannelOption<bool> DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION =
            ValueOf<bool>("DATAGRAM_CHANNEL_ACTIVE_ON_REGISTRATION");

    public static readonly ChannelOption<bool> SINGLE_EVENTEXECUTOR_PER_GROUP =
            ValueOf<bool>("SINGLE_EVENTEXECUTOR_PER_GROUP");


}

public class ChannelOption<T> : ChannelOption
{
    /**
     * Creates a new {@link ChannelOption} with the specified unique {@code name}.
     */
    internal ChannelOption(int id, string name) : base(id, name) { }
    [Obsolete("Use ChannelOption.ValueOf<T>.")]
    protected ChannelOption(string name) : this(NextId(), name) { }
    public static ChannelOption<T> ValueOf(string name) => ChannelOption.ValueOf<T>(name);
    public static ChannelOption<T> ValueOf(Type firstNameComponent, string secondNameComponent)
        => ChannelOption.ValueOf<T>(firstNameComponent, secondNameComponent);
    [Obsolete("Use ValueOf.")]
    public static ChannelOption<T> NewInstance(string name) => ChannelOption.NewInstance<T>(name);
    /**
     * Validate the value which is set for the {@link ChannelOption}. Sub-classes
     * may override this for special checks.
     */
    public virtual void Validate(T value) => ArgumentNullException.ThrowIfNull(value);
}
