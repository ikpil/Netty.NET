/*
 * Copyright 2013 The Netty Project
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

namespace Netty.NET.Transport.Channels.Groups;

/**
 * Helper class which provides often used {@link ChannelMatcher} implementations.
 */
// CLR: ChannelMatcher is the BCL predicate Func<IChannel, bool> that DefaultChannelGroup accepts. Each matcher
// class keeps its matches(Channel) method, and the returned delegate is bound to that method.
public static class ChannelMatchers
{
    private static readonly Func<IChannel, bool> ALL_MATCHER = channel => true;

    private static readonly Func<IChannel, bool> SERVER_CHANNEL_MATCHER = IsInstanceOf(typeof(IServerChannel));
    private static readonly Func<IChannel, bool> NON_SERVER_CHANNEL_MATCHER = IsNotInstanceOf(typeof(IServerChannel));

    // static methods only
    // CLR: a static class replaces the private constructor.

    /**
     * Returns a {@link ChannelMatcher} that matches all {@link Channel}s.
     */
    public static Func<IChannel, bool> All()
    {
        return ALL_MATCHER;
    }

    /**
     * Returns a {@link ChannelMatcher} that matches all {@link Channel}s except the given.
     */
    public static Func<IChannel, bool> IsNot(IChannel channel)
    {
        return Invert(Is(channel));
    }

    /**
     * Returns a {@link ChannelMatcher} that matches the given {@link Channel}.
     */
    public static Func<IChannel, bool> Is(IChannel channel)
    {
        return new InstanceMatcher(channel).Matches;
    }

    /**
     * Returns a {@link ChannelMatcher} that matches all {@link Channel}s that are an instance of sub-type of
     * the given class.
     */
    // CLR: Type has no Class<? extends Channel> bound; a type that is not a channel never matches.
    public static Func<IChannel, bool> IsInstanceOf(Type clazz)
    {
        return new ClassMatcher(clazz).Matches;
    }

    /**
     * Returns a {@link ChannelMatcher} that matches all {@link Channel}s that are <strong>not</strong> an
     * instance of sub-type of the given class.
     */
    public static Func<IChannel, bool> IsNotInstanceOf(Type clazz)
    {
        return Invert(IsInstanceOf(clazz));
    }

    /**
     * Returns a {@link ChannelMatcher} that matches all {@link Channel}s that are of type {@link ServerChannel}.
     */
    public static Func<IChannel, bool> IsServerChannel()
    {
        return SERVER_CHANNEL_MATCHER;
    }

    /**
     * Returns a {@link ChannelMatcher} that matches all {@link Channel}s that are <strong>not</strong> of type
     * {@link ServerChannel}.
     */
    public static Func<IChannel, bool> IsNonServerChannel()
    {
        return NON_SERVER_CHANNEL_MATCHER;
    }

    /**
     * Invert the given {@link ChannelMatcher}.
     */
    public static Func<IChannel, bool> Invert(Func<IChannel, bool> matcher)
    {
        return new InvertMatcher(matcher).Matches;
    }

    /**
     * Return a composite of the given {@link ChannelMatcher}s. This means all {@link ChannelMatcher} must
     * return {@code true} to match.
     */
    public static Func<IChannel, bool> Compose(params Func<IChannel, bool>[] matchers)
    {
        if (matchers.Length < 1)
        {
            throw new ArgumentException("matchers must at least contain one element");
        }
        if (matchers.Length == 1)
        {
            return matchers[0];
        }

        return new CompositeMatcher(matchers).Matches;
    }

    private sealed class CompositeMatcher
    {
        private readonly Func<IChannel, bool>[] _matchers;

        internal CompositeMatcher(params Func<IChannel, bool>[] matchers)
        {
            _matchers = matchers;
        }

        public bool Matches(IChannel channel)
        {
            foreach (Func<IChannel, bool> m in _matchers)
            {
                if (!m(channel))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private sealed class InvertMatcher
    {
        private readonly Func<IChannel, bool> _matcher;

        internal InvertMatcher(Func<IChannel, bool> matcher)
        {
            _matcher = matcher;
        }

        public bool Matches(IChannel channel)
        {
            return !_matcher(channel);
        }
    }

    private sealed class InstanceMatcher
    {
        private readonly IChannel _channel;

        internal InstanceMatcher(IChannel channel)
        {
            _channel = channel;
        }

        public bool Matches(IChannel ch)
        {
            return ReferenceEquals(_channel, ch);
        }
    }

    private sealed class ClassMatcher
    {
        private readonly Type _clazz;

        internal ClassMatcher(Type clazz)
        {
            _clazz = clazz;
        }

        public bool Matches(IChannel ch)
        {
            return _clazz.IsInstanceOfType(ch);
        }
    }
}
