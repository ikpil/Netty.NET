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
bytes: whole ASCII "챕" becomes E9 in Java, but Encoding.ASCII uses 3F; a lone
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

## Array extraction

ByteBufUtil.GetBytes now covers all three original overloads and retains their API
comments. Default input is the readable range and always copied; explicit ranges
may precede ReaderIndex or extend past WriterIndex up to Capacity. ReadOnlySequence
copies directly into the result, without a second byte-array flattening buffer.
Null, bounds and lifetime checks apply even to empty requests. Empty results use
Array.Empty<byte>(), including copy=true, rather than distinct Java empty arrays.

Copy=false allows sharing only when the requested complete buffer maps to an entire
writable managed array with offset zero and exact length. Existing Memory borrowing
and MemoryMarshal.TryGetArray replace Java hasArray/array/arrayOffset APIs. Read-only
roots/components, native storage, partial underlying arrays and segmented ranges
copy. Writable probing handles read-only components of an otherwise mutable composite
without exposing their arrays or failing the read. The returned array keeps managed
storage alive, but shared content is mutable; resize can detach old storage. Copying
provides a stable snapshot. No retain occurs; keep content/layout/lifetime stable
during extraction. Indices, marks and reference counts remain unchanged.

CLR sharing follows the writable contiguous memory mapping. A slice covering the
first whole array of a multi-component composite, or a single-array composite with
empty components before/after it, can therefore share where Java hasArray returns
false. This deliberate policy differs in 384 oracle rows; content still matches.
No new ByteBuf storage abstraction or Java array facade is introduced.

39 tests port the original GetBytes, nonzero-array-offset and larger-array-than-slice
scenarios, adding seven storage/view layouts, default/full/partial snapshot isolation,
absolute capacity ranges, 14 sharing configurations, write-through behavior, marks,
ownership, null/empty/dead/overflow guards and managed array resize/release behavior.
28672 Java/CLR content and index/mark/reference rows match per Debug/Release across
16 layouts, 128 seeds, seven ranges and both copy modes. Independent byte and
write-through alias models verify each runtime; sharing differences are recorded
separately. Twenty-four edge rows match except empty-copy identity reuse. Evidence:
artifacts/buffer-array-extraction-validation (ignored), buffer-array-extraction-*.trx.
net10.0 Debug, Release and rebuilt checked Release pass 952/952, no failures/skips.
Checked build: zero errors and 51 existing Common warnings; no new Buffer/test
warnings. Common is unchanged and its suite was not rerun. Inventory remains 159:
126 pending, 27 in-progress, 5 verified, 1 CLR replacement. Source/fixture remain
partial; AsciiString copying, allocator/cache integration and broader APIs are pending.

## AsciiString copying

All three ByteBufUtil.Copy overloads retain their original Java API comments.
AsciiString.AsSpan().Slice supplies logical source bounds and backing-array offsets;
all 256 octets copy unchanged, without ASCII decoding/re-encoding. Absolute copies
use SetBytes, may extend past WriterIndex up to Capacity, never grow and change no
indices. Relative/full copies use WriteBytes, may grow, and advance WriterIndex only
after success. ReaderIndex, marks and reference counts remain unchanged. Source null
and range checks precede destination validation, growth and mutation, using CLR
argument exceptions. Empty requests still validate logical source/destination ranges,
lifetime and write permission; the shared empty sentinel accepts a valid empty copy.

Existing span writes supply overlap handling: contiguous storage uses memmove and
segmented writes snapshot before changing components. Pinned Java instead performs
sequential component array writes; forward overlap where AsciiString and two destination
components share one array can overwrite later source bytes. Two oracle rows record
the deliberate CLR snapshot adaptation. Managed source arrays stay alive when heap
growth replaces destination storage. Composite write failures can leave earlier bytes
changed, and relative growth can remain, while WriterIndex stays unchanged. Callers
must exclude concurrent source content and destination content/layout/lifetime changes;
no reference is retained and no Common or storage API was added.

The pinned ByteBufUtilTest has no dedicated copy fixtures. The new Buffer-owned
tests cover actual consumer contracts from codec-http HttpHeadersEncoder.encoderHeader
(absolute name/value writes between colon-space/CRLF writes, followed by one explicit
WriterIndex publication) and HttpHeaders.encodeAscii (relative AsciiString writing).
This records consumer evidence without porting HTTP or claiming its fixture coverage.
54 new cases cover eight storage/view/permission layouts, all octets, copied and
nested offset sources, marks/ownership, growth/exhaustion, null/negative/overflow/end
bounds, empty/dead buffers, bidirectional same-array overlap, source aliasing during
heap growth, partial composite failure and HTTP header byte assembly.

13312 pinned Java/CLR byte/index/mark/reference/source-immutability rows match per
Debug/Release across eight layouts, 128 seeds and all overloads. An independent
byte model agrees. Twenty-three edge rows cover growth, aliasing, released buffers
and the empty sentinel; only the two documented overlap rows differ. Evidence:
artifacts/buffer-ascii-copy-validation (ignored), buffer-ascii-copy-*.trx.
net10.0 Debug, Release and rebuilt checked Release pass 1006/1006, no failures/skips.
Checked build: zero errors and 51 existing Common warnings; no Buffer/test warnings.
Common is unchanged and its suite was not rerun. Inventory remains 159: 126 pending,
27 in-progress, 5 verified, 1 CLR replacement. ByteBufUtil remains partial; this unit
adds no original test completion claim. Remaining utilities, allocator/cache integration
and broader APIs are pending.

## Allocator policy foundation

IByteBufAllocator and AbstractByteBufAllocator now connect real buffer allocation
policy to heap/native owners and composites. Optional parameters replace the Java
zero/one/two-argument factory overloads; IsDirectBufferPooled and ByteBuf.Allocator
are CLR properties. The interface retains every original comment. Implemented
class/constructor/growth comments are retained; unrelated instrumentation remains
upstream with partial coverage recorded in the manifest.

UnpooledByteBufAllocator creates managed heap or explicitly owned native storage,
with an optional NativeMemoryAllocator reservation domain. Buffer follows the
explicit preferDirect setting; HeapBuffer and DirectBuffer select their requested
storage, and IoBuffer selects native storage. CLR native owners reliably free their
allocations, so Java Unsafe/Cleaner detection does not gate these choices. The
current Default uses unpooled heap Buffer and native IoBuffer, preserving this
port's heap default; it does not implement Netty's global pooled/adaptive selection.
JVM Unsafe/no-cleaner provider switches are not exposed as inert CLR parameters.

Each allocator owns an immutable, permanently accessible (0,0) empty sentinel.
It is shared across all that allocator's zero/max-zero factories and has no native
reservation or reference-count cost. (0,positive-max) creates an owned growable
buffer. Initial/max and composite-limit checks precede allocation hooks. Defaults
remain 256 bytes, Int32.MaxValue maximum, and 16 composite components.

Heap/native owners, slices, duplicates, read-only/unreleasable views and copies
retain their creating allocator. Both EnsureWritable paths call its virtual
capacity calculation hook; the existing exact 64-byte/power-of-two/4-MiB kernel
is reused. Heap copies remain heap, native copies remain native. Composite padding,
consolidation and copies call the creator's heap/native factories according to
the composite's declared allocation policy. Existing constructors accepting a
native reservation domain keep it for later copies/composite allocations. Quotas
continue to include transient old/new native storage and outstanding physical pins;
failed growth/copy keeps original bytes/indices/reservations intact.

23 new cases translate original ByteBufAllocatorTest factory scenarios and
AbstractByteBufAllocatorTest capacity calculations, adding creator propagation,
actual custom growth/factory hooks, empty identity, invalid bounds/component limits,
consolidation/padding, native quotas/failure/pins and concurrent independent allocations.
16552 pinned Java/CLR capacity rows agree per Debug/Release, including threshold
and Int32 boundaries; an independent integer model agrees. Seventy-four factory,
view/copy and custom-growth rows agree on byte/index/reference/creator contracts.
Seventeen rows differ only in documented direct-storage selection: the Java21
noUnsafe oracle cannot reliably free direct buffers, while CLR owns native storage.
Evidence: artifacts/buffer-allocator-validation (ignored), buffer-allocator-*.trx.

net10.0 Debug, Release and rebuilt checked Release pass 1029/1029, no failures/skips.
Checked build: zero errors and 51 existing Common warnings; no Buffer/test warnings.
Common is unchanged and its suite was not rerun. Inventory remains 159: 121 pending,
31 in-progress, 6 verified, 1 CLR replacement. The complete original
ByteBufAllocatorTest factory scenarios are verified under the stated CLR selection
policy; the other five allocator source/test entries remain in-progress. Allocation metrics, leak-aware wrappers,
global default selection, pooled/adaptive allocators and original remaining test
scenarios are pending; creating an allocation interface does not complete them.

## Allocator-returning utilities

ByteBufUtil.ReadBytes, allocator WriteUtf8/WriteAscii and both EncodeString forms
now return buffers from IByteBufAllocator, retaining all five original API comments
and both allocation comments. UTF-16 input uses ReadOnlySpan<char>; AsciiString has
its own offset-aware raw-octet overloads. The returned reference belongs to the
caller. Every failure after allocation releases the new buffer, including native
storage, read bounds/permission failure and encoding exceptions after partial writes.
Pinned Java UTF8/ASCII allocation helpers omit this cleanup; four edge rows expose
their surviving reference and verify the corrected CLR release.

ReadBytes copies exactly length and advances source ReaderIndex/result WriterIndex
by length, preserving source WriterIndex, marks and reference count. Java instead
reads the result's writable capacity; two custom overallocating-factory edge rows
show the CLR correction prevents overread or a spurious short-source failure.
Null and negative arguments fail before allocation, while readable/lifetime bounds
are checked by the actual transfer even for empty reads. Empty results are ordinary
owned zero-capacity buffers with a growable maximum, rather than the shared sentinel.

UTF8 reserves three bytes per UTF-16 code unit and uses the existing Netty malformed
surrogate mapping; ASCII uses its one-octet-per-code-unit mapping. Raw AsciiString
copies every octet unchanged. Its allocator UTF8 overload retains the original
initial length allocation followed by the public writer's 3x reservation and growth.
EncodeString honors the caller's Encoding/fallback without a preamble. Unlike Java
maxBytesPerChar sizing, capacity is the exact encoded byte count plus checked,
nonnegative extraCapacity. ReadOnlySpan replaces the remaining CharBuffer range
without consuming a mutable cursor. Extra capacity stays unwritten for a suffix;
strict fallback and size overflow fail before allocation. Source content and Encoding
configuration must remain stable while sizing/writing. Existing ByteBuf writers
support heap/native/segmented results; Unpooled keeps its heap-only encoding policy.

47 new cases exercise six source layouts, three destination layouts, independent
copies/indices/marks, zero/dead/read-only cases, overflow/null/allocation failure,
native quota failure/cleanup, caller fallback, partial encoding failure, and actual
codec-base StringEncoder/LineEncoder span-range and separator-reservation consumers.
The original ByteBufUtilTest has no direct readBytes/encodeString fixtures; these
are additional Buffer contract tests, not a whole original fixture completion claim.
6708 pinned Java/CLR byte/index/ownership rows agree per Debug/Release and independent
models: 524 UTF16 inputs (including malformed pairs), 129 raw inputs, 896 valid text
inputs across seven encodings, and readable-bound transfers. 5396 rows match fully;
1312 differ only by documented exact encoding capacity. Nine edge rows record seven
adaptations (four releases, two exact-length reads and early null validation).
Evidence: artifacts/buffer-allocated-util-validation (ignored), buffer-allocated-util-*.trx.

