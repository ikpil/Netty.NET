# Netty buffer port

Baseline: `64cc10f38ea5f5bd7eae48507817c66680d0afdc` in `../netty`.
Scope: buffer implementation/tests and common changes required by real buffer consumers.
The common backlog stays open; unrelated common API polishing is not a buffer prerequisite.

`buffer-porting-manifest.json` inventories every original Java source/test file.
Regenerate with `pwsh tools/Update-BufferPortingManifest.ps1`; existing decisions
are retained. Partial source/API and test coverage is explicitly in-progress.
No throwing placeholder API or artificial skipped test is used to represent
unported work. Use the existing common design records for actual shared decisions.

## First implementation unit: heap buffers and shared views

Add net10.0 Buffer and Buffer.Tests projects to the default solution.
ByteBuf combines the original ByteBuf/AbstractByteBuf index and data contracts;
AbstractReferenceCountedByteBuf reuses common's atomic ref-int counter. Heap
storage uses managed arrays. Primitive wire access uses BinaryPrimitives,
explicit BE/LE methods, 24-bit signed extension and bit-preserving float/double
conversion. Bulk copies use overlapping-safe spans. These CLR choices replace
NIO/Unsafe strategy dispatch; no separate Java provider hierarchy is added.

Get/set operations address capacity without moving indices. Sequential reads
require written bytes; writes grow with the original 64-byte/power-of-two and
4-MiB step policy. Clear changes indices only. Discard compacts readable bytes
and adjusts marks. Capacity changes preserve the prefix and trim indices;
allocation/copy succeeds before the replacement is published.

Slice and Duplicate have independent indices and shared storage/reference count.
Only retained variants acquire another lifetime claim. The private ByteBufView
uses fixed-capacity slices or a duplicate's current parent capacity. It checks
each actual parent range, including valid slice prefixes after parent shrinking.
Release delegates to the common counter and deallocates exactly once. GC and
IDisposable do not substitute for this shared ownership contract.

CLR bytes are unsigned octets; unsigned 16-/32-bit reads return ushort/uint.
Explicit integer setters truncate to wire width. Index/capacity bounds use
ArgumentOutOfRangeException; common ownership validation retains ArgumentException
and IllegalReferenceCountException. Memory/Span are borrowed views, not retain
tokens. Exclude final release/reallocation throughout borrowed access. Saved heap
memory describes the original array after replacement, rather than retargeting.

Original class and implemented-operation documentation is kept beside the native
implementation. AbstractReferenceCountedByteBuf preserves all its original
comments. Comments for still-unported operations remain upstream, with the source
entry in-progress; this checkpoint does not claim full ByteBuf API/comment coverage.

Tests translate all six original AbstractReferenceCountedByteBufTest scenarios
(the two resurrection variants share a theory), plus selected AbstractByteBufTest
index/capacity/discard/copy/derived-view scenarios. CLR tests cover common-interface
ownership, 50 competing releases, overlap, unsigned octets, integer overflow and
floating bit patterns. Ten word/endianness cases each retain the original 100
consistency iterations, using an independent byte-by-byte wire oracle.

The ignored Java probe compiles the exact pinned HeapByteBufUtil and
VarHandleByteBufferAccess. A probe-only PlatformDependent selects Java scalar or
standard VarHandle views. Both Java modes and the compiled native Buffer consumer
agree on all 1000 normalized wire rows (including poisoned neighboring bytes).
This is word-kernel evidence, not execution of the whole Java ByteBuf/allocator.

Validation on Windows/net10.0: Buffer.Tests passes 52/52 in Debug and checked
Release. Default whole-solution Debug and Release each execute Buffer 52 passed/
0 failed/0 skipped and unchanged Common 2676 passed/0 failed/14 existing skips.
Combined: 2742 discovered, 2728 passed, zero failed, 14 existing skips.
All 159 inventory paths match pinned Git objects (93 sources, 66 tests).
The full reference-count source's eight comments and original fixture's two
comments are preserved. Other source/test entries explicitly retain partial
coverage and pending methods. Evidence: buffer-foundation-*.trx in each test
project's ignored TestResults, and artifacts/buffer-foundation-validation.
The new projects add no source exclusion or synthetic skip. Common source and
its test configuration are unchanged. Existing common warnings remain.

## Native storage and the shared empty buffer

Unpooled.DirectBuffer and UnpooledDirectByteBuf connect actual Buffer operations
to common NativeMemoryAllocator/Owner. Allocation is explicitly native and zeroed;
a constructor can accept an independent reservation domain. Capacity replacement
allocates/copies before publication, preserves the prefix, trims indices and frees
the previous owner. Failure leaves the original memory and indices usable. The
budget covers the transient old/new allocations; no in-place realloc cost or
allocator performance equivalence is claimed. Copy preserves native storage and
has independent lifetime.

Retained ByteBuf views preserve logical ownership; MemoryHandle pins preserve
physical allocation. Final Release rejects new buffer access and invalidates new
native-memory access, while outstanding pins keep their original bytes and quota
alive until disposed. A saved Memory is invalid after native owner replacement;
an acquired pin remains bound to the previous allocation. This differs from a
managed array view, which GC can keep alive after replacement.

Growing WriteBytes(ReadOnlySpan<byte>) pins its current native allocation across
replacement. That covers a source span aliasing the old storage, including writes
through a duplicate. Otherwise disposing the previous owner could invalidate the
source span before copying. This protects self-aliasing growth; it does not acquire
ownership of an arbitrary external span or make data access thread-safe.

Unpooled.EmptyBuffer restores Netty's shared zero-capacity sentinel for empty
factories/reads/views. Its reference count is permanently one and Release returns
false. As in original EmptyByteBuf, ownership increments/decrements are ignored,
including nonpositive values. Capacity changes are unsupported even for zero.
Explicit native construction at zero capacity remains an owned, growable buffer
and uses common's documented one-byte physical allocation/accounting policy.

The selected heap index/capacity/compaction/derived-view scenarios now also run
against native memory. Additional cases check quota restoration, allocation failure,
independent native copies, retained ownership, old-allocation pins and aliased growth.
Buffer.Tests passes all 95 cases in Debug, Release and checked Release, with no
skips. Both CLR heap and native consumers match all 1000 pinned Java word rows.
Evidence: buffer-native-*.trx and artifacts/buffer-foundation-validation.
Focused solution Debug/Release integration runs each pass Buffer 95 and Common
101 native-memory/reference-count cases, with zero failures or skips.
The common source, tests and configuration remain unchanged from the preceding
full Debug/Release checkpoint; no repeated whole-common run is claimed here.

## Text and search

ByteBuf.GetString/ReadString/SetString/WriteString use Encoding and
ReadOnlySpan<char>; CharSequence/Charset do not gain a second CLR hierarchy.
Indexed operations leave reader/writer indices unchanged. SetString cannot grow
the buffer; WriteString reserves the exact encoded size and advances the writer
only after successful encoding. ReadString validates written/readable bytes before
decoding and advances the reader only after success. This follows ByteBuf's
documented contract, while pinned AbstractByteBuf.readString lacks that explicit
readability check. Zero-length indexed access and BytesBefore ranges retain the
CLR buffer's consistent bounds/lifetime checks instead of Java's unchecked early
returns or index+length overflow. Capacity-bounded searches can inspect unwritten
bytes; readable searches inspect the current reader/writer interval.

EmptyByteBuf validates both IndexOf endpoints, preserving the original sentinel
override rather than inheriting the owned zero-capacity search shortcut.
Native text APIs accept empty input consistently, including the sentinel; pinned
EmptyByteBuf.setCharSequence/writeCharSequence instead throw for every input.
CLR callbacks and encodings must be non-null even on an empty range.

