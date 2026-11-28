/*
 * Copyright 2015 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Netty.NET.Common.Internal;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.Http2Error;
using static Netty.NET.Handler.Codec.Http2.Http2Exception;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Handler.Codec.Http2;

/**
 * A {@link StreamByteDistributor} that is sensitive to stream priority and uses
 * <a href="https://en.wikipedia.org/wiki/Weighted_fair_queueing">Weighted Fair Queueing</a> approach for distributing
 * bytes.
 * <p>
 * Inspiration for this distributor was taken from Linux's
 * <a href="https://www.kernel.org/doc/Documentation/scheduler/sched-design-CFS.txt">Completely Fair Scheduler</a>
 * to model the distribution of bytes to simulate an "ideal multi-tasking CPU", but in this case we are simulating
 * an "ideal multi-tasking NIC".
 * <p>
 * Each write operation will use the {@link #allocationQuantum(int)} to know how many more bytes should be allocated
 * relative to the next stream which wants to write. This is to balance fairness while also considering goodput.
 */
// CLR: IntObjectMap<State>/IntObjectHashMap are Dictionary<int, State> (owner decision 2026-11-19). The
// IntCollections.emptyMap() of stateOnlyMap (maxStateOnlySize 0) is an empty Dictionary that the code never adds to
// (every put is guarded by maxStateOnlySize == 0), because IntCollections' empty map returns null from remove(int)
// where a read-only dictionary throws; the empty children map is the shared EMPTY_CHILDREN sentinel, compared by
// reference as Java compares IntCollections.emptyMap(), which is never added to (initChildrenIfEmpty replaces it
// first). The Iterator<PrimitiveEntry<State>> through which takeChild removes a child is the Dictionary being
// enumerated: Iterator.remove() is Dictionary.Remove of the current key, which "may be safely called without
// invalidating active enumerators" (.NET Core 3.0+;
// https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2.remove?view=net-10.0).
// Netty's PriorityQueue/DefaultPriorityQueue/EmptyPriorityQueue/PriorityQueueNode are Common's IPriorityQueue,
// DefaultPriorityQueue, EmptyPriorityQueue and IPriorityQueueNode (Netty.NET.Common.Internal): peek()/poll()/add()/
// offer()/size()/removeTyped/containsTyped are TryPeek/TryDequeue/TryEnqueue/Count/Remove/Contains (the private Peek
// and Poll helpers return null for an empty queue, as Java does). Comparator<State> is IComparer<State>; Serializable
// and serialVersionUID have no counterpart; Long.compare is long.CompareTo. The private method state(...) is State_:
// it cannot share the name of the nested State class (CS0102, owner rule: trailing underscore). The inner State class
// and the anonymous Http2ConnectionAdapter (the private nested StreamListener) receive the distributor explicitly. A
// Java switch on the stream State is an if/else chain on reference equality. The package-private notifyParentChanged
// is private, because its parameter type ParentChangedEvent is private (CS0051); the test-only isChild/numChildren
// are internal. Throwable is Exception; assert is Debug.Assert. The flags byte keeps its value: ~STATE_IS_ACTIVE is
// cast back to byte unchecked, which leaves the other bits as Java's byte arithmetic does.
public sealed class WeightedFairQueueByteDistributor : IStreamByteDistributor
{
    /**
     * The initial size of the children map is chosen to be conservative on initial memory allocations under
     * the assumption that most streams will have a small number of children. This choice may be
     * sub-optimal if when children are present there are many children (i.e. a web page which has many
     * dependencies to load).
     *
     * Visible only for testing!
     */
    internal static readonly int INITIAL_CHILDREN_MAP_SIZE =
            Math.Max(1, SystemPropertyUtil.GetInt("io.netty.http2.childrenMapSize", 2));
    /**
     * FireFox currently uses 5 streams to establish QoS classes.
     */
    private const int DEFAULT_MAX_STATE_ONLY_SIZE = 5;

    private readonly IHttp2Connection.IPropertyKey stateKey;
    /**
     * If there is no Http2Stream object, but we still persist priority information then this is where the state will
     * reside.
     */
    private readonly Dictionary<int, State> stateOnlyMap;
    /**
     * This queue will hold streams that are not active and provides the capability to retain priority for streams which
     * have no {@link Http2Stream} object. See {@link StateOnlyComparator} for the priority comparator.
     */
    private readonly IPriorityQueue<State> stateOnlyRemovalQueue;
    private readonly IHttp2Connection connection;
    private readonly State connectionState;
    /**
     * The minimum number of bytes that we will attempt to allocate to a stream. This is to
     * help improve goodput on a per-stream basis.
     */
    private int allocationQuantum = DEFAULT_MIN_ALLOCATION_CHUNK;
    private readonly int maxStateOnlySize;