net10.0 Debug, Release and rebuilt checked Release pass 1076/1076, no failures/skips.
Checked build: zero errors and 51 existing Common warnings; no Buffer/test warnings.
Common is unchanged and its suite was not rerun. Inventory remains 159: 121 pending,
31 in-progress, 6 verified, 1 CLR replacement. ByteBufUtil remains in-progress;
remaining scalar/I/O/cache/leak utilities and allocator metrics/pooling are pending.

## Unpooled allocation metrics

IByteBufAllocatorMetric and IByteBufAllocatorMetricProvider translate the complete
original getter/provider interfaces into read-only Int64 properties, preserving
all original comments. UnpooledByteBufAllocator exposes one stable live metric
object with separate atomic heap/native counters (Interlocked updates and Volatile
reads). Reading both properties is not a transactional snapshot.

Factory-created root capacities are charged to their creator. Successful resize
changes that charge; copies add their independent storage. Slices, duplicates,
read-only/unreleasable wrappers and additional references do not duplicate storage
charges. Final logical reference release removes the owner's capacity. Explicit
raw constructors and external wrapped storage stay uninstrumented as in Java;
their copies use the creator's charged factories. One optional internal counter
sink replaces Java's provider-specific instrumented subclasses without exposing
JVM switches or changing existing public constructor behavior.

Composite capacity itself has no storage charge. Its components remain charged
to their respective creators until release or consolidation; padding, consolidation
and copies charge actual new storage to the composite's allocator. Shrinking by
slicing a component still owns the component's original allocation. Independent
buffer allocators have independent metrics even when sharing a native reservation
domain. Rejected allocation/growth/copy and utility read/write failures leave no
surviving charge for an unsuccessful result.

CLR native pins preserve physical memory after resize/final buffer release, while
allocator metrics follow logical owned capacity. NativeMemoryAllocator.ReservedBytes
continues to include those outstanding pins, transient old/new storage and the
minimum one-byte reservation for a zero-length native owner. The shared (0,0)
sentinel has neither capacity nor reservation cost. These counters describe buffer
ownership, not process GC/native heap usage. Concurrent operations on independent
buffers are supported; this does not make concurrent mutation of one buffer safe.

21 new cases include all four original unpooled metric/32-MiB scenarios and both
original resize comments, plus growth/shrink/rejected sizes, copy/view/reference
ownership, raw constructors, composite storage, independent allocators, native
quota failures, utility cleanup, physical pins and concurrent allocation/release.
6144 pinned Java/CLR snapshots agree exactly per Debug/Release and an independent
owner-capacity ledger. Evidence: artifacts/buffer-metric-validation (ignored),
buffer-metric-*.trx. net10.0 Debug, Release and rebuilt checked Release each pass
1097/1097, no failures/skips. Checked build has zero errors and 51 existing Common
warnings; no Buffer/test warnings. Common is unchanged and its tests were not rerun.

Inventory remains 159 pinned Java files: 119 pending, 31 in-progress, 8 verified,
1 CLR replacement. Only the two small metric interfaces newly become verified.
Allocator implementations and inherited original fixtures remain partial; leak
wrappers, global default selection and pooled/adaptive allocators remain pending.

## Scalar and endian utilities

ByteBufUtil now implements IsAccessible/EnsureAccessible, EnsureWritableSuccess,
SwapShort/Medium/Int/Long, WriteShortBE, SetShortBE, WriteMediumBE,
ReadUnsignedShortBE and ReadIntBE, preserving all twelve original API comments.
BinaryPrimitives handles signed 16/32/64-bit reversal. SwapMedium ignores the
high byte, reverses the remaining three and sign-extends bit 23; all operations
remain correct under checked arithmetic. Writes keep the original low-word
truncation policy. The unsigned 16-bit read uses ushort in C#.

The existing unsuffixed ByteBuf operations are always BE and named LE methods
select LE explicitly, so BE helpers delegate without a mutable order facade.
Java BE and LE-view oracle paths produce the same wire bytes as these CLR APIs.
Writes/read operations retain existing bounds, permissions and index/mark rules;
SetShortBE is capacity-bounded and does not change indices. Accessibility only
observes the shared logical reference count; it neither retains nor guards against
a concurrent final release. EnsureWritableSuccess accepts exactly codes 0 and 2,
including rejection of forced-but-insufficient code 3. Null is an explicit CLR
ArgumentNullException. No new Common abstraction or byte-order wrapper is needed.

21 new cases cover five original endian scenarios and all four unsigned-short
inline comments, every short bit pattern, signed/24-bit boundaries, heap/native/
segmented/slice storage, read-only reads, unchanged marks, failure behavior,
borrowed/retained/shared-empty lifetime, actual growth status and HTTP CRLF/chunk
delimiters. Pinned consumers include HttpObjectEncoder/HttpRequestEncoder.
70772 pinned Java/CLR rows agree per Debug/Release and independent octet models:
65536 short patterns, 4108 medium/int/long vectors, 1120 storage/order/wire/lifetime
rows (560 Java LE-view rows mapped to explicit CLR BE), and eight status values.
Evidence: artifacts/buffer-scalar-validation (ignored), buffer-scalar-*.trx.

net10.0 Debug, Release and rebuilt checked Release each pass 1118/1118, no failures
or skips. Checked build: zero errors, 51 existing Common warnings, no Buffer/test
warnings. Common is unchanged and its tests were not rerun. Inventory remains
159: 119 pending, 31 in-progress, 8 verified, 1 CLR replacement. ByteBufUtil and its
original fixture remain in-progress; no full fixture completion is inferred.

## External read-only memory

ReadOnlyMemoryByteBuf maps the ReadOnlyByteBufferBuf storage contract to a fixed
ReadOnlyMemory range, and Unpooled.WrappedBuffer(ReadOnlyMemory<byte>) supplies the
factory. Slicing memory selects Java's remaining region; the wrapper starts at
ReaderIndex 0 / WriterIndex length with independent reference counting. It borrows
external storage without copying, retaining a ByteBuf parent or disposing its owner.
External writes are visible. Keep the external owner valid throughout all access.
Final wrapper release clears its memory reference; explicit owner disposal still
invalidates manager-backed access. Source leases pin through ReadOnlyMemory.Pin.

The original source's three comments, factory comment and both fixture licenses/
three inline comments are retained. Existing BE/LE primitives, read-only guards,
Memory/Sequence views, derived ownership, Stream and positioned-file paths are
reused. Copies use the creating allocator, are writable and independent, and use
Int32.MaxValue maximum (matching this original source rather than the fixed root
maximum). Allocated results are released on write failure. Wrapping external storage
has no allocator-capacity charge; copying and composite consolidation charge their
actual new storage. Empty factory input shares EmptyBuffer; explicit empty roots
remain owned/read-only. CLR negative/released EnsureWritable status calls validate
arguments/lifetime where Java unconditionally returns 1.

Actual Buffer consumers require preserving direct-copy selection. The sole Common
extension, NativeMemoryOwner.TryGetOwner, identifies its private memory manager via
MemoryMarshal without reflection, unsafe pointer exposure, pins or lifetime claims.
It recognizes slices and previously obtained disposed-owner memory as metadata;
memory access after disposal still fails. Known NativeMemoryOwner memory is direct.
Unknown managers, even potentially native third-party managers, use heap-copy policy
because ReadOnlyMemory has no general storage-kind metadata. This is a stated CLR
limitation, not an inference that every non-array manager is native.

28 new Buffer cases and two Common cases cover arrays/native/custom managers,
original selected copy/read/write/round-trip/derived-release scenarios, independent
concurrent copy ranges, Composite consolidation, sync/async Stream output, capacity
bounds, zero/dead/null cases, native quotas, pins and post-allocation cleanup.
2232 pinned Java/CLR byte/index/capacity/storage/copy/refcount rows agree per
Debug/Release and independent models, with two explicit status-guard adaptations.
Evidence: artifacts/buffer-readonly-memory-validation (ignored), corresponding TRX.

net10.0 Buffer Debug/Release/rebuilt checked Release each pass 1146/1146 without
failures/skips. Common full Debug/Release each pass 2678, zero failed, 14 existing
skips; checked NativeMemoryContractTest passes 28/28 with no skips.
Buffer checked build has zero errors and 51 existing Common warnings; no Buffer/test
warnings. Inventory: 159 files, 116 pending, 34 in-progress, 8 verified, 1 CLR replacement.
The source and both original fixtures remain partial; writable external Memory,
mapped-file/raw-address/provider-specific and remaining inherited scenarios are open.

## Writable external memory

Unpooled.WrappedBuffer(Memory<byte>) ports the writable NIO wrapping branch.
The selected range is borrowed at reader 0 / writer length, without copying,
retaining an owner or disposing it on Release. Array-backed ranges reuse existing
array wrapping (whole array root or fixed partial slice). Known NativeMemoryOwner
ranges use the new UnpooledDirectByteBuf wrapping constructor. Its public maximum
and optional native reservation domain also support explicit growable wrappers.
The original constructor and factory comments are retained. Mutable NIO position/
order are represented by explicit Memory slicing and BE/LE operations.

For native roots, equal capacity keeps aliases. Any changed capacity detaches to
owned zeroed native storage, preserves the capacity prefix, and trims indices only
after successful allocation/copy. Original external memory stays valid and ceases
to alias after detachment; later resize/final release disposes only owned storage.
Failed allocation/copy leaves storage and indices intact (Java trims indices before
attempting a shrink allocation). Keep external owners valid during borrowed access.
Pins preserve their original physical allocation across detach or logical release.
Copies preserve the creator, direct kind and root maximum; a partial array slice's
copy inherits its underlying array root maximum, matching Java delegation. Raw wraps
and replacements stay outside factory metrics; copies charge their actual allocator.

Unknown MemoryManager implementations have no general native-kind metadata or Java
provider equivalent. A private fixed-capacity wrapper permits live writable access,
uses heap-copy policy, and never disposes the manager. Manager Pin behavior applies
when operations acquire a lease. Empty memory returns the shared sentinel. This is
an explicit CLR policy; third-party native managers are not claimed to be direct.

24 new cases include original wrapping/empty/selected-address scenarios (address
comparison through Memory.Pin), BE/LE and overlapping writes, copies/shared views,
Composite/Stream consumers, quotas, failed resize/copy cleanup, external ownership
and pins. net10.0 Debug/Release/rebuilt checked Release each pass 1170/1170 without
failures/skips. 3200 pinned Java/CLR snapshots match per Debug/Release and independent
memory/capacity/ownership models. Checked build has zero errors and 51 existing
Common warnings; no Buffer/test warnings. Common sources are unchanged this unit.
Evidence: artifacts/buffer-writable-memory-validation (ignored), corresponding TRX.
Inventory remains 159 files: 116 pending, 34 in-progress, 8 verified, 1 CLR replacement.
Whole sources/fixtures remain partial; raw-address/provider wrapping, mutable order
facades and remaining inherited scenarios have not been declared complete.

## External memory copying

Unpooled.CopiedBuffer(ReadOnlyMemory<byte>) and its params memory-array overload
port the single/multiple NIO copying factories. Writable Memory converts to the
read-only input type; Memory slicing selects Java's remaining region. Results are
independent writable heap roots at reader 0 / writer copied length, with maximum
equal to that length (unlike the growable single-ByteBuf copy). Source kind/read-only
status do not change result kind. Empty input shares the sentinel; empty managers
are not accessed. Null lists fail; default Memory values represent empty ranges.