Encoding controls fallback, endian order and replacement. No preamble is emitted:
choose UTF-16BE explicitly for Java's UTF-16 byte order and write a preamble
explicitly if the protocol requires it. Default CLR UTF-8 replaces malformed
UTF-16 with U+FFFD; strict/custom fallback is honored. Encoding.ASCII replaces
octets above 127 on decode, whereas pinned ByteBufUtil.decodeString's deprecated
ASCII fast path maps all octets as Latin-1. Encoding.Latin1 provides that mapping.
Generic text writes deliberately use exact sizing rather than Java's specialized
UTF-8 3-times-UTF-16 reservation. These are explicit Encoding API adaptations,
consistent with the common module's existing encoding decision, not a claim that
all Java charset paths produce identical bytes. Encoding failures preserve
indices; arbitrary custom Encoding implementations can still write partial data
before throwing. Borrowed memory follows the existing buffer access rules.

ByteBufUtil.WriteUtf8, Utf8Bytes/Utf8MaxBytes, ReserveAndWriteUtf8 and WriteAscii
preserve Netty's specialized wire contracts separately. UTF-8 writes reserve
3 bytes per UTF-16 unit, write '?' for lone surrogates, and preserve the original
consumption/truncation rule for a high surrogate followed by a non-low surrogate.
AsciiString input copies raw octets, including bytes above 127. As in pinned
writeUtf8, the public AsciiString write also reserves 3 times its length even
though Utf8MaxBytes(AsciiString) reports its raw length. ASCII char-span writes
preserve Latin-1 octets and replace code units above 255 with '?'. Size arithmetic
is checked; an undersized explicit reservation is rejected before writing.
Slice a char span to select text rather than add Java subsequence facades.
Native text writes pin the old allocation across growth when their UTF-16 input
span aliases that storage, including writes through a duplicate.

Directional IndexOf retains the original exclusive upper bound, lower-bound
clamping on forward search and upper-bound clamping on reverse search.
BytesBefore returns a relative distance. Func<byte,bool> replaces ByteProcessor;
false stops at the buffer-relative index and exceptions propagate unchanged.
Callbacks reacquire bytes after each invocation so releasing/resizing native
memory cannot leave a captured span targeting freed storage.
ByteBufUtil.IndexOf(needle,haystack) searches readable ranges and returns a
haystack-relative index; original null/not-found (-1) and empty-needle (0)
contracts remain. Span.IndexOf/LastIndexOf supply CLR search strategies without
recreating JVM SWAR/Two-Way internals or claiming equivalent performance.

Selected original AbstractByteBufTest and ByteBufUtilTest scenarios run on both
heap/native storage. Literal byte fixtures cover encoding order, malformed
surrogates, ASCII raw octets, strict/custom fallback, failure indices, slice
coordinates, callback order/abort/mutation, and native aliasing during growth.
Numeric UTF-16 test data avoids test discovery replacing lone surrogates.
The ignored Java probe executes exact pinned safeArrayWriteUtf8 and
utf8ByteCount/utf8BytesNonAscii methods on 5000 inputs (single units, pairs and
longer boundary-heavy sequences). Both CLR backends match every wire/count row.
Probe scaffolding supplies only compilation dependencies; this is scalar-kernel
evidence, not full Java buffer/Unsafe/allocator execution.

Validation: Buffer.Tests 153 passed, zero failed/skipped in Debug, Release and
checked Release. Focused solution Debug/Release pass 118 common native-memory,
reference-count and EncodingConstructor cases; unchanged Common's whole suite
is not rerun for this unit. Evidence: buffer-text-search-*.trx and
artifacts/buffer-text-search-validation. All 159 original paths remain inventoried;
140 pending, 17 in-progress, 2 verified. Partial APIs/fixtures stay in-progress.
Original documentation/licenses and comments for the implemented operations are
preserved; comment counts across each entire original file stay explicitly partial.
Common source, tests and configuration remain unchanged.

## Read-only derived views

ByteBuf.AsReadOnly returns a live, borrowed view without retaining its parent.
IsReadOnly/IsWritable and CanWrite distinguish permission from the original
WritableBytes/MaxWritableBytes metadata. Reader/writer indices are copied and
then independent; constructor marks start at zero. Parent writes remain visible.
Slices/duplicates stay read-only, retained derivatives share parent ownership,
and copies use the parent's writable-copy policy. AsReadOnly on a read-only view
returns the same instance. The empty singleton stays non-read-only; its read-only
view is distinct and shares the permanently-one reference count.

Getters, source transfers, decoding, searching and copying now use bounded
ReadOnlyMemory/ReadOnlySpan. Mutable AsMemory/AsSpan, setters/writers, capacity
changes and DiscardReadBytes reject access with standard NotSupportedException.
EnsureWritable(n,force) returns 1 on a read-only view even when n is zero; its
throwing overload rejects write permission. CLR argument/lifetime checks remain
consistent. All mutable borrows/writes reject even an empty range, instead of
reproducing Java helpers whose zero-length early returns bypass write overrides.
DiscardSomeReadBytes retains the original distinction: fully consumed content
resets only indices/marks, a below-threshold prefix is left alone, and real
compaction requires write permission. This corrects the earlier delegation to
DiscardReadBytes which would have rejected the index-only read-only case.

The internal sealed ReadOnlyByteBuf replaces public deprecated wrapper creation
and the type/cast-based factory. ByteBuf.AsReadOnly is the supported CLR entry
point; no Unpooled.unmodifiableBuffer alias is added. Nested read-only wrappers
flatten, while private slice/duplicate parents are preserved to keep offset and
capacity semantics. ReadOnlyAbstractByteBuf's sole unchecked-word optimization
is a CLR replacement: all types use bounded read memory and BinaryPrimitives;
there is no second unchecked subtype or JVM performance claim. Its original
license/class comments and the read-only wrapper/API/fixture comments are retained.

Read-only memory is a borrowed API view, not immutable ownership or a snapshot.
Unwrap, other owner views and deliberate MemoryMarshal/pointer operations can
access the shared storage. Native saved memory becomes invalid after owner resize
or final release; an acquired ReadOnlyMemory pin preserves physical storage and
quota until disposed, exactly as for writable memory. Access remains subject to
the existing exclusion of concurrent resize/release. Segmented/composite memory
and wrapping external read-only storage are separate pending source contracts.

The new heap/native fixtures cover every implemented mutation family, mutable
borrows, literal unaligned/endian word reads, text/search/visitor access, source
transfers, independent indices/marks, retained/nested views, writable copies,
parent resize, index-only compaction, empty lifetime and native pins. Original
ReadOnlyByteBufTest is still in-progress: JVM channels/NIO/order objects, public
constructor tests and other unported cases are not represented by stubs or skips.
Buffer.Tests passes 172/172 in Debug, Release and checked Release, zero skips.
Focused solution Debug/Release also validate the existing common native-memory,
reference-count and EncodingConstructor contracts; common source/configuration
are unchanged and no new whole-common run is claimed. Evidence:
buffer-readonly-*.trx and artifacts/buffer-readonly-validation. Inventory remains
159 paths: 137 pending, 19 in-progress, 2 verified and 1 CLR replacement.

## Composite storage and segmented access

CompositeByteBuf owns each original source buffer with its captured readable
start/length. AddComponent transfers one existing reference without retaining or
moving source indices; its optional increaseWriterIndex defaults to false. Indexed
insertion, empty components, checked capacity overflow, original duplicate views,
captured ComponentSlice views (cached by the later enumeration unit), borrowed Decompose slices, component/offset
mapping, removal and final release are implemented. Retain before adding when the
caller needs separate ownership. Validation failure releases an incoming reference;
null/cyclic ownership is rejected before transfer. Explicit removal leaves indices
unchanged like Netty; callers must repair any stale indices before further use.

