/*
 * Copyright 2026 The Netty Project
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
/*
 * Written by Doug Lea with assistance from members of JCP JSR-166
 * Expert Group and released to the public domain, as explained at
 * https://creativecommons.org/publicdomain/zero/1.0/
 *
 * With substantial modifications by The Netty Project team.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace Netty.NET.Common.Concurrent;

/**
 * A scalable concurrent multimap implementation.
 * The map is sorted according to the natural ordering of its {@code int} keys.
 *
 * <p>This class implements a concurrent variant of <a
 * href="https://en.wikipedia.org/wiki/Skip_list" target="_top">SkipLists</a>
 * providing expected average <i>log(n)</i> time cost for the
 * {@code containsKey}, {@code get}, {@code put} and
 * {@code remove} operations and their variants.  Insertion, removal,
 * update, and access operations safely execute concurrently by
 * multiple threads.
 *
 * <p>This class is a multimap, which means the same key can be associated with
 * multiple values. Each such instance will be represented by a separate
 * {@code IntEntry}. There is no defined ordering for the values mapped to
 * the same key.
 *
 * <p>As a multimap, certain atomic operations like {@code putIfPresent},
 * {@code compute}, or {@code computeIfPresent}, cannot be supported.
 * Likewise, some get-like operations cannot be supported.
 *
 * <p>Iterators and spliterators are
 * <a href="package-summary.html#Weakly"><i>weakly consistent</i></a>.
 *
 * <p>All {@code IntEntry} pairs returned by methods in this class
 * represent snapshots of mappings at the time they were
 * produced. They do <em>not</em> support the {@code Entry.setValue}
 * method. (Note however that it is possible to change mappings in the
 * associated map using {@code put}, {@code putIfAbsent}, or
 * {@code replace}, depending on exactly which effect you need.)
 *
 * <p>Beware that bulk operations {@code putAll}, {@code equals},
 * {@code toArray}, {@code containsValue}, and {@code clear} are
 * <em>not</em> guaranteed to be performed atomically. For example, an
 * iterator operating concurrently with a {@code putAll} operation
 * might view only some of the added elements.
 *
 * <p>This class does <em>not</em> permit the use of {@code null} values
 * because some null return values cannot be reliably distinguished from
 * the absence of elements.
 *
 * @param <V> the type of mapped values
 */