Two original factory docs and all five inline comments are retained. Immutable
range metadata replaces duplicate/save/restore of NIO cursors. ReadOnlyMemory has
no byte-order metadata: bytes remain unchanged, results use normal BE defaults and
explicit LE accessors, and there is no mixed-order validation. The pinned Java
single LE NIO copy defaults to BE through duplicate(); a multiple LE copy preserves
LE order, and mixed nonempty orders throw. Three oracle edge rows record this CLR
adaptation instead of claiming that the order facade was ported. The empty sentinel
is direct in both runtimes; nonempty copy results are heap.

Total length is checked before allocation or source access. Source owners must
remain valid during the call; they are never retained, pinned or disposed. Copies
survive immediate source ByteBuf release or external owner disposal. A source Span
failure publishes no result, leaves input ownership/ranges unchanged, and lets GC
reclaim the unpublished managed array. Raw wrapped result arrays follow the existing
factory metric policy rather than charging a second allocator-owned root.

26 new cases cover the original selected NIO copying/empty scenarios and CLR
array/native/custom-manager ranges, mixed/repeated/empty inputs, aliases, source
indices/marks, overflow without huge allocation, failure/recovery, lifetime, capacity
and endian operations. net10.0 Debug/Release/rebuilt checked Release each pass
1196/1196 without failures/skips. 6994 pinned Java/CLR rows match per Debug/Release
and independent byte/ownership models, plus the order edge records. Checked build
has zero errors and 51 existing Common warnings, no Buffer/test warnings. Common
sources are unchanged. Evidence: artifacts/buffer-memory-copy-validation (ignored),
corresponding TRX. Inventory remains 159 files: 116 pending, 34 in-progress, 8 verified,
1 CLR replacement. Unpooled and its original fixture remain partial; provider-specific
and multi-memory wrapping, leak wrappers and pooled/adaptive allocation remain open.

## Multiple external memory wrapping

Unpooled.WrappedBuffer now accepts params Memory<byte>[] and ReadOnlyMemory<byte>[],
including explicit maximum-component overloads. The two original NIO factory docs
and generic-strategy comments are retained. Cached static delegates replace Java
ByteWrapper strategies; no intermediate ByteBuf array is required. Single-array
arity reuses the single-range wrapper. Multiple supplied ranges with at least one
nonempty range create a composite, even when only one range is nonempty. Empty
ranges are skipped; all-empty inputs share the sentinel. Limits are validated only
when a composite is actually created, matching Java. Null lists fail; default Memory
values are empty and cannot represent Java's null-element termination.

Ranges borrow external storage, so original writes remain visible until consolidation.
Keep owners valid throughout access. Each range gets an independent wrapper reference;
repeated ranges can alias the same bytes without acquiring owner disposal rights.
Component-limit consolidation runs once after the full batch, copies into owned heap
storage and ends aliasing for every component. Read-only components reject writes;
the ordinary composite IsReadOnly flag remains false, and consolidated storage is
writable. Existing ByteBuf[] wrapping supports explicitly mixed mutability. Memory
has no mutable NIO cursor/order; explicit slicing and BE/LE access remain the policy.

Insertion/overflow/consolidation failure releases the inaccessible temporary composite
and its acquired wrapper references, plus any failed owned replacement. External
owners are neither retained nor disposed, including on failure. CLR source-access
exceptions remain explicit; a later retry with valid memory works.

27 new cases cover selected original NIO-array wrapping/empty scenarios across
array/native/custom manager backings, captured offsets, readonly writes, arity,
full-batch consolidation, repeated wrappers/retained views, limits/nulls, overflow
without huge allocation and source failure/retry. net10.0 Debug/Release/rebuilt
checked Release each pass 1223/1223, no failures/skips. 6084 pinned Java/CLR rows
agree per Debug/Release and independent segment/ownership models, including initial
allocator charges and final zero balances. Checked build has zero errors and 51
existing Common warnings; no Buffer/test warnings. Common sources are unchanged.
Evidence: artifacts/buffer-memory-wrap-validation (ignored), corresponding TRX.
Inventory remains 159 files: 116 pending, 34 in-progress, 8 verified, 1 CLR replacement.
Unpooled, CompositeByteBuf and their original fixtures remain partial; provider/raw
address wrapping, leak detection and pooled/adaptive allocation are still open.

## Buffer holders and conversion

IByteBufConvertible, IByteBufHolder and DefaultByteBufHolder port the complete
three original small source contracts. Every ByteBuf returns itself from AsByteBuf
without changing reference counts, checking accessibility or changing state, including
after release. Holder does not implement this conversion interface, matching Java.
The CLR interface prefix and read-only Content/ReferenceCount properties follow
existing conventions; ownership remains explicit through IReferenceCounted.

Holder construction adopts the caller's existing buffer reference. Content checks
accessibility. Copy owns independent readable bytes; Duplicate shares storage and
the existing reference, with independent indices; RetainedDuplicate acquires one
reference. All three call virtual Replace on the stored buffer's result, independently
of an overridden Content accessor. Derived messages can preserve their type/metadata
by overriding Replace. Replace adopts new content without retaining it or releasing
old content, and can run after old content release. As in Java, a Replace override
is responsible for accepting the supplied content, including on failure; clone
methods do not add rollback around a throwing override. Underlying buffer CLR
copy/lifetime policies still apply. No IDisposable ownership is inferred.

Retain/Touch return the holder, Release returns the underlying final-release result,
and explicit IReferenceCounted bridges preserve Common ReferenceCountUtil consumers
and virtual dispatch. Equality requires exact runtime class before comparing readable
bytes; identity remains reflexive after release. Derived extra-field equality stays
symmetric. Hash delegates to the current buffer content and is not stable across
mutation/index changes. ContentToString is protected and nonvirtual, bypassing Content
and reference checks. ToString preserves the holder-name/content shape and works
after release, using the existing CLR ByteBuf diagnostic string; exact Java diagnostic
formatting is not claimed.

All original comments in the three sources and original fixture are retained, plus
the ByteBuf.asByteBuf documentation. The complete three-test original fixture uses
named distinct subclasses for Java anonymous subclasses. 37 new cases also cover
heap/native/readonly/composite/fixed-composite/slice/duplicate backings, bytes and
indices/marks, copy/retained lifetime, subclass factories/equality, Common interfaces,
Touch hints, null/count errors, dead access/diagnostics and empty/unreleasable policies.
net10.0 Debug/Release/rebuilt checked Release each pass 1260/1260, no failures/skips.
6272 pinned Java/CLR rows agree per Debug/Release and independent byte/index/hash/
reference models over 1344 scenarios. Read-only exceptions are normalized to rejected
writes; the probe compares actual write permission rather than the pinned fixed
composite's IsReadOnly metadata. Checked build has zero errors and 51 existing Common
warnings, no Buffer/test warnings. Common sources are unchanged. Evidence:
artifacts/buffer-holder-validation (ignored), corresponding buffer-holder-*.trx.
Inventory remains 159 files: 112 pending, 34 in-progress, 12 verified, 1 CLR replacement.
Only these three small sources and original fixture are newly complete; ByteBuf and
the Buffer module remain partial.

## Composite buffer construction

The public CompositeByteBuf constructors accepting params ByteBuf[] and
IEnumerable<ByteBuf> now adopt existing references and captured readable ranges.
ReaderIndex starts at zero and WriterIndex at final capacity. Empty components are
owned components rather than skipped ranges. Null ends insertion and releases the
remaining yielded tail. A composite supplied as IEnumerable is adopted as one
buffer. List storage replaces Java component-array sizing; enumerable inputs are
streamed without materialization or collection-size inspection. Consolidation runs
once after the complete batch and follows the creator's heap/native policy.

Ownership preflight follows the existing AddComponents contracts: invalid allocator/
limit/null arguments do not consume inputs; array length overflow leaves inputs
caller-owned, whereas a streaming overflow consumes the successfully yielded prefix,
the failing entry and yielded tail. Retain each supplied reference first if the caller
needs independent ownership. Source indices/marks are preserved; structural changes
and storage lifetime remain subject to the existing composite borrowing rules.

Unlike a successful AddComponents call on an existing composite, a failed constructor
cannot return the partial composite for caller cleanup. CLR construction therefore
releases acquired components on insertion, enumeration, source-copy or consolidation
failure. Cleanup bypasses derived Deallocate callbacks before derived initialization,
and releases each acquired entry safely. A persistently failing enumerator can leave
unyielded inputs caller-owned; disposal follows IEnumerator. Array preflight failures
still leave all inputs caller-owned. Four pinned runtime failure rows reproduce Java
retaining its acquired references when consolidation allocation fails; CLR releases
them. This is an explicit ownership correction, not an exact failure-behavior match.

26 new cases cover captured ranges/marks, readonly aliases, mixed heap/native creator
policy and live metrics, params/enumerable/ByteBuf dispatch, owned empties, null tails,
invalid arguments, overflow without large allocations, insertion/copy/allocation
failure and retry, enumeration failure/disposal and derived construction cleanup.
Original constructor-bound/overflow scenarios inform these tests; no original test
method or entire fixture is claimed newly complete. The pinned public constructors
have no operation comments; license/class and all reused batch comments are retained.
net10.0 Debug/Release/rebuilt checked Release pass 1286/1286 without failures/skips.
4608 pinned Java/CLR rows agree per configuration and independent byte/index/metric/
reference models over 1152 scenarios, plus the four explicit failure adaptations.
Checked build has zero errors and 51 existing Common warnings, no Buffer/test warnings.
Common sources are unchanged. Evidence: artifacts/buffer-construction-validation
(ignored), corresponding buffer-construction-*.trx. Inventory is unchanged at 159:
112 pending, 34 in-progress, 12 verified, 1 CLR replacement. CompositeByteBuf and
AbstractCompositeByteBufTest remain partial.

## Simple leak-aware buffer wrappers

WrappedByteBuf now provides the forwarding base required by the actual
SimpleLeakAwareByteBuf consumer. CLR shared index/mark references and bounded memory/
sequence hooks replace the Java forwarding method table. Storage kind, readonly flags,
capacity, allocator, ownership, derived views, copy/discard/search and native leases
delegate to the parent; construction acquires no extra reference. The base returns
ordinary unwrapped derived views, and the leak-aware subclass wraps them through a
virtual factory. Existing unpooled/view/composite CLR storage policies remain applicable.

SimpleLeakAwareByteBuf shares the exact original tracked object and tracker across
Slice/ranged Slice/Duplicate/ReadSlice/AsReadOnly and four retained variants. A final
Release closes the tracker exactly once; partial release does not close it. Closure
executes independently of debug assertions. Simple Touch does not record accesses or
forward hints. Copy has independent ordinary allocation/lifetime and does not close
the source tracker. Failed Retain/Release and retained-view operations attach the
original close trace to the same IllegalReferenceCountException and rethrow it.
Existing Common ThrowableUtil stores CLR suppressed diagnostics; callers retrieve
them with GetSuppressed rather than relying on Exception.ToString to render them.

94 new cases adapt the original nine view contracts and two double-free release
mechanisms, including real Common tracking/close-location capture without global
settings changes. They cover seven storage/view backings, tracked identity, root/view/
counted final release, copy isolation, shared indices/marks, failed references, wrapper
factory overrides, null/count errors, immortal/unreleasable parents, native pin lifetime
and encoding/growth provenance. A native pin can keep physical storage reserved after
logical final release and tracker closure, matching the existing CLR ownership policy.
InternalsVisibleTo exposes the internal wrapper only to Buffer.Tests as in Common.