List replaces JVM manual component-array growth. Bounded component transfers and
an eight-byte stack fallback let the existing primitive, sequential, bulk, zero,
copy, derived/read-only view, encoding/search and visitor APIs cross boundaries.
Contiguous root/range access still uses Memory/Span and BinaryPrimitives. AsMemory,
AsSpan and their read-only counterparts never silently materialize disjoint ranges:
they reject a multi-segment range with NotSupportedException. AsReadOnlySequence
and ReadableSequence expose live borrowed segments, including nested views, without
retain or pin. Their layout is captured; exclude layout changes/resize/release while
borrowing them. AsReadOnly explicitly forbids writes. A composite containing some
read-only components remains IsReadOnly=false like Netty; writes to those component
ranges fail, and a multi-component write can modify an earlier writable prefix before
failing. No all-or-nothing write guarantee is added.

Segmented bulk operations snapshot when needed to preserve overlap; cross-segment
text encoding/decoding and multi-byte needle search can allocate temporary byte
arrays. Text is decoded as a whole so multibyte characters spanning components are
not broken. This is a correctness port, without JVM performance equivalence claims.
Small scalar operations use stack bytes, not heap arrays. Native root leases remain
value types; composite groups pin every native source allocation, including through
read-only/derived/nested components, across growing aliased byte/UTF-16 span writes.
Logical ownership can be released during consolidation while physical native storage
and quota survive until the write's leases end. A failed group acquisition disposes
all pins already acquired.

Capacity grows through owned padding and shrinks by trimming/releasing tail
components; source indices remain independent. Automatic consolidation occurs only
when component count exceeds the configured maximum. Full/ranged consolidation
preserves composite indices and copies captured component ranges before publishing
and releasing old ownership. CLR allocation/source-read failure leaves the original
layout intact; a failed automatic consolidation leaves the successfully inserted
component attached and owned. IsDirect reflects actual components (false when empty
or mixed), while the constructor's direct flag/domain selects padding, consolidation
and Copy allocation. DiscardReadComponents/DiscardSomeReadBytes remove fully read
components; DiscardReadBytes also trims the partial leading range without copying.
Both adjust source coordinates, indices and marks. Retaining a composite slice keeps
the composite alive, but does not retain components removed from it; invalid old
coordinates fail CLR bounds checks without exposing freed storage.

The new 39 fixtures cover selected AbstractCompositeByteBufTest contracts plus
literal boundary/endian/NaN wire values, readonly/nested views, empty components,
independent indices/marks, ownership/shrink/discard/decompose, allocation rollback,
failed automatic consolidation, native quota/pins and aliased growth/text. A one-MiB
shared owner reproduces the original signed-capacity overflow without allocating
GiB of backing memory. Two seeded 100-layout cases compare every eight-byte offset
against independently assembled flat wire bytes and verify discard/consolidation.
All implemented original operation documentation/licenses and rationale comments
are retained, with CLR changes explained. Whole source/fixture coverage stays partial.

The ignored probe compiles 293 exact pinned common/buffer Java sources with cached
real dependencies; five GraalVM substitution sources are excluded from this ordinary
JVM probe. All 93 buffer sources are compiled unchanged, with Unsafe disabled for
this probe's heap/direct runtime. Across 200 randomized captured/empty-component
layouts on each backend, 400 Java/CLR rows agree on byte order, views, search,
Decompose segment lengths, capacity/indices/component counts, discard/marks,
consolidation and final source reference counts. This validates those contracts;
pooled/Unsafe/channel/flattening behavior is not claimed. Evidence is in
artifacts/buffer-composite-validation (ignored), including inputs, runtime rows,
compilation log and zero mismatches.

Buffer.Tests passes 211/211 with zero skips in Debug, Release and rebuilt checked
Release. Focused common native-memory/reference-count/EncodingConstructor tests
pass 118/118 with zero skips in Debug/Release; common source/tests/configuration
are unchanged and its whole suite is not rerun. Evidence: buffer-composite-*.trx.
All 159 original paths remain inventoried: 135 pending, 21 in-progress, 2 verified
and 1 CLR replacement. Multi-add/flatten, iterator/cached internal component APIs,
constructor variants, allocator/leak wrappers, array/address and I/O integration,
and the remaining original composite tests stay pending.

## Composite batch additions and shallow flattening

AddComponents now accepts params/ByteBuf[] and IEnumerable<ByteBuf>, with indexed
insertion and a C# writer-increase option following the existing buffer-first API.
Default additions leave the writer index unchanged. Arrays preflight total capacity
and insertion index before transferring references; failures there leave all inputs
caller-owned. Streaming insertion instead keeps each successful prefix and consumes
the failing incoming reference, then safely drains remaining yielded inputs. Null
ends additions and releases the unvisited tail. Both preserve prefix writer updates
on later failure, add ordinary empty components, and consolidate once after the batch.
SafeRelease failure on a dead tail does not prevent later entries being released.
No temporary collection is built for IEnumerable; its single enumerator is disposed.
Its own MoveNext/Current/Dispose error behavior governs what can still be yielded and
released. A ByteBuf also implementing IEnumerable is transferred as a single buffer,
matching the original dispatch rule instead of enumerating its contents. C# extends
the indexed public overloads with the same optional writer increase; Java's public
indexed forms otherwise require the caller to update that index explicitly.

AddFlattenedComponents performs the original shallow operation: an actual composite
contributes only intersections of its readable range with non-empty components.
Each original component source is retained with captured coordinates, preserving
slice-specific ownership, indices and permissions. Nested component composites stay
nested. Slices/duplicates/read-only wrappers passed as input are ordinary single
components, including read-only protection; they are not peeled into writable roots.
An unreadable input is released without adding a component. Successful composite
transfer releases one input-container reference; separately retained containers stay
alive. Composite retain/allocation/source-read failures release all new references,
restore destination layout/indices and leave the input caller-owned. Existing failure
behavior for an ordinary single component is preserved. A source already separately
retained and owned by the destination also survives automatic consolidation correctly.
Cyclic/self-containing input graphs remain rejected without consuming those references.
JVM WrappedCompositeByteBuf and pooled independent-count hierarchy variants are still
pending; the native derived/read-only tests do not claim those specific subtype cases.

The pinned Java addFlattenedComponents path lacks a capacity-overflow check. An exact
runtime probe appends one MiB to a 2146435072-byte virtual composite and obtains
capacity -2147483648 while consuming the input. The backing allocation is only one
MiB shared through retained duplicates. CLR preflight rejects this addition before
retaining components or transferring the input; destination state and caller ownership
remain unchanged. Array total checking also avoids signed accumulator wrap by checking
remaining capacity before each addition. Original documentation/licenses and all
comments from the implemented batch/flatten/consolidation kernels are preserved;
List storage, enumerator disposal and checked arithmetic adaptations are explained.

The new 31 test cases cover original batch/null/overflow/flatten/offset contracts,
heap/native ownership, prefix and tail failures, single consolidation observed during
third-source access, native quota rollback, retained and read-only views, unreadable
input, a retained source already in the destination, and retry after source repair.
The prior exact pinned Java runtime is reused: 1202 normal heap/direct batch/flatten
rows and six preflight/prefix/tail/overflow failure rows match CLR bytes, capacity,
indices, component counts and source/final reference counts. Normal indexed writer
increase uses the equivalent Java indexed insertion plus explicit index update.
Native enumerator disposal, rollback and checked-overflow tests separately cover
CLR adaptations. Evidence: artifacts/buffer-composite-add-validation (ignored).

Buffer.Tests passes 242/242, zero failed/skipped, in Debug, Release and rebuilt
checked Release. Common source/tests/configuration are unchanged; the whole or
focused Common suite is not rerun for this Buffer-only unit. All 159 original paths
remain in the JSON: 135 pending, 21 in-progress, 2 verified and 1 CLR replacement.
Both CompositeByteBuf and its original abstract fixture remain in-progress because
constructor variants, enumeration/internal component APIs, allocator/wrapper/I/O
integration and the remaining original test contracts are still pending.

## Composite enumeration and cached component views

