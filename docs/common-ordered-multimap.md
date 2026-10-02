# Ordered multi-map contracts for the CLR chunk cache

Baseline: `e66ce34777f9c4a0c57ac74bb97396ca2f54b43c` in the local netty repository.
Native implementation: ConcurrentOrderedMultiMap<T>. Original source inventory
entry remains **in progress**, while its original test scenarios are translated.

## Consumer evidence and native design

`buffer/.../AdaptivePoolingAllocator.java`, ConcurrentSkipListChunkCache, stores
multiple chunks with the same remaining capacity. Its fast path must atomically
claim a chunk at the smallest available key at least the requested size.
Its fallback/eviction paths enumerate candidates and process or deallocate a
chunk only after successful conditional removal. Freelist processing can change
the chunk's size key, so reinsertion must use its updated remaining capacity.

Neither Dictionary nor ConcurrentDictionary provides ordering, duplicate keys
and atomic ceiling extraction. A SortedList<int,List<Entry>> supplies standard
CLR ordering/storage; a private lock supplies the required compound operations.
Integer binary search determines lower/floor/ceiling/higher candidates without
overflowing subtraction. A private insertion identity protects conditional
removal after evaluating user-defined equality outside the lock. No chunk
processing, deallocation or user-defined equality executes under the storage lock.

The public surface uses Add, Count, IsEmpty, Clear, Snapshot and typed Try methods
with KeyValuePair<int,T>. It does not expose Java IntEntry, Iterator.remove,
noKey sentinels, JDK AtomicReferenceFieldUpdater or skip-list node/index types.
All integer keys and non-null CLR values, including value types/default values,
are supported. Absence is the Try result, rather than null/default(T) or -1.

CLR differences are explicit:

- This implementation uses locking. It does not preserve or claim the original
  nonblocking algorithm or equivalent throughput/tail latency. SortedList bucket
  insertion/removal shifts keys, bucket removal shifts equal-key entries, and
  snapshot/equality removal allocate arrays. Measure the allocator workload
  before optimizing or claiming equivalent cost.
- Count is exact at the instant its lock is held; the Java size is approximate.
  Neither Count nor IsEmpty can safely replace an atomic TryTake operation.
- Same-key values are selected in insertion order. The original tests allow
  either duplicate value; the chunk consumer has no stronger same-key ordering.
- Snapshot is sorted and stable at capture. Original iteration is weakly
  consistent. New insertions may require a later cache attempt, as concurrent
  candidate scans cannot promise every insertion will be visited. Snapshot grants
  no pooled-value ownership. TryRemove/TryTake success is the claim boundary.
- Equality uses EqualityComparer<T>.Default. Original remove uses value.equals;
  Chunk uses reference identity and string tests use value equality. CLR types
  implementing IEquatable follow that native contract. Equality may throw/reenter;
  insertion identities are revalidated after comparison before removal.
- Null insertion is rejected. Null conditional removal returns false. The old
  constructor sentinel has no native counterpart, so -1 is an ordinary valid key.
- Clear removes references; it does not dispose values. The allocator must claim
  and deallocate its chunks explicitly, exactly as its original free path does.

## Original test migration

All 20 original test methods have scenario counterparts. Combined theories keep
both parameter variants. Each of the five @RepeatedTest(100) scenarios retains
100 repetitions, 50 inserted values and, for neighbors, ten queries per repetition.
A fixed CLR generator replaces ThreadLocalRandom without reducing those counts.
The original comments and license are preserved in the native test file.

| Original methods | C# counterpart |
| --- | --- |
| addIterateAndRemoveEntries | Sorted Snapshot plus successful TryRemove per entry; empty/count checks retained. |
| clearMustRemoveAllEntries | Clear removes all entries, snapshot and count become empty. |
| pollingFirstEntryOfUniqueKeys, pollingLastEntryOfUniqueKeys, pollingFirstEntryOfMultiMappedKeys, pollingLastEntryOfMultiMappedKeys | Four theory cases retain first/last order and duplicate multiplicity. |
| addMultipleEntriesForSameKey | Distinct duplicate values, ordered surrounding keys and removal of every occurrence. |
| iteratorRemoveSecondOfMultiMappedEntry | Both prior-removal variants use conditional removal; the already-removed case returns false and leaves the other entry. |
| firstKeyOrEntry, lastKeyOrEntry | Two theory cases retain incremental insertions, duplicate choices, endpoint removal and empty results. |
| firstLastKeyOrEntry | Hundred randomized repetitions retain first/last keys and values. |
| lowerEntryOrKey, floorEntryOrKey, ceilEntryOrKey, higherEntryOrKey | Four theory cases compare all repeated queries to an independently sorted key array. Key is obtained from the returned standard pair. |
| lowerEntryOrKeyMismatch, floorEntryOrKeyMismatch, ceilEntryOrKeyMismatch, higherEntryOrKeyMismatch | Both duplicate variants retain every strict/inclusive boundary assertion in combined cases. |
| pollCeilingEntry | Repeated atomic extraction retains both duplicates and leaves the smaller key untouched. |