net10.0 Debug/Release/rebuilt checked Release pass 1380/1380 without failures/skips.
12096 pinned Java/CLR rows match per configuration and independent byte/index/reference/
tracker models over 2016 scenarios. The probes explicitly disable automatic allocator
tracking to isolate this wrapper, normalize readonly exceptions and read CLR suppressed
diagnostics through GetSuppressed. Checked build has zero errors and 51 existing
Common warnings, no Buffer/test warnings; Common sources are unchanged. Evidence:
artifacts/buffer-simple-leak-validation (ignored), corresponding buffer-simple-leak-*.trx.

Implemented operation/license comments are preserved. Pooled independent derived-count
reparenting/forced tracking and swapped-order branches (including their comments),
advanced access recording, composite wrappers and allocator auto wrapping remain pending.
The inherited original fixture is incomplete; all three source/test entries remain
in-progress. Inventory remains 159: 110 pending, 36 in-progress, 12 verified,
1 CLR replacement. This unit does not enable automatic leak detection for allocations.

## UTF-8 subsequences and ASCII reservations

Utf8Bytes/WriteUtf8/ReserveAndWriteUtf8 now support [start,end) on ReadOnlySpan<char>
and AsciiString, alongside explicit full AsciiString reservations. Span slicing reuses
existing encoding without Java CharSequence/subsequence objects. AsciiString preserves
raw octets, including 0x80-0xff and nested backing offsets. Range endpoints are checked
before subtraction or destination growth, including extreme Int32 values. WriteUtf8
reserves three bytes per selected code unit even for AsciiString; explicit reservations
can be exact or larger. Only actual bytes advance the writer; marks/counts are preserved.
Native character aliases survive growth under existing leases. Raw byte overlap follows
SetBytes. Failed composite writes can change earlier bytes without advancing the writer.
Six pinned Java probes show undersized reservations can write beyond the reservation
or fail after changing a byte, depending on storage. CLR consistently rejects them
before growth/mutation, extending the existing bounded UTF-8 policy to raw AsciiString.
Original implemented operation and selected test comments are preserved.

44 new cases adapt seven original subsequence/count/invalid-range scenarios and cover
six backings/views/wrappers, all raw octets, malformed/split surrogates, bounds, growth,
readonly/dead/empty/null destinations, aliasing and partial-write failures. net10.0
Debug/Release/rebuilt checked Release pass 1424/1424 without failures/skips. 36536
pinned Java/CLR rows match per Debug/Release and independent Unicode/octet/index/mark/
capacity/reference models; six undersized-reservation rows record the CLR adaptation.
Checked build has zero errors and 51 existing Common warnings, no Buffer/test warnings.
Common is unchanged. Existing utility source/fixture stay partial; inventory remains
159 (110 pending, 36 in-progress, 12 verified, 1 CLR replacement). Evidence:
artifacts/buffer-utf8-range-validation (ignored), buffer-utf8-range-*.trx.

## Relative buffer transfers

Eight missing ByteBuf overloads now preserve the original cursor contracts: default/
length GetBytes append to the destination writer; default/length SetBytes consume the
source reader; default ReadBytes fills destination WritableBytes and advances both;
explicit-destination ReadBytes advances only the source reader; default WriteBytes
consumes all readable input; explicit-source WriteBytes advances only the destination
writer. Get/Set/Read never grow the destination. Writes may grow, including before an
invalid absolute source range is detected, as in Java. Marks/counts stay unchanged and
all methods return their receiver. Pinned ByteToMessageDecoder MERGE_CUMULATOR uses
the explicit-source write and advances its input separately after success.

The existing bounded bulk kernel supplies CLR span/snapshot overlap semantics and
native alias safety. Java repeats overwritten bytes in three composite self-copy
probes; CLR retains memmove behavior. Failed composite writes can change earlier bytes
without publishing cursor changes. Default Get/Read check destination lifetime before
reading WritableBytes; Java reports bounds/argument errors in two released-destination
probes instead. Null arguments fail early. Array ranges continue to use Span slices.
All eight original API comments and original test license are preserved.

130 new cases cover 48 source/destination pairs, all overloads, capacity/readable bounds,
48 self-copy combinations, marks/ownership, readonly/null/dead/empty failures, growth,
partial composite writes and native aliases. net10.0 Debug/Release/rebuilt checked
Release pass 1554/1554 without failures/skips. 36910 pinned Java/CLR rows agree per
Debug/Release with independent byte/cursor/mark/capacity/reference models. The primary
Java probe uses its default Unsafe provider; a separate no-Unsafe probe agrees on 36526
rows and rejects 128 direct-to-composite copies that CLR supports. These provider limits,
three overlap differences and two dead-destination diagnostics are explicit adaptations.
Checked build has zero errors and 51 existing Common warnings, no Buffer/test warnings.
Common is unchanged. The three affected original source/test entries stay in-progress;
inventory remains 159 (110 pending, 36 in-progress, 12 verified, 1 CLR replacement).
Evidence: artifacts/buffer-relative-transfer-validation (ignored), buffer-relative-transfer-*.trx.

## Decoder storage hints and allocated reads

IsContiguous and MaxFastWritableBytes port the two storage hints consumed by pinned
ByteToMessageDecoder MERGE_CUMULATOR. Heap/native/Memory owners and empty sentinels
are contiguous; composite/fixed-composite buffers remain false even after consolidation
to one component. Derived/read-only views forward topology but use their own WritableBytes
for the fast-writable hint. Transparent/unreleasable wrappers forward both hints, including
an overridden parent hidden reservation. The hint does not grant read-only write access.
The test-only reserved owner verifies delegation; pooled allocators remain unimplemented.

ReadBytes(length), also used in the original decoder documentation example, now calls
Allocator.Buffer(length, MaxCapacity) rather than constructing a raw heap owner. Nonempty
results follow the creator heap/native policy, participate in its metrics and have
independent writable ownership. Only the requested bytes become readable even if an
allocator reserves extra capacity. Zero reads retain the shared empty sentinel; validation
precedes allocation. Source ReaderIndex advances only after success. Existing CLR cleanup
releases the new output on copy failure; pinned AbstractByteBuf.readBytes lacks this cleanup.
Both original hint API comments and the selected default-fast-writable test comment are
preserved. Existing Unpooled defaults and explicit native Copy reservation policy remain.

43 new cases cover original fast-writable/read scenarios, nine storage topologies, six
view/wrapper kinds, structural changes, hidden reservation forwarding, allocator choice,
metrics, independent release, invalid/dead/zero reads, extra capacity and failure cleanup.
net10.0 Debug/Release/rebuilt checked Release each pass 1597/1597, no failures/skips.
4032 pinned Java/CLR rows agree per Debug/Release: 1728 topology/view rows and 576
allocated-read scenarios with four snapshots each. Independent models check storage,
capacity, bytes, indices, marks, ownership and metrics. Java uses its default Unsafe
provider; automatic allocator leak wrapping is disabled to isolate manually wrapped views.
Checked build has zero errors and 51 existing Common warnings, no Buffer/test warnings.
Common is unchanged. All affected original entries remain in-progress; inventory remains
159 (110 pending, 36 in-progress, 12 verified, 1 CLR replacement).
Evidence: artifacts/buffer-decoder-policy-validation (ignored), buffer-decoder-policy-*.trx.

## Stateful single-pattern search

The search subpackage now has ISearchProcessor, ISearchProcessorFactory,
AbstractSearchProcessorFactory, KmpSearchProcessorFactory and BitapSearchProcessorFactory.
Factories precompute immutable tables; each processor carries independent mutable session
state and Reset. Pass processor.Process as the existing Func<byte,bool> visitor to
ByteBuf.ForEachByte (or AsciiString.ForEachByte). False reports the last byte of a match.
Continuing at that byte plus one finds overlapping occurrences; retaining the same
processor across buffer boundaries finds fragmented patterns without assembling input.

KMP uses a copied pattern and safe managed prefix-table reads with int positions.
Bitap uses ulong masks and the full C# byte alphabet, preserving all 64 bits in checked
builds. The original byte[] factories reject null; additional ReadOnlySpan<byte> factories
accept stack/sliced patterns without retaining borrowed memory. Bitap supports 1-64 bytes;
KMP supports longer managed patterns. Both reject empty patterns at construction. Pinned
Java no-Unsafe KMP constructs then throws on Process for empty input; Java Bitap silently
never matches. These two edge behaviors are explicit CLR adaptations. No Common changes
or new visitor overloads are required. All original comments in the seven mapped files
are preserved; original Java examples are accompanied by the CLR method-group usage.

63 new cases include all five original SearchProcessorTest methods for KMP/Bitap and both
Bitap limit tests, eight storage/view/wrapper kinds, every split boundary, binary/high-bit
patterns, independent state/reset, ownership/marks and concurrent factory reuse. A separate
sliding-window model checks deterministic random sessions. 3144 pinned Java/CLR sessions
agree per Debug/Release across six backings, pattern lengths 1-64 plus KMP 65/128/1025,
pattern mutation after factory creation, fragmented input and reset boundaries. Java uses
the no-Unsafe provider; no cross-runtime performance equivalence is claimed.

net10.0 Debug/Release/rebuilt checked Release each pass 1660/1660, no failures/skips.
Checked build has zero errors and 51 existing Common warnings, no Buffer/test warnings.
The five source entries and Bitap limit fixture become verified. At this stage
SearchProcessorTest was partial; its Aho-Corasick parameter is completed in the next unit. Inventory remains
159 (103 pending, 37 in-progress, 18 verified, 1 CLR replacement).
Evidence: artifacts/buffer-search-validation (ignored), buffer-search-*.trx.

## Stateful multiple-pattern search

IMultiSearchProcessor/IMultiSearchProcessorFactory, AbstractMultiSearchProcessorFactory
and AhoCorasicSearchProcessorFactory port simultaneous Aho-Corasick search. Original
Java spelling is retained in the public type/factory name for source traceability.
Use processor.Process with ForEachByte and read FoundNeedleId at a match. The returned
buffer index is the match end; continuing at the next byte preserves overlaps and
retaining the processor across buffers preserves fragmented matches. At the same end
position the longest registered pattern wins; duplicate patterns choose the last ID.
FoundNeedleId follows current state and resets to -1. An empty scan leaves it unchanged.

Dense 256-way transitions, breadth-first suffix linking, inherited suffix matches and
signed success transitions follow pinned Java. CLR List<int>/Queue<int>, checked row
capacity, int offsets and safe managed array indexing replace boxed collections, Unsafe
access, long offsets and the temporary Context object. Precomputed arrays retain no
caller patterns and are shared by independent session processors. Typed/base factory
interface dispatch reaches the same creation path through explicit CLR implementation.
An empty collection matches nothing; null collections/elements throw ArgumentNullException,
and empty patterns throw ArgumentException. Original comments are all preserved.

69 added cases include all three original multi-pattern tests on eight storage/view/wrapper
kinds and complete the original single-pattern fixture Aho-Corasick parameter. They cover
longest/duplicate choice, suffix fallback, full binary alphabet, 1025-byte shared prefixes,
every buffer split, marks/indices/ownership, state/reset, caller mutation, empty/invalid
patterns, independent random suffix models and concurrent factory reuse.
1578 pinned Java/CLR sessions and 405990 byte decisions agree per Debug/Release and both
Java Unsafe providers with an independent longest-suffix/last-duplicate model. Six backing
kinds include composite and manual leak-aware wrappers; automatic leak wrapping is disabled.

net10.0 Debug/Release/rebuilt checked Release each pass 1729/1729, no failures/skips.
Checked build has zero errors and 51 existing Common warnings, no Buffer/test warnings.
Common is unchanged. Four source entries, MultiSearchProcessorTest and the previously
partial SearchProcessorTest become verified. Inventory remains 159 (98 pending,
36 in-progress, 24 verified, 1 CLR replacement). Buffer as a whole remains incomplete.
Evidence: artifacts/buffer-multi-search-validation (ignored), buffer-multi-search-*.trx.