CompositeByteBuf implements IEnumerable<ByteBuf> and the nongeneric enumeration
interface. Enumeration includes empty components and returns borrowed cached
component views without retaining or releasing them. It checks accessibility when
created. A nonempty enumerator compares the component count captured at creation,
matching Java: additions/removals/consolidation that change count invalidate it,
while same-count replacements can be observed. An empty enumerator remains empty
after additions or release. This is not thread synchronization. CLR MoveNext returns
false on exhaustion, Current rejects invalid positioning, Reset is unsupported,
and Dispose ends enumeration without releasing components. Java exception classes
map to InvalidOperationException/NotSupportedException and CLR bounds errors.

InternalComponent/InternalComponentAtOffset and ComponentSlice now share the
original per-component slice cache. This corrects the earlier fresh-slice behavior:
repeated access and enumeration return the same object and do not reset indices.
An ordinary full-source component reuses its source, including the empty sentinel;
partial components lazily slice captured coordinates. Flattened entries start with
no cached view even for a full source, matching the original flatten constructor.
Component/ComponentAtOffset still return fresh full-source duplicates. Cached
indices can affect later view access; original undefined-behavior documentation
for internal index mutation is retained. Retain before keeping any borrowed view
past removal/consolidation/release. Original readonly permissions are preserved.

Capacity shrink and partial discard replace an existing cache with a derived slice,
preserving old view coordinates and reference ownership. CLR prepares that view
before publishing new coordinates. An unmaterialized cache uses updated captured
coordinates; offset shifts preserve surviving cache identities. Free clears cache
references and releases only the original transferred ownership. Failed slice
materialization does not poison the cache and can be retried after source repair.
All original documentation and rationale for these operations is preserved.

The 24 new cases cover selected original iterator/internal-view contracts and the
flatten/componentSlice retain-remove-add-back regression on heap/native storage,
plus CLR protocol, readonly nested views, retained/borrowed ownership, cache trim
and retry cases. The reused exact pinned Java source runtime (Unsafe disabled)
matches 1603 Java/CLR rows: 50 seeded layouts on heap/direct, writable/readonly,
full/partial sources and ordinary/flattened composition, before shrink, after shrink
and discard, and after release; three rows cover empty/count-change/same-count
iteration. The comparison checks cache/source/iterator identities, bytes, indices,
offset lookup, component counts and source references. CLR enumerator lifecycle
is verified separately. Evidence: artifacts/buffer-composite-enumeration-validation
(ignored); buffer-composite-enumeration-*.trx. WrappedCompositeByteBuf and pooled
independent-reference-count variants are not claimed by these tests.

Buffer.Tests passes 266/266, no failures/skips, in Debug, Release and rebuilt checked
Release. The checked build has zero errors and 51 existing Common warnings. Common
source/tests/configuration are unchanged; its suite is not rerun for this Buffer-only
unit. All 159 paths remain inventoried: 135 pending, 21 in-progress, 2 verified and
1 CLR replacement. CompositeByteBuf and its abstract fixture remain in-progress;
constructor/wrapper/allocator/array/address/I/O and remaining tests are pending.

## Unpooled single and multiple-input wrapping factories

WrappedBuffer now covers byte-array subregions, a single ByteBuf readable slice,
and params byte[][]/ByteBuf[] with optional component limits. Single-buffer wrapping
transfers one existing reference without retaining; unreadable inputs are released
and return the shared empty sentinel. Indices remain independent and content is
shared until consolidation. C# zero-input overloads resolve the two params choices;
null tests use typed casts. Byte order remains explicit BE/LE operations rather than
introducing JVM swapped-buffer state.

ByteBuf arrays release leading unreadable inputs, then transfer the entire suffix,
including later empty components. Null in that suffix stops insertion and safely
releases the unvisited tail. A null before the first readable input throws after
already skipped inputs have been released, leaving later inputs caller-owned.
Byte-array lists instead skip every empty array and stop at null. One-item/all-empty
paths ignore unused component limits as in Java; multi-item paths create composites
even if only one nonempty input remains. Default limits equal input array length.
Consolidation runs once after adding the batch. It uses heap storage, matching the
original factory, and can produce a writable copy of readonly input components.
Without consolidation, original native flags, permissions and sharing are preserved.
Internal offset loops reuse composite array preflight without copying arrays or
adding Java's generic ByteWrapper hierarchy. Original operation comments are kept.

A factory cannot return its partial composite after failure. CLR cleanup releases
already acquired component references while preserving the original exception;
preflight failure leaves the untransferred suffix caller-owned. The exact pinned
Java probe with readable prefix/dead input/tail ends with reference counts 1/0/0,
retaining the unreachable prefix; C# ends with 0/0/0. Source-read/consolidation failure
also releases acquired ownership. CLR region null/bounds checks apply even to zero
length; the original accepts invalid empty ranges through its early-empty shortcut.
These two adaptations are reproduced and recorded separately from matching rows.

31 new cases cover selected original UnpooledTest wrapping/release/issue-5597
contracts plus CLR regions, empty/null/overload paths, captured indices, sharing vs
consolidation, native/readonly/nested/repeated sources, failure cleanup and overflow
with one-MiB shared backing. 2000 exact pinned Java/CLR runtime rows compare 50 seeded
layouts, default/1/2/128 limits, heap/direct and writable/readonly sources, byte arrays,
component counts, capacity/indices, mutation sharing and final references. Eight more
rows match null/preflight/single/empty/region/overflow ownership, with exception types
normalized. Evidence: artifacts/buffer-unpooled-wrapping-validation (ignored) and
buffer-unpooled-wrapping-*.trx. NIO/address/swapped/pooled variants remain pending.

Buffer.Tests passes 297/297 in Debug, Release and rebuilt checked Release, no failures
or skips. The checked build has zero errors and 51 existing Common warnings. Common
source/tests/configuration are unchanged and its suite is not rerun. All 159 original
paths remain inventoried: 134 pending, 22 in-progress, 2 verified, 1 CLR replacement.
Unpooled and its original test fixture remain in-progress; this implements wrapping
overloads rather than the whole factory class or fixture.

## Unpooled single and multiple-input copying factories

CopiedBuffer now accepts byte[], array subregions, a single ByteBuf and params
byte[][]/ByteBuf[]. Copies produce independent writable heap storage; input indices,
marks, references and permissions remain unchanged. Array/Span and multiple-input
results have a maximum equal to copied length. A single ByteBuf, including an array
containing one ByteBuf, returns a growable heap buffer as in Java. This fixes the
earlier Span factory's int.MaxValue maximum. The zero-input overload resolves C#
params ambiguity. Existing explicit BE/LE operations preserve bytes without Java
mutable byte-order state; original order-check documentation is kept with CLR remarks.

Length preflight rejects overflow before allocating or reading payloads. Null entries
throw rather than terminating input. Empty-only buffers return the shared sentinel
without releasing inputs; with readable input, original zero-length source reads
still validate accessibility. Single-source failure releases its new output, while
multiple-source output ownership is published only after every copy succeeds.
Input indices/storage must stay stable during copying. Array-region null/bounds
checks apply to zero length, retaining the CLR adaptation from wrapping; the pinned
Java early-empty shortcut accepts invalid empty ranges. Original comments for the
implemented operations, including both merge rationale comments, are preserved.

27 new tests cover selected original UnpooledTest copy scenarios, capacity differences,
storage and lifetime independence, native/readonly/segmented/nested/retained inputs,
marks, endian wire bytes, failure/retry, empty/null/ranges and shared-backing overflow.
1600 exact pinned Java/CLR rows compare 50 seeded layouts across heap/direct,
writable/readonly and single/array-of-one/multiple buffer paths, plus array/Span/region
and multiple-array copies. They check bytes, maxima, indices, source independence and
reference counts before/after releasing copies and originals. CLR Span is compared
with the original array-copy contract. Ten further rows match null, accessibility,
overflow, empty ownership and arity behavior; exception types are normalized.
Evidence: artifacts/buffer-unpooled-copying-validation (ignored) and
buffer-unpooled-copying-*.trx. NIO and swapped-order subtype cases remain pending.