Nineteen translated cases plus ten CLR cases cover integer extremes/default
values, stable snapshots, FIFO ties, nulls, reentrant/throwing equality, concurrent
publication/claim, competing stale snapshot removers, publication across empty
transitions and the actual AdaptivePoolingAllocator dirty-chunk fallback algorithm.
The initial targeted Debug selection passes 29 cases. Both final default full
runs execute all 29, including the strengthened gated mixed-publication case:
**1161 passed / 0 failed / 14 skipped** (1175 total) on Windows/net10.0.
Evidence: ordered-multimap-full-debug.trx and ordered-multimap-full-release.trx.
Latest builds emit 338 test warnings / 0 errors with the library up to date;
earlier heap-memory builds emitted 415 library/test warnings. No warnings are
claimed resolved. No opt-in batch substitutes for these full runs.

## Remaining source review

The original file also declares get/getOrDefault, contains operations, remove(key),
replace, forEach and replaceAll. They have no pinned production caller beyond the
class itself; their broader purpose/API decisions are still pending. Do not add
those Java-shaped methods just to make the original type exist, and do not mark
the entire source verified because its current consumers/tests work.

Real buffer integration and workload measurements remain future module work.
This common-stage consumer test uses a small model of the original dirty-chunk
algorithm; it does not claim the buffer allocator itself has been ported.

## Original comment provenance

The pinned skip-list/JDK node, index, fence and iterator explanations below are
preserved as provenance for the replaced implementation. They do not describe
the CLR SortedList/lock algorithm. Unreviewed public-method documentation remains
provenance, not a claim those APIs have been implemented.

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1

```java
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
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 16

```java
/*
 * Written by Doug Lea with assistance from members of JCP JSR-166
 * Expert Group and released to the public domain, as explained at
 * https://creativecommons.org/publicdomain/zero/1.0/
 *
 * With substantial modifications by The Netty Project team.
 */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 42

```java
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
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 86

```java
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
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 301

```java
/** No-key sentinel value */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 303

```java
/** Lazily initialized topmost index of the skiplist. */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 304

```java
/*XXX: Volatile only required for ARFU; remove if we can use VarHandle*/
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 305

```java
/** Element count */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 308

```java
/**
     * Nodes hold keys and values, and are singly linked in sorted
     * order, possibly with some intervening marker nodes. The list is
     * headed by a header node accessible as head.node. Headers and
     * marker nodes have null keys. The val field (but currently not
     * the key field) is nulled out upon deletion.
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 316

```java
// currently, never detached
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 317

```java
/*XXX: Volatile only required for ARFU; remove if we can use VarHandle*/
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 318

```java
/*XXX: Volatile only required for ARFU; remove if we can use VarHandle*/
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 326

```java
/**
     * Index nodes represent the levels of the skip list.
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 330

```java
// currently, never detached
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 332

```java
/*XXX: Volatile only required for ARFU; remove if we can use VarHandle*/
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 340

```java
/**
     * The multimap entry type with primitive {@code int} keys.
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 352

```java
/**
         * Get the corresponding key.
         */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 359

```java
/**
         * Get the corresponding value.
         */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 394

```java
/* ----------------  Utilities -------------- */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 396

```java
/**
     * Compares using comparator or natural ordering if null.
     * Called only by methods that have performed required type checks.
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 404

```java
/**
     * Returns the header for base node list, or null if uninitialized
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 413

```java
/**
     * Tries to unlink deleted node n from predecessor b (if both
     * exist), by first splicing in a marker if not already present.
     * Upon return, node n is sure to be unlinked from b, possibly
     * via the actions of some other thread.
     *
     * @param b if nonnull, predecessor
     * @param n if nonnull, node known to be deleted
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 427

```java
// already marked
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 431

```java
// add marker
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 439

```java
/**
     * Adds to element count, initializing adder if necessary
     *
     * @param c count to add
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 448

```java
/**
     * Returns element count, initializing adder if necessary.
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 453

```java
// ignore transient negatives
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 456

```java
/* ---------------- Traversal -------------- */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 458