    public WeightedFairQueueByteDistributor(IHttp2Connection connection)
        : this(connection, DEFAULT_MAX_STATE_ONLY_SIZE)
    {
    }

    public WeightedFairQueueByteDistributor(IHttp2Connection connection, int maxStateOnlySize)
    {
        CheckPositiveOrZero(maxStateOnlySize, "maxStateOnlySize");

        if (maxStateOnlySize == 0)
        {
            stateOnlyMap = new Dictionary<int, State>(0);
            stateOnlyRemovalQueue = EmptyPriorityQueue<State>.Instance();
        }
        else
        {
            stateOnlyMap = new Dictionary<int, State>(maxStateOnlySize);
            // +2 because we may exceed the limit by 2 if a new dependency has no associated Http2Stream object. We need
            // to create the State objects to put them into the dependency tree, which then impacts priority.
            stateOnlyRemovalQueue = new DefaultPriorityQueue<State>(StateOnlyComparator.INSTANCE, maxStateOnlySize + 2);
        }

        this.maxStateOnlySize = maxStateOnlySize;

        this.connection = connection;
        stateKey = connection.NewKey();
        IHttp2Stream connectionStream = connection.ConnectionStream();
        connectionStream.SetProperty(stateKey, connectionState = new State(this, connectionStream, 16));

        // Register for notification of new streams.
        connection.AddListener(new StreamListener(this));
    }

    private sealed class StreamListener : Http2ConnectionAdapter
    {
        private readonly WeightedFairQueueByteDistributor distributor;

        internal StreamListener(WeightedFairQueueByteDistributor distributor)
        {
            this.distributor = distributor;
        }

        public override void OnStreamAdded(IHttp2Stream stream)
        {
            distributor.stateOnlyMap.Remove(stream.Id(), out State state);

            if (state == null)
            {
                state = new State(distributor, stream);
                // Only the stream which was just added will change parents. So we only need an array of size 1.
                List<ParentChangedEvent> events = new List<ParentChangedEvent>(1);
                distributor.connectionState.TakeChild(state, false, events);
                distributor.NotifyParentChanged(events);
            }
            else
            {
                distributor.stateOnlyRemovalQueue.Remove(state);
                state.stream = stream;
            }

            IHttp2Stream.State streamState = stream.State_();
            if (streamState == IHttp2Stream.State.RESERVED_REMOTE || streamState == IHttp2Stream.State.RESERVED_LOCAL)
            {
                state.SetStreamReservedOrActivated();
                // wasStreamReservedOrActivated is part of the comparator for stateOnlyRemovalQueue there is no
                // need to reprioritize here because it will not be in stateOnlyRemovalQueue.
            }

            stream.SetProperty(distributor.stateKey, state);
        }

        public override void OnStreamActive(IHttp2Stream stream)
        {
            distributor.State_(stream).SetStreamReservedOrActivated();
            // wasStreamReservedOrActivated is part of the comparator for stateOnlyRemovalQueue there is no need to
            // reprioritize here because it will not be in stateOnlyRemovalQueue.
        }

        public override void OnStreamClosed(IHttp2Stream stream)
        {
            distributor.State_(stream).Close();
        }

        public override void OnStreamRemoved(IHttp2Stream stream)
        {
            // The stream has been removed from the connection. We can no longer rely on the stream's property
            // storage to track the State. If we have room, and the precedence of the stream is sufficient, we
            // should retain the State in the stateOnlyMap.
            State state = distributor.State_(stream);

            // Typically the stream is set to null when the stream is closed because it is no longer needed to write
            // data. However if the stream was not activated it may not be closed (reserved streams) so we ensure
            // the stream reference is set to null to avoid retaining a reference longer than necessary.
            state.stream = null;

            if (distributor.maxStateOnlySize == 0)
            {
                state.parent.RemoveChild(state);
                return;
            }

            if (distributor.stateOnlyRemovalQueue.Count == distributor.maxStateOnlySize)
            {
                State stateToRemove = Peek(distributor.stateOnlyRemovalQueue);
                if (StateOnlyComparator.INSTANCE.Compare(stateToRemove, state) >= 0)
                {
                    // The "lowest priority" stream is a "higher priority" than the stream being removed, so we
                    // just discard the state.
                    state.parent.RemoveChild(state);
                    return;
                }

                Poll(distributor.stateOnlyRemovalQueue);
                stateToRemove.parent.RemoveChild(stateToRemove);
                distributor.stateOnlyMap.Remove(stateToRemove.streamId);
            }

            distributor.stateOnlyRemovalQueue.TryEnqueue(state);
            distributor.stateOnlyMap[state.streamId] = state;
        }
    }