// CLR: V is constrained to reference types. The Java V is always a reference type: a null val marks a logically
// deleted node, CAS compares references, and the C# volatile modifier and Interlocked.CompareExchange<T> both need a
// reference type. Iterable<IntEntry<V>> maps to IEnumerable<IntEntry>; the nested types use the map's V instead of
// declaring their own type parameter (C# nested types already see the enclosing V).
public class ConcurrentSkipListIntObjMultimap<V> : IEnumerable<ConcurrentSkipListIntObjMultimap<V>.IntEntry>
    where V : class
{
    /*
     * This class implements a tree-like two-dimensionally linked skip
     * list in which the index levels are represented in separate
     * nodes from the base nodes holding data.  There are two reasons
     * for taking this approach instead of the usual array-based
     * structure: 1) Array based implementations seem to encounter
     * more complexity and overhead 2) We can use cheaper algorithms
     * for the heavily-traversed index lists than can be used for the
     * base lists.  Here's a picture of some of the basics for a
     * possible list with 2 levels of index:
     *
     * Head nodes          Index nodes
     * +-+    right        +-+                      +-+
     * |2|---------------->| |--------------------->| |->null
     * +-+                 +-+                      +-+
     *  | down              |                        |
     *  v                   v                        v
     * +-+            +-+  +-+       +-+            +-+       +-+
     * |1|----------->| |->| |------>| |----------->| |------>| |->null
     * +-+            +-+  +-+       +-+            +-+       +-+
     *  v              |    |         |              |         |
     * Nodes  next     v    v         v              v         v
     * +-+  +-+  +-+  +-+  +-+  +-+  +-+  +-+  +-+  +-+  +-+  +-+
     * | |->|A|->|B|->|C|->|D|->|E|->|F|->|G|->|H|->|I|->|J|->|K|->null
     * +-+  +-+  +-+  +-+  +-+  +-+  +-+  +-+  +-+  +-+  +-+  +-+
     *
     * The base lists use a variant of the HM linked ordered set
     * algorithm. See Tim Harris, "A pragmatic implementation of
     * non-blocking linked lists"
     * https://www.cl.cam.ac.uk/~tlh20/publications.html and Maged
     * Michael "High Performance Dynamic Lock-Free Hash Tables and
     * List-Based Sets"
     * https://www.research.ibm.com/people/m/michael/pubs.htm.  The
     * basic idea in these lists is to mark the "next" pointers of
     * deleted nodes when deleting to avoid conflicts with concurrent
     * insertions, and when traversing to keep track of triples
     * (predecessor, node, successor) in order to detect when and how
     * to unlink these deleted nodes.
     *
     * Rather than using mark-bits to mark list deletions (which can
     * be slow and space-intensive using AtomicMarkedReference), nodes
     * use direct CAS'able next pointers.  On deletion, instead of
     * marking a pointer, they splice in another node that can be
     * thought of as standing for a marked pointer (see method
     * unlinkNode).  Using plain nodes acts roughly like "boxed"
     * implementations of marked pointers, but uses new nodes only
     * when nodes are deleted, not for every link.  This requires less
     * space and supports faster traversal. Even if marked references
     * were better supported by JVMs, traversal using this technique
     * might still be faster because any search need only read ahead
     * one more node than otherwise required (to check for trailing
     * marker) rather than unmasking mark bits or whatever on each
     * read.
     *
     * This approach maintains the essential property needed in the HM
     * algorithm of changing the next-pointer of a deleted node so
     * that any other CAS of it will fail, but implements the idea by
     * changing the pointer to point to a different node (with
     * otherwise illegal null fields), not by marking it.  While it
     * would be possible to further squeeze space by defining marker
     * nodes not to have key/value fields, it isn't worth the extra
     * type-testing overhead.  The deletion markers are rarely
     * encountered during traversal, are easily detected via null
     * checks that are needed anyway, and are normally quickly garbage
     * collected. (Note that this technique would not work well in
     * systems without garbage collection.)
     *
     * In addition to using deletion markers, the lists also use
     * nullness of value fields to indicate deletion, in a style
     * similar to typical lazy-deletion schemes.  If a node's value is
     * null, then it is considered logically deleted and ignored even
     * though it is still reachable.
     *
     * Here's the sequence of events for a deletion of node n with
     * predecessor b and successor f, initially:
     *
     *        +------+       +------+      +------+
     *   ...  |   b  |------>|   n  |----->|   f  | ...
     *        +------+       +------+      +------+
     *
     * 1. CAS n's value field from non-null to null.
     *    Traversals encountering a node with null value ignore it.
     *    However, ongoing insertions and deletions might still modify
     *    n's next pointer.
     *
     * 2. CAS n's next pointer to point to a new marker node.
     *    From this point on, no other nodes can be appended to n.
     *    which avoids deletion errors in CAS-based linked lists.
     *
     *        +------+       +------+      +------+       +------+
     *   ...  |   b  |------>|   n  |----->|marker|------>|   f  | ...
     *        +------+       +------+      +------+       +------+
     *
     * 3. CAS b's next pointer over both n and its marker.
     *    From this point on, no new traversals will encounter n,
     *    and it can eventually be GCed.
     *        +------+                                    +------+
     *   ...  |   b  |----------------------------------->|   f  | ...
     *        +------+                                    +------+
     *
     * A failure at step 1 leads to simple retry due to a lost race
     * with another operation. Steps 2-3 can fail because some other
     * thread noticed during a traversal a node with null value and
     * helped out by marking and/or unlinking.  This helping-out
     * ensures that no thread can become stuck waiting for progress of
     * the deleting thread.
     *
     * Skip lists add indexing to this scheme, so that the base-level
     * traversals start close to the locations being found, inserted
     * or deleted -- usually base level traversals only traverse a few
     * nodes. This doesn't change the basic algorithm except for the
     * need to make sure base traversals start at predecessors (here,
     * b) that are not (structurally) deleted, otherwise retrying
     * after processing the deletion.
     *
     * Index levels are maintained using CAS to link and unlink
     * successors ("right" fields).  Races are allowed in index-list
     * operations that can (rarely) fail to link in a new index node.
     * (We can't do this of course for data nodes.)  However, even
     * when this happens, the index lists correctly guide search.
     * This can impact performance, but since skip lists are
     * probabilistic anyway, the net result is that under contention,
     * the effective "p" value may be lower than its nominal value.
     *
     * Index insertion and deletion sometimes require a separate
     * traversal pass occurring after the base-level action, to add or
     * remove index nodes.  This adds to single-threaded overhead, but
     * improves contended multithreaded performance by narrowing
     * interference windows, and allows deletion to ensure that all
     * index nodes will be made unreachable upon return from a public
     * remove operation, thus avoiding unwanted garbage retention.
     *
     * Indexing uses skip list parameters that maintain good search
     * performance while using sparser-than-usual indices: The
     * hardwired parameters k=1, p=0.5 (see method doPut) mean that
     * about one-quarter of the nodes have indices. Of those that do,
     * half have one level, a quarter have two, and so on (see Pugh's
     * Skip List Cookbook, sec 3.4), up to a maximum of 62 levels
     * (appropriate for up to 2^63 elements).  The expected total
     * space requirement for a map is slightly less than for the
     * current implementation of java.util.TreeMap.
     *
     * Changing the level of the index (i.e, the height of the
     * tree-like structure) also uses CAS.  Creation of an index with
     * height greater than the current level adds a level to the head
     * index by CAS'ing on a new top-most head. To maintain good
     * performance after a lot of removals, deletion methods
     * heuristically try to reduce the height if the topmost levels
     * appear to be empty.  This may encounter races in which it is
     * possible (but rare) to reduce and "lose" a level just as it is
     * about to contain an index (that will then never be
     * encountered). This does no structural harm, and in practice
     * appears to be a better option than allowing unrestrained growth
     * of levels.
     *
     * This class provides concurrent-reader-style memory consistency,
     * ensuring that read-only methods report status and/or values no
     * staler than those holding at method entry. This is done by
     * performing all publication and structural updates using
     * (volatile) CAS, placing an acquireFence in a few access
     * methods, and ensuring that linked objects are transitively
     * acquired via dependent reads (normally once) unless performing
     * a volatile-mode CAS operation (that also acts as an acquire and
     * release).  This form of fence-hoisting is similar to RCU and
     * related techniques (see McKenney's online book
     * https://www.kernel.org/pub/linux/kernel/people/paulmck/perfbook/perfbook.html)
     * It minimizes overhead that may otherwise occur when using so
     * many volatile-mode reads. Using explicit acquireFences is
     * logistically easier than targeting particular fields to be read
     * in acquire mode: fences are just hoisted up as far as possible,
     * to the entry points or loop headers of a few methods. A
     * potential disadvantage is that these few remaining fences are
     * not easily optimized away by compilers under exclusively
     * single-thread use.  It requires some care to avoid volatile
     * mode reads of other fields. (Note that the memory semantics of
     * a reference dependently read in plain mode exactly once are
     * equivalent to those for atomic opaque mode.)  Iterators and
     * other traversals encounter each node and value exactly once.
     * Other operations locate an element (or position to insert an
     * element) via a sequence of dereferences. This search is broken
     * into two parts. Method findPredecessor (and its specialized
     * embeddings) searches index nodes only, returning a base-level
     * predecessor of the key. Callers carry out the base-level
     * search, restarting if encountering a marker preventing link
     * modification.  In some cases, it is possible to encounter a
     * node multiple times while descending levels. For mutative
     * operations, the reported value is validated using CAS (else
     * retrying), preserving linearizability with respect to each
     * other. Others may return any (non-null) value holding in the
     * course of the method call.  (Search-based methods also include
     * some useless-looking explicit null checks designed to allow
     * more fields to be nulled out upon removal, to reduce floating
     * garbage, but which is not currently done, pending discovery of
     * a way to do this with less impact on other operations.)
     *
     * To produce random values without interference across threads,
     * we use within-JDK thread local random support (via the
     * "secondary seed", to avoid interference with user-level
     * ThreadLocalRandom.)
     *
     * For explanation of algorithms sharing at least a couple of
     * features with this one, see Mikhail Fomitchev's thesis
     * (https://www.cs.yorku.ca/~mikhail/), Keir Fraser's thesis
     * (https://www.cl.cam.ac.uk/users/kaf24/), and Hakan Sundell's
     * thesis (https://www.cs.chalmers.se/~phs/).
     *
     * Notation guide for local variables
     * Node:         b, n, f, p for  predecessor, node, successor, aux
     * Index:        q, r, d    for index node, right, down.
     * Head:         h
     * Keys:         k, key
     * Values:       v, value
     * Comparisons:  c
     */

    // CLR: Java's volatile head/val/next/right fields stay C# volatile fields, so every read of them is a volatile
    // (acquire) read and every plain store a volatile (release) store, as Volatile.Read/Volatile.Write would do. Each
    // HEAD/NEXT/VAL/RIGHT.compareAndSet(obj, expect, update) is Interlocked.CompareExchange(ref obj.field, update,
    // expect) == expect (a full-fence, reference-comparing CAS). Java "break outer" from a labeled loop is
    // "goto outer", whose label sits on the statement right after that loop (C# has no labeled break).

    /** No-key sentinel value */
    private readonly int _noKey;
    /** Lazily initialized topmost index of the skiplist. */
    private volatile /*XXX: Volatile only required for ARFU; remove if we can use VarHandle*/ Index _head;
    /** Element count */
    // CLR: LongAdder (a striped counter that only reduces update contention) is a single long updated with
    // Interlocked.Add and read with Interlocked.Read (atomic on 32-bit platforms too).
    private long _adder;

    /**
     * Nodes hold keys and values, and are singly linked in sorted
     * order, possibly with some intervening marker nodes. The list is
     * headed by a header node accessible as head.node. Headers and
     * marker nodes have null keys. The val field (but currently not
     * the key field) is nulled out upon deletion.
     */
    internal sealed class Node
    {
        internal readonly int key; // currently, never detached
        internal volatile /*XXX: Volatile only required for ARFU; remove if we can use VarHandle*/ V val;
        internal volatile /*XXX: Volatile only required for ARFU; remove if we can use VarHandle*/ Node next;
        internal Node(int key, V value, Node next)
        {
            this.key = key;
            val = value;
            this.next = next;
        }
    }

    /**
     * Index nodes represent the levels of the skip list.
     */
    internal sealed class Index
    {
        internal readonly Node node;  // currently, never detached
        internal readonly Index down;
        internal volatile /*XXX: Volatile only required for ARFU; remove if we can use VarHandle*/ Index right;
        internal Index(Node node, Index down, Index right)
        {
            this.node = node;
            this.down = down;
            this.right = right;
        }
    }

    /**
     * The multimap entry type with primitive {@code int} keys.
     */
    public sealed class IntEntry : IComparable<IntEntry>, IEquatable<IntEntry>
    {
        private readonly int _key;
        private readonly V _value;

        public IntEntry(int key, V value)
        {
            _key = key;
            _value = value;
        }

        /**
         * Get the corresponding key.
         */
        public int GetKey()
        {
            return _key;
        }

        /**
         * Get the corresponding value.
         */
        public V GetValue()
        {
            return _value;
        }

        public override bool Equals(object o)
        {
            if (!(o is IntEntry))
            {
                return false;
            }

            IntEntry intEntry = (IntEntry) o;
            return _key == intEntry._key && object.Equals(_value, intEntry._value);
        }

        // CLR: IEquatable<T> routes generic equality (EqualityComparer<T>.Default, which xUnit consults before
        // IComparable<T>) to the Java equals body above instead of the key-only compareTo below.
        public bool Equals(IntEntry other)
        {
            return Equals((object) other);
        }

        public override int GetHashCode()
        {
            int result = _key;
            result = unchecked(31 * result + (_value == null ? 0 : _value.GetHashCode()));
            return result;
        }

        public override string ToString()
        {
            // CLR: Java string concatenation prints an int with invariant digits and a null value as "null".
            return "IntEntry[" + _key.ToString(CultureInfo.InvariantCulture) + " => " + (_value?.ToString() ?? "null") + ']';
        }

        public int CompareTo(IntEntry o)
        {
            // CLR: Java dereferences o and throws NullPointerException; the argument check reports it as
            // ArgumentNullException.
            ArgumentNullException.ThrowIfNull(o);
            return _key.CompareTo(o._key);
        }
    }

    /* ----------------  Utilities -------------- */

    /**
     * Compares using comparator or natural ordering if null.
     * Called only by methods that have performed required type checks.
     */
    internal static int Cpr(int x, int y)
    {
        return x.CompareTo(y);
    }

    /**
     * Returns the header for base node list, or null if uninitialized
     */
    internal Node BaseHead()
    {
        Index h;
        AcquireFence();
        return (h = _head) == null ? null : h.node;
    }

    /**
     * Tries to unlink deleted node n from predecessor b (if both
     * exist), by first splicing in a marker if not already present.
     * Upon return, node n is sure to be unlinked from b, possibly
     * via the actions of some other thread.
     *
     * @param b if nonnull, predecessor
     * @param n if nonnull, node known to be deleted
     */
    internal static void UnlinkNode(Node b, Node n, int noKey)
    {
        if (b != null && n != null)
        {
            Node f, p;
            for (;;)
            {
                if ((f = n.next) != null && f.key == noKey)
                {
                    p = f.next;               // already marked
                    break;
                }
                else if (Interlocked.CompareExchange(ref n.next,
                                                     new Node(noKey, null, f), f) == f)
                {
                    p = f;                    // add marker
                    break;
                }
            }
            Interlocked.CompareExchange(ref b.next, p, n);
        }
    }

    /**
     * Adds to element count, initializing adder if necessary
     *
     * @param c count to add
     */
    private void AddCount(long c)
    {
        Interlocked.Add(ref _adder, c);
    }

    /**
     * Returns element count, initializing adder if necessary.
     */
    internal long GetAdderCount()
    {
        long c;
        return (c = Interlocked.Read(ref _adder)) <= 0L ? 0L : c; // ignore transient negatives
    }

    /* ---------------- Traversal -------------- */

    /**
     * Returns an index node with key strictly less than given key.
     * Also unlinks indexes to deleted nodes found along the way.
     * Callers rely on this side-effect of clearing indices to deleted
     * nodes.
     *
     * @param key if nonnull the key
     * @return a predecessor node of key, or null if uninitialized or null key
     */
    private Node FindPredecessor(int key)
    {
        Index q;
        AcquireFence();
        if ((q = _head) == null || key == _noKey)
        {
            return null;
        }
        else
        {
            for (Index r, d; ;)
            {
                while ((r = q.right) != null)
                {
                    Node p; int k;
                    if ((p = r.node) == null || (k = p.key) == _noKey ||
                        p.val == null) // unlink index to deleted node
                    {
                        Interlocked.CompareExchange(ref q.right, r.right, r);
                    }
                    else if (Cpr(key, k) > 0)
                    {
                        q = r;
                    }
                    else
                    {
                        break;
                    }
                }
                if ((d = q.down) != null)
                {
                    q = d;
                }
                else
                {
                    return q.node;
                }
            }
        }
    }

    /**
     * Returns node holding key or null if no such, clearing out any
     * deleted nodes seen along the way.  Repeatedly traverses at
     * base-level looking for key starting at predecessor returned
     * from findPredecessor, processing base-level deletions as
     * encountered. Restarts occur, at traversal step encountering
     * node n, if n's key field is null, indicating it is a marker, so
     * its predecessor is deleted before continuing, which we help do
     * by re-finding a valid predecessor.  The traversal loops in
     * doPut, doRemove, and findNear all include the same checks.
     *
     * @param key the key
     * @return node holding key, or null if no such
     */
    private Node FindNode(int key)
    {
        if (key == _noKey)
        {
            throw new ArgumentException(); // don't postpone errors
        }
        Node b;
        while ((b = FindPredecessor(key)) != null)
        {
            for (;;)
            {
                Node n; int k; int c;
                if ((n = b.next) == null)
                {
                    goto outer;               // empty
                }
                else if ((k = n.key) == _noKey)
                {
                    break;                     // b is deleted
                }
                else if (n.val == null)
                {
                    UnlinkNode(b, n, _noKey);   // n is deleted
                }
                else if ((c = Cpr(key, k)) > 0)
                {
                    b = n;
                }
                else if (c == 0)
                {
                    return n;
                }
                else
                {
                    goto outer;
                }
            }
        }
        outer:
        return null;
    }

    /**
     * Gets value for key. Same idea as findNode, except skips over
     * deletions and markers, and returns first encountered value to
     * avoid possibly inconsistent rereads.
     *
     * @param key the key
     * @return the value, or null if absent
     */
    private V DoGet(int key)
    {
        Index q;
        AcquireFence();
        if (key == _noKey)
        {
            throw new ArgumentException();
        }
        V result = null;
        if ((q = _head) != null)
        {
            for (Index r, d; ;)
            {
                while ((r = q.right) != null)
                {
                    Node p; int k; V v; int c;
                    if ((p = r.node) == null || (k = p.key) == _noKey ||
                        (v = p.val) == null)
                    {
                        Interlocked.CompareExchange(ref q.right, r.right, r);
                    }
                    else if ((c = Cpr(key, k)) > 0)
                    {
                        q = r;
                    }
                    else if (c == 0)
                    {
                        result = v;
                        goto outer;
                    }
                    else
                    {
                        break;
                    }
                }
                if ((d = q.down) != null)
                {
                    q = d;
                }
                else
                {
                    Node b, n;
                    if ((b = q.node) != null)
                    {
                        while ((n = b.next) != null)
                        {
                            V v; int c;
                            int k = n.key;
                            if ((v = n.val) == null || k == _noKey ||
                                (c = Cpr(key, k)) > 0)
                            {
                                b = n;
                            }
                            else
                            {
                                if (c == 0)
                                {
                                    result = v;
                                }
                                break;
                            }
                        }
                    }
                    break;
                }
            }
        }
        outer:
        return result;
    }

    /* ---------------- Insertion -------------- */

    /**
     * Main insertion method.  Adds element if not present, or
     * replaces value if present and onlyIfAbsent is false.
     *
     * @param key the key
     * @param value the value that must be associated with key
     * @param onlyIfAbsent if should not insert if already present
     */
    private V DoPut(int key, V value, bool onlyIfAbsent)
    {
        if (key == _noKey)
        {
            throw new ArgumentException();
        }
        for (;;)
        {
            Index h; Node b;
            AcquireFence();
            int levels = 0;                    // number of levels descended
            if ((h = _head) == null)           // try to initialize
            {
                Node @base = new Node(_noKey, null, null);
                h = new Index(@base, null, null);
                b = Interlocked.CompareExchange(ref _head, h, null) == null ? @base : null;
            }
            else
            {
                for (Index q = h, r, d; ;)     // count while descending
                {
                    while ((r = q.right) != null)
                    {
                        Node p; int k;
                        if ((p = r.node) == null || (k = p.key) == _noKey ||
                            p.val == null)
                        {
                            Interlocked.CompareExchange(ref q.right, r.right, r);
                        }
                        else if (Cpr(key, k) > 0)
                        {
                            q = r;
                        }
                        else
                        {
                            break;
                        }
                    }
                    if ((d = q.down) != null)
                    {
                        ++levels;
                        q = d;
                    }
                    else
                    {
                        b = q.node;
                        break;
                    }
                }
            }
            if (b != null)
            {
                Node z = null;              // new node, if inserted
                for (;;)                       // find insertion point
                {
                    Node n, p; int k; V v; int c;
                    if ((n = b.next) == null)
                    {
                        if (b.key == _noKey)       // if empty, type check key now TODO: remove?
                        {
                            Cpr(key, key);
                        }
                        c = -1;
                    }
                    else if ((k = n.key) == _noKey)
                    {
                        break;                   // can't append; restart
                    }
                    else if ((v = n.val) == null)
                    {
                        UnlinkNode(b, n, _noKey);
                        c = 1;
                    }
                    else if ((c = Cpr(key, k)) > 0)
                    {
                        b = n; // Multimap
                    }
//                    } else if (c == 0 &&
//                             (onlyIfAbsent || VAL.compareAndSet(n, v, value))) {
//                        return v;

                    if (c <= 0 &&
                        Interlocked.CompareExchange(ref b.next,
                                                    p = new Node(key, value, n), n) == n)
                    {
                        z = p;
                        break;
                    }
                }

                if (z != null)
                {
                    // CLR: ThreadLocalRandom.current().nextInt() draws from all 2^32 int values (negative ones
                    // included, which the sign test on rnd below relies on). Random.Next() is never negative, so
                    // 32 uniform bits come from the thread-safe Random.Shared.NextInt64(2^32) instead.
                    int lr = unchecked((int) Random.Shared.NextInt64(0x1_0000_0000L));
                    if ((lr & 0x3) == 0)         // add indices with 1/4 prob
                    {
                        int hr = unchecked((int) Random.Shared.NextInt64(0x1_0000_0000L));
                        long rnd = ((long) hr << 32) | ((long) lr & 0xffffffffL);
                        int skips = levels;      // levels to descend before add
                        Index x = null;
                        for (;;)                 // create at most 62 indices
                        {
                            x = new Index(z, x, null);
                            if (rnd >= 0L || --skips < 0)
                            {
                                break;
                            }
                            else
                            {
                                rnd <<= 1;
                            }
                        }
                        if (AddIndices(h, skips, x, _noKey) && skips < 0 &&
                            _head == h)          // try to add new level
                        {
                            Index hx = new Index(z, x, null);
                            Index nh = new Index(h.node, h, hx);
                            Interlocked.CompareExchange(ref _head, nh, h);
                        }
                        if (z.val == null)        // deleted while adding indices
                        {
                            FindPredecessor(key); // clean
                        }
                    }
                    AddCount(1L);
                    return null;
                }
            }
        }
    }

    /**
     * Add indices after an insertion. Descends iteratively to the
     * highest level of insertion, then recursively, to chain index
     * nodes to lower ones. Returns null on (staleness) failure,
     * disabling higher-level insertions. Recursion depths are
     * exponentially less probable.
     *
     * @param q starting index for current level
     * @param skips levels to skip before inserting
     * @param x index for this insertion
     */
    internal static bool AddIndices(Index q, int skips, Index x, int noKey)
    {
        Node z;
        int key;
        if (x != null && (z = x.node) != null && (key = z.key) != noKey &&
            q != null)                              // hoist checks
        {
            bool retrying = false;
            for (;;)                                // find splice point
            {
                Index r, d;
                int c;
                if ((r = q.right) != null)
                {
                    Node p;
                    int k;
                    if ((p = r.node) == null || (k = p.key) == noKey ||
                        p.val == null)
                    {
                        Interlocked.CompareExchange(ref q.right, r.right, r);
                        c = 0;
                    }
                    else if ((c = Cpr(key, k)) > 0)
                    {
                        q = r;
                    }
                    else if (c == 0)
                    {
                        break;                      // stale
                    }
                }
                else
                {
                    c = -1;
                }

                if (c < 0)
                {
                    if ((d = q.down) != null && skips > 0)
                    {
                        --skips;
                        q = d;
                    }
                    else if (d != null && !retrying &&
                             !AddIndices(d, 0, x.down, noKey))
                    {
                        break;
                    }
                    else
                    {
                        x.right = r;
                        if (Interlocked.CompareExchange(ref q.right, x, r) == r)
                        {
                            return true;
                        }
                        else
                        {
                            retrying = true;         // re-find splice point
                        }
                    }
                }
            }
        }
        return false;
    }

    /* ---------------- Deletion -------------- */

    /**
     * Main deletion method. Locates node, nulls value, appends a
     * deletion marker, unlinks predecessor, removes associated index
     * nodes, and possibly reduces head index level.
     *
     * @param key the key
     * @param value if non-null, the value that must be
     * associated with key
     * @return the node, or null if not found
     */
    internal V DoRemove(int key, object value)
    {
        if (key == _noKey)
        {
            throw new ArgumentException();
        }
        V result = null;
        Node b;
        while ((b = FindPredecessor(key)) != null &&
               result == null)
        {
            for (;;)
            {
                Node n; int k; V v; int c;
                if ((n = b.next) == null)
                {
                    goto outer;
                }
                else if ((k = n.key) == _noKey)
                {
                    break;
                }
                else if ((v = n.val) == null)
                {
                    UnlinkNode(b, n, _noKey);
                }
                else if ((c = Cpr(key, k)) > 0)
                {
                    b = n;
                }
                else if (c < 0)
                {
                    goto outer;
                }
                else if (value != null && !value.Equals(v))
                {
//                    break outer;
                    b = n; // Multimap.
                }
                else if (Interlocked.CompareExchange(ref n.val, null, v) == v)
                {
                    result = v;
                    UnlinkNode(b, n, _noKey);
                    break; // loop to clean up
                }
            }
        }
        outer:
        if (result != null)
        {
            TryReduceLevel();
            AddCount(-1L);
        }
        return result;
    }

    /**
     * Possibly reduce head level if it has no nodes.  This method can
     * (rarely) make mistakes, in which case levels can disappear even
     * though they are about to contain index nodes. This impacts
     * performance, not correctness.  To minimize mistakes as well as
     * to reduce hysteresis, the level is reduced by one only if the
     * topmost three levels look empty. Also, if the removed level
     * looks non-empty after CAS, we try to change it back quick
     * before anyone notices our mistake! (This trick works pretty
     * well because this method will practically never make mistakes
     * unless current thread stalls immediately before first CAS, in
     * which case it is very unlikely to stall again immediately
     * afterwards, so will recover.)
     * <p>
     * We put up with all this rather than just let levels grow
     * because otherwise, even a small map that has undergone a large
     * number of insertions and removals will have a lot of levels,
     * slowing down access more than would an occasional unwanted
     * reduction.
     */
    private void TryReduceLevel()
    {
        Index h, d, e;
        if ((h = _head) != null && h.right == null &&
            (d = h.down) != null && d.right == null &&
            (e = d.down) != null && e.right == null &&
            Interlocked.CompareExchange(ref _head, d, h) == h &&
            h.right != null)  // recheck
        {
            Interlocked.CompareExchange(ref _head, h, d);  // try to backout
        }
    }

    /* ---------------- Finding and removing first element -------------- */

    /**
     * Gets first valid node, unlinking deleted nodes if encountered.
     * @return first node or null if empty
     */
    internal Node FindFirst()
    {
        Node b, n;
        if ((b = BaseHead()) != null)
        {
            while ((n = b.next) != null)
            {
                if (n.val == null)
                {
                    UnlinkNode(b, n, _noKey);
                }
                else
                {
                    return n;
                }
            }
        }
        return null;
    }

    /**
     * Entry snapshot version of findFirst
     */
    internal IntEntry FindFirstEntry()
    {
        Node b, n;
        V v;
        if ((b = BaseHead()) != null)
        {
            while ((n = b.next) != null)
            {
                if ((v = n.val) == null)
                {
                    UnlinkNode(b, n, _noKey);
                }
                else
                {
                    return new IntEntry(n.key, v);
                }
            }
        }
        return null;
    }

    /**
     * Removes first entry; returns its snapshot.
     * @return null if empty, else snapshot of first entry
     */
    private IntEntry DoRemoveFirstEntry()
    {
        Node b, n;
        V v;
        if ((b = BaseHead()) != null)
        {
            while ((n = b.next) != null)
            {
                if ((v = n.val) == null || Interlocked.CompareExchange(ref n.val, null, v) == v)
                {
                    int k = n.key;
                    UnlinkNode(b, n, _noKey);
                    if (v != null)
                    {
                        TryReduceLevel();
                        FindPredecessor(k); // clean index
                        AddCount(-1L);
                        return new IntEntry(k, v);
                    }
                }
            }
        }
        return null;
    }

    /* ---------------- Finding and removing last element -------------- */

    /**
     * Specialized version of find to get last valid node.
     * @return last node or null if empty
     */
    internal Node FindLast()
    {
        for (;;)
        {
            Index q; Node b;
            AcquireFence();
            if ((q = _head) == null)
            {
                break;
            }
            for (Index r, d; ;)
            {
                while ((r = q.right) != null)
                {
                    Node p;
                    if ((p = r.node) == null || p.val == null)
                    {
                        Interlocked.CompareExchange(ref q.right, r.right, r);
                    }
                    else
                    {
                        q = r;
                    }
                }
                if ((d = q.down) != null)
                {
                    q = d;
                }
                else
                {
                    b = q.node;
                    break;
                }
            }
            if (b != null)
            {
                for (;;)
                {
                    Node n;
                    if ((n = b.next) == null)
                    {
                        if (b.key == _noKey) // empty
                        {
                            goto outer;
                        }
                        else
                        {
                            return b;
                        }
                    }
                    else if (n.key == _noKey)
                    {
                        break;
                    }
                    else if (n.val == null)
                    {
                        UnlinkNode(b, n, _noKey);
                    }
                    else
                    {
                        b = n;
                    }
                }
            }
        }
        outer:
        return null;
    }

    /**
     * Entry version of findLast
     * @return Entry for last node or null if empty
     */
    internal IntEntry FindLastEntry()
    {
        for (;;)
        {
            Node n; V v;
            if ((n = FindLast()) == null)
            {
                return null;
            }
            if ((v = n.val) != null)
            {
                return new IntEntry(n.key, v);
            }
        }
    }

    /**
     * Removes last entry; returns its snapshot.
     * Specialized variant of doRemove.
     * @return null if empty, else snapshot of last entry
     */
    private IntEntry DoRemoveLastEntry()
    {
        for (;;)
        {
            Index q; Node b;
            AcquireFence();
            if ((q = _head) == null)
            {
                break;
            }
            for (;;)
            {
                Index d, r; Node p;
                while ((r = q.right) != null)
                {
                    if ((p = r.node) == null || p.val == null)
                    {
                        Interlocked.CompareExchange(ref q.right, r.right, r);
                    }
                    else if (p.next != null)
                    {
                        q = r;  // continue only if a successor
                    }
                    else
                    {
                        break;
                    }
                }
                if ((d = q.down) != null)
                {
                    q = d;
                }
                else
                {
                    b = q.node;
                    break;
                }
            }
            if (b != null)
            {
                for (;;)
                {
                    Node n; int k; V v;
                    if ((n = b.next) == null)
                    {
                        if (b.key == _noKey) // empty
                        {
                            goto outer;
                        }
                        else
                        {
                            break; // retry
                        }
                    }
                    else if ((k = n.key) == _noKey)
                    {
                        break;
                    }
                    else if ((v = n.val) == null)
                    {
                        UnlinkNode(b, n, _noKey);
                    }
                    else if (n.next != null)
                    {
                        b = n;
                    }
                    else if (Interlocked.CompareExchange(ref n.val, null, v) == v)
                    {
                        UnlinkNode(b, n, _noKey);
                        TryReduceLevel();
                        FindPredecessor(k); // clean index
                        AddCount(-1L);
                        return new IntEntry(k, v);
                    }
                }
            }
        }
        outer:
        return null;
    }

    /* ---------------- Relational operations -------------- */

    // Control values OR'ed as arguments to findNear

    private const int EQ = 1;
    private const int LT = 2;
    private const int GT = 0; // Actually checked as !LT

    /**
     * Variant of findNear returning IntEntry
     * @param key the key
     * @param rel the relation -- OR'ed combination of EQ, LT, GT
     * @return Entry fitting relation, or null if no such
     */
    internal IntEntry FindNearEntry(int key, int rel)
    {
        for (;;)
        {
            Node n; V v;
            if ((n = FindNear(key, rel)) == null)
            {
                return null;
            }
            if ((v = n.val) != null)
            {
                return new IntEntry(n.key, v);
            }
        }
    }

    /**
     * Utility for ceiling, floor, lower, higher methods.
     * @param key the key
     * @param rel the relation -- OR'ed combination of EQ, LT, GT
     * @return nearest node fitting relation, or null if no such
     */
    internal Node FindNear(int key, int rel)
    {
        if (key == _noKey)
        {
            throw new ArgumentException();
        }
        Node result;
        for (Node b; ;)
        {
            if ((b = FindPredecessor(key)) == null)
            {
                result = null;
                break;                   // empty
            }
            for (;;)
            {
                Node n; int k; int c;
                if ((n = b.next) == null)
                {
                    result = (rel & LT) != 0 && b.key != _noKey ? b : null;
                    goto outer;
                }
                else if ((k = n.key) == _noKey)
                {
                    break;
                }
                else if (n.val == null)
                {
                    UnlinkNode(b, n, _noKey);
                }
                else if (((c = Cpr(key, k)) == 0 && (rel & EQ) != 0) ||
                         (c < 0 && (rel & LT) == 0))
                {
                    result = n;
                    goto outer;
                }
                else if (c <= 0 && (rel & LT) != 0)
                {
                    result = b.key != _noKey ? b : null;
                    goto outer;
                }
                else
                {
                    b = n;
                }
            }
        }
        outer:
        return result;
    }

    /* ---------------- Constructors -------------- */

    /**
     * Constructs a new, empty map, sorted according to the
     * {@linkplain Comparable natural ordering} of the keys.
     * @param noKey The value to use as a sentinel for signaling the absence of a key.
     */
    public ConcurrentSkipListIntObjMultimap(int noKey)
    {
        _noKey = noKey;
        _adder = 0L; // CLR: new LongAdder() starts with a zero sum.
    }

    /* ------ Map API methods ------ */

    /**
     * Returns {@code true} if this map contains a mapping for the specified
     * key.
     *
     * @param key key whose presence in this map is to be tested
     * @return {@code true} if this map contains a mapping for the specified key
     * @throws ClassCastException if the specified key cannot be compared
     *         with the keys currently in the map
     * @throws NullPointerException if the specified key is null
     */
    public bool ContainsKey(int key)
    {
        return DoGet(key) != null;
    }

    /**
     * Returns the value to which the specified key is mapped,
     * or {@code null} if this map contains no mapping for the key.
     *
     * <p>More formally, if this map contains a mapping from a key
     * {@code k} to a value {@code v} such that {@code key} compares
     * equal to {@code k} according to the map's ordering, then this
     * method returns {@code v}; otherwise it returns {@code null}.
     * (There can be at most one such mapping.)
     *
     * @throws ClassCastException if the specified key cannot be compared
     *         with the keys currently in the map
     * @throws NullPointerException if the specified key is null
     */
    public V Get(int key)
    {
        return DoGet(key);
    }

    /**
     * Returns the value to which the specified key is mapped,
     * or the given defaultValue if this map contains no mapping for the key.
     *
     * @param key the key
     * @param defaultValue the value to return if this map contains
     * no mapping for the given key
     * @return the mapping for the key, if present; else the defaultValue
     * @throws NullPointerException if the specified key is null
     * @since 1.8
     */
    public V GetOrDefault(int key, V defaultValue)
    {
        V v;
        return (v = DoGet(key)) == null ? defaultValue : v;
    }

    /**
     * Associates the specified value with the specified key in this map.
     * If the map previously contained a mapping for the key, the old
     * value is replaced.
     *
     * @param key key with which the specified value is to be associated
     * @param value value to be associated with the specified key
     * @throws ClassCastException if the specified key cannot be compared
     *         with the keys currently in the map
     * @throws NullPointerException if the specified key or value is null
     */
    public void Put(int key, V value)
    {
        ArgumentNullException.ThrowIfNull(value);
        DoPut(key, value, false);
    }

    /**
     * Removes the mapping for the specified key from this map if present.
     *
     * @param  key key for which mapping should be removed
     * @return the previous value associated with the specified key, or
     *         {@code null} if there was no mapping for the key
     * @throws ClassCastException if the specified key cannot be compared
     *         with the keys currently in the map
     * @throws NullPointerException if the specified key is null
     */
    public V Remove(int key)
    {
        return DoRemove(key, null);
    }

    /**
     * Returns {@code true} if this map maps one or more keys to the
     * specified value.  This operation requires time linear in the
     * map size. Additionally, it is possible for the map to change
     * during execution of this method, in which case the returned
     * result may be inaccurate.
     *
     * @param value value whose presence in this map is to be tested
     * @return {@code true} if a mapping to {@code value} exists;
     *         {@code false} otherwise
     * @throws NullPointerException if the specified value is null
     */
    public bool ContainsValue(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Node b, n;
        V v;
        if ((b = BaseHead()) != null)
        {
            while ((n = b.next) != null)
            {
                if ((v = n.val) != null && value.Equals(v))
                {
                    return true;
                }
                else
                {
                    b = n;
                }
            }
        }
        return false;
    }

    /**
     * Get the approximate size of the collection.
     */
    public int Size()
    {
        long c;
        return BaseHead() == null ? 0 :
                (c = GetAdderCount()) >= int.MaxValue ?
                int.MaxValue : (int) c;
    }

    /**
     * Check if the collection is empty.
     */
    public bool IsEmpty()
    {
        return FindFirst() == null;
    }

    /**
     * Removes all of the mappings from this map.
     */
    public void Clear()
    {
        Index h, r, d;
        Node b;
        AcquireFence();
        while ((h = _head) != null)
        {
            if ((r = h.right) != null)        // remove indices
            {
                Interlocked.CompareExchange(ref h.right, null, r);
            }
            else if ((d = h.down) != null)    // remove levels
            {
                Interlocked.CompareExchange(ref _head, d, h);
            }
            else
            {
                long count = 0L;
                if ((b = h.node) != null)      // remove nodes
                {
                    Node n;
                    V v;
                    while ((n = b.next) != null)
                    {
                        if ((v = n.val) != null &&
                            Interlocked.CompareExchange(ref n.val, null, v) == v)
                        {
                            --count;
                            v = null;
                        }
                        if (v == null)
                        {
                            UnlinkNode(b, n, _noKey);
                        }
                    }
                }
                if (count != 0L)
                {
                    AddCount(count);
                }
                else
                {
                    break;
                }
            }
        }
    }

    /* ------ ConcurrentMap API methods ------ */

    /**
     * Remove the specific entry with the given key and value, if it exist.
     *
     * @throws ClassCastException if the specified key cannot be compared
     *         with the keys currently in the map
     * @throws NullPointerException if the specified key is null
     */
    public bool Remove(int key, object value)
    {
        if (key == _noKey)
        {
            throw new ArgumentException();
        }
        return value != null && DoRemove(key, value) != null;
    }

    /**
     * Replace the specific entry with the given key and value, with the given replacement value,
     * if such an entry exist.
     *
     * @throws ClassCastException if the specified key cannot be compared
     *         with the keys currently in the map
     * @throws NullPointerException if any of the arguments are null
     */
    public bool Replace(int key, V oldValue, V newValue)
    {
        if (key == _noKey)
        {
            throw new ArgumentException();
        }
        ArgumentNullException.ThrowIfNull(oldValue);
        ArgumentNullException.ThrowIfNull(newValue);
        for (;;)
        {
            Node n; V v;
            if ((n = FindNode(key)) == null)
            {
                return false;
            }
            if ((v = n.val) != null)
            {
                if (!oldValue.Equals(v))
                {
                    return false;
                }
                if (Interlocked.CompareExchange(ref n.val, newValue, v) == v)
                {
                    return true;
                }
            }
        }
    }

    /* ------ SortedMap API methods ------ */

    public int FirstKey()
    {
        Node n = FindFirst();
        if (n == null)
        {
            return _noKey;
        }
        return n.key;
    }

    public int LastKey()
    {
        Node n = FindLast();
        if (n == null)
        {
            return _noKey;
        }
        return n.key;
    }

    /* ---------------- Relational operations -------------- */

    /**
     * Returns a key-value mapping associated with the greatest key
     * strictly less than the given key, or {@code null} if there is
     * no such key. The returned entry does <em>not</em> support the
     * {@code Entry.setValue} method.
     *
     * @throws NullPointerException if the specified key is null
     */
    public IntEntry LowerEntry(int key)
    {
        return FindNearEntry(key, LT);
    }

    /**
     * @throws NullPointerException if the specified key is null
     */
    public int LowerKey(int key)
    {
        Node n = FindNear(key, LT);
        return n == null ? _noKey : n.key;
    }

    /**
     * Returns a key-value mapping associated with the greatest key
     * less than or equal to the given key, or {@code null} if there
     * is no such key. The returned entry does <em>not</em> support
     * the {@code Entry.setValue} method.
     *
     * @param key the key
     * @throws NullPointerException if the specified key is null
     */
    public IntEntry FloorEntry(int key)
    {
        return FindNearEntry(key, LT | EQ);
    }

    /**
     * @param key the key
     * @throws NullPointerException if the specified key is null
     */
    public int FloorKey(int key)
    {
        Node n = FindNear(key, LT | EQ);
        return n == null ? _noKey : n.key;
    }

    /**
     * Returns a key-value mapping associated with the least key
     * greater than or equal to the given key, or {@code null} if
     * there is no such entry. The returned entry does <em>not</em>
     * support the {@code Entry.setValue} method.
     *
     * @throws NullPointerException if the specified key is null
     */
    public IntEntry CeilingEntry(int key)
    {
        return FindNearEntry(key, GT | EQ);
    }

    /**
     * @throws NullPointerException if the specified key is null
     */
    public int CeilingKey(int key)
    {
        Node n = FindNear(key, GT | EQ);
        return n == null ? _noKey : n.key;
    }

    /**
     * Returns a key-value mapping associated with the least key
     * strictly greater than the given key, or {@code null} if there
     * is no such key. The returned entry does <em>not</em> support
     * the {@code Entry.setValue} method.
     *
     * @param key the key
     * @throws NullPointerException if the specified key is null
     */
    public IntEntry HigherEntry(int key)
    {
        return FindNearEntry(key, GT);
    }

    /**
     * @param key the key
     * @throws NullPointerException if the specified key is null
     */
    public int HigherKey(int key)
    {
        Node n = FindNear(key, GT);
        return n == null ? _noKey : n.key;
    }

    /**
     * Returns a key-value mapping associated with the least
     * key in this map, or {@code null} if the map is empty.
     * The returned entry does <em>not</em> support
     * the {@code Entry.setValue} method.
     */
    public IntEntry FirstEntry()
    {
        return FindFirstEntry();
    }

    /**
     * Returns a key-value mapping associated with the greatest
     * key in this map, or {@code null} if the map is empty.
     * The returned entry does <em>not</em> support
     * the {@code Entry.setValue} method.
     */
    public IntEntry LastEntry()
    {
        return FindLastEntry();
    }

    /**
     * Removes and returns a key-value mapping associated with
     * the least key in this map, or {@code null} if the map is empty.
     * The returned entry does <em>not</em> support
     * the {@code Entry.setValue} method.
     */
    public IntEntry PollFirstEntry()
    {
        return DoRemoveFirstEntry();
    }

    /**
     * Removes and returns a key-value mapping associated with
     * the greatest key in this map, or {@code null} if the map is empty.
     * The returned entry does <em>not</em> support
     * the {@code Entry.setValue} method.
     */
    public IntEntry PollLastEntry()
    {
        return DoRemoveLastEntry();
    }

    public IntEntry PollCeilingEntry(int key)
    {
        // TODO optimize this
        Node node;
        V val;
        do
        {
            node = FindNear(key, GT | EQ);
            if (node == null)
            {
                return null;
            }
            val = node.val;
        } while (val == null || !Remove(node.key, val));
        return new IntEntry(node.key, val);
    }

    /* ---------------- Iterators -------------- */

    /**
     * Base of iterator classes
     */
    // CLR: java.util.Iterator<T> has no BCL counterpart with remove(), so Iter keeps the Java hasNext()/next()/remove()
    // shape (returned by Iterator()) and also implements IEnumerator<T> for GetEnumerator()/foreach. Java's inner
    // class reaches ConcurrentSkipListIntObjMultimap.this implicitly; the C# nested class receives the map instead.
    public abstract class Iter<T> : IEnumerator<T>
    {
        /** the last node returned by next() */
        internal Node lastReturned;
        /** the next node to return from next(); */
        internal Node next;
        /** Cache of next value field to maintain weak consistency */
        internal V nextValue;
        private readonly ConcurrentSkipListIntObjMultimap<V> _map;
        private T _current;

        /** Initializes ascending iterator for entire range. */
        internal Iter(ConcurrentSkipListIntObjMultimap<V> map)
        {
            _map = map;
            Advance(map.BaseHead());
        }

        public bool HasNext()
        {
            return next != null;
        }

        // CLR: java.util.Iterator.next(), declared abstract here because C# has no Java-shaped iterator interface.
        public abstract T Next();

        /** Advances next to higher entry. */
        internal void Advance(Node b)
        {
            Node n = null;
            V v = null;
            if ((lastReturned = b) != null)
            {
                while ((n = b.next) != null && (v = n.val) == null)
                {
                    b = n;
                }
            }
            nextValue = v;
            next = n;
        }

        public void Remove()
        {
            Node n;
            int k;
            if ((n = lastReturned) == null || (k = n.key) == _map._noKey)
            {
                throw new InvalidOperationException();
            }
            // It would not be worth all of the overhead to directly
            // unlink from here. Using remove is fast enough.
            _map.Remove(k, n.val); // TODO: inline and optimize this
            lastReturned = null;
        }

        // CLR: IEnumerator<T> over the same hasNext()/next() traversal (MoveNext = hasNext() then next()).
        public T Current => _current;

        object IEnumerator.Current => _current;

        public bool MoveNext()
        {
            if (!HasNext())
            {
                return false;
            }
            _current = Next();
            return true;
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }
    }

    internal sealed class EntryIterator : Iter<IntEntry>
    {
        internal EntryIterator(ConcurrentSkipListIntObjMultimap<V> map) : base(map)
        {
        }

        public override IntEntry Next()
        {
            Node n;
            if ((n = next) == null)
            {
                // CLR: NoSuchElementException maps to InvalidOperationException, the BCL error for reading past
                // the end of a sequence.
                throw new InvalidOperationException();
            }
            int k = n.key;
            V v = nextValue;
            Advance(n);
            return new IntEntry(k, v);
        }
    }

    public Iter<IntEntry> Iterator()
    {
        return new EntryIterator(this);
    }

    // CLR: Iterable.iterator() is IEnumerable<T>.GetEnumerator(); both return the same EntryIterator.
    public IEnumerator<IntEntry> GetEnumerator()
    {
        return Iterator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    // default Map method overrides

    public void ForEach(Action<int, V> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Node b, n;
        V v;
        if ((b = BaseHead()) != null)
        {
            while ((n = b.next) != null)
            {
                if ((v = n.val) != null)
                {
                    action(n.key, v);
                }
                b = n;
            }
        }
    }

    public void ReplaceAll(Func<int, V, V> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        Node b, n;
        V v;
        if ((b = BaseHead()) != null)
        {
            while ((n = b.next) != null)
            {
                while ((v = n.val) != null)
                {
                    V r = function(n.key, v);
                    ArgumentNullException.ThrowIfNull(r);
                    if (Interlocked.CompareExchange(ref n.val, r, v) == v)
                    {
                        break;
                    }
                }
                b = n;
            }
        }
    }

    // VarHandle mechanics
    // CLR: the JVM-only static initializer is not ported: it looked up VarHandle.acquireFence (or the
    // acquireFenceFallback method) as the MethodHandle ACQUIRE_FENCE and created the AtomicReferenceFieldUpdater
    // instances HEAD, NEXT, VAL and RIGHT through the cls(Class) cast helper and reflection. The CLR CASes the fields
    // directly with Interlocked.CompareExchange and calls Volatile.ReadBarrier() in place of
    // ACQUIRE_FENCE.invokeExact(), so neither the updaters, the reflection nor the
    // ExceptionInInitializerError/LinkageError paths have a counterpart.

    /**
     * Orders LOADS before the fence, with LOADS and STORES after the fence.
     */
    private static void AcquireFence()
    {
        // CLR: Volatile.ReadBarrier() (.NET 10) keeps reads before it from executing after any memory access that
        // follows it, which is the VarHandle.acquireFence() contract.
        Volatile.ReadBarrier();
    }

    // CLR: acquireFenceFallback() and its static volatile int acquireFenceVariable are not ported; that fallback only
    // served JVMs without VarHandle, and Volatile.ReadBarrier() is always available here. Its body comments were:
    // Volatile store prevent prior loads from ordering down.
    // Volatile load prevent following loads and stores from ordering up.
    // Note: Putting the volatile store before the volatile load ensures
    // surrounding loads and stores don't order "into" the fence.
}