```java
/**
     * Returns an index node with key strictly less than given key.
     * Also unlinks indexes to deleted nodes found along the way.
     * Callers rely on this side-effect of clearing indices to deleted
     * nodes.
     *
     * @param key if nonnull the key
     * @return a predecessor node of key, or null if uninitialized or null key
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 477

```java
// unlink index to deleted node
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 494

```java
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
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 510

```java
// don't postpone errors
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 517

```java
// empty
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 519

```java
// b is deleted
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 521

```java
// n is deleted
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 534

```java
/**
     * Gets value for key. Same idea as findNode, except skips over
     * deletions and markers, and returns first encountered value to
     * avoid possibly inconsistent rereads.
     *
     * @param key the key
     * @return the value, or null if absent
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 591

```java
/* ---------------- Insertion -------------- */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 593

```java
/**
     * Main insertion method.  Adds element if not present, or
     * replaces value if present and onlyIfAbsent is false.
     *
     * @param key the key
     * @param value the value that must be associated with key
     * @param onlyIfAbsent if should not insert if already present
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 608

```java
// number of levels descended
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 609

```java
// try to initialize
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 614

```java
// count while descending
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 636

```java
// new node, if inserted
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 637

```java
// find insertion point
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 640

```java
// if empty, type check key now TODO: remove?
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 645

```java
// can't append; restart
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 650

```java
// Multimap
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 651

```java
//                    } else if (c == 0 &&
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 652

```java
//                             (onlyIfAbsent || VAL.compareAndSet(n, v, value))) {
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 653

```java
//                        return v;
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 666

```java
// add indices with 1/4 prob
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 669

```java
// levels to descend before add
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 671

```java
// create at most 62 indices
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 680

```java
// try to add new level
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 685

```java
// deleted while adding indices
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 686

```java
// clean
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 696

```java
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
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 710

```java
// hoist checks
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 712

```java
// find splice point
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 723

```java
// stale
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 741

```java
// re-find splice point
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 750

```java
/* ---------------- Deletion -------------- */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 752

```java
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
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 783

```java
//                    break outer;
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 784

```java
// Multimap.
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 788

```java
// loop to clean up
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 799

```java
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
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 825

```java
// recheck
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 826

```java
// try to backout
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 830

```java
/* ---------------- Finding and removing first element -------------- */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 832

```java
/**
     * Gets first valid node, unlinking deleted nodes if encountered.
     * @return first node or null if empty
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 850

```java
/**
     * Entry snapshot version of findFirst
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 867

```java
/**
     * Removes first entry; returns its snapshot.
     * @return null if empty, else snapshot of first entry
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 880

```java
// clean index
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 890

```java
/* ---------------- Finding and removing last element -------------- */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 892

```java
/**
     * Specialized version of find to get last valid node.
     * @return last node or null if empty
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 923

```java
// empty
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 941

```java
/**
     * Entry version of findLast
     * @return Entry for last node or null if empty
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 957

```java
/**
     * Removes last entry; returns its snapshot.
     * Specialized variant of doRemove.
     * @return null if empty, else snapshot of last entry
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 975

```java
// continue only if a successor
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 991

```java
// empty
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 994

```java
// retry
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1005

```java
// clean index
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1015

```java
/* ---------------- Relational operations -------------- */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1017

```java
// Control values OR'ed as arguments to findNear
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1021

```java
// Actually checked as !LT
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1023

```java
/**
     * Variant of findNear returning IntEntry
     * @param key the key
     * @param rel the relation -- OR'ed combination of EQ, LT, GT
     * @return Entry fitting relation, or null if no such
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1041

```java
/**
     * Utility for ceiling, floor, lower, higher methods.
     * @param key the key
     * @param rel the relation -- OR'ed combination of EQ, LT, GT
     * @return nearest node fitting relation, or null if no such
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1055

```java
// empty
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1081

```java
/* ---------------- Constructors -------------- */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1083

```java
/**
     * Constructs a new, empty map, sorted according to the
     * {@linkplain Comparable natural ordering} of the keys.
     * @param noKey The value to use as a sentinel for signaling the absence of a key.
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1093

```java
/* ------ Map API methods ------ */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1095

```java
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
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1109

```java
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
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1127

```java
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
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1143

```java
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
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1159

```java
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
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1173

```java
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
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1200