    public void UpdateStreamableBytes(IStreamByteDistributor.IStreamState state)
    {
        State_(state.Stream()).UpdateStreamableBytes(StreamableBytes(state),
                                                    state.HasFrame() && state.WindowSize() >= 0);
    }

    public void UpdateDependencyTree(int childStreamId, int parentStreamId, short weight, bool exclusive)
    {
        State state = State_(childStreamId);
        if (state == null)
        {
            // If there is no State object that means there is no Http2Stream object and we would have to keep the
            // State object in the stateOnlyMap and stateOnlyRemovalQueue. However if maxStateOnlySize is 0 this means
            // stateOnlyMap and stateOnlyRemovalQueue are empty collections and cannot be modified so we drop the State.
            if (maxStateOnlySize == 0)
            {
                return;
            }

            state = new State(this, childStreamId);
            stateOnlyRemovalQueue.TryEnqueue(state);
            stateOnlyMap[childStreamId] = state;
        }

        State newParent = State_(parentStreamId);
        if (newParent == null)
        {
            // If there is no State object that means there is no Http2Stream object and we would have to keep the
            // State object in the stateOnlyMap and stateOnlyRemovalQueue. However if maxStateOnlySize is 0 this means
            // stateOnlyMap and stateOnlyRemovalQueue are empty collections and cannot be modified so we drop the State.
            if (maxStateOnlySize == 0)
            {
                return;
            }

            newParent = new State(this, parentStreamId);
            stateOnlyRemovalQueue.TryEnqueue(newParent);
            stateOnlyMap[parentStreamId] = newParent;
            // Only the stream which was just added will change parents. So we only need an array of size 1.
            List<ParentChangedEvent> events = new List<ParentChangedEvent>(1);
            connectionState.TakeChild(newParent, false, events);
            NotifyParentChanged(events);
        }

        // if activeCountForTree == 0 then it will not be in its parent's pseudoTimeQueue and thus should not be counted
        // toward parent.totalQueuedWeights.
        if (state.activeCountForTree != 0 && state.parent != null)
        {
            state.parent.totalQueuedWeights += weight - state.weight;
        }

        state.weight = weight;

        if (newParent != state.parent || exclusive && newParent.children.Count != 1)
        {
            List<ParentChangedEvent> events;

            if (newParent.IsDescendantOf(state))
            {
                events = new List<ParentChangedEvent>(2 + (exclusive ? newParent.children.Count : 0));
                state.parent.TakeChild(newParent, false, events);
            }
            else
            {
                events = new List<ParentChangedEvent>(1 + (exclusive ? newParent.children.Count : 0));
            }

            newParent.TakeChild(state, exclusive, events);
            NotifyParentChanged(events);
        }

        // The location in the dependency tree impacts the priority in the stateOnlyRemovalQueue map. If we created new
        // State objects we must check if we exceeded the limit after we insert into the dependency tree to ensure the
        // stateOnlyRemovalQueue has been updated.
        while (stateOnlyRemovalQueue.Count > maxStateOnlySize)
        {
            State stateToRemove = Poll(stateOnlyRemovalQueue);
            stateToRemove.parent.RemoveChild(stateToRemove);
            stateOnlyMap.Remove(stateToRemove.streamId);
        }
    }

    public bool Distribute(int maxBytes, IStreamByteDistributor.IWriter writer)
    {
        // As long as there is some active frame we should write at least 1 time.
        if (connectionState.activeCountForTree == 0)
        {
            return false;
        }

        // The goal is to write until we write all the allocated bytes or are no longer making progress.
        // We still attempt to write even after the number of allocated bytes has been exhausted to allow empty frames
        // to be sent. Making progress means the active streams rooted at the connection stream has changed.
        int oldIsActiveCountForTree;

        do
        {
            oldIsActiveCountForTree = connectionState.activeCountForTree;
            // connectionState will never be active, so go right to its children.
            maxBytes -= DistributeToChildren(maxBytes, writer, connectionState);
        } while (connectionState.activeCountForTree != 0 &&
                (maxBytes > 0 || oldIsActiveCountForTree != connectionState.activeCountForTree));

        return connectionState.activeCountForTree != 0;
    }