## Pooled allocator size classes

SizeClasses and ISizeClassesMetric port the size/page classification required by
PoolArena allocation and PoolChunk page-run selection. The managed tables and integer
formulas preserve small/normal boundaries, aligned size lookup, raw size computation,
page ceiling/floor indices and normalization. This is allocator groundwork; pooled
storage, arena allocation and thread caches are not implemented by this unit.
All original comments from both source files are preserved.

BitOperations.Log2 replaces the Java PoolThreadCache helper. Constructor checks enforce
page/chunk powers of two, the original minimum page/maximum chunk bounds and alignment
prerequisites locally. Negative sizes, invalid indices and nonpositive page counts throw
ArgumentOutOfRangeException. Page-count shifting uses long before the chunk check;
unrepresentable normalized/aligned capacities throw OverflowException instead of Java
int wrapping. Index lookup still returns NSizes/NPSizes for out-of-chunk requests.
SizeIdx2SizeCompute deliberately computes raw classes while SizeIdx2Size includes alignment.

Pinned Java has an alignment/geometry edge: with page=4096, chunk=4096 and alignment=1024,
it constructs only 27 classes, then size2SizeIdx(4096) returns the out-of-range index 27;
no page-size class remains. Alignment=2048/4096 and chunk=8192 with alignment=2048/4096
also truncate the raw table. Java assertions catch the alignment=chunk case only.
CLR rejects a truncated final raw class with ArgumentException at construction.
Larger chunks continue to support alignment up to pageSize. This is a deliberate
diagnostic adaptation, not a claim that those broken Java tables are valid pool geometry.

72 added tests port all six size-calculation scenarios from PoolArenaTest directly,
including every byte through 16 MiB and every page through the chunk. The original
fixture's pageSize=8192/pageShifts=11 mismatch is retained verbatim. Additional cases
cover 57 configurations with a separate grouped-size model, maximum 1 GiB chunk,
aligned/raw tables, boundary queries and CLR error policies. Actual arena allocations,
address alignment, counters and cache behavior remain pending in that original fixture.

110058 pinned Java/CLR rows agree per Debug/Release across 61 configurations: all table
entries, near-class boundaries, lookup/range queries, deterministic random byte/page
requests, aligned normalization and maximum chunk. A separate grouped-integer/bisect
model checks every row without reusing the logarithmic index formulas. The five broken
alignment configurations are recorded separately with Java assertions enabled/disabled.
net10.0 Debug/Release/rebuilt checked Release each pass 1801/1801, no failures/skips.
Checked build has zero errors and 51 existing Common warnings, no Buffer/test warnings.
Common is unchanged. The two source entries become verified; PoolArenaTest becomes
in-progress. Inventory remains 159 (95 pending, 37 in-progress, 26 verified,
1 CLR replacement). Buffer as a whole remains incomplete.
Evidence: artifacts/buffer-size-classes-validation (ignored), buffer-size-classes-*.trx.

## Pooled chunk page runs

PoolChunk now contains page-run bookkeeping: whole-chunk initialization, size-class
bucket selection, lowest-offset allocation, trailing splits, boundary lookup and
two-sided coalescence. IPoolChunkMetric exposes the original usage/chunk/free metrics
as readonly CLR properties. Original handle bits and percentage rounding are retained.
This unit does not allocate backing memory or produce pooled ByteBuf instances.
Subpages, arena lists/destruction, huge chunks, pinned metrics and caches remain pending.

BCL PriorityQueue<int,int> replaces the private Java IntPriorityQueue; Dictionary<int,long>
replaces the run-boundary LongLongHashMap. Packed free-run values are unique priorities,
and missing boundaries return -1L. lock(object) provides reentrant mutual exclusion.
Original run comments and queue/interface comments are preserved, with the original
binary-heap description explicitly distinguished from the BCL replacement. The original
standalone queue fixture is replaced by consumer tests: no sentinel-insertion/duplicate
queue facade is introduced. Constructor checks matching page geometry and maxOrder<=14;
run calls check basic arguments. Callers still own exactly-once, same-chunk handle return.
Original conservative floor/ceiling bucket selection is retained, including failure to
use a 9-page tail for a request that starts at the 10-page bucket. Metric reads each take
the run lock; multiple property reads are not one atomic snapshot.

44 added cases cover split/reuse/exhaustion, all three-run free orders, one-page edges,
lowest bucket/offset selection, non-root queue removal, fragmentation, percentage
rounding, 24 independent page-occupancy configurations, eight-worker nonoverlap,
maximum 16384-page/1 GiB geometry and invalid arguments. 37112 pinned Java/CLR rows
agree per Debug/Release and an independent occupancy/free-span/bucket model, with Java
assertions enabled. Original Java uses an untouched zero-length dummy backing because
only its actual run bookkeeping methods execute; no storage equivalence is claimed.
All final chunks coalesce back to one whole run. net10.0 Debug/Release/rebuilt checked
Release each pass 1845/1845, no failures/skips. Checked build has zero errors and 51
existing Common warnings, no Buffer/test warnings. Common is unchanged.
PoolChunk remains in-progress; its metric interface is verified. IntPriorityQueue and
its private-heap fixture are recorded as CLR replacements. Inventory remains 159
(91 pending, 38 in-progress, 27 verified, 3 CLR replacements).
Evidence: artifacts/buffer-run-validation (ignored), buffer-run-*.trx.

## Pooled subpage slots

PoolSubpage and PoolChunk now allocate/free small slots through shared size-class heads,
bitmap words and available lists. Full subpages leave the list, first frees reinsert them,
the last freed slot is reused before lowest-hole scanning, and surplus empty subpages
return their page runs for coalescence. The last available empty run per size class is
retained across all chunks sharing the heads. AllocateSmall returns the actual owner,
including reuse from another chunk. IPoolSubpageMetric exposes the four original metrics.
All PoolSubpage/interface comments and implemented chunk/test comments are preserved.

ulong bitmaps preserve Java logical-shift/high-bit behavior under checked arithmetic.
Scoped Monitor locks replace ReentrantLock and explicit lock/unlock methods; each subpage
retains its head directly. Heads carry the immutable SizeClasses identity, and chunk
constructors capture a copy of caller arrays while sharing the head objects. Mutation
takes the head lock before any run lock. Invalid/double-slot frees fail before mutation;
callers still own exactly-once return to the actual chunk, including reissued handles.
The head sentinel PageSize is zero instead of Java's incidental 1 << -1 negative value.
LCM arithmetic uses long. For page/chunk=4096 and elem=48, pinned Java asserts that the
12288-byte LCM run fits; without assertions it accepts a shorter 4096-byte run. CLR
returns allocation failure without changing chunk/list state when the LCM cannot fit.

26 added cases cover bitmap/partial-word limits through 257 slots, every small class
in nine page/alignment settings, LCM partitioning, caching/eviction, shared-owner routing,
failure bounds and eight-worker nonoverlap. The original single-element page-return
condition from testNormalPoolSubpageRelease is checked with 1024 metadata allocations.
79642 pinned Java/CLR rows agree per Debug/Release across nine shared-head domains and
375 small-class configurations with an independent slot/list/gcd/page-occupancy model.
Java assertions are enabled; both runners exercise bookkeeping without backing storage.
net10.0 Debug/Release/rebuilt checked Release pass 1871/1871, no failures/skips; zero
build errors and 51 existing Common warnings, no Buffer/test warnings. Common is unchanged.
PoolChunk/PoolSubpage and the selected allocator fixture remain in-progress: real storage,
pooled ByteBuf initialization, arena/destruction hooks and caches are still pending.
Inventory remains 159 (88 pending, 40 in-progress, 28 verified, 3 CLR replacements).
Evidence: artifacts/buffer-subpage-validation (ignored), buffer-subpage-*.trx.

## Pooled heap storage and buffer lifetimes

Internal PoolHeapArena and PooledHeapByteBuf connect reviewed run/subpage metadata to
real GC.AllocateUninitializedArray<byte> backing. Small slots bind the actual owner's
array and original bitmap-relative offset; normal runs use page offsets; huge requests
use exact dedicated arrays. Logical capacity, normalized reservation, original resize
thresholds, prefix copying, index trimming, creator allocator and exactly-once returns
are connected. Both EnsureWritable overloads now consult MaxFastWritableBytes before
the allocator growth hook, as the original AbstractPooledByteBufTest requires.

Storage reservations have their own atomic lifetime. Internal span-write/composite
leases defer recycling old regions across buffer replacement or final release; logical
buffer reference counts still reach zero normally. Public Memory/Span borrows and array
pins do not retain reservations: callers must exclude replacement/final release during
access. Wrapper objects are never recycled, so stale CLR references remain released.
Shared Memory/Span primitives replace Java Unsafe/NIO providers. Existing views retain
the CLR shared-root count model; independently counted pooled derived classes remain
pending. Original comments from implemented paths and both selected fixture copyrights
are preserved. Private Monitor locks serialize capacity changes and arena bookkeeping;
byte/cursor access and final release still require caller synchronization.

This is an internal heap foundation. A test allocator exercises its real storage;
public PooledByteBufAllocator/default selection and native pooling are still pending.
Arena chunk selection currently scans registered chunks and retains pooled chunks for
the arena lifetime. Utilization lists, eviction/last-destroyed reuse, subpage destruction,
metrics, caches, event/leak integration and full inherited fixtures remain pending.
Heap native-address alignment is rejected. A small LCM run that cannot fit a tiny chunk
uses exact dedicated storage instead of Java's assertion/truncated-run behavior.

40 added cases cover all directly declared original pooled scenarios on heap, all size
classes in three geometries, nonoverlap/actual-owner binding, resize thresholds, copies,
views, stale wrappers, alias leases, composite release, primitive/stream offsets and
eight-worker lifetimes. The original normal-subpage release condition now runs through
1024 real 8192-byte buffers in one 16-MiB arena, with no caches. 6247 pinned Java/CLR rows
agree per Debug/Release across page sizes 4096/8192/65536: logical/reserved capacities,
fast writable space, indices, region reuse and SHA-256 of every preserved byte prefix.
An independent grouped-size/reservation/cursor/byte model checks every row. Fresh pinned
Java heap classes run with assertions enabled and Unsafe disabled; allocation selection,
cache/eviction performance, native storage and independent derived counts are not claimed.

net10.0 Debug/Release/rebuilt checked Release pass 1911/1911, no failures/skips. Checked
build has zero errors and 51 existing Common warnings, no Buffer/test warnings. Common
is unchanged. Five entries become in-progress; none are newly marked complete. Inventory
remains 159 (83 pending, 45 in-progress, 28 verified, 3 CLR replacements).
Evidence: artifacts/buffer-pooled-heap-validation (ignored), buffer-pooled-heap-*.trx.

## Pooled native storage and arena lifetimes

Internal PoolDirectArena and PooledDirectByteBuf now connect pooled regions to actual
NativeMemoryAllocator/NativeMemoryOwner storage. Heap and native arenas/buffers share
a nongeneric Memory<byte> core and one abstract test fixture. Original owner/offset
binding, resize thresholds, prefix copying and region leases apply to both. Direct
copies dispatch through the creator's DirectBuffer policy. Original implemented-path
comments, including the NIO-copy rationale and aligned-fixture copyright, are retained.