```java
/**
     * Get the approximate size of the collection.
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1210

```java
/**
     * Check if the collection is empty.
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1217

```java
/**
     * Removes all of the mappings from this map.
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1224

```java
// remove indices
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1226

```java
// remove levels
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1230

```java
// remove nodes
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1252

```java
/* ------ ConcurrentMap API methods ------ */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1254

```java
/**
     * Remove the specific entry with the given key and value, if it exist.
     *
     * @throws ClassCastException if the specified key cannot be compared
     *         with the keys currently in the map
     * @throws NullPointerException if the specified key is null
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1268

```java
/**
     * Replace the specific entry with the given key and value, with the given replacement value,
     * if such an entry exist.
     *
     * @throws ClassCastException if the specified key cannot be compared
     *         with the keys currently in the map
     * @throws NullPointerException if any of the arguments are null
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1298

```java
/* ------ SortedMap API methods ------ */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1316

```java
/* ---------------- Relational operations -------------- */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1318

```java
/**
     * Returns a key-value mapping associated with the greatest key
     * strictly less than the given key, or {@code null} if there is
     * no such key. The returned entry does <em>not</em> support the
     * {@code Entry.setValue} method.
     *
     * @throws NullPointerException if the specified key is null
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1330

```java
/**
     * @throws NullPointerException if the specified key is null
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1338

```java
/**
     * Returns a key-value mapping associated with the greatest key
     * less than or equal to the given key, or {@code null} if there
     * is no such key. The returned entry does <em>not</em> support
     * the {@code Entry.setValue} method.
     *
     * @param key the key
     * @throws NullPointerException if the specified key is null
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1351

```java
/**
     * @param key the key
     * @throws NullPointerException if the specified key is null
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1360

```java
/**
     * Returns a key-value mapping associated with the least key
     * greater than or equal to the given key, or {@code null} if
     * there is no such entry. The returned entry does <em>not</em>
     * support the {@code Entry.setValue} method.
     *
     * @throws NullPointerException if the specified key is null
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1372

```java
/**
     * @throws NullPointerException if the specified key is null
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1380

```java
/**
     * Returns a key-value mapping associated with the least key
     * strictly greater than the given key, or {@code null} if there
     * is no such key. The returned entry does <em>not</em> support
     * the {@code Entry.setValue} method.
     *
     * @param key the key
     * @throws NullPointerException if the specified key is null
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1393

```java
/**
     * @param key the key
     * @throws NullPointerException if the specified key is null
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1402

```java
/**
     * Returns a key-value mapping associated with the least
     * key in this map, or {@code null} if the map is empty.
     * The returned entry does <em>not</em> support
     * the {@code Entry.setValue} method.
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1412

```java
/**
     * Returns a key-value mapping associated with the greatest
     * key in this map, or {@code null} if the map is empty.
     * The returned entry does <em>not</em> support
     * the {@code Entry.setValue} method.
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1422

```java
/**
     * Removes and returns a key-value mapping associated with
     * the least key in this map, or {@code null} if the map is empty.
     * The returned entry does <em>not</em> support
     * the {@code Entry.setValue} method.
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1432

```java
/**
     * Removes and returns a key-value mapping associated with
     * the greatest key in this map, or {@code null} if the map is empty.
     * The returned entry does <em>not</em> support
     * the {@code Entry.setValue} method.
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1443

```java
// TODO optimize this
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1456

```java
/* ---------------- Iterators -------------- */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1458

```java
/**
     * Base of iterator classes
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1462

```java
/** the last node returned by next() */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1464

```java
/** the next node to return from next(); */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1466

```java
/** Cache of next value field to maintain weak consistency */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1469

```java
/** Initializes ascending iterator for entire range. */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1479

```java
/** Advances next to higher entry. */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1498

```java
// It would not be worth all of the overhead to directly
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1499

```java
// unlink from here. Using remove is fast enough.
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1500

```java
// TODO: inline and optimize this
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1524

```java
// default Map method overrides
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1556

```java
// VarHandle mechanics
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1598

```java
/**
     * Orders LOADS before the fence, with LOADS and STORES after the fence.
     */
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1612

```java
// Volatile store prevent prior loads from ordering down.
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1614

```java
// Volatile load prevent following loads and stores from ordering up.
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1616

```java
// Note: Putting the volatile store before the volatile load ensures
```

Source: common/src/main/java/io/netty/util/concurrent/ConcurrentSkipListIntObjMultimap.java, line 1617

```java
// surrounding loads and stores don't order "into" the fence.
```