    /**
     * Sets the amount of bytes that will be allocated to each stream. Defaults to 1KiB.
     * @param allocationQuantum the amount of bytes that will be allocated to each stream. Must be &gt; 0.
     */
    public void AllocationQuantum(int allocationQuantum)
    {
        CheckPositive(allocationQuantum, "allocationQuantum");

        this.allocationQuantum = allocationQuantum;
    }

    private int Distribute(int maxBytes, IStreamByteDistributor.IWriter writer, State state)
    {
        if (state.IsActive())
        {
            int nsent = Math.Min(maxBytes, state.streamableBytes);
            state.Write(nsent, writer);

            if (nsent == 0 && maxBytes != 0)
            {
                // If a stream sends zero bytes, then we gave it a chance to write empty frames and it is now
                // considered inactive until the next call to updateStreamableBytes. This allows descendant streams to
                // be allocated bytes when the parent stream can't utilize them. This may be as a result of the
                // stream's flow control window being 0.
                state.UpdateStreamableBytes(state.streamableBytes, false);
            }

            return nsent;
        }

        return DistributeToChildren(maxBytes, writer, state);
    }

    /**
     * It is a pre-condition that {@code state.poll()} returns a non-{@code null} value. This is a result of the way
     * the allocation algorithm is structured and can be explained in the following cases:
     * <h3>For the recursive case</h3>
     * If a stream has no children (in the allocation tree) than that node must be active or it will not be in the
     * allocation tree. If a node is active then it will not delegate to children and recursion ends.
     * <h3>For the initial case</h3>
     * We check connectionState.activeCountForTree == 0 before any allocation is done. So if the connection stream
     * has no active children we don't get into this method.
     */
    private int DistributeToChildren(int maxBytes, IStreamByteDistributor.IWriter writer, State state)
    {
        long oldTotalQueuedWeights = state.totalQueuedWeights;
        State childState = state.PollPseudoTimeQueue();
        State nextChildState = state.PeekPseudoTimeQueue();
        childState.SetDistributing();

        try
        {
            Debug.Assert(nextChildState == null || nextChildState.pseudoTimeToWrite >= childState.pseudoTimeToWrite,
                "nextChildState[" + nextChildState?.streamId.ToString(CultureInfo.InvariantCulture) + "].pseudoTime(" +
                nextChildState?.pseudoTimeToWrite.ToString(CultureInfo.InvariantCulture) +
                ") < " + " childState[" + childState.streamId.ToString(CultureInfo.InvariantCulture) + "].pseudoTime(" +
                childState.pseudoTimeToWrite.ToString(CultureInfo.InvariantCulture) + ')');
            int nsent = Distribute(nextChildState == null ? maxBytes :
                            Math.Min(maxBytes, (int) Math.Min((nextChildState.pseudoTimeToWrite - childState.pseudoTimeToWrite) *
                                               childState.weight / oldTotalQueuedWeights + allocationQuantum, int.MaxValue)
                               ),
                               writer,
                               childState);
            state.pseudoTime += nsent;
            childState.UpdatePseudoTime(state, nsent, oldTotalQueuedWeights);
            return nsent;
        }
        finally
        {
            childState.UnsetDistributing();

            // Do in finally to ensure the internal flags is not corrupted if an exception is thrown.
            // The offer operation is delayed until we unroll up the recursive stack, so we don't have to remove from
            // the priority pseudoTimeQueue due to a write operation.
            if (childState.activeCountForTree != 0)
            {
                state.OfferPseudoTimeQueue(childState);
            }
        }
    }

    private State State_(IHttp2Stream stream)
    {
        return stream.GetProperty<State>(stateKey);
    }

    private State State_(int streamId)
    {
        IHttp2Stream stream = connection.Stream(streamId);
        return stream != null ? State_(stream) : stateOnlyMap.GetValueOrDefault(streamId);
    }

    /**
     * For testing only!
     */
    internal bool IsChild(int childId, int parentId, short weight)
    {
        State parent = State_(parentId);
        State child;
        return parent.children.ContainsKey(childId) &&
                (child = State_(childId)).parent == parent && child.weight == weight;
    }

    /**
     * For testing only!
     */
    internal int NumChildren(int streamId)
    {
        State state = State_(streamId);
        return state == null ? 0 : state.children.Count;
    }