Native backing reserves the original alignment headroom and exposes bounded aligned
slices. Arena disposal closes new reservations, frees inactive native chunks and lets
active buffers/internal leases finish before freeing their owners. Dedicated native
regions return on their last lease. Raw pins defer physical SafeHandle release only;
they do not retain a pooled slot. Public Memory/Span/pointer access requires keeping
the logical reservation alive. Allocation limits/overflow fail before publication,
preserving the old buffer and indices. Open arenas still retain pooled chunks.

Two language/provider differences are explicit. Java aligned huge initialization can
publish normalized logical capacity above maxCapacity (131073 requested/max becomes
163840). CLR reserves those same bytes while keeping requested logical capacity and
Capacity<=MaxCapacity. Three such comparisons are recorded separately. Java's public
alignment gate requires Unsafe; CLR alignment uses NativeMemoryOwner independently.
Unaligned tiny-chunk fallback still uses exact dedicated storage; aligned fallback
reserves the normalized class.

61 added cases comprise 40 shared native scenarios and 21 alignment/ownership/failure
cases. net10.0 Debug/Release/rebuilt checked Release each pass 1972/1972, no failures or
skips. Checked build has zero errors and 51 existing Common warnings, no Buffer/test
warnings. 24984 pinned Java/CLR rows agree per Debug/Release across 12 page/alignment
domains, plus the three explicit logical-capacity adaptations. An independent grouped
size/reservation/cursor/SHA-256 model checks the results. Java assertions are enabled;
the safe direct provider also agrees on all 6246 unaligned rows. Shared-core heap
regression retains all 6247 prior Java/model rows per Debug/Release. Every native oracle
arena finishes with zero reserved native bytes. Common is unchanged.

This remains internal allocator infrastructure. Public allocator/default selection,
utilization lists/normal eviction, counters, caches, event/leak integration, independent
pooled-derived counts and complete inherited fixtures remain pending. No allocation
selection/cache/eviction performance equivalence is claimed. Three entries become
in-progress; none are newly complete. Inventory is 159 (80 pending, 48 in-progress,
28 verified, 3 CLR replacements).
Evidence: artifacts/buffer-pooled-native-validation (ignored), buffer-pooled-native-*.trx.

## Chunk utilization lists and retirement

PoolChunkList now connects original qInit/q000/q025/q050/q075/q100 membership to
actual heap/native arena storage. Allocation searches q050/q025/q000/qInit/q075,
applying each list's normalized-capacity bound and head order. Shared available
subpages are consulted first, including slots in q100 chunks. Existing-slot reuse
does not change chunk page usage. Original free-byte thresholds promote/demote chunks;
fully unused chunks descending past q000 retire. Never-promoted qInit chunks can remain
empty, and the original conservative bound rejects a whole-chunk request even there.

Heap keeps the first retired chunk for reuse; native releases retired owners at once.
Operation leases delay region return and retirement; raw pins delay physical native
free without making retired chunks reusable. Arena disposal drains lists, clears the
heap cache and stops new allocations, preserving active buffer/lease storage until
return. Registered storage remains listed after initial subpage metadata allocation
failure. Native owner cleanup stays under the CLR arena Monitor and calls no user/arena
callback. Coarse arena locking remains; thread caches/counters/events are pending.

IPoolChunkListMetric uses properties and IEnumerable<IPoolChunkMetric>. Enumeration
captures membership under the arena lock; chunk metrics remain live and retain no
storage. The outer list collection is read-only. List/chunk descriptions retain the
original format with CLR identity hashes/newlines. Detached chunks clear stale links.
All 35 original list comments and all four metric-interface comments are preserved.
Java's saturating double-to-int conversion for extreme qInit/q100 thresholds is explicit
in CLR checked/unchecked builds; AtomicReference is unnecessary under the arena Monitor.

21 added cases cover migration/retirement in three geometries on heap/native, initial
retention/conservative selection, head/middle/tail removal, shared subpages in full chunks,
snapshot/live metrics, heap reuse, native pins/leases and concurrent traversal. Prior
native tests now expect normal retirement instead of the earlier retain-all foundation.
Fresh pinned Java buffer sources with assertions enabled produce 27252 matching CLR
rows per Debug/Release across 18 page/order/heap/native/alignment domains. An independent
page-occupancy/free-span/usage/list model agrees, including 5517 non-head removals and
834 retired-heap reuses. Live first/last byte markers detect storage overlap/corruption.
Prior heap 6247 and native 24984 matching capacity/SHA-256 rows plus three documented
CLR logical-capacity adaptations remain unchanged per Debug/Release. Native runners
finish each arena with zero reserved bytes after disposal.

net10.0 Debug/Release/rebuilt checked Release pass 1993/1993, no failures/skips. Checked
build has zero errors and 51 existing Common warnings, no Buffer/test warnings. Common
is unchanged. List policy is connected; its source remains in-progress pending broader
public allocator/cache integration. The metric interface is verified. Inventory is
159 (78 pending, 49 in-progress, 29 verified, 3 CLR replacements).
Evidence: artifacts/buffer-chunk-list-validation (ignored), buffer-chunk-list-*.trx.

## Public pooled factories and reservation metrics

PooledByteBufAllocator now connects public heap/native factories, creator provenance,
growth, copies and composite allocations to the reviewed pooled storage kernel. Each
enabled storage kind has one arena; disabling its pool selects an unpooled buffer with
the same creator/native reservation domain. The constructor defaults to heap preference,
while PooledByteBufAllocator.Default prefers native storage, as in the original policy.
IByteBufAllocator.Default still selects the existing unpooled allocator. Geometry and
alignment are validated before backing allocation. Backing storage is lazy, including
valid 1-GiB chunk geometries. CLR options replace JVM CPU/memory/system-property heuristics.

This is deliberately a cache-free, single-arena implementation. Cache sizes/counts and
thread bindings are zero; Java creates one thread binding even with cache sizes zero.
That difference is asserted separately in the oracle, rather than hidden in equivalent
metric rows. Multi-arena assignment, thread-cache lifecycle/scheduling, allocator events,
automatic leak wrapping and global default selection remain pending. No cache-enabled
performance equivalence or complete original allocator/fixture coverage is claimed.

IPoolArenaMetric exposes every original arena metric and size-class operation through
CLR properties/read-only collections; all 28 original comments are preserved. The
allocator metric retains all 11 original comments. Reservation-class allocations and
returns, chunk creation/retirement, active counts and used/pinned bytes now reflect actual
arena storage. One arena Monitor replaces Java's split LongCounter/lock strategy;
counter arithmetic remains unchecked like Java. Separate property reads are not an
atomic snapshot. Available-subpage membership is snapshotted; individual metrics stay live.

Used bytes count registered chunks plus dedicated huge reservations. Pinned bytes count
normalized live reservations, including internal operation leases across resize/release.
Neither includes alignment headroom, the retired heap reuse cache or native memory held
only by a raw pin. NativeMemoryAllocator.ReservedBytes separately reports physical native
reservations. Disabled pools return -1 for unknown used/pinned bytes. Initial subpage
metadata failure leaves registered reusable storage charged without counting an allocation
that was never bound. Explicit CLR Dispose closes factories, drains idle chunks and keeps
active chunk/huge reservations charged until their last lease returns; Java has no matching
public shutdown operation. Unpooled fallback buffers retain independent lifetimes. Empty
sentinels/composites reserve no storage and remain available after allocator shutdown.

59 added cases cover public factories/fallbacks, original no-cache counters, subpage/list
metrics, used/pinned memory, resize/release, whole-chunk retirement, aligned huge storage,
native limits/failure retry, shutdown/leases/pins, consumer provenance, concurrent counts
and geometry/default policy. net10.0 Debug/Release/rebuilt checked Release each pass
2052/2052, no failures/skips. Checked build has zero errors and 51 existing Common warnings,
no Buffer/test warnings. Common is unchanged.

Fresh pinned Java public allocators and CLR public allocators agree on 31233 rows of 18
allocation/return/chunk/active/used/pinned/list-count metric fields per Debug/Release.
31230 capacity/cursor/SHA-256 byte rows also match across 12 native page/alignment and
three heap page domains; three aligned-huge logical-capacity adaptations retain the
previous documented CLR bound. An independent size/reservation/cursor/byte model checks
those rows and independently derives reservation-class transitions/metric invariants.
Native CLR arenas finish with zero reserved bytes. All 27252 prior Java/model chunk-list
rows remain unchanged per Debug/Release across 18 domains, including retirement/reuse.

Only the complete arena metric interface becomes verified. The public allocator and its
metric implementation remain in-progress pending broader arena/cache integration.
Inventory is 159 (75 pending, 51 in-progress, 30 verified, 3 CLR replacements).
Evidence: artifacts/buffer-public-pool-validation (ignored), buffer-public-pool-*.trx.

## Multiple pooled arenas and physical-thread bindings

Public pooled allocation now accepts explicit nonnegative heap/native arena counts.
The existing bool constructor retains its 0/1-arena behavior and default policy. Size
classes are shared within each storage kind; subpage heads, chunks and reuse caches
remain private to each arena. Backing storage stays lazy. Zero arenas use the existing
unpooled fallback. Used/pinned memory sums every arena with Java's overflow saturation.

First allocation on a physical thread selects the first arena with the fewest thread
bindings, independently for heap and native storage. Both choices remain stable until
that thread's binding is released. This retains Java's zero-cache-size binding and
least-used/tie-order policy. NumThreadCaches/NumThreadLocalCaches now count those actual
bindings even though buffer cache sizes remain zero. No arenas means no CLR binding.
Copies select the caller's arenas; slow resize initializes the caller's binding while
reserving replacement storage from the buffer's original arena. Fast resize within the
existing reservation does not initialize a binding. Cross-thread return uses the owning
arena; thread exit or reassignment never releases a live buffer's reservation.

CLR does not have FastThreadLocalThread cleanup. An allocator-owned registry uses weak
physical Thread identity and managed IDs, rather than AsyncLocal/ExecutionContext flow
or a thread-local root that could retain the allocator. ReleaseCurrentThreadBinding
explicitly releases the caller's assignment; ReleaseTerminatedThreadBindings reclaims
dead-thread assignments. New thread assignment and allocator binding-count reads also
reap dead registrations. Individual arena counts reflect the latest cleanup. Live CLR
pool workers keep their assignments until explicit release or allocator disposal.
These are metadata bindings, with no cached memory to flush and no finalizer/watcher.
Shutdown atomically closes factories and detaches all registrations before draining
every arena; buffers/internal leases keep their previous storage lifetime. The existing
factory Monitor remains, so multi-arena throughput equivalence is not claimed.

30 new cases adapt original thread-to-arena and aggregate binding-count scenarios and
cover unequal arena counts/ties, explicit/automatic cleanup, cross-thread resize/copy,
fast resize, execution-context flow, active buffers after thread exit, allocation failure
retry, aggregate memory, active leases at shutdown and concurrent assignment/release/close.
net10.0 Debug/Release/rebuilt checked Release pass 2082/2082, no failures/skips. Checked
build has zero errors and 51 existing Common warnings; no Buffer/test warnings. Common
and global default selection are unchanged. Implemented-path Java comments are retained.

Fresh pinned Java buffer sources with assertions enabled and CLR public allocators
produce 10024 identical rows per Debug/Release across 28 page/alignment/arena-count
domains. Seven dedicated threads execute allocation, copy, resize, return, binding release
and thread restart, including 740 resizes where the caller's assigned arena differs from
the buffer owner. An independent binding/reservation/counter/owner/SHA-256 model agrees.
Prior public allocator byte/metric regression retains all 31233 metric rows and 31230
matching capacity/byte rows plus three documented aligned-huge adaptations per build;
its prior CLR zero-binding assertion now correctly expects one, matching Java. Native
CLR runners end with zero reserved bytes after disposal. Inventory is 159 (74 pending,
52 in-progress, 30 verified, 3 CLR replacements). PoolThreadCache is only in-progress for
its binding/count lifetime subset; region caches, trimming/scheduling, allocator events,
automatic leak wrappers and broader original fixtures remain pending.
Evidence: artifacts/buffer-multi-arena-validation (ignored), buffer-multi-arena-*.trx.