Buffer.Tests passes 324/324 in Debug, Release and rebuilt checked Release, no failures
or skips. The checked build has zero errors and 51 existing Common warnings. Common
source/tests/configuration are unchanged and its suite is not rerun. The JSON still
contains all 159 paths: 134 pending, 22 in-progress, 2 verified and 1 CLR replacement.
Unpooled and its original fixture remain in-progress because other factories and
original tests are still unported.

## Unpooled text copying factories

CopiedBuffer now accepts string/char[] whole input and UTF-16 subregions, plus
ReadOnlySpan<char> in place of Java CharBuffer ranges. Results have independent
writable heap storage and can grow to int.MaxValue. Whole empty strings keep an
independent growable owner; empty arrays/ranges/spans use the shared sentinel.
Nonempty input whose custom fallback emits no bytes still has its own owner.
Sizing failure allocates no buffer; encoding failure releases the newly allocated
owner. Original public operation comments and shared allocation rationale are kept.

All forms consistently honor the caller's Encoding and fallback, as ByteBuf.WriteString
already does, and emit no preamble. Java selects specialized UTF-8/ASCII paths for
whole strings, while regions/arrays use CharsetEncoder. These can produce different
bytes: whole ASCII "é" becomes E9 in Java, but Encoding.ASCII uses 3F; a lone
surrogate becomes 3F in Java UTF-8, but Encoding.UTF8 uses EF BF BD. Strict/custom
fallback must not be bypassed by selecting a fast path from a CLR code page.
Existing ByteBufUtil.WriteUtf8/WriteAscii preserve Netty's specialized byte mappings.
UTF-16 offsets can split surrogate pairs; the supplied fallback handles those ranges.
Null encoding and empty-range bounds are validated even where Java returns early.
Capacity is the exact encoded byte count instead of CharsetEncoder's upper-bound
reservation (e.g. UTF-8 string region "A": Java capacity 3, CLR capacity 1); maximum
capacity and growth behavior remain the same. This is an explicit CLR adaptation.

58 new cases cover the three original UTF-8/ASCII/Latin-1 roundtrips, seven encodings
across five input shapes with golden bytes, BOM omission, mutable-array/native-span
independence, bounds/overflow/split pairs, fallback policies, empty ownership, nulls
and encoding failure/retry. 1785 exact pinned Java/CLR runtime rows match bytes,
indices, maxima, ownership, growth and release for 50 seeded layouts plus empties
across seven encodings and five shapes. The 615 upper-bound capacity differences
are recorded separately, along with ten CLR Encoding policy rows, five matching
specialized ByteBufUtil rows and two empty-validation differences. This does not
claim byte equivalence for malformed or unmappable text under differing policies.
Evidence: artifacts/buffer-unpooled-text-copying-validation (ignored) and
buffer-unpooled-text-copying-*.trx.

Buffer.Tests passes 382/382 in Debug, Release and rebuilt checked Release, no failures
or skips. Checked build: zero errors, 51 existing Common warnings. Common files are
unchanged and its suite is not rerun. All 159 upstream paths remain in JSON: 134
pending, 22 in-progress, 2 verified and 1 CLR replacement. Unpooled and its original
fixture remain in-progress; allocator/external-storage factories and other
original scenarios remain pending.

## Unpooled primitive copying factories

All 15 original scalar/array CopyInt/Short/Medium/Long/Boolean/Float/Double operations
are implemented with their original comments, including both short[] and int[]
CopyShort forms. Eight ReadOnlySpan overloads also support stack and subrange input
without intermediate arrays. Null/empty arrays and empty spans return the shared
sentinel. An explicit zero-input CopyShort resolves the two C# params overloads.
Nonempty copies own independent writable heap storage with exact initial capacity
and int.MaxValue maximum. Byte order is big-endian; Short/Medium truncate high bits,
booleans encode as 0/1, and floating point preserves raw IEEE bits, including NaN
payloads. Byte-count multiplication is checked before allocation rather than using
Java's wrapping int arithmetic. Original allocation/wire/ownership behavior is kept.

62 new tests cover selected original primitive testWrap scenarios plus Span/stack
and subrange inputs, null/empty/arity, storage independence, growth/release, signed
boundaries/truncation and signed zero/subnormal/infinity/quiet/signaling NaN bits.
3352 exact pinned Java/CLR runtime rows compare 100 seeded layouts, eight input types
and four scalar/array/Span/range shapes, plus integer/IEEE boundaries and null/empty
input. Bytes, capacity/maxima, indices, heap/writable flags, growth and release match.
Span/range forms are compared with equivalent original array copies. 3328 payloads
also match independent Python integer/IEEE-bit calculations; the remaining 24 rows
cover null/empty/zero-input behavior. Evidence:
artifacts/buffer-unpooled-primitive-copying-validation (ignored) and
buffer-unpooled-primitive-copying-*.trx.

Buffer.Tests passes 444/444 in Debug, Release and rebuilt checked Release, with no
failures/skips. Checked build: zero errors, 51 existing Common warnings. Common
files are unchanged and its suite is not rerun. Inventory remains 159 paths: 134
pending, 22 in-progress, 2 verified and 1 CLR replacement. Unpooled and its original
test fixture remain in-progress; remaining factories and fixture scenarios are pending.

## Unreleasable wrappers and shared index state

Unpooled.UnreleasableBuffer now returns a borrowed lifetime-suppressing facade.
It acquires no reference and does not make the parent immortal: the actual owner
must stay alive and perform the final release. Retain/Release ignore even invalid
counts, Touch never reaches the parent, and reported references/accessibility follow
the actual owner. Nested wrappers remove the redundant layer. Slices, duplicates,
read-slices and read-only views retain this policy; their retained variants acquire
no reference, preserving all four original leak rationale comments. Copies keep
ordinary independent ownership. Parent-specific discard/search, flags, resizing,
segmented memory and native pin leases are delegated. Original factory/class comments
are preserved. Explicit BE/LE operations replace Java mutable order/SwappedByteBuf.

Transparent wrappers must share parent indices and marks. ByteBuf now has a private
shared-index constructor and ref-return access to the existing index owner's fields;
no separate state allocation or Java-style full forwarding class is introduced.
Ordinary buffers and derived views keep independent state. View conveniences and
AsReadOnly allow overrides to preserve unreleasable ownership. This is driven by the
pinned WrappedByteBuf contract, whose class documentation is retained.

The Java probe exposed an existing derived-copy mismatch: ByteBufView used heap
storage and its fixed slice maximum. Pinned AbstractUnpooledSlicedByteBuf/DuplicatedByteBuf
delegate Copy to their parent. The CLR view now validates/translates the source range
and delegates likewise, preserving native/heap policy and the parent maximum.

67 new cases include both original UnreleaseableByteBufTest scenarios (complete),
36 original retained-view combinations across heap/native/composite owners, and
shared indices/marks, readonly permissions, ignored counts/Touch, failed ranges,
native alias growth/shrink, composite discard/ownership, bulk aliasing and copy
lifetime/policy. 2700 exact pinned Java/CLR rows cover 50 seeded layouts, three
storage kinds, writable/readonly sources and nine derived-view forms. They compare
indices/marks, bytes, maxima, flags, nested-wrapper identity, ordinary copy policy
and lifetime after the parent dies. 64 further rows match live/dead invalid-count,
empty and structural-discard behavior (exception types normalized). Evidence:
artifacts/buffer-unreleasable-validation (ignored) and buffer-unreleasable-*.trx.

Buffer.Tests passes 511/511 in Debug, Release and rebuilt checked Release, with no
failures/skips. Checked build: zero errors, 51 existing Common warnings. Common files
are unchanged and its suite is not rerun. All 159 paths remain inventoried: 131
pending, 24 in-progress, 3 verified and 1 CLR replacement. The dedicated two-test
fixture is verified; generic wrapper/allocator/NIO/I/O surface remains partial.