    /**
     * Notify all listeners of the priority tree change events (in ascending order)
     * @param events The events (top down order) which have changed
     */
    private void NotifyParentChanged(List<ParentChangedEvent> events)
    {
        for (int i = 0; i < events.Count; ++i)
        {
            ParentChangedEvent @event = events[i];
            stateOnlyRemovalQueue.PriorityChanged(@event.state);

            if (@event.state.parent != null && @event.state.activeCountForTree != 0)
            {
                @event.state.parent.OfferAndInitializePseudoTime(@event.state);
                @event.state.parent.ActiveCountChangeForTree(@event.state.activeCountForTree);
            }
        }
    }

    // CLR: PriorityQueue.peek(): the head, or null when the queue is empty.
    private static State Peek(IPriorityQueue<State> queue)
    {
        return queue.TryPeek(out State state) ? state : null;
    }

    // CLR: PriorityQueue.poll(): removes and returns the head, or null when the queue is empty.
    private static State Poll(IPriorityQueue<State> queue)
    {
        return queue.TryDequeue(out State state) ? state : null;
    }

    /**
     * A comparator for {@link State} which has no associated {@link Http2Stream} object. The general precedence is:
     * <ul>
     *     <li>Was a stream activated or reserved (streams only used for priority are higher priority)</li>
     *     <li>Depth in the priority tree (closer to root is higher priority></li>
     *     <li>Stream ID (higher stream ID is higher priority - used for tie breaker)</li>
     * </ul>
     */
    private sealed class StateOnlyComparator : IComparer<State>
    {

        internal static readonly StateOnlyComparator INSTANCE = new StateOnlyComparator();

        public int Compare(State o1, State o2)
        {
            // "priority only streams" (which have not been activated) are higher priority than streams used for data.
            bool o1Actived = o1.WasStreamReservedOrActivated();
            if (o1Actived != o2.WasStreamReservedOrActivated())
            {
                return o1Actived ? -1 : 1;
            }

            // Numerically greater depth is higher priority.
            int x = o2.dependencyTreeDepth - o1.dependencyTreeDepth;

            // I also considered tracking the number of streams which are "activated" (eligible transfer data) at each
            // subtree. This would require a traversal from each node to the root on dependency tree structural changes,
            // and then it would require a re-prioritization at each of these nodes (instead of just the nodes where the
            // direct parent changed). The costs of this are judged to be relatively high compared to the nominal
            // benefit it provides to the heuristic. Instead folks should just increase maxStateOnlySize.

            // Last resort is to give larger stream ids more priority.
            return x != 0 ? x : o1.streamId - o2.streamId;
        }
    }

    private sealed class StatePseudoTimeComparator : IComparer<State>
    {

        internal static readonly StatePseudoTimeComparator INSTANCE = new StatePseudoTimeComparator();

        public int Compare(State o1, State o2)
        {
            return o1.pseudoTimeToWrite.CompareTo(o2.pseudoTimeToWrite);
        }
    }

    /**
     * The remote flow control state for a single stream.
     */
    private sealed class State : IPriorityQueueNode<State>
    {
        private const byte STATE_IS_ACTIVE = 0x1;
        private const byte STATE_IS_DISTRIBUTING = 0x2;
        private const byte STATE_STREAM_ACTIVATED = 0x4;

        private static readonly Dictionary<int, State> EMPTY_CHILDREN = new Dictionary<int, State>(0);

        private readonly WeightedFairQueueByteDistributor distributor;

        /**
         * Maybe {@code null} if the stream if the stream is not active.
         */
        internal IHttp2Stream stream;
        internal State parent;
        internal Dictionary<int, State> children = EMPTY_CHILDREN;
        private readonly IPriorityQueue<State> pseudoTimeQueue;
        internal readonly int streamId;
        internal int streamableBytes;
        internal int dependencyTreeDepth;
        /**
         * Count of nodes rooted at this sub tree with {@link #isActive()} equal to {@code true}.
         */
        internal int activeCountForTree;
        private int pseudoTimeQueueIndex = IPriorityQueueNode<State>.INDEX_NOT_IN_QUEUE;
        private int stateOnlyQueueIndex = IPriorityQueueNode<State>.INDEX_NOT_IN_QUEUE;
        /**
         * An estimate of when this node should be given the opportunity to write data.
         */
        internal long pseudoTimeToWrite;
        /**
         * A pseudo time maintained for immediate children to base their {@link #pseudoTimeToWrite} off of.
         */
        internal long pseudoTime;
        internal long totalQueuedWeights;
        private byte flags;
        internal short weight = DEFAULT_PRIORITY_WEIGHT;

        internal State(WeightedFairQueueByteDistributor distributor, int streamId)
            : this(distributor, streamId, null, 0)
        {
        }

        internal State(WeightedFairQueueByteDistributor distributor, IHttp2Stream stream)
            : this(distributor, stream, 0)
        {
        }