## Pooled small/normal region caches and trimming

Each physical-thread binding can now own independent heap/native small and normal
region caches. Explicit smallCacheSize/normalCacheSize constructor options enable
caching on CLR threads; defaults remain zero, including static Default. Configured
sizes are reported unchanged, while bounded FIFO queues round capacity to the next
power of two (saturated at 1 << 30). Small classes ignore the normal-capacity limit.
Normal classes cover normalized sizes up to min(chunkSize, maxCachedBufferCapacity),
default 32768; huge/dedicated reservations bypass caches. Cache entries are value
records in lazy BCL queues, replacing Java Recycler entries and mutable NIO buffers.
Queue growth failure safely returns the handle to its arena. Negative sizes/capacity
and nonpositive cacheTrimInterval fail at construction before backing reservation,
including disabled-cache configurations (stricter CLR argument validation).

Cache hits do not increment arena allocation counts. Cached free handles remain
active arena allocations until drained; logical pinned bytes decrease after the
last buffer/internal operation lease returns, even when its handle enters a cache.
The allocation's original cache accepts cross-thread returns if still open and not
full. Late/full/ineligible returns go directly to the actual owning arena. A slow
resize uses the caller's cache: a miss allocates through the old arena, but a hit
can change the buffer's owning arena. Fast resize remains within its reservation.
Raw native pins preserve physical storage, without retaining a logical reservation.

TrimCurrentThreadCache does not create a binding. Each class frees up to rounded
cache size minus successful hits since its previous trim, then resets its hit count.
Eligible cache hits and misses count toward cacheTrimInterval (default 8192);
ineligible and huge requests do not. Manual trim and automatic sweeps use original
direct-small/direct-normal/heap-small/heap-normal order. Binding release, observed
thread termination and allocator Dispose close/drain caches exactly once; live
reservations stay usable and later bypass the closed cache. Lock order is allocator
then cache then arena; return drops the arena lock before entering the cache.
No timer, event executor, JVM finalizer or automatic thread-exit watcher is added.
Live CLR workers retain their cache until explicit cleanup or allocator disposal.
Scheduling, allocator events/leak wrappers and broader original fixtures remain pending.

29 new cases include the original normal-cache capacity-boundary scenario, FIFO/full
queues and rounded limits, hit-budget/manual/automatic trim, huge exclusions, separate
classes/storage kinds, cross-thread returns/resize, thread exit, close races, native
pins, internal leases, failed native allocation/resize and lazy extreme-size queues.
net10.0 Debug/Release/rebuilt checked Release pass 2111/2111 with no failures/skips.
Checked build has 51 existing Common warnings and zero Buffer/test warnings/errors.
Fresh pinned Java buffer sources and CLR allocators produce 40720 identical rows
per Debug/Release across 80 page/alignment/arena-count/cache-size domains. An independent
bounded-cache/trim/owner/counter/reservation/SHA-256 model agrees, including 1218 hits,
276 cross-arena hits, 612 automatic sweeps and 132 full-cache returns per runtime.
The previous cache-free 10024-row multi-arena trace remains a regression check.
Inventory remains 159 (74 pending, 52 in-progress, 30 verified, 3 CLR replacements).
Implemented-path Java comments are retained; JVM Recycler/finalizer-only paths are
explicit CLR adaptations, and complete source/fixture coverage remains in-progress.
Evidence: artifacts/buffer-region-cache-validation (ignored), buffer-region-cache-*.trx.

## Executor-based periodic cache trimming

A positive cacheTrimPeriod TimeSpan registers one fixed-rate trim when a physical
thread's first binding is created on ThreadExecutorMap.CurrentExecutor. Initial delay
and period are equal, matching the pinned Java allocator; zero disables scheduling.
Later executor mappings do not reschedule an existing binding. Explicit cache sizes
remain unchanged, including zero-sized caches. Shared empty buffers and fully unpooled
factories create neither a CLR binding nor a schedule. Common's existing executor
scheduler supplies deadlines/catch-up and cancellation; no separate timer is introduced.

The callback targets its original cache directly, permitting execution on another
physical worker without creating or trimming an unrelated binding. Registration and
fault observation suppress ExecutionContext flow, retaining no caller AsyncLocal data.
Immediate unsupported/rejected scheduling rolls back the binding and both arena counts
before storage reservation. A later backing allocation failure retains the single
binding/schedule for retry or cleanup. Later scheduler faults are observed and stop
that schedule; executor shutdown also stops it without draining cached handles. Manual,
allocation-driven and binding/allocator cleanup remain available.

Explicit binding release, observed thread termination and allocator Dispose cancel
the schedule before closing/draining its cache. Already-transferred callbacks cannot
revive a closed cache or trim a replacement binding; live buffers/leases keep their
previous lifetime. This is an intentional CLR adaptation: Java's initialValue ignores
the periodic future, and freeThreadLocalCache does not cancel that old task before a
new binding registers another. Cancellation avoids duplicate sweeps and retained CLR
objects. Negative explicit periods fail eagerly; extreme positive periods use Common's
saturating TimeSpan deadlines. No executor throughput equivalence is claimed.

26 added cases cover first deadlines, hit budgets, fixed-rate catch-up, absent/changed
mappings, zero caches, rollback/retry, transferred callbacks, thread cleanup, live leases,
ExecutionContext/GC and native lifetime. Real executors with independently controlled
clocks additionally verify separate physical-thread caches and concurrent return/close.
net10.0 Debug/Release/rebuilt checked Release each pass 2137/2137, no failures/skips.
Checked build has zero errors and 51 existing Common warnings; no Buffer/test warnings.
Common is unchanged. Implemented-path Java comments are retained.

Fresh pinned Java buffer sources and CLR allocators produce 22176 identical rows per
Debug/Release across 72 page/alignment/arena-count/cache-size/executor-mapping domains.
Rows include clock/deadline/queue/sweep state, cache/arena metrics, reservations and
SHA-256 bytes. An independent fixed-rate/cache/hit-budget/owner/counter/byte model
agrees, including 2772 periodic sweeps and 392 allocation-driven sweeps per runtime.
All 40720 prior region-cache Java rows remain unchanged per Debug/Release with periodic
trimming disabled. Native CLR runners finish with zero reserved bytes after disposal.
Inventory remains 159 (74 pending, 52 in-progress, 30 verified, 3 CLR replacements);
allocator/cache source and complete fixtures remain in-progress.
Evidence: artifacts/buffer-cache-scheduling-validation (ignored), buffer-cache-scheduling-*.trx.

## Advanced leak-aware buffer access records

AdvancedLeakAwareByteBuf records existing data/view/capacity operations before
delegating, including rejected accesses. Retain/Release and Touch always record;
io.netty.leakDetection.acquireAndReleaseOnly suppresses other records, matching
Java's static property (CLR process environment). Touch preserves the outer wrapper
and optional hint. Common's existing stack exclusions hide Touch and the recording
helper. Final release and original-close diagnostics remain SimpleLeakAwareByteBuf's
responsibility; shared/retained views keep the advanced wrapper and original tracker.
Copies retain independent allocator ownership.

Existing ByteBuf data methods become virtual so the wrapper can intercept each
public operation once and forward directly to its underlying buffer. This avoids
recording nested primitive implementation calls, preserves fluent receiver identity
and shared cursors/marks, and leaves ordinary buffers' implementations unchanged.
WrappedByteBuf also forwards hash/equality/comparison directly as Java does, avoiding
spurious records from the wrapper's own primitive reads during those operations.
141 operation overrides also cover CLR Span/Memory/ReadOnlySequence and ValueTask
stream/file transfers. Async calls record once when started; existing cancellation,
borrowed lifetime and success-only cursor publication remain in effect. Explicit
little-endian floating-point APIs also record (a CLR extension beyond Java's explicit
override list). Byte-order facades, remaining Java-only NIO/channel surfaces, pooled
independent derived counts, composite leak wrappers and automatic allocator wrapping
remain separate work; this internal wrapper is not yet selected by allocator factories.

762 added cases include the original sliced-component regression, 123 operations
across six storage/view kinds, pooled native/heap retained lifetimes, failed-reference
diagnostics, metadata/hints, memory access, sync/async stream/file I/O and cancellation.
net10.0 Debug/Release/rebuilt checked Release each pass 2899/2899, no failures/skips.
Checked build has zero errors and 51 existing Common warnings; no Buffer/test warnings.
Both original AdvancedLeakAwareByteBuf source comments and the original fixture license
are retained. Common is unchanged.

93 freshly extracted pinned Java Buffer sources supply the differential runtime.
Per Debug/Release and recording policy, all 738 access-record/closure rows agree with
Java and an independent recording-policy model; 736 complete result/cursor/byte rows
agree. Two existing read-only WriteByte/WriteBoolean failure rows retain CLR's unchanged
writer index, whereas Java increments it before failure. No byte/lifetime difference is
hidden by this adaptation. Both static policy values run in separate processes.
Inventory is 159 (72 pending, 54 in-progress, 30 verified, 3 CLR replacements).
Evidence: artifacts/buffer-advanced-leak-validation (ignored), buffer-advanced-leak-*.trx.

## Simple leak-aware composite wrappers

WrappedCompositeByteBuf remains a CompositeByteBuf while sharing the original
component metadata and index/mark owner. It introduces no component ownership or
Retain. Storage/view/reference hooks delegate to the wrapped composite; existing
CLR structural kernels use the shared list, including nested wrappers, enumeration,
cached component views and flattened-source transfers. ReferenceCount/Retain/Release
are now overridable in AbstractReferenceCountedByteBuf as in Java; ordinary counter
operations are unchanged. Raw/wrapped/sibling aliases are the same component owner
for the existing CLR cycle rejection, without consuming an incoming reference.

SimpleLeakAwareCompositeByteBuf wraps nine derived views with SimpleLeakAwareByteBuf
and the exact immediate wrapped referent/tracker. Only final logical release closes
the tracker, even when a retained view is the last owner. Native pins/internal leases
can outlive that close. The virtual view factory also supports advanced views without
double closure. Simple composite accesses do not record; Touch still forwards like
Java's composite wrapper. Composite Release retains its original lack of the ordinary
simple byte wrapper's additional close-trace attachment. Optional decrement combines
Java's two Release overloads; their identical explanatory comment is shared once.

CLR fluent little-endian writes deliberately return the outer receiver. The pinned
WrappedCompositeByteBuf returns its inner buffer for eight such methods, allowing a
chained Release to bypass the simple composite tracker. Eight explicit regressions
verify the CLR return/close policy. This adaptation changes neither bytes nor cursors.
Byte-order/NIO/channel facades, full inherited fixtures, advanced composite access
recording and automatic allocator wrapping remain pending.

763 added cases cover 123 operations on heap/native/mixed-read-only composites through
plain/simple wrappers, all nine original view scenarios and the original nested
forEachByte regression. Structural aliases, flattening, borrowed iteration, ownership
cycles, virtual view factories, final reference/pin/lease lifetimes and async streams
are also checked. net10.0 Debug/Release/rebuilt checked Release pass 3662/3662 with no
failures/skips. Checked build has zero errors and 51 existing Common warnings, with
no Buffer/test warnings. Common is unchanged; original unique source comments and
both fixture licenses are retained, with duplicate Release comment accounting explicit.

