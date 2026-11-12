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
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Netty.NET.Transport.Channels.Groups;

/**
 * A thread-safe {@link Set} that contains open {@link Channel}s and provides
 * various bulk operations on them.  Using {@link ChannelGroup}, you can
 * categorize {@link Channel}s into a meaningful group (e.g. on a per-service
 * or per-state basis.)  A closed {@link Channel} is automatically removed from
 * the collection, so that you don't need to worry about the life cycle of the
 * added {@link Channel}.  A {@link Channel} can belong to more than one
 * {@link ChannelGroup}.
 *
 * <h3>Broadcast a message to multiple {@link Channel}s</h3>
 * <p>
 * If you need to broadcast a message to more than one {@link Channel}, you can
 * add the {@link Channel}s associated with the recipients and call {@link ChannelGroup#write(Object)}:
 * <pre>
 * <strong>{@link ChannelGroup} recipients =
 *         new {@link DefaultChannelGroup}({@link GlobalEventExecutor}.INSTANCE);</strong>
 * recipients.add(channelA);
 * recipients.add(channelB);
 * ..
 * <strong>recipients.write({@link Unpooled}.copiedBuffer(
 *         "Service will shut down for maintenance in 5 minutes.",
 *         {@link CharsetUtil}.UTF_8));</strong>
 * </pre>
 *
 * <h3>Simplify shutdown process with {@link ChannelGroup}</h3>
 * <p>
 * If both {@link ServerChannel}s and non-{@link ServerChannel}s exist in the
 * same {@link ChannelGroup}, any requested I/O operations on the group are
 * performed for the {@link ServerChannel}s first and then for the others.
 * <p>
 * This rule is very useful when you shut down a server in one shot:
 *
 * <pre>
 * <strong>{@link ChannelGroup} allChannels =
 *         new {@link DefaultChannelGroup}({@link GlobalEventExecutor}.INSTANCE);</strong>
 *
 * public static void main(String[] args) throws Exception {
 *     {@link ServerBootstrap} b = new {@link ServerBootstrap}(..);
 *     ...
 *     b.childHandler(new MyHandler());
 *
 *     // Start the server
 *     b.getPipeline().addLast("handler", new MyHandler());
 *     {@link Channel} serverChannel = b.bind(..).sync();
 *     <strong>allChannels.add(serverChannel);</strong>
 *
 *     ... Wait until the shutdown signal reception ...
 *
 *     // Close the serverChannel and then all accepted connections.
 *     <strong>allChannels.close().awaitUninterruptibly();</strong>
 * }
 *
 * public class MyHandler extends {@link ChannelInboundHandlerAdapter} {
 *     {@code @Override}
 *     public void channelActive({@link ChannelHandlerContext} ctx) {
 *         // closed on shutdown.
 *         <strong>allChannels.add(ctx.channel());</strong>
 *         super.channelActive(ctx);
 *     }
 * }
 * </pre>
 */
// CLR: ISet supplies the inherited set surface; Tasks and Func predicates replace JVM futures/matchers.
public partial class DefaultChannelGroup
{
    // ChannelGroup extends Set and Comparable. Use CLR interfaces and reference-based HashSet
    // operations; all mutations still go through the group's Id admission and close-listener rules.
    // Each operation has a membership snapshot, not an atomic transaction spanning multiple calls.
    public bool IsReadOnly => false;
    void ICollection<IChannel>.Add(IChannel item) => Add(item);

    /// <summary>Copies a server-first membership snapshot without closing or transferring channel ownership.</summary>
    public IChannel[] ToArray() => _channels.Values.OrderBy(channel => channel is IServerChannel ? 0 : 1).ToArray();

    public void CopyTo(IChannel[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
        if (arrayIndex > array.Length) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        var snapshot = ToArray();
        if (snapshot.Length > array.Length - arrayIndex) throw new ArgumentException("Destination array is too small.", nameof(array));
        snapshot.CopyTo(array, arrayIndex);
    }

    public void UnionWith(IEnumerable<IChannel> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this)) return;
        foreach (var channel in other) Add(channel);
    }
    public void ExceptWith(IEnumerable<IChannel> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this)) { Clear(); return; }
        foreach (var channel in other) Remove(channel);
    }
    public void IntersectWith(IEnumerable<IChannel> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this)) return;
        var keep = Members(other);
        foreach (var channel in ToArray()) if (!keep.Contains(channel)) Remove(channel);
    }
    public void SymmetricExceptWith(IEnumerable<IChannel> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this)) { Clear(); return; }
        foreach (var channel in Members(other)) if (!Remove(channel)) Add(channel);
    }
    public bool IsSubsetOf(IEnumerable<IChannel> other) => Members(this).IsSubsetOf(Checked(other));
    public bool IsSupersetOf(IEnumerable<IChannel> other) => Members(this).IsSupersetOf(Checked(other));
    public bool IsProperSubsetOf(IEnumerable<IChannel> other) => Members(this).IsProperSubsetOf(Checked(other));
    public bool IsProperSupersetOf(IEnumerable<IChannel> other) => Members(this).IsProperSupersetOf(Checked(other));
    public bool Overlaps(IEnumerable<IChannel> other) => Members(this).Overlaps(Checked(other));
    public bool SetEquals(IEnumerable<IChannel> other) => Members(this).SetEquals(Checked(other));
    private static IEnumerable<IChannel> Checked(IEnumerable<IChannel> other)
    { ArgumentNullException.ThrowIfNull(other); return other; }
    private static HashSet<IChannel> Members(IEnumerable<IChannel> channels) => new(channels, ReferenceEqualityComparer.Instance);

    // Name is ordinal, as in Java String.compareTo. Same-name groups retain object identity;
    // group membership does not turn group equality/hash into mutable set-value equality.
    public int CompareTo(DefaultChannelGroup other)
    {
        if (other == null) return 1;
        int byName = string.CompareOrdinal(Name, other.Name);
        return byName != 0 ? byName : RuntimeHelpers.GetHashCode(this).CompareTo(RuntimeHelpers.GetHashCode(other));
    }
}