        internal State(WeightedFairQueueByteDistributor distributor, IHttp2Stream stream, int initialSize)
            : this(distributor, stream.Id(), stream, initialSize)
        {
        }

        internal State(WeightedFairQueueByteDistributor distributor, int streamId, IHttp2Stream stream, int initialSize)
        {
            this.distributor = distributor;
            this.stream = stream;
            this.streamId = streamId;
            pseudoTimeQueue = new DefaultPriorityQueue<State>(StatePseudoTimeComparator.INSTANCE, initialSize);
        }

        internal bool IsDescendantOf(State state)
        {
            State next = parent;

            while (next != null)
            {
                if (next == state)
                {
                    return true;
                }

                next = next.parent;
            }

            return false;
        }

        internal void TakeChild(State child, bool exclusive, List<ParentChangedEvent> events)
        {
            TakeChild(null, child, exclusive, events);
        }

        /**
         * Adds a child to this priority. If exclusive is set, any children of this node are moved to being dependent on
         * the child.
         */
        // CLR: childItr is the Dictionary being enumerated (see the class comment); childItr.remove() removes the
        // child's key from it.
        internal void TakeChild(Dictionary<int, State> childItr, State child, bool exclusive,
                       List<ParentChangedEvent> events)
        {
            State oldParent = child.parent;

            if (oldParent != this)
            {
                events.Add(new ParentChangedEvent(child, oldParent));
                child.SetParent(this);

                // If the childItr is not null we are iterating over the oldParent.children collection and should
                // use the iterator to remove from the collection to avoid concurrent modification. Otherwise it is
                // assumed we are not iterating over this collection and it is safe to call remove directly.
                if (childItr != null)
                {
                    childItr.Remove(child.streamId);
                }
                else if (oldParent != null)
                {
                    oldParent.children.Remove(child.streamId);
                }

                // Lazily initialize the children to save object allocations.
                InitChildrenIfEmpty();

                children.Remove(child.streamId, out State oldChild);
                children[child.streamId] = child;
                Debug.Assert(oldChild == null, "A stream with the same stream ID was already in the child map.");
            }

            if (exclusive && children.Count != 0)
            {
                // If it was requested that this child be the exclusive dependency of this node,
                // move any previous children to the child node, becoming grand children of this node.
                Dictionary<int, State> prevChildren = RemoveAllChildrenExcept(child);

                foreach (KeyValuePair<int, State> entry in prevChildren)
                {
                    child.TakeChild(prevChildren, entry.Value, false, events);
                }
            }
        }

        /**
         * Removes the child priority and moves any of its dependencies to being direct dependencies on this node.
         */
        internal void RemoveChild(State child)
        {
            if (children.Remove(child.streamId))
            {
                List<ParentChangedEvent> events = new List<ParentChangedEvent>(1 + child.children.Count);
                events.Add(new ParentChangedEvent(child, child.parent));
                child.SetParent(null);

                if (child.children.Count != 0)
                {
                    // Move up any grand children to be directly dependent on this node.
                    Dictionary<int, State> itr = child.children;
                    long totalWeight = child.GetTotalWeight();

                    foreach (KeyValuePair<int, State> entry in itr)
                    {
                        // Redistribute the weight of child to its dependency proportionally.
                        State dependency = entry.Value;
                        dependency.weight = (short) Math.Max(1, dependency.weight * child.weight / totalWeight);
                        TakeChild(itr, dependency, false, events);
                    }
                }

                distributor.NotifyParentChanged(events);
            }
        }

        private long GetTotalWeight()
        {
            long totalWeight = 0L;

            foreach (State state in children.Values)
            {
                totalWeight += state.weight;
            }

            return totalWeight;
        }

        /**
         * Remove all children with the exception of {@code streamToRetain}.
         * This method is intended to be used to support an exclusive priority dependency operation.
         * @return The map of children prior to this operation, excluding {@code streamToRetain} if present.
         */
        private Dictionary<int, State> RemoveAllChildrenExcept(State stateToRetain)
        {
            children.Remove(stateToRetain.streamId, out stateToRetain);
            Dictionary<int, State> prevChildren = children;
            // This map should be re-initialized in anticipation for the 1 exclusive child which will be added.
            // It will either be added directly in this method, or after this method is called...but it will be added.
            InitChildren();

            if (stateToRetain != null)
            {
                children[stateToRetain.streamId] = stateToRetain;
            }

            return prevChildren;
        }

