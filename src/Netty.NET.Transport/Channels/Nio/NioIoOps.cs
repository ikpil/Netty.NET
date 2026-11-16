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

namespace Netty.NET.Transport.Channels.Nio;

/**
 * Implementation of {@link IoOps} for
 * that is used by {@link NioIoHandler} and so for NIO based transports.
 */
// CLR: java.nio.channels.SelectionKey is jvm-only-replaced; its OP_READ (1), OP_WRITE (4), OP_CONNECT (8) and
// OP_ACCEPT (16) values are kept as the interest bits.
// https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/nio/channels/SelectionKey.html
public sealed class NioIoOps : IIoOps
{
    internal const int OP_READ = 1 << 0;
    internal const int OP_WRITE = 1 << 2;
    internal const int OP_CONNECT = 1 << 3;
    internal const int OP_ACCEPT = 1 << 4;

    /**
     * Interested in NO IO events.
     */
    public static readonly NioIoOps NONE = new NioIoOps(0);

    /**
     * Interested in IO events that should be handled by accepting new connections
     */
    public static readonly NioIoOps ACCEPT = new NioIoOps(OP_ACCEPT);

    /**
     * Interested in IO events which should be handled by finish pending connect operations
     */
    public static readonly NioIoOps CONNECT = new NioIoOps(OP_CONNECT);

    /**
     * Interested in IO events which tell that the underlying channel is writable again.
     */
    public static readonly NioIoOps WRITE = new NioIoOps(OP_WRITE);

    /**
     * Interested in IO events which should be handled by reading data.
     */
    public static readonly NioIoOps READ = new NioIoOps(OP_READ);

    /**
     * Interested in IO events which should be either handled by reading or accepting.
     */
    public static readonly NioIoOps READ_AND_ACCEPT = new NioIoOps(OP_READ | OP_ACCEPT);

    /**
     * Interested in IO events which should be either handled by reading or writing.
     */
    public static readonly NioIoOps READ_AND_WRITE = new NioIoOps(OP_READ | OP_WRITE);

    // Just use an array to store often used values.
    private static readonly INioIoEvent[] EVENTS;

    static NioIoOps()
    {
        NioIoOps all = new NioIoOps(
                NONE._value | ACCEPT._value | CONNECT._value | WRITE._value | READ._value);

        EVENTS = new INioIoEvent[all._value + 1];
        AddToArray(EVENTS, NONE);
        AddToArray(EVENTS, ACCEPT);
        AddToArray(EVENTS, CONNECT);
        AddToArray(EVENTS, WRITE);
        AddToArray(EVENTS, READ);
        AddToArray(EVENTS, READ_AND_ACCEPT);
        AddToArray(EVENTS, READ_AND_WRITE);
        AddToArray(EVENTS, all);
    }

    private static void AddToArray(INioIoEvent[] array, NioIoOps opt)
    {
        array[opt._value] = new DefaultNioIoEvent(opt);
    }

    internal readonly int _value;

    private NioIoOps(int value)
    {
        _value = value;
    }

    /**
     * Returns {@code true} if this {@link NioIoOps} is a combination of the given {@link NioIoOps}.
     * @param ops   the ops.
     * @return      {@code true} if a combination of the given.
     */
    public bool Contains(NioIoOps ops)
    {
        return IsIncludedIn(ops._value);
    }

    /**
     * Return a {@link NioIoOps} which is a combination of the current and the given {@link NioIoOps}.
     *
     * @param ops   the {@link NioIoOps} that should be added to this one.
     * @return      a {@link NioIoOps}.
     */
    public NioIoOps With(NioIoOps ops)
    {
        if (Contains(ops))
        {
            return this;
        }
        return ValueOf(_value | ops.Value());
    }

    /**
     * Return a {@link NioIoOps} which is not a combination of the current and the given {@link NioIoOps}.
     *
     * @param ops   the {@link NioIoOps} that should be remove from this one.
     * @return      a {@link NioIoOps}.
     */
    public NioIoOps Without(NioIoOps ops)
    {
        if (!Contains(ops))
        {
            return this;
        }
        return ValueOf(_value & ~ops.Value());
    }

    /**
     * Returns the underlying ops value of the {@link NioIoOps}.
     *
     * @return value.
     */
    public int Value()
    {
        return _value;
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
        NioIoOps nioOps = (NioIoOps)o;
        return _value == nioOps._value;
    }

    public override int GetHashCode()
    {
        return _value;
    }

    /**
     * Returns a {@link NioIoOps} for the given value.
     *
     * @param   value the value
     * @return  the {@link NioIoOps}.
     */
    public static NioIoOps ValueOf(int value)
    {
        return EventOf(value).Ops();
    }

    /**
     * Returns {@code true} if this {@link NioIoOps} is <strong>included </strong> in the given {@code ops}.
     *
     * @param ops   the ops to check.
     * @return      {@code true} if <strong>included</strong>, {@code false} otherwise.
     */
    public bool IsIncludedIn(int ops)
    {
        return (ops & _value) != 0;
    }

    /**
     * Returns {@code true} if this {@link NioIoOps} is <strong>not included</strong> in the given {@code ops}.
     *
     * @param ops   the ops to check.
     * @return      {@code true} if <strong>not included</strong>, {@code false} otherwise.
     */
    public bool IsNotIncludedIn(int ops)
    {
        return (ops & _value) == 0;
    }

    internal static INioIoEvent EventOf(int value)
    {
        if (value > 0 && value < EVENTS.Length)
        {
            INioIoEvent @event = EVENTS[value];
            if (@event != null)
            {
                return @event;
            }
        }
        return new DefaultNioIoEvent(new NioIoOps(value));
    }

    private sealed class DefaultNioIoEvent : INioIoEvent
    {
        private readonly NioIoOps _ops;

        internal DefaultNioIoEvent(NioIoOps ops)
        {
            _ops = ops;
        }

        public NioIoOps Ops()
        {
            return _ops;
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
            INioIoEvent @event = (INioIoEvent)o;
            return @event.Ops().Equals(Ops());
        }

        public override int GetHashCode()
        {
            return Ops().GetHashCode();
        }
    }
}