93 freshly extracted pinned Java Buffer sources provide 5148 oracle rows across
36 storage/component-limit/wrapper-depth/tracking domains and 143 operations per
Debug/Release. All recording/close/byte/cursor/component/reference states match; 4864
complete rows match, with 284 explicit little-endian receiver-return adaptations.
Independent recording/close/receiver checks agree, and each CLR case returns shared
native reserved bytes to zero. Inventory is 159 (68 pending, 58 in-progress, 30
verified, 3 CLR replacements). The new sources/fixtures remain in-progress.
Evidence: artifacts/buffer-composite-leak-validation (ignored), buffer-composite-leak-*.trx.

## Advanced leak-aware composite access recording

AdvancedLeakAwareCompositeByteBuf now records the current CLR data, view, stream/file
and composite mutation/iteration APIs before delegation, including failed operations.
It reuses AdvancedLeakAwareByteBuf's acquire-and-release-only policy. Retain/Release
and Touch always record; IsReadOnly records as in the original composite wrapper,
while component lookups, index metadata and comparisons retain their original lack
of recording. Null optional Touch hints use the existing CLR no-hint convention.

Fifteen composite structural methods are now virtual as in Java. The advanced wrapper
delegates them to its immediate wrapped composite, avoiding duplicate records from
convenience overloads and internal virtual calls. All nine shared/retained/read-only
views use AdvancedLeakAwareByteBuf with the original exact referent/tracker. Final
logical release closes once; outstanding native pins/operation leases can outlive it.
No additional composite close-trace attachment or automatic allocator wrapping is added.

937 added tests include 152 operations over three storage shapes and two wrapper
depths, the inherited original view cases and nested forEachByte regression, exact
tracker ownership, nested advanced wrappers, interface enumeration, metadata/Touch,
native lifetimes, CLR memory/floating LE access and synchronous/asynchronous I/O.
The existing simple-composite operation helpers are shared without duplicating their
operation table. net10.0 Debug/Release/rebuilt checked Release pass 4599/4599, with no
failures/skips. Checked build has zero errors and 51 existing Common warnings; no
Buffer/test warnings. Common source is unchanged. Both original licenses are retained.

93 freshly extracted pinned Java Buffer sources provide 5472 oracle rows per recording
policy across 36 storage/component-limit/wrapper-depth/tracking domains. Debug/Release
and both policy values agree with independent record/final-close checks and all Java
byte/cursor/component/reference states. Each run has 5188 complete matches and 284
explicit existing LE fluent-receiver adaptations. Every CLR case returns shared native
reserved bytes to zero. Inventory is 159 (66 pending, 60 in-progress, 30 verified,
3 CLR replacements). New source/fixture remain in-progress: byte-order/NIO/channel
facades and complete inherited original fixtures are still pending.
Evidence: artifacts/buffer-advanced-composite-leak-validation (ignored), buffer-advanced-composite-leak-*.trx.

## Automatic allocator leak wrapping

The combined CLR ByteBuf skeletal type now captures one factory-created detector at
type initialization, including initialization through an empty sentinel. Configure a
custom ResourceLeakDetectorFactory before creating buffers/allocators; later factory
replacement does not change this detector. AbstractByteBufAllocator's two protected
helpers call Track and then IsRecordEnabled, preserving null/sample misses and custom
provider policy. Creation stacks exclude these helper frames.

Unpooled tracks direct and composite factory allocations; its heap factories remain
unwrapped as in Java. A working preferDirect/disableLeakDetector/nativeAllocator
constructor adds the original opt-out while preserving the existing constructor.
Pooled tracks heap/native allocations through arenas and unpooled fallbacks, and its
composites use the base helper. Detector callbacks run outside the CLR allocator lock.
Empty (0,0) sentinels, explicit constructors and external wrapping acquire no automatic
root tracker. Copies consult their creator allocator and acquire independent tracking
where applicable. Existing CLR retained views share the original tracker/root count;
Java pooled-independent derived tracking remains outside this unit.

236 added tests cover factory policy, disabled detection, empty/growable buffers,
all nine view forms, independent copies, native pins surviving logical close and public
factory override dispatch through base/interface references. Five public factory
methods become virtual to match Java, completing the skeletal allocator contract.
Global level changes are isolated from parallel tests and restored. Existing cache,
arena and copy fixtures inspect unwrapped storage while releasing the original public
wrapper; they no longer assume factories always return raw pool buffers.
net10.0 Debug/Release/rebuilt checked Release pass 4835/4835 without failures/skips.
The full Debug and checked Release suites also run with sampling interval 1. Checked
build has 51 existing Common warnings, zero Buffer/test warnings and zero errors.
Common source is unchanged. Original implemented constructor/default comments are retained.

93 freshly extracted pinned Java Buffer sources yield 5376 exact oracle rows for each
targetRecords value (0/4), per Debug/Release: four detector levels, four real/custom
provider modes, allocator/storage policies, 14 factory/constructor paths and three
data/view/copy actions. An independent model checks wrapper kinds, track attempts,
tracker counts, storage/bytes/cursors and final references. Provider trackers verify
exact referents, one final close, simple/advanced recording, factory snapshot arguments
and helper-frame exclusions. Every CLR case returns allocator/shared native reservations
to zero. All 26 original AbstractByteBufAllocator constructor/method forms are accounted
for by CLR optional overloads, shared kernels and the completed wrapping helpers; its
source is now verified with all 10 original comment occurrences preserved. The original
fixtures and broader allocator configuration remain partial. Inventory is 159
(66 pending, 59 in-progress, 31 verified, 3 CLR replacements).
Evidence: artifacts/buffer-allocator-leak-validation (ignored), buffer-allocator-leak-*.trx.

## Buffer leak listener

ByteBufUtil.SetLeakListener forwards the original public API to the shared ByteBuf
detector using Common's existing listener interface. Registration replaces the listener;
null removes it. The listener installed when a subsequent tracking call drains a
collected leak receives ByteBuf and its records on that tracking thread. Existing
reporting gates and duplicate suppression apply. Common source is unchanged; the
original API comment and source/fixture licenses are retained.

Eight added CLR tests cover real-GC listener replacement/removal after collection,
payload/thread/single-notification contracts and normal final release over
unpooled/pooled heap, native, composite and retained views.
Global changes are isolated and restored. net10.0 Debug/Release/rebuilt sampled checked
Release pass 4843/4843, zero failures/skips. Checked build has 51 existing Common warnings,
zero Buffer/test warnings or errors. The original utility source/fixture remain partial;
the pinned ByteBufUtilTest has no dedicated listener test.

Real-GC isolated probes reuse pinned Java classes after rechecking all 93 extracted
Buffer sources. Seven modes over three allocator shapes yield 21 exact Java/CLR matches
per targetRecords value (0/4) and Debug/Release. An independent callback/report/active-
tracker model verifies delivery, replacement/removal/late registration after collection,
reporting suppression, explicit final close and worker-thread delivery. Payloads contain
the exact resource type and creation hint; repeated drains do not notify again.
Inventory remains 159 (66 pending, 59 in-progress, 31 verified, 3 CLR replacements).
Evidence: artifacts/buffer-leak-listener-validation (ignored), buffer-leak-listener-*.trx.

## Buffer diagnostic representations

The original object ToString contract now reports indices/capacity/finite maximum,
unwrapped state or a freed marker. Ordinary wrappers surround their parent's text;
composite wrappers delegate it without wrapper names. Composites append component
counts, including after final release; CLR clears component references and preserves
only the last count. EmptyByteBuf uses the original BE name. FixedCompositeByteBuf's
diagnostic omits the maximum as its original skeletal constructor does, independently
of public MaxCapacity. Decimal formatting is invariant and class names are CLR names.
The original API comment/licenses are retained. No content is read or leak access
record added; the freed skeletal path does not touch capacity or unwrap.

53 added cases cover heap/native/pools/composites, finite/unbounded maxima, four view
forms, changed cursors/marks, nested simple/advanced/unreleasable wrappers, empty/freed
states, metadata-only guards and caller culture. net10.0 Debug/Release/rebuilt sampled
checked Release pass 4896/4896, no failures/skips. Checked build has 51 existing Common
warnings and zero Buffer/test warnings/errors; Common source is unchanged.

Pinned Java probes recheck all 93 extracted Buffer sources. Each targetRecords value
(0/4), per Debug/Release, yields 140 states: 137 match after explicit implementation-
class-name normalization, and three preserve the existing CLR fixed-composite
IsReadOnly/AsReadOnly identity policy (see fixed read-only composites). Independent
metadata/max-bound/view/wrapper/component/leak-record/close models verify both policies.
Every CLR case frees allocator/shared native reservations. The original charset
ToString tests concern text decoding; the new cases supplement the object diagnostic
contract. Whole sources/fixtures remain partial; inventory stays 159 (66 pending,
59 in-progress, 31 verified, 3 CLR replacements).
Evidence: artifacts/buffer-diagnostics-validation (ignored), buffer-diagnostics-*.trx.

## Buffer size predicates

Original isReadable(int) is now CanRead(int), preserving its public Java comment,
readable-index comparison and EmptyByteBuf specialization. Transparent ordinary,
composite and unreleasable wrappers forward virtual queries without leak recording.
CanWrite(int) no longer throws on negative counts: original comparisons apply,
while read-only buffers and the empty sentinel return false. Neither query grows,
consumes or retains a buffer. CanRead reads indices without checking accessibility;
its metadata remains queryable after release. An owned zero-capacity buffer differs
from the shared sentinel: nonpositive counts succeed on ordinary index comparisons.

53 added tests cover six backings/six views, exact/insufficient/zero/negative/extreme
counts, independent indices/marks, retained ownership, current capacity versus maximum,
empty wrappers, virtual dispatch, no content/reference access or leak records and
freed readable metadata. Debug/Release/rebuilt sampled checked Release each pass
4949/4949 on net10.0, zero failures/skips. Checked build has 51 existing Common
warnings and zero Buffer/test warnings/errors; Common source is unchanged.

Pinned Java probes recheck all 93 extracted Buffer sources. For targetRecords 0/4,
Debug and Release each match 1362 of 1408 rows exactly. The remaining 46 preserve
existing CLR fixed-composite derived read-only permissions; original Java derived
queries can report writable bytes although their fixed parent rejects actual writes.
All readable results agree. Independent index/permission/recording models validate
both policies; CLR native reservations return to zero. Original sources/fixtures
remain partial; inventory stays 159 (66 pending, 59 in-progress, 31 verified,
3 CLR replacements). Evidence: artifacts/buffer-size-predicates-validation (ignored),
buffer-size-predicates-*.trx.

## Remaining work

Unpooled factories cover heap/native allocation, single and multiple-input wrapping,
single and multiple-input copying, encoded text/primitive copying, unreleasable views, fixed read-only composites and the shared empty sentinel. Adaptive allocator metrics,
swapped buffers and provider-specific external memory, remaining composite APIs/encoding/utilities, channel/native I/O,
borrowed-address wrapping, allocator events and adaptive allocators remain unported. Automatic wrapping is connected for the current unpooled/pooled allocators. Global default allocator selection remains pending. Advanced leak access recording covers the current CLR ordinary/composite buffer API; its remaining upstream surfaces are pending. Simple composite leak wrappers cover shared-root views and the current CLR composite API. Most original
test classes and the rest of AbstractByteBufTest remain pending/in-progress.
Next units continue remaining factories/utilities/original fixtures, leak wrappers and
remaining allocator integration. Common
changes must cite the actual buffer contract that requires them.