        private void SetParent(State newParent)
        {
            // if activeCountForTree == 0 then it will not be in its parent's pseudoTimeQueue.
            if (activeCountForTree != 0 && parent != null)
            {
                parent.RemovePseudoTimeQueue(this);
                parent.ActiveCountChangeForTree(-activeCountForTree);
            }

            parent = newParent;
            // Use MAX_VALUE if no parent because lower depth is considered higher priority by StateOnlyComparator.
            dependencyTreeDepth = newParent == null ? int.MaxValue : newParent.dependencyTreeDepth + 1;
        }

        private void InitChildrenIfEmpty()
        {
            if (children == EMPTY_CHILDREN)
            {
                InitChildren();
            }
        }

        private void InitChildren()
        {
            children = new Dictionary<int, State>(INITIAL_CHILDREN_MAP_SIZE);
        }

        internal void Write(int numBytes, IStreamByteDistributor.IWriter writer)
        {
            Debug.Assert(stream != null);

            try
            {
                writer.Write(stream, numBytes);
            }
            catch (Exception t)
            {
                throw ConnectionError(INTERNAL_ERROR, t, "byte distribution write error");
            }
        }

        internal void ActiveCountChangeForTree(int increment)
        {
            Debug.Assert(activeCountForTree + increment >= 0);
            activeCountForTree += increment;

            if (parent != null)
            {
                Debug.Assert(activeCountForTree != increment ||
                       pseudoTimeQueueIndex == IPriorityQueueNode<State>.INDEX_NOT_IN_QUEUE ||
                       parent.pseudoTimeQueue.Contains(this),
                     "State[" + streamId.ToString(CultureInfo.InvariantCulture) + "].activeCountForTree changed from 0 to " +
                     increment.ToString(CultureInfo.InvariantCulture) + " is in a " +
                     "pseudoTimeQueue, but not in parent[ " + parent.streamId.ToString(CultureInfo.InvariantCulture) +
                     "]'s pseudoTimeQueue");

                if (activeCountForTree == 0)
                {
                    parent.RemovePseudoTimeQueue(this);
                }
                else if (activeCountForTree == increment && !IsDistributing())
                {
                    // If frame count was 0 but is now not, and this node is not already in a pseudoTimeQueue (assumed
                    // to be pState's pseudoTimeQueue) then enqueue it. If this State object is being processed the
                    // pseudoTime for this node should not be adjusted, and the node will be added back to the
                    // pseudoTimeQueue/tree structure after it is done being processed. This may happen if the
                    // activeCountForTree == 0 (a node which can't stream anything and is blocked) is at/near root of
                    // the tree, and is popped off the pseudoTimeQueue during processing, and then put back on the
                    // pseudoTimeQueue because a child changes position in the priority tree (or is closed because it is
                    // not blocked and finished writing all data).
                    parent.OfferAndInitializePseudoTime(this);
                }

                parent.ActiveCountChangeForTree(increment);
            }
        }

        internal void UpdateStreamableBytes(int newStreamableBytes, bool isActive)
        {
            if (IsActive() != isActive)
            {
                if (isActive)
                {
                    ActiveCountChangeForTree(1);
                    SetActive();
                }
                else
                {
                    ActiveCountChangeForTree(-1);
                    UnsetActive();
                }
            }

            streamableBytes = newStreamableBytes;
        }

        /**
         * Assumes the parents {@link #totalQueuedWeights} includes this node's weight.
         */
        internal void UpdatePseudoTime(State parentState, int nsent, long totalQueuedWeights)
        {
            Debug.Assert(streamId != CONNECTION_STREAM_ID && nsent >= 0);
            // If the current pseudoTimeToSend is greater than parentState.pseudoTime then we previously over accounted
            // and should use parentState.pseudoTime.
            pseudoTimeToWrite = Math.Min(pseudoTimeToWrite, parentState.pseudoTime) + nsent * totalQueuedWeights / weight;
        }

        /**
         * The concept of pseudoTime can be influenced by priority tree manipulations or if a stream goes from "active"
         * to "non-active". This method accounts for that by initializing the {@link #pseudoTimeToWrite} for
         * {@code state} to {@link #pseudoTime} of this node and then calls {@link #offerPseudoTimeQueue(State)}.
         */
        internal void OfferAndInitializePseudoTime(State state)
        {
            state.pseudoTimeToWrite = pseudoTime;
            OfferPseudoTimeQueue(state);
        }

        internal void OfferPseudoTimeQueue(State state)
        {
            pseudoTimeQueue.TryEnqueue(state);
            totalQueuedWeights += state.weight;
        }