## Fixed read-only composites

Unpooled.WrappedUnmodifiableBuffer now preserves the original zero/single/multiple
input split: shared Empty, borrowed AsReadOnly with original indices, or an internal
fixed-layout read-only owner. Multiple inputs transfer one existing reference each,
including empties; repeated owners require one reference per occurrence. No component
slices or content copies are created. As in pinned Java, component lengths come from
ReadableBytes but map from absolute index zero: use input.Slice() to select a nonzero
readable range. Storage/layout must remain stable. Derived views share readonly
ownership; copies are independent writable heap buffers with int.MaxValue maximum.
Copy follows the current CLR heap default. The Java oracle disables Unsafe and uses
the same heap default; upstream allocation can prefer direct storage on other JVMs.
Allocator policy abstraction remains pending.
Bounded ReadOnlyMemory/ReadOnlySequence expose contiguous/segmented data. Recursive
native leases keep borrowed spans alive through outer composite consolidation.

Runtime evidence identifies two original flag defects: FixedCompositeByteBuf inherits
IsReadOnly=false despite rejecting setters, and its IsDirect loop ignores the first
input. CLR reports readonly and checks all inputs. Descriptors snapshot the caller's
array/layout instead of lazily replacing its entries; null/dead/overflow preflight
consumes no references, and single-byte reads retain capacity guards. All original
FixedCompositeByteBuf comments and the modern factory comment are preserved. Explicit
BE/LE operations replace mutable Java order; the deprecated factory alias is omitted.

39 new tests cover original portable setters/copy/segmentation plus boundary words,
heap/native/nested and empty layouts, arity/indices, snapshots/shared data, retained
ownership/copies, preflight failure/overflow, bounds and native alias consolidation.
1080 pinned Java/CLR contract rows match, with independently computed bytes. The 1080
readonly flag and 240 direct flag corrections are recorded separately, not counted
as exact flag matches. Evidence: artifacts/buffer-fixed-composite-validation (ignored)
and buffer-fixed-composite-*.trx. Debug, Release and rebuilt checked Release each
pass 550/550 tests, no failures/skips. Checked build: zero errors, 51 existing Common
warnings. Common is unchanged and its suite was not rerun. Inventory: 159 paths,
129 pending, 26 in-progress, 3 verified, 1 CLR replacement. Source and original fixture
remain in-progress: allocator, pooled/channel/stream/file tests and raw address/array
consumer mapping are still incomplete.

## Input stream adapter

ByteBufInputStream is a nonseekable System.IO.Stream over the original captured
readable range. Reads advance the supplied buffer's reader; Mark/Reset share its
mark, appending bytes never extends the captured end, and Skip is bounded. Default
disposal borrows; releaseOnDispose returns one existing reference exactly once,
including original invalid-length construction cleanup. BytesRead stays diagnostic
after disposal. All original source comments are preserved.

All input operations are mapped. Typed helpers use CLR width names and explicit BE
wire order; ReadByte is the Stream octet-or-minus-one operation and ReadUnsignedByte
is the required-byte operation. ReadFully checks the whole field before consuming; inherited Stream.ReadExactly keeps
its standard partial-read-on-EOF contract.
ReadLine maps bytes to UTF-16 units and respects CR/LF/CRLF within the captured end.
ReadUtf preserves Java DataInput modified UTF-8 framing, NUL/overlong/surrogate rules,
full-payload consumption on malformed data and prefix-only consumption on truncation.
Encoding.UTF8 and BinaryReader.ReadString have different wire contracts.

CLR bulk reads return zero at EOF/empty input and validate arguments even at EOF.
Disposed adapters reject access even when borrowing; dead construction and external
reader escape fail explicitly. Memory/array async reads honor cancellation without
consuming; CopyToAsync and BinaryReader use the ordinary Stream interface. BinaryReader
selects its own little-endian policy. No fake seeking or writable interface is added.
Concurrent use, external cursor changes and content discard remain caller exclusions.

56 tests cover original input scenarios and CLR integration across heap/native and
mutable/fixed composite storage. 800 pinned Java/CLR range/primitive/mark/line/lifetime
rows and 1336 modified UTF rows match after bulk-EOF and exception normalization.
An independent encoder covers all 65536 UTF-16 code units and a 65535-byte payload.
Evidence: artifacts/buffer-input-stream-validation (ignored), buffer-input-stream-*.trx.
Debug, Release and rebuilt checked Release pass 606/606, no failures/skips. Checked
build has zero errors and 51 existing Common warnings; Common is unchanged and its
suite was not rerun. Inventory stays 159: 127 pending, 27 in-progress, 4 verified,
1 CLR replacement. Input source is verified; the combined original stream fixture
remains in-progress because output/pooled scenarios are still unported.

## Output stream adapter

ByteBufOutputStream maps all original output operations to System.IO.Stream over
the supplied writer index. Buffer/BytesWritten remain diagnostic after disposal.
Default disposal borrows; releaseOnDispose returns one existing reference exactly
once without retaining it. Numeric helpers use CLR width names and explicit BE wire
order. WriteInt16 retains low-bit truncation; WriteByte/WriteSByte and WriteChar use
CLR octet/character types. Single/Double preserve passed runtime IEEE bits. Test
signaling NaNs are materialized at runtime: Release constant folding can quiet a
constant before the stream receives it.

WriteBytes(string) writes the low eight bits per UTF-16 unit without fallback;
WriteChars preserves all units as two BE bytes, including its original partial
progress on capacity failure. WriteUtf directly builds the Java modified UTF-8
frame, including unsigned length, NUL and individual surrogate units. Oversize
payloads fail before writing; valid prefix/payload transfer together. All 11 original
source comments remain, with CLR remarks replacing the lazy DataOutputStream facade.

CLR rejects all disposed writes and readonly empty writes consistently. CanWrite
reflects permission; dead construction and invalid ranges fail explicitly. Async
array/Memory writes honor cancellation without consuming. Native aliases remain
valid across buffer growth/composite consolidation through existing leases.
BinaryWriter selects its own ordinary little-endian/framing policy. Seeking, reads
and length mutation are unsupported. 18 original Java edge probes record the closed
and readonly-empty inconsistencies corrected by these CLR rules.

49 new tests include the original combined roundtrip and output ownership/low-byte
scenarios across heap/native/composite storage. In both Debug and Release, 775 pinned
Java/CLR rows match after exception normalization: 600 general writes, 172 modified
UTF frames and 3 capacity-failure progress cases. 772 wire results also match an
independent encoder, covering all 65536 UTF-16 units and maximum 65535-byte payloads.
Evidence: artifacts/buffer-output-stream-validation (ignored), buffer-output-stream-*.trx.
Debug, Release and rebuilt checked Release each pass 655/655, no failures/skips.
Checked build: zero errors, 51 existing Common warnings; Common unchanged, its suite
not rerun. Inventory: 159 paths, 126 pending, 27 in-progress, 5 verified, 1 CLR
replacement. Both stream sources are reviewed; original pooled stream-test variants
still need a real pool, so the combined original fixture remains in-progress.

## Synchronous stream transfers

ByteBuf now exposes GetBytes(index, Stream, length), SetBytes(index, Stream, length),
ReadBytes(Stream, length) and WriteBytes(Stream, length). All four original API
comments are preserved. Absolute operations leave both indices alone. Relative
output advances the reader after success; relative input reserves requested capacity
before reading and advances the writer by the actual returned count. Growth at EOF
and unchanged indices on exceptions retain the original contract.

The CLR input policy performs one bounded Stream.Read for every backing kind,
returns short reads immediately and returns 0 at EOF. This deliberately replaces
Java CompositeByteBuf's fill-until-EOF loop: storage layout no longer determines
whether a short read triggers more I/O, and CLR EOF cannot cause infinite retries.
Callers requiring a full field must explicitly loop. Empty requests validate bounds,
permission, accessibility and null arguments without invoking the stream.

