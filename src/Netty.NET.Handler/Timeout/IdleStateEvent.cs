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

using Netty.NET.Common.Internal;

namespace Netty.NET.Handler.Timeout;

/**
 * A user event triggered by {@link IdleStateHandler} when a {@link Channel} is idle.
 */
public class IdleStateEvent
{
    public static readonly IdleStateEvent FIRST_READER_IDLE_STATE_EVENT =
            new DefaultIdleStateEvent(IdleState.READER_IDLE, true);
    public static readonly IdleStateEvent READER_IDLE_STATE_EVENT =
            new DefaultIdleStateEvent(IdleState.READER_IDLE, false);
    public static readonly IdleStateEvent FIRST_WRITER_IDLE_STATE_EVENT =
            new DefaultIdleStateEvent(IdleState.WRITER_IDLE, true);
    public static readonly IdleStateEvent WRITER_IDLE_STATE_EVENT =
            new DefaultIdleStateEvent(IdleState.WRITER_IDLE, false);
    public static readonly IdleStateEvent FIRST_ALL_IDLE_STATE_EVENT =
            new DefaultIdleStateEvent(IdleState.ALL_IDLE, true);
    public static readonly IdleStateEvent ALL_IDLE_STATE_EVENT =
            new DefaultIdleStateEvent(IdleState.ALL_IDLE, false);

    private readonly IdleState state;
    private readonly bool first;

    /**
     * Constructor for sub-classes.
     *
     * @param state the {@link IdleStateEvent} which triggered the event.
     * @param first {@code true} if its the first idle event for the {@link IdleStateEvent}.
     */
    // CLR: IdleState is a C# enum, a value type that cannot be null, so ObjectUtil.checkNotNull(state, "state") has no
    // counterpart (https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/enum).
    protected IdleStateEvent(IdleState state, bool first)
    {
        this.state = state;
        this.first = first;
    }

    /**
     * Returns the idle state.
     */
    public IdleState State()
    {
        return state;
    }

    /**
     * Returns {@code true} if this was the first event for the {@link IdleState}
     */
    public bool IsFirst()
    {
        return first;
    }

    // CLR: the enum's ToString() is its constant name, as the Java enum's toString()/name().
    public override string ToString()
    {
        return StringUtil.SimpleClassName(this) + '(' + state + (first ? ", first" : "") + ')';
    }

    private sealed class DefaultIdleStateEvent : IdleStateEvent
    {
        private readonly string representation;

        internal DefaultIdleStateEvent(IdleState state, bool first) : base(state, first)
        {
            this.representation = "IdleStateEvent(" + state + (first ? ", first" : "") + ')';
        }

        public override string ToString()
        {
            return representation;
        }
    }
}