        /**
         * Must only be called if the pseudoTimeQueue is non-empty!
         */
        internal State PollPseudoTimeQueue()
        {
            State state = Poll(pseudoTimeQueue);
            // This method is only ever called if the pseudoTimeQueue is non-empty.
            totalQueuedWeights -= state.weight;
            return state;
        }

        internal void RemovePseudoTimeQueue(State state)
        {
            if (pseudoTimeQueue.Remove(state))
            {
                totalQueuedWeights -= state.weight;
            }
        }

        internal State PeekPseudoTimeQueue()
        {
            return Peek(pseudoTimeQueue);
        }

        internal void Close()
        {
            UpdateStreamableBytes(0, false);
            stream = null;
        }

        internal bool WasStreamReservedOrActivated()
        {
            return (flags & STATE_STREAM_ACTIVATED) != 0;
        }

        internal void SetStreamReservedOrActivated()
        {
            flags |= STATE_STREAM_ACTIVATED;
        }

        internal bool IsActive()
        {
            return (flags & STATE_IS_ACTIVE) != 0;
        }

        private void SetActive()
        {
            flags |= STATE_IS_ACTIVE;
        }

        private void UnsetActive()
        {
            flags &= unchecked((byte) ~STATE_IS_ACTIVE);
        }

        internal bool IsDistributing()
        {
            return (flags & STATE_IS_DISTRIBUTING) != 0;
        }

        internal void SetDistributing()
        {
            flags |= STATE_IS_DISTRIBUTING;
        }

        internal void UnsetDistributing()
        {
            flags &= unchecked((byte) ~STATE_IS_DISTRIBUTING);
        }

        public int PriorityQueueIndex(DefaultPriorityQueue<State> queue)
        {
            return queue == distributor.stateOnlyRemovalQueue ? stateOnlyQueueIndex : pseudoTimeQueueIndex;
        }

        public void PriorityQueueIndex(DefaultPriorityQueue<State> queue, int i)
        {
            if (queue == distributor.stateOnlyRemovalQueue)
            {
                stateOnlyQueueIndex = i;
            }
            else
            {
                pseudoTimeQueueIndex = i;
            }
        }

        public override string ToString()
        {
            // Use activeCountForTree as a rough estimate for how many nodes are in this subtree.
            StringBuilder sb = new StringBuilder(256 * (activeCountForTree > 0 ? activeCountForTree : 1));
            ToString(sb);

            return sb.ToString();
        }

        private void ToString(StringBuilder sb)
        {
            sb.Append("{streamId ").Append(streamId.ToString(CultureInfo.InvariantCulture))
                    .Append(" streamableBytes ").Append(streamableBytes.ToString(CultureInfo.InvariantCulture))
                    .Append(" activeCountForTree ").Append(activeCountForTree.ToString(CultureInfo.InvariantCulture))
                    .Append(" pseudoTimeQueueIndex ").Append(pseudoTimeQueueIndex.ToString(CultureInfo.InvariantCulture))
                    .Append(" pseudoTimeToWrite ").Append(pseudoTimeToWrite.ToString(CultureInfo.InvariantCulture))
                    .Append(" pseudoTime ").Append(pseudoTime.ToString(CultureInfo.InvariantCulture))
                    .Append(" flags ").Append(flags.ToString(CultureInfo.InvariantCulture))
                    .Append(" pseudoTimeQueue.size() ").Append(pseudoTimeQueue.Count.ToString(CultureInfo.InvariantCulture))
                    .Append(" stateOnlyQueueIndex ").Append(stateOnlyQueueIndex.ToString(CultureInfo.InvariantCulture))
                    .Append(" parent.streamId ").Append((parent == null ? -1 : parent.streamId).ToString(CultureInfo.InvariantCulture))
                    .Append("} [");

            if (!pseudoTimeQueue.IsEmpty())
            {
                foreach (State s in pseudoTimeQueue)
                {
                    s.ToString(sb);
                    sb.Append(", ");
                }

                // Remove the last ", "
                sb.Length = sb.Length - 2;
            }

            sb.Append(']');
        }
    }

    /**
     * Allows a correlation to be made between a stream and its old parent before a parent change occurs.
     */
    private sealed class ParentChangedEvent
    {
        internal readonly State state;
        internal readonly State oldParent;

        /**
         * Create a new instance.
         * @param state The state who has had a parent change.
         * @param oldParent The previous parent.
         */
        internal ParentChangedEvent(State state, State oldParent)
        {
            this.state = state;
            this.oldParent = oldParent;
        }
    }
}