ArrayPool staging snapshots output before one Stream.Write and copies successful
input after the callback. No native span or borrowed composite segment survives a
stream callback; same-buffer append remains safe across native growth/consolidation.
Unlike Java heap input, a throwing read cannot publish its temporary bytes. A later
composite storage-commit failure may change earlier components while leaving writer
unchanged, and source input may already be consumed. A throwing output may already
have accepted a prefix while the buffer reader remains unchanged. Streams and buffer
references are borrowed. Concurrent mutation/release and external changes to the
active cursor must be excluded during I/O. Temporary memory is proportional to the
requested length plus pool rounding; these APIs deliberately copy and make no
zero-copy or performance claim.

37 tests port selected original block, released access and independent duplicate/
slice concurrent output scenarios, plus CLR short/zero/invalid reads, readonly/fixed
composites, views/wrappers, failures, stream ownership and native aliases. 1200 pinned
Java/CLR count/index/byte rows match in each Debug/Release after EOF normalization,
also checked against an independent byte model. 12 separate Java/CLR edge rows
record the intentional short-read, empty EOF, input-failure and output-call-shape
differences. Evidence: artifacts/buffer-stream-transfer-validation (ignored) and
buffer-stream-transfer-*.trx. net10.0 Debug, Release and rebuilt checked Release each
pass 692/692 tests without failures/skips. Checked build has zero errors and 51
existing Common warnings. Common is unchanged; its suite was not rerun. Inventory
remains 159: 126 pending, 27 in-progress, 5 verified, 1 CLR replacement. Original
sources/fixtures remain partial; async ByteBuf transfers and channel/positioned-file
contracts remain pending.

## Asynchronous stream transfers

GetBytesAsync, SetBytesAsync, ReadBytesAsync and WriteBytesAsync expose the four
stream-transfer contracts through ValueTask and CancellationToken. These are CLR
asynchronous counterparts of the Java synchronous APIs; Java has no matching async
stream overloads. The original four API comments remain in ByteBuf.Streams.cs.
The implementation awaits Memory-based Stream.ReadAsync/WriteAsync directly with
ConfigureAwait(false), without wrapping synchronous calls in Task.Run. Underlying
streams still determine how their own asynchronous operations execute.

The synchronous CLR policies remain: absolute operations preserve both indices,
relative operations publish only after success, input performs one bounded read and
returns 0 at EOF, and output snapshots the whole range. ArrayPool staging is owned
until the awaited operation finishes and returned in finally on every outcome.
No borrowed native memory or composite segments cross await. Buffer/stream ownership
is borrowed for the entire operation; callers must keep them alive and exclude
concurrent mutation and external changes to the active cursor until completion.
Controlled stream callbacks can resize storage; input resolves and checks the
destination again, and output retains its independent snapshot. Existing temporary
memory/copying costs and partial composite storage-commit limits still apply.

Invalid arguments/access/permissions are checked before pre-cancellation. Valid
pre-canceled operations, including empty ones, are canceled without I/O or growth.
Valid empty operations otherwise finish without touching the stream. Once started,
capacity growth is not rolled back on EOF, cancellation or failure. Cancellation is
cooperative: a stream returning a valid count/completion commits successfully even
if its token was canceled meanwhile. A thrown cancellation/failure leaves the active
index unchanged; staged input is not published, but the source may have consumed
data and an output may already have accepted a prefix. No implicit Retain, disposal,
blocking wait or cancellation-as-rollback contract is added.

49 CLR cases use delayed streams whose synchronous operations throw, covering
publication before/after actual suspension, all cancellation phases, argument
precedence, short/zero/invalid reads, failure progress, readonly/fixed/empty/dead
buffers, slice/shared indices, stream adapters, native resizing/composite consolidation
callbacks and partial commit failure. 1200 pinned synchronous Java wire/index/count
rows match forced-yield CLR async execution in both Debug and Release after EOF
normalization; an independent byte model checks every row. This validates the wire
contract, not Java async scheduling/cancellation. Evidence:
artifacts/buffer-async-transfer-validation (ignored), buffer-async-transfer-*.trx.
net10.0 Debug, Release and rebuilt checked Release each pass 741/741 without failures
or skips. Checked build: zero errors, 51 existing Common warnings, no new Buffer/test
warnings. Common is unchanged; its suite was not rerun. Inventory remains 159:
126 pending, 27 in-progress, 5 verified, 1 CLR replacement. Original source/fixtures
remain partial; channel/positioned-file APIs still need a separate consumer mapping.

## Positioned file transfers

ByteBuf.Files maps the four original FileChannel overloads to borrowed SafeFileHandle
and System.IO.RandomAccess, with four CLR ValueTask/CancellationToken counterparts.
The original four API comments and both original test position comments are retained.
File position never changes. Absolute operations preserve buffer indices; relative
output advances reader after successful write, and relative input advances writer
by the actual count after a successful read/commit.

CLR RandomAccess.Write/WriteAsync completes the requested range or throws, so
successful output returns length rather than exposing Java's possible partial write
count. Input performs one bounded read on every backing kind, returns short reads
immediately and reports EOF as 0. Closed handles throw instead of Java input's -1;
six original edge rows show even Java's zero-length closed behavior varies by layout.
Null/closed/invalid handles, negative ranges and signed file-end overflow are checked
before growth/I/O, including empty requests. A valid empty request skips I/O.

Pooled managed staging snapshots output and commits successful input; temporary
memory scales with requested length plus pool rounding. No native borrow crosses
await. Buffers/handles are borrowed through completion; exclude concurrent buffer
mutation/release and external active-cursor changes. Failed reads do not publish
staging, but composite commit can change a writable prefix before failure. Failed
writes may partially modify the file while leaving reader unchanged. Relative input
reserves requested space before actual I/O and keeps growth on EOF/error. Async
validation precedes cancellation; pre-cancellation prevents growth/I/O, including
empty operations. Cancellation remains cooperative, with successful I/O results
committed and thrown cancellation leaving active indices unchanged.

38 tests include the two original file-channel roundtrips across heap/native/composite
and sync/async modes, file position plus next sequential read, EOF/large offsets,
range/overflow/handle/permission failures, pre-cancellation, readonly/fixed/empty/dead
buffers, views/shared indices, partial composite commit, and 128 concurrent independent
views writing disjoint offsets. 1200 pinned Java byte/index/count/file-position/content
rows match per Debug/Release and sync/async mode (4800 CLR comparisons), with independent
byte checks and EOF normalization. Six closed-input edge rows record adaptations.
Real-file tests do not claim deterministic mid-I/O cancellation, injected kernel
partial-write coverage or Java async equivalence. Evidence:
artifacts/buffer-file-transfer-validation (ignored), buffer-file-transfer-*.trx.
net10.0 Debug, Release and rebuilt checked Release each pass 779/779 without failures
or skips. Checked build: zero errors and 51 existing Common warnings; no new Buffer/test
warnings. Common is unchanged; its suite was not rerun. Inventory remains 159:
126 pending, 27 in-progress, 5 verified, 1 CLR replacement. Sources/fixtures remain
partial; gathering/scattering channels still require consumer-based CLR mapping.

## Hex conversion and dumps

ByteBufUtil now covers all 11 original public hex/pretty/decode operations, with
native byte[]/string and additional ReadOnlySpan<byte>/ReadOnlySpan<char> inputs.
HexDump returns lowercase ASCII pairs: contiguous ranges use Convert.ToHexStringLower,
and segmented buffers write directly into the final string through ReadOnlySequence.
Strict decoding accepts ASCII 0..9/a..f/A..F only, rejects odd lengths, whitespace,
prefixes/separators and Unicode lookalikes, and reuses Common.StringUtil nibble
decoding without new facade types or Common changes.

PrettyHexDump/AppendPrettyHexDump retain exact header, 16-byte rows, spacing,
printable ASCII 32..126, period substitution, partial-row padding, append semantics
and StringUtil.NEWLINE, without a trailing newline. Formatting copies at most 16
bytes to stack per row. BCL conversion, computed padding and existing Common byte
conversion replace Java HexUtil lookup initialization. All selected original API
and HexUtil implementation comments remain with explicit CLR rationale.
The pinned row-prefix quirk is preserved for output compatibility: rows below 4096
are labeled relative to the dump start, while later rows include the absolute buffer
offset. Tests and large Java probes cover both zero and nonzero offsets at 64KiB.

Default ranges use readable bytes; explicit ranges are absolute and may extend past
WriterIndex to Capacity. Formatting changes no indices, marks or reference counts.
Null, empty, released and overflowing ranges are checked consistently before output,
including builder preflight; seven Java edge rows record the original empty-request
bypasses deliberately rejected in CLR. Hex output size uses checked arithmetic.
Buffer contents/lifetime must stay stable during formatting. No Java table/JIT
performance equivalence is claimed.

42 tests include all original hex decoding scenarios and suffix-loop comment, all
octets, uppercase/lowercase, array/string/span ranges, seven backing/view layouts,
exact formatting/ASCII/row widths, empty output and failure guards. 65988 pinned
Java/CLR rows match in each Debug/Release: 448 formatting/roundtrip rows, four large
dumps and all 65536 UTF-16 nibble inputs (CLR string and span paths). Every row also
matches an independent hex/format/ASCII model; native newlines match without
normalization. Evidence: artifacts/buffer-hex-validation (ignored), buffer-hex-*.trx.
net10.0 Debug, Release and rebuilt checked Release pass 821/821, no failures/skips.
Checked build: zero errors and 51 existing Common warnings; no new Buffer/test
warnings. Common is unchanged and its suite was not rerun. Inventory remains 159:
126 pending, 27 in-progress, 5 verified, 1 CLR replacement. ByteBufUtil and its
original fixture remain partial; broader utilities are pending.

## Content comparison and hashing

ByteBufUtil.HashCode, both Equals overloads and Compare preserve the pinned content
contract. ByteBuf now implements IEquatable<ByteBuf>/IComparable<ByteBuf> and overrides
Equals/GetHashCode, allowing native HashSet lookup/removal and default sorting.
Readable content determines whole-buffer equality/hash/order regardless of absolute
indices or backing. C# == and ownership-cycle checks keep reference identity.
Absolute equality may precede ReaderIndex, but ends at WriterIndex; a nonnegative
overrun returns false using subtraction to avoid overflow. Contiguous equality uses
Span.SequenceEqual; segmented comparisons read bounded crossing words without flattening storage.

Hash preserves unchecked signed Int32 big-endian words, signed Java trailing bytes,
and zero-to-one coercion. Compare preserves unsigned big-endian word comparison and
its exact Int32-clamped difference, unsigned tail difference and prefix length
difference. Existing explicit-endian access replaces Java mutable-order branches.
All seven original API comments, two implementation comments and four selected
test comment lines are retained. No Java compatibility facade or equality operator is added.

Operations preserve indices, marks and reference counts; callers keep content,
indices and lifetime stable. Do not mutate/release buffers while used as hash keys.
Utility self-equality/self-comparison remain reflexive even after release; null/null
retains the original identity results. Distinct buffers must be accessible, including
empty or length-mismatched comparisons, and hash/range access rejects released buffers
consistently. These deliberately replace six Java storage-check bypasses. Typed
CompareTo(null)=1 follows CLR sorting, replacing the original Java exception.

45 tests port the four original subsection-equality scenarios and selected original
object equality, comparison/prefix, byte-order-independent and HashSet scenarios.
They add seven storage/view layouts, signed tails, word overflow/zero coercion,
unsigned/clamped exact differences, writer/reader bounds, unchanged marks/ownership,
native sorting and explicit null/released handling. 25088 pinned Java rows match
per Debug/Release across four Java order combinations and 49 backing pairs, with
an independent byte/hash/order model; twelve edge rows record seven adaptations.
Evidence: artifacts/buffer-comparison-validation (ignored), buffer-comparison-*.trx.
net10.0 Debug, Release and rebuilt checked Release pass 866/866, no failures/skips.
Checked build: zero errors and 51 existing Common warnings; no new Buffer/test
warnings. Common is unchanged and its suite was not rerun. Inventory remains 159:
126 pending, 27 in-progress, 5 verified, 1 CLR replacement. Original sources/fixtures
remain partial; broader utility/allocator APIs are still pending.

## Text validation

Both ByteBufUtil.IsText overloads use native Encoding. Default input is the readable
range; explicit ranges may precede ReaderIndex but must end at WriterIndex, using
subtraction bounds checks. The original range comment says readableBytes, while
its implementation actually bounds the end by WriterIndex; CLR remarks clarify this.

Canonical UTF8/ASCII use BCL Utf8.IsValid/Ascii.IsValid. Segmented UTF8 and other
encodings use a fresh Decoder, strict ExceptionFallback and fixed 256-char stack
scratch, retaining decoder state across segments and flushing once at the end.
There is no whole-buffer flattening or decoded string allocation. Caller Encoding
and fallback stay unchanged; custom same-code-page encodings keep their own decoder.
Only malformed/unmappable decoding returns false; other decoder errors propagate,
and a nonprogressing custom decoder throws. Original API/helper/RFC and algorithm
comments remain with the CLR replacement rationale. No Java ByteProcessor facade is needed.

Null, writer-overrun and released ranges are checked, including empty requests.
Four pinned edge rows show Java returning true for released empty UTF8 on heap,
native, mutable and fixed composites; CLR consistently rejects these accesses.
Indices, marks and reference counts stay unchanged. Keep bytes/layout/lifetime
stable during validation; independent local decoders support concurrent readers.

47 tests retain all original IsText vectors, bounds and concurrent-use scenarios,
adding seven storage/view layouts, scalar/overlong/surrogate/truncation rules,
every component split, UTF16/32 flush, large inputs, strict fallback isolation,
custom decoder/failure/nonprogress contracts and lifetime/index guards.
204544 Java rows match per Debug/Release for UTF8, ASCII, UTF16LE/BE, Latin1 and
UTF32LE/BE, with four range masks and index/mark/reference checks per row. The
inputs include all 256 one-byte and 65536 two-byte combinations on heap/mutable/fixed
storage, plus 1024 mixed random/encoded/truncated cases across seven layouts.
Independent Python strict decoders agree without normalization; 24 edge rows
record the four adaptations. This does not claim equivalence for every JVM charset.
Evidence: artifacts/buffer-text-validation (ignored), buffer-text-validation-*.trx.
net10.0 Debug, Release and rebuilt checked Release pass 913/913, no failures/skips.
Checked build: zero errors and 51 existing Common warnings; no new Buffer/test
warnings. Common is unchanged and its suite was not rerun. Inventory remains 159:
126 pending, 27 in-progress, 5 verified, 1 CLR replacement. Sources/fixture remain
partial; remaining byte utilities, allocator/cache integration and broader APIs are pending.

## Remaining work

Unpooled factories cover heap/native allocation, single and multiple-input wrapping,
single and multiple-input copying, encoded text/primitive copying, unreleasable views, fixed read-only composites and the shared empty sentinel. Allocator interfaces/metrics,
external read-only storage/swapped buffers, remaining composite APIs/encoding/search/utilities, channel/native I/O,
borrowed-address wrapping, leak-aware wrappers and pooled/adaptive allocators remain unported. Most original
test classes and the rest of AbstractByteBufTest remain pending/in-progress.
Next units cover ByteBufUtil remaining byte utilities and I/O consumer mapping,
Unpooled factories, utilities and original tests, followed by allocator/cache integration. Common
changes must cite the actual buffer contract that requires them.
