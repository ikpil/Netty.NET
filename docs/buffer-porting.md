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
Int32.MaxValue maximum for generic managed/opaque memory. Known-native copies now
follow the optimized provider and preserve the captured root maximum (see Native
read-only provider below). Allocated results are released on write failure.
Wrapping external storage
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
where applicable. At this checkpoint retained views shared the original tracker/root
count; pooled-independent derived tracking is now covered under pooled derived ownership.

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

## Native address wrapping

Unpooled.WrappedBuffer(nint,int,bool) now wraps raw native addresses without copying,
with the original Java factory comment and wrapper license. MemoryManager/SafeHandle
replace the original WrappedUnpooledUnsafeDirectByteBuf constructor/free specialization;
existing direct-buffer operations supply views, primitives, copies and resize. doFree
requires a NativeMemory.Free-compatible allocation base and transfers ownership only
on success. Otherwise the caller preserves storage through all access and pins.
External storage is uncharged; capacity changes detach to reserved native storage.
Failure preserves the original range/ownership. Final release/detachment invalidates
new Memory access; existing idempotent pins delay owned free. SafeHandle finalization
also frees abandoned owned allocations. Zero-length wrappers retain their own logical
lifetime. CLR rejects null nonempty, negative and wrapping address ranges before transfer.

32 new cases cover ownership/views/address aliases, exact wire bytes, independent
copies, resize/rollback/metrics, zero/null/invalid input, pin-delayed free/copied pins,
concurrent pins, streams, consolidation and aliased composite growth. Debug/Release/
rebuilt sampled checked Release pass 4981/4981 on net10.0, no failures/skips. Checked
build has 51 existing Common warnings, zero Buffer/test warnings/errors; Common unchanged.
Pinned Java probes recheck all 93 extracted Buffer sources; each targetRecords 0/4
and Debug/Release yields 166 exact states against an independent address/view/reference/
copy/resize model. Java probes enable its reflective raw-direct-buffer constructor.
The pointer wrapper is a complete CLR replacement; its larger parent/factory/fixture
remain partial. Inventory: 159 (65 pending, 59 in-progress, 31 verified, 4 CLR replacements).
Evidence: artifacts/buffer-native-address-validation (ignored), buffer-native-address-*.trx.

## Pooled derived ownership

Pooled roots now create independently counted retained slices/duplicates. Each view
acquires one immediate-parent reference; its final release clears parent/root fields
and releases that parent. Borrowed descendants delegate the derived counter, so a
live root cannot make an already-released child accessible. Slice offsets, duplicate
capacity/marks, parent-preserving copy policy and original translated-root bounds for
borrowed retained slicing follow the pinned source. Memory/Span/sequence and .NET I/O
reuse the existing kernel. CLR wrappers are never recycled or resurrected.

Leak-aware retained children get forced independent tracking and reparent through the
original leak facade, closing each ancestor tracker only at that ancestor's final
release. Ordinary views share trackers. Failed virtual wrapper construction rolls
back ownership/tracking. Native pins preserve physical storage after logical release.
All 12/1/1 comments of the original derived base/slice/duplicate sources are retained.
Independent ownership/current CLR views are verified; remaining upstream channel/native
I/O surfaces and larger inherited fixtures keep the whole sources partial.

50 new cases include the original multiple-level retained/non-retained slice scenario,
plus hierarchy/release orders, siblings, delegate counts, coordinates/marks/copy/resize,
zero/bounds/overflow, atomic release, native pins, forced trackers and failed factories.
Existing pooled/leak fixtures now assert independent counters/trackers where applicable.
Debug/Release/rebuilt sampled checked Release pass 5031/5031 on net10.0, no failures/skips.
Checked build: 51 existing Common warnings; zero Buffer/test warnings/errors. Common unchanged.
Rechecked pinned Java probes match 1152 states exactly per targetRecords 0/4 and Debug/
Release; independent parent-claim/counter/coordinate/record/close models agree. Forced
trackers close and CLR native reservations return to zero. Inventory: 159 (62 pending,
62 in-progress, 31 verified, 4 CLR replacements). Evidence:
artifacts/buffer-pooled-derived-validation (ignored), buffer-pooled-derived-*.trx.

## Adaptive allocation policy and buddy core

The pinned allocator's actual size/controller/tree code is now ported as internal
AdaptiveAllocationPolicy and AdaptiveBuddyTree components. It uses 16 fixed classes,
128-KiB minimum chunks with at least 32 segments, power-of-two buddy reservations,
and a remembered maximum for new buddy chunks. Its historical histogram/percentile
class description does not describe the current controller implementation; original
implemented-path comments are retained with explanatory CLR notes. Fixed-class chunks
need not be multiples of 128 KiB. Buddy reservations can exceed a buffer's maximum;
the eventual public writable hint must clamp separately. Explicit low-memory mode
preserves the source dedicated fallback above the last small class.

Unsigned-byte flags and widened classification arithmetic preserve the rules under
checked CLR builds. The tree splits first-fit blocks, coalesces matching siblings,
and packs returned offsets/sizes. Its owner must serialize changes; external queued
returns must be processed before invoking Free. This policy/tree unit preceded actual
chunk storage/leases, magazines, cache/purge/scaling, AdaptiveByteBuf/public allocator,
runtime/default selection and JFR. It supplies their core decisions without changing the
current default allocator or representing the entire adaptive allocator as complete.

64 added cases include the complete original classification fixture and supplementary
controller/tree/guard cases. Debug/Release/rebuilt sampled checked Release pass
5095/5095 on net10.0, zero failures/skips. Checked build: 51 existing Common warnings,
zero Buffer/test warnings/errors; Common unchanged. Rechecked pinned Java controllers
and actual BuddyChunk allocations/queued releases yield 19025 normal-mode and 16978
low-memory-mode exact Java/CLR rows per Debug/Release. Independent interval/flag/
controller models agree, all 511 packed intervals are covered, and Java chunk registries
return to zero. Probes explicitly process frees before comparing tree states; pending
queue/cache semantics are outside the claim. Inventory: 159 (60 pending, 63 in-progress,
32 verified, 4 CLR replacements). Evidence: artifacts/buffer-adaptive-policy-validation
(ignored), buffer-adaptive-policy-*.trx.

## Adaptive chunk storage and lifetime

Dedicated, fixed-class and buddy chunks now own actual heap/native delegates and
physical-capacity registries. Fixed slots preserve shared FIFO/local stack order,
including tail-shifted local offsets, lazy capacity estimates and the segment-count
retirement state machine. ConcurrentQueue/Stack replace JVM primitive containers;
explicit enqueue fences and Interlocked retirement preserve Java StoreLoad ordering.
Buddy returns queue packed segments before dropping their chunk claim; allocating
owners drain them before reuse. Initialization failures restore reservations/counters.

CLR region MemoryManagers preserve heap array discovery and invalidate released
borrowed Memory. Operation/pin claims defer both slot reuse and delegate release;
copied handles release once, and abandoned pin tokens finalize. Idempotent chunk Dispose
retires owner ownership while live regions survive. Local allocation/inspection/retirement
requires the physical owner thread; shared allocation requires magazine serialization.
This chunk unit preceded magazine/cache transfer, owner handoff, buffer integration,
public allocator/runtime/default selection and JFR. Current runtime factories are unchanged.

88 supplementary cases cover all 16 classes, both owner modes, heap/native storage,
reuse/exhaustion, deferred queues, copied/abandoned pins, empty storage, rollback and
600 concurrent retirement scenarios. Debug/Release/rebuilt sampled checked Release
pass 5183/5183 on net10.0, no failures/skips. Checked build: 51 existing Common warnings,
zero Buffer/test warnings/errors; Common unchanged. All 93 pinned Java Buffer sources
were rechecked; real chunk allocation/return/retirement matches 73526 exact rows per
Debug/Release and an independent FIFO/stack/aligned-interval/lifetime model. Final Java
registries and CLR registries/native reservations are zero. Inventory remains 159
(60 pending, 63 in-progress, 32 verified, 4 CLR replacements). Original implemented-path
comments/license are retained; the entire adaptive allocator remains partial. Evidence:
artifacts/buffer-adaptive-chunk-validation (ignored), buffer-adaptive-chunk-*.trx.

## Adaptive buffer region integration

AdaptiveByteBuf now binds real chunk regions while keeping logical capacity separate
from reserved capacity. Growth within the reservation and every shrink reuse storage;
larger growth copies the full old logical prefix before publication. Buffer identity,
indices/marks and old state on allocation/copy failure are preserved. Copies retain the
raw delegate's maximum and storage kind. Borrowed/retained views share the adaptive root
counter; Unwrap never exposes raw chunk storage. Fresh CLR wrappers clear the region and
allocation callback on final release. Operation/pin claims protect old regions during
aliased growing writes and defer reuse/free until their claims return.

The existing Memory/Span kernel supplies primitive/bulk/text/sequence and .NET stream/
positioned-file operations with logical bounds. This buffer unit used an explicitly
test-only serialized storage binder before magazine/cache and public factory integration.
It did not change public/default factories or complete the original allocator fixture.

49 added cases include original unwrap scenarios and portable logical-boundary regressions,
plus storage transitions, copying, views, pins/leases, owner-local chunks and failure
rollback. Debug/Release/rebuilt sampled checked Release pass 5232/5232 on net10.0, no
failures/skips. Checked build: 51 existing Common warnings; zero Buffer/test warnings/
errors; Common unchanged. Rechecked pinned Java adaptive buffers match 1196 normal-mode
and 1196 low-memory-mode rows per Debug/Release and an independent capacity/cursor/view/
copy/reference/byte model. Probe registries/native reservations reach zero after explicit
cleanup. Original implemented-path comments/license are preserved. Inventory: 159
(59 pending, 64 in-progress, 32 verified, 4 CLR replacements). Evidence:
artifacts/buffer-adaptive-buffer-validation (ignored), buffer-adaptive-buffer-*.trx.

## Adaptive shared magazines and public allocator

AdaptiveByteBufAllocator now exposes heap/native/preferred/IoBuffer factories, automatic
leak wrapping, inherited composites, self-provided metrics and Dispose. Real shared
magazine groups select stripes by managed physical thread id, retain current/atomic
next-in-line chunks, expand under contention and use dedicated fallback after bounded
attempts. Chunks transfer their owner claim to caches while regions remain live.

Shared fixed-class FIFO caches preserve generation/epoch scans, active preference,
bounded ring-equivalent capacity, idle-only aging and the retention floor. Locked sorted
buddy maps preserve newest-first equal keys, ceiling lookup, largest recovered pending
return and idle-only pressure eviction. Monitor/Interlocked and locked BCL collections
replace JVM synchronization/containers; performance equivalence is not claimed.
Residual buddy capacity that is not a power of two now fails reservation as in Java,
allowing magazine fallback. Exception paths preserve ownership of claimed next chunks.

Pool lifetime gates close allocation before draining every fixed/buddy group. Live
buffers/views/pin claims preserve storage until final return; native SafeHandles cover
unreachable backing storage. lowMemory/native budgets are explicit; shared reuse/purge
environment settings are supported. The global default stays unchanged. Thread-local
groups/ring caches/owner cleanup, their runtime eligibility/non-event-loop options,
runtime/default selection and events remain pending; sources/fixtures stay partial.

40 added cases cover public factories/metrics/reuse/shared purge, 200 shuffled buddy
repetitions, parallel allocation, forced expansion/dedicated fallback, composites,
native budget failure, disposal/views/pins and real-slot shared/buddy cache scenarios.
Debug/Release/rebuilt sampled checked Release pass 5272/5272 on net10.0, no failures/
skips. Checked build: 51 existing Common warnings; zero Buffer/test warnings/errors;
Common unchanged. All 93 pinned Java Buffer sources were rechecked. Public factory/
buffer/shared-cache execution matches 12808 exact rows per Debug/Release. Independent
models cover 2392 buffer states and 10402 FIFO/geometry/metric rows; cache aging/lifetime
invariants agree. Explicit cleanup returns registries/native reservations to zero.
Original implemented-path comments/licenses are retained. Inventory: 159 (57 pending,
66 in-progress, 32 verified, 4 CLR replacements). Evidence:
artifacts/buffer-adaptive-shared-validation (ignored), buffer-adaptive-shared-*.trx.

## Adaptive physical-thread local magazines and cleanup

Owner-local magazines and fixed-class ring caches now connect to the public heap/native
allocator. Eligibility follows the pinned executable rules: guaranteed FastThreadLocal
cleanup plus an executor mapping or explicit non-event-loop cache option. Ordinary CLR
workers, low-memory mode and buddy allocations use shared magazines. Ownership stays on
physical threads and never flows through AsyncLocal or ExecutionContext.

The ring preserves head selection, fallback scanning, doubling/linearization, wrapped
compaction, capacity-first/Dutch-flag partition, idle epochs, retention floors and per-class
max/min byte bounds. Original purge and byte-bound environment defaults/clamping are
supported. FastThreadLocal.OnRemoval drains current/next/cache chunks. The allocator also
tracks local bindings so off-thread Dispose can quiesce allocations, detach owner identity
and reclaim all local groups. A local return/retirement gate sends late returns through
the external queue; live buffers and pins preserve storage until final return. This adds
CLR synchronization; performance equivalence is not claimed. Wrappers stay fresh. The
pinned low-memory dispatch already bypasses local groups regardless of its auxiliary
disable flag, so that redundant JVM setting has no CLR switch.

38 new cases cover real-slot local cache scenarios, eligibility, actual fast-thread exit,
scope failure/restart, task non-inheritance, burst purge and 80 disposal/owner-return stress
repetitions across heap/native storage. Debug/Release/rebuilt checked Release with SIMPLE
leak sampling interval 1 pass 5310/5310 on net10.0, no failures/skips. Checked build: 51
existing Common warnings; zero Buffer/test warnings/errors; Common unchanged. All 93
pinned Java Buffer sources were rechecked. Public normal/low-memory factories across all
16 small classes and both storage kinds, plus cache/ring/diagnostic snapshots, match
139626 exact Java/CLR rows per Debug/Release. Independent first-pass geometry/data models
cover 69736 states per configuration; ring membership/aging/retention/cleanup invariants
agree. Explicit cleanup returns registries/native reservations to zero.

Implemented-path comments/licenses are preserved, including the shared-cache description
omitted in the preceding unit. The duplicate-aware source audit retains 158/164 comments;
the six unmatched comments describe omitted JVM recycler paths, the redundant low-memory
switch, or a shared race comment merged into one helper. Original fixtures remain partial
with merged/unported variants recorded in JSON. One pinned behavior is deliberately
preserved: an exhausted local stack does not immediately notice only external-queue
returns through HasRemainingCapacity; RemainingCapacity refreshes the cached estimate.
This unit does not silently change that upstream policy. Inventory: 159 (57 pending,
66 in-progress, 32 verified, 4 CLR replacements). Evidence:
artifacts/buffer-adaptive-local-validation (ignored), buffer-adaptive-local-*.trx.

## Global allocator selection and runtime preferences

IByteBufAllocator.Default now follows the pinned selector: adaptive when unset,
exact unpooled/pooled/adaptive strings, and pooled fallback for unknown values.
Separate CLR holders publish one process-owned instance and snapshot
io.netty.noPreferDirect without singleton initialization cycles. SafeHandle-backed
native release replaces JVM Cleaner capability checks. Unpooled/Pooled singletons
and adaptive defaults follow the preference; explicit adaptive bool values override it.
Unpooled.Buffer continues to request heap storage. Utility-only calls deliberately
leave global allocator/processor configuration uninitialized, unlike Java's eager
ByteBufUtil class initializer.

Adaptive lowMemory null reads the explicit environment property with false default;
CLR GC heap limits never imply native budgets. NettyRuntime configured processors
now determine adaptive stripe/reuse defaults. Existing CLR pooled one-arena/cache-off
policy remains explicit; JVM automatic memory-budget heuristics and remaining portable
pooled runtime properties are outside this unit.

45 new cases isolate static state in collectible contexts and restore environment
variables in a nonparallel collection. They cover selector branches, preferences,
low-memory overrides, singleton ordering/concurrency, snapshots, lazy utility calls,
processor settings, public factories/copies and native cleanup. Debug/Release/rebuilt
checked Release with SIMPLE leak sampling interval 1 pass 5355/5355 on net10.0, no
failures/skips. Checked build: 51 existing Common warnings; zero Buffer/test warnings/
errors; Common unchanged. All 93 pinned Java Buffer files and three relevant Common
sources were rechecked. Forty isolated-process configurations match 3540 nonempty
Java/CLR executable rows per Debug/Release; independent models verify 960 factory
states, heap/native metric retention and 130 large-buffer low/normal decisions per
configuration. Java pooled arena/cache settings are constrained to the CLR policy.
Explicit CLR cleanup returns registries/native reservations to zero; Java adaptive
registries drain and its process-owned pooled defaults end with the probe process.

The complete portable allocator interface and adaptive facade are now verified;
this does not complete the pooling kernel/events, concrete provider variants or
original fixtures. Original interface/facade comments are preserved. Inventory: 159
(57 pending, 64 in-progress, 34 verified, 4 CLR replacements). Evidence:
artifacts/buffer-default-allocator-validation (ignored), buffer-default-allocator-*.trx.

## Pooled runtime configuration and cache eligibility

The shared pooled Default now snapshots portable page/alignment/maxOrder fallback,
arena counts, cache sizes/capacity, trim thresholds/periods and all-thread cache policy.
Current trim-period properties override their deprecated alias. PascalCase getters
expose original portable defaults. Explicit C# constructor options stay unchanged;
CLR defaults to one arena per storage kind without JVM memory-budget inference.
Shared-default caching requires a physical FastThreadLocalThread, executor mapping
or the all-thread option. Guaranteed fast scopes register weak allocator/binding
callbacks that detach the correct binding on removal, drain caches and cancel trim;
live buffers remain valid and later bindings are not detached accidentally.

Nonpositive cache sizes disable caching. Invalid cached capacity/trim thresholds fail
when an eligible binding constructs caches, with CLR rollback before publication.
The pinned Java trim-error constructor increments arena thread-cache counters before
throwing; that metadata leak is deliberately not reproduced. NIO wrapper caches and
cache-finalizer switches have no CLR consumer; Memory/Span values, explicit cleanup
and weak registrations replace them. TimeSpan bounds saturate very large millisecond
periods. The existing alignment-truncated-table guard remains a documented CLR policy.
Portable diagnostics/events and complete original fixtures remain pending.

37 added cases cover defaults/fallback, arena disable/clamp, eligibility, scope cleanup,
period aliases, snapshots, explicit options, disabled/invalid caches and rejection.
Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1 pass
5392/5392 on net10.0, no failures/skips. Checked build: 51 existing Common warnings;
zero Buffer/test warnings/errors; Common unchanged. All 93 pinned Buffer sources were
rechecked. Thirty-one isolated-process profiles match 509 exact Java/CLR executable
states per Debug/Release; independent models verify 400 buffer states and 75 geometry/
cache/binding/lifetime snapshots per configuration. Error profiles compare rejection
timing; successful counters and removal agree. CLR cleanup returns native reservations
to zero. Original implemented-path comments/licenses are preserved. Inventory remains
159 (57 pending, 64 in-progress, 34 verified, 4 CLR replacements). Evidence:
artifacts/buffer-pooled-configuration-validation (ignored), buffer-pooled-configuration-*.trx.

## Socket channel transfers

Connected Socket overloads now cover the four original Gathering/ScatteringByteChannel
get/set/read/write contracts. Absolute calls keep both indices; relative calls advance
only by the actual successful count. Each call invokes Send/Receive once. Nonblocking
WouldBlock returns zero; other socket errors propagate. Stream EOF is zero rather than
Java minus one. Empty requests validate buffer/null/permissions but skip socket I/O,
including disposed sockets. Original public Java comments/licenses are preserved.

ValueTask/CancellationToken counterparts await Socket's Memory-based operations directly.
Pre-cancellation follows validation and prevents growth/I/O; started growth survives
EOF, failure or cancellation. A successful result commits despite late cancellation;
thrown failures leave active cursors unchanged. Async waits for readiness instead of
exposing synchronous WouldBlock. Socket/buffer ownership is borrowed; callers keep both
alive and exclude concurrent mutation/release/external cursor changes until completion.

CLR staging/snapshots use O(length) pooled managed storage on every backing kind.
Composite input receives once instead of Java's repeated per-component reads; output
uses one snapshot rather than zero-copy gathering. A throwing receive publishes no
staging bytes; a later failed composite commit may change a prefix. Native memory is
never borrowed across await. Arbitrary Java channel providers/native transport surfaces
are not declared complete. Raw null arguments now need an explicit Stream/Socket type
where overloads would otherwise be ambiguous; five existing null tests specify Stream.

71 added cases cover six backing kinds (heap/native/mixed composite/offset slice/pooled/
adaptive), marks, absolute/relative transfers, short input/EOF, growth, closed sockets,
read-only/released buffers, nonblocking backpressure, pending cancellation/reuse and
single advanced-leak recording on success/failure. Loopback tests accept actual OS
counts; advisory socket buffer sizes are not assumed to force a particular partial count.
Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1 pass 5463/5463
on net10.0, no failures/skips. Checked build: 51 existing Common warnings; zero Buffer/
test warnings/errors; Common unchanged. All 93 pinned Buffer sources were rechecked.
Six backing kinds x three offsets x eight lengths x four operations match 576 exact
Java/CLR final endpoint states per Debug/Release and sync/async mode, also checked by
independent byte/cursor/capacity/reference models. The probes retry outside the API to
reach known endpoint states; OS syscall split counts are not claimed equivalent. CLR
cleanup returns native reservations to zero; Java process-owned pools end with the probe.
Inventory remains 159 (57 pending, 64 in-progress, 34 verified, 4 CLR replacements).
Evidence: artifacts/buffer-socket-validation (ignored), buffer-socket-*.trx.

## Thread-local direct transport helper

ByteBufUtil.ThreadLocalDirectBuffer now ports the original optional native scratch
factory used by AbstractNioChannel (527/559) and SocketWritableByteChannel (54).
Unset/nonpositive/invalid io.netty.threadLocalDirectBufferSize returns null; positive
values create 256-byte, int.MaxValue-capacity contiguous direct buffers with unpooled
allocator provenance and no automatic leak facade. Settings snapshot at the first
helper call; unrelated utility/global allocator initialization remains lazy.

Guaranteed physical fast-thread cleanup scopes retain one idle native owner at most,
if its final capacity is <= the configured cutoff. Other callers allocate uncached;
cross-thread returns free storage. CLR storage caching replaces Java object recycling,
its ratio and multi-entry/cross-thread policy. Fresh wrappers/memory facades prevent
stale-reference resurrection; cached bytes remain dirty while indices/marks reset.
Pins/operation claims delay reuse. Resize retires the old allocation, with existing
CLR full-prefix copying; scope removal drains idle owners and frees late returns while
live buffers/pins remain valid. Native reservation failure preserves prior state.

26 added cases cover isolated settings/snapshots, successful/failed fast scopes and
actual thread exit, cutoff/reuse, stale views/Memory, pins/copied handles/leases, late
cross-thread return, native budget failure/retry, retained ownership, copy provenance
and self-aliased growth. Debug/Release/rebuilt checked Release with SIMPLE leak sampling
interval 1 pass 5489/5489, no failures/skips. Checked build: 51 existing Common warnings;
zero Buffer/test warnings/errors; Common unchanged. Nine isolated profiles match 114
executable Java Unsafe/NIO and CLR states per Debug/Release, independently checked for
factory/cutoff/bytes/cursors/references. Java recycler ratio=1 isolates the single-owner
reuse contract; identity, addresses, performance, backing cleanup timing and broader
recycler policies are not claimed equivalent. All 93 Buffer sources were rechecked;
original public API comments/licenses retained; CLR cleanup returns native bytes to zero.
Whole utility and original fixtures remain partial. Inventory unchanged: 159 (57 pending,
64 in-progress, 34 verified, 4 CLR replacements). Evidence:
artifacts/buffer-thread-local-direct-validation (ignored), buffer-thread-local-direct-*.trx.

## Pooled allocator state diagnostics

PooledByteBufAllocator.DumpStats now connects the original arena reports: configured
heap then direct arena order, six utilization labels/list order, empty none entries,
and available small-subpage size-class lines. Full subpages are omitted; available
siblings share the original separator-free line. The arena Monitor and ordered head
locks serialize metadata traversal. Reports create no arena storage or thread binding;
the complete allocator is not a transactional snapshot. Environment.NewLine replaces
StringUtil.NEWLINE and RuntimeHelpers supplies diagnostic identity hashes.

Existing chunk/subpage/pooled metric strings now preserve Java invariant decimal syntax,
including negative sentinel/disabled-memory values. Original DumpStats comment/licenses
are preserved. CLR disposal clears heads/retires lists; reports remain readable while
live buffers/pins can still preserve storage. This lifetime extension is tested explicitly.

16 added cases cover empty/disabled/unequal arenas, full/fragmented subpages, multiple
chunks/utilization transitions, cached returns/drain, culture, read-only metric collections,
live native storage after disposal and concurrent allocation/return/shutdown. Debug/Release/
rebuilt checked Release with SIMPLE leak sampling interval 1 pass 5505/5505, no failures/
skips. Checked build: 51 existing Common warnings; zero Buffer/test warnings/errors;
Common unchanged. Six configurations match 206 complete Java/CLR report/metric snapshots
per Debug/Release, normalizing only object identity hashes. Independent models check
layout, geometry, configured counts, used memory and logical pinned bytes. All 93 pinned
Buffer files were rechecked; explicit CLR cleanup returns native reservations to zero.

The complete pooled allocator metric facade is now verified, with all original comments
retained. This does not complete the allocator/pooling kernel, events or original fixtures.
Inventory: 159 (57 pending, 63 in-progress, 35 verified, 4 CLR replacements). Evidence:
artifacts/buffer-pool-diagnostics-validation (ignored), buffer-pool-diagnostics-*.trx.

## CLR primitive access replacements

HeapByteBufUtil (18 accessors/nine private fallback methods) and all 24 overloads of
VarHandleByteBufferAccess are now reviewed CLR replacements. The existing ByteBuf
Memory/Span/BinaryPrimitives kernel covers heap/native arrays, derived and pooled
storage; 24-bit words use explicit bytes and crossing words use at most eight stack
bytes. Duplicate Java helper classes, signature-polymorphic calls and Android loading
isolation add no CLR contract. The consumer audit covers heap/pooled heap/direct/unsafe
direct buffers and UnsafeByteBufUtil; remaining raw-address/bulk/provider logic is not
declared complete. This unit adds verification/provenance rather than a new algorithm.

Java signed-byte results map to CLR byte wire values; integer narrowing stays unchecked.
Public CLR range/permission/lifetime checks precede memory access; unchecked Java helper
partial out-of-bounds writes are not replicated. Mixed-readonly composite policies remain
separate. No atomicity/volatile, JVM loading or performance equivalence is claimed.

Original licenses/class descriptions are retained in ByteBuf.Segmented.cs. JVM-only
IntelliJ directives and group labels are preserved verbatim as referenceComments in the
existing JSON rather than inserted into CLR code. Comment counts explicitly distinguish
implementation preservation from archived references; both classes have no missing comments.

144 added cases cover eight backings, edge/random/high-bit/truncated values, both byte
orders, unaligned/end/cross-component ranges, neighbor bytes, readonly reads, cursor/mark/
reference stability and guarded failures. Debug/Release/rebuilt checked Release with SIMPLE
leak sampling interval 1 pass 5649/5649, no failures/skips. Checked build: 51 existing Common
warnings; zero Buffer/test warnings/errors; Common unchanged. Five pinned JVM paths execute
with asserted VarHandle gates; per Debug/Release, 12096 independently modeled CLR states
yield 48384 applicable Java-path matches. All 93 Buffer files were rechecked; CLR native
reservations return to zero. Inventory: 159 (55 pending, 63 in-progress, 35 verified,
6 CLR replacements). Evidence: artifacts/buffer-primitive-access-validation (ignored),
buffer-primitive-access-*.trx.

## Native read-only provider

The optimized ReadOnlyUnsafeDirectByteBuf has a portable copy difference: it passes
the captured root maximum to the direct factory, while the generic provider uses the
factory default maximum. A regression reproduced eight-byte native input producing
MaxCapacity 2147483647 instead of 8. ReadOnlyMemoryByteBuf now preserves the native
captured maximum through root/duplicate/slice copies and skips writing for empty native
copies. Managed/opaque memory keeps its generic heap/default maximum. Existing native
copy test expectations were corrected; creators, writable independence and source
cursors/references stay intact, and failures release newly allocated results.

Bounded native spans replace unchecked Unsafe primitive/bulk access; Memory.Pin
replaces memoryAddress and preserves captured source/derived offsets. External ownership
remains borrowed. CLR destination permission/lifetime/range guards are retained, including
rejecting readonly native writes that raw Java copyMemory can bypass. ReadOnlyMemory is
the type-level readonly view; JVM writable-ByteBuffer constructor checks/provider switches
are not duplicated. Unknown memory managers retain existing heap classification. The
original native provider license, description and offset comments are preserved.

20 added cases cover three creators/four views, ranged/empty copies, growth bounds,
independence after external release, offsets/pins, primitive/bulk reads, guarded failures
and result cleanup. Debug/Release/rebuilt checked Release with SIMPLE leak sampling
interval 1 pass 5669/5669, no failures/skips. Checked build: 51 existing Common warnings;
zero Buffer/test warnings/errors; Common unchanged. Per Debug/Release, 576 independently
modeled CLR states yield 864 matches against original aligned Unsafe, bytewise Unsafe
and generic heap paths. Captured addresses are checked locally, never emitted. All 93
Buffer files were rechecked; explicit CLR cleanup returns native reservations to zero.

The optimized native class is now a CLR replacement; its own fixture pointer scenario
uses CLR native pins. Full inherited read-only/NIO fixtures remain partial. Inventory:
159 (53 pending, 64 in-progress, 35 verified, 7 CLR replacements). Evidence:
artifacts/buffer-readonly-native-provider-validation (ignored), buffer-readonly-native-provider-*.trx.

## Pooled buffer lifecycle events

The three pooled buffer JFR events now use CLR EventSource: provider Netty-NET-Buffer,
AllocateBuffer (ID/keyword 1/1), FreeBuffer (2/2), ReallocateBuffer (3/4). Events are off
until a tracing consumer enables them. Payloads retain allocator type, configured/fast/
maximum capacity, native flag/address, pooled storage and actual thread-cache hits.
No-arena fallback factories preserve the original allocation-only instrumentation;
in-place capacity changes and derived reference changes add no root lifecycle events.
Reallocation records the old snapshot/target followed by successful replacement;
allocation failure emits the attempt only and preserves the existing buffer.

JFR classes, availability/property gates and annotations are replaced by EventSource
metadata and independent keyword gates. Original schema comments and annotation
field/event descriptions are retained. Disabled events skip capture/pins/payload boxing.
Native addresses use short-lived pins and grant no access or lifetime. Original Unsafe
allocation events can contain zero/stale addresses because init commits before refreshing
the pointer; CLR captures current storage. Address availability remains provider-specific,
so addresses are checked locally rather than compared numerically.

EventListener callbacks are synchronous. Immutable value snapshots are captured during
mutations but published after allocator/resize/storage-return locks. Therefore attempt/
free publication timestamps follow the operation rather than exact pre-operation JFR
timing. Events do not retain buffers, owners or operation leases. Listener exceptions
follow EventSource suppression. A full Release run exposed a duplicate EventSource GUID
when configuration tests loaded library copies. The default load context retains the
fixed provider name; separate contexts receive unique Netty-NET-Buffer-{identifier}
identities, discoverable through Log.Name. A simultaneous two-context regression covers
independent subscriptions and keeps the default provider usable.

25 new cases cover payloads, cache miss/hit, zero/huge/no-arena paths, in-place/reallocated/
failed capacity changes, maximum clamping, keyword enable/disable, pointer offsets,
retained views, logical release/operation lease cleanup, observer failures and lock safety.
Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1 pass 5694/5694,
no failures/skips. Checked build: 51 existing Common warnings, zero Buffer/test warnings/
errors; Common unchanged. Per Debug/Release, 144 independent cases match 360 JFR event
payloads/order and 144 byte/cursor/reference states from each original Unsafe/NIO path:
1008 original matches. All 93 Buffer source bytes were checked; CLR native cleanup returns
reservations to zero. Evidence: artifacts/buffer-pooled-events-validation (ignored),
buffer-pooled-events-*.trx, including the pre-fix load-context collision.

The five event/schema sources and JFR fixture move from pending to in-progress. Chunk
and adaptive producers and remaining fixtures stay pending/partial; no whole allocator
completion is claimed. Inventory: 159 (47 pending, 70 in-progress, 35 verified,
7 CLR replacements).

## Pooled chunk lifecycle events

Pooled AllocateChunk/FreeChunk now use EventSource IDs/keywords 4/8 and 5/16.
Payloads retain allocator type, usable capacity, native flag/address and pooled storage;
AllocateChunk threadLocal is always false for this shared allocator. ChunkInfo and the
JFR chunk schema become immutable values over CLR ChunkStorage Memory. Alignment
headroom is excluded from usable capacity; the existing aligned-huge logical-capacity
adaptation remains unchanged. Original event/interface comments and metadata descriptions
are preserved, including the shared-chunk explanation.

Events describe logical registration/retirement. A retained empty subpage can keep a
chunk registered. Returning its last eligible normal region can retire it; heap arenas
may retain one retired backing and register it again, producing another allocation event.
Huge reservations are unpooled. Cache entries defer retirement until drained. Explicit
shutdown defers live regions until their operation leases return; a native pin may keep
physical bytes alive after logical retirement. No duplicate event is emitted on final
pin disposal or disposal of an already retired heap cache entry. Failed native reservations
publish no chunk allocation.

All five buffer/chunk events are captured in producer order and published at the
outermost synchronous operation boundary. This preserves buffer-free before chunk-free,
and resize/new-chunk/new-buffer/old-chunk-free ordering. Physical-thread value guards
cover factories, arena/cache operations, binding/terminated-thread cleanup, metrics cleanup
and shutdown; scheduled direct cache trimming is covered. Lists/pins/payloads are created
only for enabled events, and pending state is cleared before listener callbacks. An
operation beginning while disabled still establishes its boundary, so enabling a listener
while allocation is blocked cannot publish callbacks from inside nested locks. Publication
timestamps follow the operation; no global transactional ordering or JFR timing identity
is claimed. Listener reentry starts an independent batch. Adaptive producers remain pending.

29 added cases cover metadata/order, one/two/reused chunks, retained subpages, huge/aligned
capacity, cache trim/close/cross-thread return, live leases/pins, failed allocation/resize,
keyword gates and listener reentry/failure/late-enable lock safety. Debug/Release/rebuilt
checked Release with SIMPLE leak sampling interval 1 pass 5723/5723, no failures/skips.
Checked build: 51 existing Common warnings, zero Buffer/test warnings/errors; Common
unchanged. Per Debug/Release, 160 independently modeled cases match 652 ordered event
payloads and 160 byte/cursor/reference states per original Unsafe/NIO path: 1624 matches.
The oracle explicitly drains original thread caches and invokes original chunk-list
teardown after all buffers return; this compares teardown events without claiming Java
has CLR IDisposable. Addresses are checked locally, never emitted in comparison rows.
All 93 Buffer source files were rechecked; explicit CLR cleanup returns native bytes to zero.

Four pending chunk/schema/interface entries become in-progress; adaptive integration and
full original fixtures remain partial. Inventory: 159 (43 pending, 74 in-progress,
35 verified, 7 CLR replacements). Evidence: artifacts/buffer-pooled-chunk-events-validation
(ignored), buffer-pooled-chunk-events-*.trx.

## Adaptive allocator lifecycle events

AdaptiveByteBufAllocator now produces all five EventSource events using the existing
IDs/keywords and immutable payload schema. Shared/local fixed classes, shared buddy,
low-memory and dedicated fallback allocations report their actual capacity, pooling and
magazine flags. A last-slot allocation captures its local flag before magazine detachment.
Dedicated reallocation preserves the previous magazine association through one boolean;
CLR does not need to retain or recycle a Java buffer/magazine owner for this metadata.

Resize records the old state/target before reservation and the new allocation after
successful copy/publication. Failed reservation records only the attempt and retains the
old bytes/cursors. Shrink and growth within fast capacity emit no resize event. Final
buffer release captures live allocation metadata even after the logical reference count
reaches zero. Chunk free follows the last storage claim, including outstanding operations
and pins after allocator shutdown; tracing does not extend that lifetime. Heap diagnostic
addresses are zero. The original adaptive heap address helper can add a segment offset to
a zero base; this is deliberately corrected. Native addresses are observed through a
short-lived pin and convey no storage access or ownership.

Physical-thread operation guards cover adaptive factories, lifetime gates, magazines,
shared/local caches, returns and shutdown. All five events preserve producer order and
publish after those locks are released, including when a listener becomes enabled during
a blocked allocation. Disabled tracing establishes only the value guard, without allocating
pending lists, payload arrays or pins. Reentry uses a separate batch; observer exceptions
cannot block storage return. The existing CLR provider identity and publication-timestamp
adaptations still apply; JFR file formats, annotations and timing are replaced by
EventListener/EventPipe/ETW rather than duplicated as JVM infrastructure.

38 new cases cover adaptive storage/policy/lifetime/keyword/failure/late-enable/reentry
paths and the remaining original pooled reuse scenario. All 10 standalone JfrEventsTest
scenarios now map to three isolated CLR tracing fixtures (92 cases), using bounded chunk
geometry and physical FastThreadLocal scopes. All 13 original comments are retained:
12 in implementation and the JVM-only RecordingStream directive as a manifest reference.
The nine event/schema/interface sources and this fixture become CLR replacements based
on both actual original producers/ChunkInfo implementers. Other allocator sources and
fixtures remain in-progress; this does not complete Buffer.

Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1 pass 5761/5761,
no failures/skips. Checked build: 51 existing Common warnings, zero Buffer/test warnings/
errors; Common unchanged. Per Debug/Release, 120 independently modeled scenarios cover
3000 ordered event payloads and 120 byte/cursor/reference/burst states across normal and
low-memory modes. Both original Unsafe/NIO paths agree: 6240 applicable original matches
per CLR configuration. All 93 pinned Buffer source bytes were checked and freshly compiled
for the four JVM profiles. Original shared magazine teardown is invoked explicitly after
all returns; CLR Dispose additionally supports live claims. Addresses are checked locally
and omitted from cross-process rows. Explicit CLR cleanup returns native reservations to
zero. Evidence: artifacts/buffer-adaptive-events-validation (ignored),
buffer-adaptive-events-*.trx. Inventory: 159 (43 pending, 64 in-progress, 35 verified,
17 CLR replacements).

## Byte processing and compatibility alias

Both standalone ByteProcessorTest methods are now translated, retaining the exact input
and all 24 original expected positions. Seven heap/native/mixed-composite/fixed-composite/
offset-slice/duplicate/read-only backings exercise the same assertions. All 13 Common
predicates additionally cover every octet, component boundaries, explicit capacity ranges,
readable overloads and absolute return indices. Stateful delegates and callback exception
identity are checked without changing cursors, marks, reference counts or storage bytes.
Every native reservation is returned. Existing production paths satisfy these contracts;
this unit completes the original fixture without changing production behavior.

The deprecated ByteBufProcessor interface only aliases Common ByteProcessor and duplicates
ten predicates. The entire pinned Java tree has no other consumer. Its CLR replacement is
the existing Common ByteProcessor constants and Func<byte, bool> callbacks accepted by
ByteBuf.ForEachByte/ForEachByteDesc. True continues and false stops; closures hold state and
CLR exceptions propagate directly. The unsigned CLR byte represents the same eight bits
as Java byte; all 256 values are compared. No second obsolete callback interface or set of
constants is needed. Its license is preserved in the fixture; all eleven superseded Java
API redirection comments are archived verbatim in referenceComments. The test source has
all comments preserved. Common remains unchanged.

28 added cases bring Debug/Release/rebuilt checked Release with SIMPLE leak sampling
interval 1 to 5789/5789 passes, no failures/skips. Checked build: 51 existing Common
warnings, zero Buffer/test warnings/errors. Freshly compiled pinned Java Unsafe/NIO paths
and CLR Debug/Release each match 9710 independent model rows: 3328 current predicate
decisions, 2560 deprecated-alias decisions and 3822 scan states including every visited
byte, stop index, cursor and reference count. This is 19420 original matches per CLR
configuration. All 93 Buffer sources plus Common ByteProcessor/ByteProcessorUtils were
byte-checked against the pin before fresh compilation. Evidence:
artifacts/buffer-byte-processor-validation (ignored), buffer-byte-processor-*.trx.
Inventory: 159 (41 pending, 64 in-progress, 36 verified, 18 CLR replacements).
The complete ByteBuf API and other inherited fixtures remain partial.

## Bounded derived view chains

The portable ByteBufDerivationTest scenarios exposed missing upstream normalization:
12 of the initial 22 cases failed. Slice-of-slice and duplicate-of-duplicate kept every
intermediate CLR view; read-only-of-duplicate kept an unnecessary duplicate. A slice's
duplicate also inherited current indices as its initial marks, unlike the original fixed
slice duplicate's zero marks. These are original derivation contracts, not new CLR API.

ByteBufView now composes slice coordinates through its parent factory, collapses ordinary
duplicate intermediates and duplicates a fixed slice as another fixed slice with fresh
zero marks. ReadOnlyByteBuf skips ordinary duplicates. Each result keeps independent
indices and shared data/reference ownership. Parent factory dispatch preserves specialized
read-only/pooled/lifetime behavior; independently counted pooled owners and leak facades
are not unwrapped merely to reduce depth. Root shrink/regrow still checks live CLR memory
bounds; tracing/lifetime policies are unchanged. All original comments for the affected
sliced/duplicate/read-only sources and fixture are retained, including the JVM subtype,
constructor length-hook and inline index-adjustment rationale. These sources remain partial.

30 added cases cover eight original basic scenarios, the portable 10000-operation mixture,
fixed marks/capacity, live root bounds, retained shared ownership, independent pooled
counters and simple/advanced leak tracker closure. CLR AsReadOnly remains idempotent;
Duplicate supplies the independent read-only view requested by the deprecated Java
Unpooled.unmodifiableBuffer factory. Tests assert actual parent identity and metadata
rather than reproducing Java provider class identities. Deprecated testSwap and byte-order
toggling within testMixture remain pending with SwappedByteBuf; the fixture is in-progress.

Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1 pass 5819/5819,
no failures/skips. Checked build: 51 existing Common warnings, zero Buffer/test warnings/
errors; Common unchanged. Per Debug/Release, 22430 independent states agree with each
original Unsafe/NIO path (44860 original matches): all 243 five-operation scripts over
slice/duplicate/read-only, plus 10000 mixed operations for each heap/native backing.
The model checks composed coordinates, capacities, indices/marks, read-only flags, depth,
aliased writes and reference counts. Ordinary portable chains stay within two wrappers;
that bound does not apply to independently owned pooled parent chains or leak facades.
All 93 pinned Buffer source bytes were checked before fresh compilation; explicit CLR
cleanup returns native reservations to zero. Evidence: artifacts/buffer-derivation-validation
(ignored), buffer-derivation-*.trx, including the before-fix failures.
Inventory: 159 (40 pending, 65 in-progress, 36 verified, 18 CLR replacements).

## Explicit endian replacements for swapped buffers

SwappedByteBuf is deprecated upstream in favor of explicit little-endian accessors.
The CLR replacement keeps the existing canonical BE operations and explicit LE operations;
protocol code carries any order choice at the call site across views and copies. It does
not add a mutable order flag, cached wrapper, second cursor owner or deprecated provider
hierarchy. Wrapper identity/order(null)/diagnostic-type behavior is deliberately absent.

| Original operation | CLR operation |
| --- | --- |
| Unsuffixed numeric access on selected BE/LE buffer | Choose the corresponding BE/LE accessor |
| Explicit LE access under either selection | Always use the LE accessor |
| Little-endian char | Cast UInt16 LE to/from a UTF-16 char unit |
| Float/double | Preserve raw Int32/Int64 bits, including NaN payloads and negative zero |
| Ordered NIO views, array/address metadata | Existing Memory/Span/ReadOnlySequence, MemoryMarshal and scoped pins |
| Other wrapper forwarding | Use the underlying ByteBuf API; its remaining surfaces stay tracked separately |

The two Unsafe heap/native strategies and their abstract base become BinaryPrimitives
over current bounded memory. Storage is reacquired after resize; old native addresses
are never cached. Their architecture/unaligned-access dispatch classes are unnecessary.
All original provider comments/licenses are retained in ByteBufEndianTest, including the
native resize/address warning. No JVM Unsafe performance equivalence is claimed.
The API review catalogs 225 public/protected declarations across all four sources and
22 order-aware Buffer consumer files. Comparisons/hash use canonical content; forwarding
and future codec/transport consumers must apply the documented native API mappings.

53 added cases cover seven unpooled/pooled/adaptive heap/native/mixed-composite backings,
signed/unsigned widths, explicit LE invariance, floating raw bits, UTF-16 units,
view/copy/allocated-read cursors and resize. ByteBufDerivationTest now also covers the
adapted shared-cursor order scenario and all four operations in the 10000-step mixture.
All 10 standalone original scenarios have CLR counterparts; this fixture and four
swapped-provider sources become CLR replacements. Entire inherited little-endian fixture
families and the remaining underlying ByteBuf APIs are not thereby complete.

Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1 pass 5872/5872,
no failures/skips. Checked build: 51 existing Common warnings, zero Buffer/test warnings/
errors. Production behavior and Common are unchanged. Per Debug/Release, 20686 independent
model rows agree with each pinned Unsafe/NIO Java path (41372 original matches): 686
numeric/view/growth states and 20000 mixed-order derivation states. The JVM runs assert
actual generic, UnsafeHeapSwappedByteBuf and UnsafeDirectSwappedByteBuf provider coverage.
All 93 Buffer sources were byte-checked and freshly compiled. Explicit CLR cleanup returns
native reservations to zero. Evidence: artifacts/buffer-endian-validation (ignored),
buffer-endian-*.trx. Inventory: 159 (36 pending, 64 in-progress, 36 verified,
23 CLR replacements).

## Original consolidation fixture

Both standalone ConsolidationTest scenarios now retain the original wrapping sequence,
expected ASCII strings, license and class comment. 24 cases cover heap/native/mixed
leaves, component limits, independent copies, captured source coordinates, preserved
indices/marks and complete reference returns. An outstanding CLR pin preserves physical
storage after consolidation retires its logical buffer; disposing it returns the last
native reservation. Existing production behavior satisfies the fixture and is unchanged.

Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1 pass 5896/5896,
no failures/skips. Checked build: 51 existing Common warnings, zero Buffer/test warnings/
errors; Common unchanged. Per Debug/Release, 270 independent combinations of 1/5/12
pairs, default/1/2/3/16 limits, source padding and mutation/consolidation modes produce
4320 exact states per pinned Java Unsafe/NIO path (8640 original matches). The model
checks each wrap's bytes/layout, copy independence, indices/marks, all leaf/node counters
and live native byte accounting through final release. All 93 Buffer sources were
byte-checked and freshly compiled. Evidence: artifacts/buffer-consolidation-fixture-validation
(ignored), buffer-consolidation-fixture-*.trx. Only this fixture becomes verified;
CompositeByteBuf/Unpooled and inherited suites stay partial. Inventory: 159 (35 pending,
64 in-progress, 37 verified, 23 CLR replacements).

## Original empty buffer fixture

All 12 standalone EmptyByteBufTest scenarios now have CLR counterparts, with the
original license and inline comment retained. Twenty added cases (24 in this fixture)
cover queries, empty/nonempty writes and failure cursors, symmetric equality/hash 1,
independent owned-zero release, three text encodings and invalid lengths.

Java array access becomes MemoryMarshal.TryGetArray over borrowed ReadOnlyMemory.
NIO object identity/position/limit become value-type Memory coordinates and one empty
ReadOnlySequence segment. Java's capability-dependent nonzero empty direct address
becomes scoped Memory.Empty pins with a null pointer and no allocation. Simultaneous
writable/read-only pins remain valid across the sentinel's no-op Release. Six cases
cover unpooled/pooled/adaptive allocators with both direct preferences, allocator-local
sentinel identity and zero heap/native metrics and physical reservations. No CLR
capability skip or dummy native backing is required. The original readCharSequence
and readString tests share the existing string/Encoding API.

Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1 pass
5916/5916, no failures/skips; 51 existing Common warnings, zero Buffer/test warnings/
errors. All 93 original Buffer sources and the unmodified original test were freshly
compiled. For each unpooled/pooled/adaptive default, all 12 JVM tests pass under Unsafe;
the 10 non-address tests pass under NIO and both unsupported-address contracts are
asserted separately (66 original test invocations, 12 address-capability checks).
The standalone fixture becomes a CLR replacement; EmptyByteBuf source overrides,
I/O and remaining source comment stay in-progress. Production/Common unchanged.
Evidence: artifacts/buffer-empty-fixture-validation (ignored), buffer-empty-fixture-*.trx.
Inventory: 159 (35 pending, 63 in-progress, 37 verified, 24 CLR replacements).

## Original combined stream fixture

All 10 standalone ByteBufStreamTest scenarios are now covered by the existing input/
output fixtures. Twenty added cases complete real pooled heap/native coverage with
small/normal caches enabled, the combined primitive/text roundtrip, transferred-reference
rollback on invalid input construction, marks/long skip and retained ownership across
input close. Exact original read lengths 3/4, zero-length line/boolean reads and the
length-4 AB-newline-CEF example also run across six storage kinds. Allocator teardown
returns each isolated native reservation domain to zero.

All 17 original fixture comments are retained, including repeated case labels. The
original ownership comment incorrectly names the output adapter at the input-close
site; it is preserved and followed by a CLR clarification. Standard Stream.Read bulk
EOF returns zero, ReadByte EOF returns -1, typed EOF throws EndOfStreamException and
disposed CLR streams reject operations. The original closed-input readLine assertion
maps to active-input EOF plus the existing disposed-stream checks. Existing primitive,
low-byte string and modified UTF wire APIs remain unchanged.

Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1 pass
5936/5936 (125 input/output cases), no failures/skips; 51 existing Common warnings,
zero Buffer/test warnings/errors. All 93 original Buffer sources and the unmodified
fixture were freshly compiled: all 10 original tests pass for every Unsafe/NIO and
unpooled/pooled/adaptive default combination (60 original invocations). Existing adapter
wire evidence remains applicable; production/Common unchanged. This standalone fixture
becomes a CLR replacement. Evidence: artifacts/buffer-stream-fixture-validation (ignored),
buffer-stream-fixture-*.trx. Inventory: 159 (35 pending, 62 in-progress, 37 verified,
25 CLR replacements).

## Allocated-read specialization and forwarding

Source API comparison found four missing ReadBytes(int) overrides. EmptyByteBuf now
returns its own allocator-specific sentinel for length zero, following checkLength;
ordinary owned buffers and read-only derived views still return Unpooled.EmptyBuffer.
WrappedByteBuf and WrappedCompositeByteBuf delegate to the underlying virtual operation.
UnreleasableByteBuf preserves the same inherited Java forwarding: allocated results
retain their independent releasable ownership. Simple leak facades inherit forwarding;
advanced facades keep their existing single access record. Original licenses/comments
are retained; the original override bodies add no operation comments.

Seventy source-based regressions cover three allocators/both preferences, custom heap/
composite overrides, nested facades, invalid/zero/positive lengths, creator provenance,
cursor progress, independent result bytes/lifetime, leak recording/close and native
cleanup. Before the implementation change 54 fail; afterwards all 70 pass. Freshly
compiled exact pinned sources satisfy the same 70 independent contract scenarios on
each Unsafe/NIO path (140 Java cases). Native-versus-heap allocator selection follows
the existing explicit CLR policy; Java without reliable direct cleanup may fall back
to heap. No native provider-selection equivalence is claimed.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1
pass 6006/6006, no failures/skips; 51 existing Common warnings, zero Buffer/test warnings/
errors. Common unchanged. Only these implementation gaps are closed; whole-source and
inherited-fixture statuses stay partial. Evidence: artifacts/buffer-read-dispatch-validation
(ignored), buffer-read-dispatch-*.trx.

## Writable request forwarding

Six missing EnsureWritable overrides now forward both forms through ordinary,
composite and unreleasable facades to the underlying virtual operation. Fluent calls
keep the outer receiver, so an unreleasable facade does not expose its parent. Status
calls preserve codes 0/1/2/3. Simple leak facades inherit this dispatch and nested
advanced tracking records the request once. The advanced composite fluent override
uses the original CompositeByteBuf return type, matching its wrapped base. Original
licenses/comments are preserved; these override bodies add no operation comments.

69 added regressions all fail before the implementation change and pass afterwards.
Heap/native/composite cases cover enough room, growth, bounds/negative requests,
forced maximum growth, single subtype dispatch, bytes, indices/marks and ownership.
Six native growth-failure cases preserve the original eight-byte allocation and data;
releasing the owner returns its isolated reservation to zero. Java injects allocation
failure through the original protected hook; CLR uses a real 16-byte reservation
limit. The two memory-accounting systems are not asserted to be equivalent.

Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1 pass
6075/6075, no failures/skips; 51 existing Common warnings, zero Buffer/test warnings/
errors. All 93 pinned sources were byte-checked and freshly compiled: each Unsafe/NIO
path passes the same 69 independently modeled contract states (138 Java cases).
Common unchanged. Whole-source and inherited-suite statuses remain partial. Evidence:
artifacts/buffer-writable-dispatch-validation (ignored), buffer-writable-dispatch-*.trx.

## Unreleasable comparison forwarding

Java UnreleasableByteBuf inherits WrappedByteBuf's direct hashCode/equals/compareTo
delegates. The CLR shared-index facade now explicitly supplies these inherited
operations, including both object and IEquatable<ByteBuf> equality. Parent overrides
are preserved; after release, comparison against that same underlying parent still
returns true/zero, while distinct content access requires ownership. Comparisons do
not reread content through nested advanced facades and create extra access records.
The existing CLR released-facade reflexivity and IComparable null ordering remain.

28 added regressions cover heap/native/mixed composites and four parent arrangements,
readable bounds, canonical hash/order, parent override dispatch and dead/live cases.
HashSet/Dictionary lookups exercise CLR typed equality; keys are cleared before cursor
changes or release. Nineteen cases fail before the implementation and all pass after.
Both pinned Java Unsafe/NIO paths satisfy 27 independent states each: 24 storage/
lifetime combinations and three original comparison surfaces. CLR's additional typed
equality overload, collection behavior, null ordering and released-wrapper self identity
are checked separately. All 93 original Buffer sources were freshly byte-checked and
compiled. Original source licenses/comments remain retained.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1
pass 6103/6103 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Native cleanup returns isolated reservations to zero. Common unchanged;
whole-source status remains partial. Evidence: artifacts/buffer-unreleasable-comparison-validation
(ignored), buffer-unreleasable-comparison-*.trx.

## Byte iteration forwarding

The four ForEachByte/ForEachByteDesc overloads now delegate directly through
WrappedByteBuf, WrappedCompositeByteBuf and UnreleasableByteBuf, matching the
original wrappers. These 12 missing overrides preserve underlying subtype policy
and prevent nested advanced facades from recording a separate access for every
visited byte. Simple leak facades inherit the delegates. Existing bounded CLR
iteration still reacquires storage after each callback, so native growth during a
callback cannot leave an old borrowed span in use. Original source licenses/comments
remain retained; the original forwarding bodies have no operation comments.

All 62 added regressions fail before the change and pass afterwards. Heap/native/
mixed-composite cases cover all four overloads through ordinary/simple/unreleasable
and composite facades, absolute stop indices, readable/explicit ranges, callback
order, full iteration, exceptions, empty/invalid/released requests, indices/marks,
subtype dispatch and ownership. Six native callback-growth cases observe the next
byte's mutation after reallocation. Cleanup closes each tracker once and returns
isolated native reservations to zero.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1
pass 6165/6165, no failures/skips; 51 existing Common warnings, zero Buffer/test warnings/
errors. All 93 pinned Buffer sources were byte-checked and freshly compiled: each
original Unsafe/NIO path satisfies the same 62 independently modeled scenarios
(124 Java cases). Physical native allocation accounting is checked only on CLR.
Common unchanged; whole-source and inherited-suite statuses remain partial. Evidence:
artifacts/buffer-iteration-dispatch-validation (ignored), buffer-iteration-dispatch-*.trx.

## Text and search forwarding

WrappedByteBuf, WrappedCompositeByteBuf and UnreleasableByteBuf now directly
delegate all three BytesBefore overloads and the five GetString/ReadString/SetString/
WriteString operations. Composite IndexOf delegates too: 25 missing overrides in
total. Simple leak wrappers inherit these delegates. Underlying subtype policy,
encoding identity and single advanced access records are preserved, including failed
requests. Java Charset/CharSequence surfaces continue to use the existing CLR
Encoding/ReadOnlySpan<char>/string adaptation. Original source comments/licenses are
retained; the Java forwarding bodies add no operation comments.

126 new cases cover heap/native/mixed-composite storage, all nine request shapes,
ordinary/simple/unreleasable/composite facades, UTF-8 text across components, exact
bytes, readable/explicit search ranges, not-found results, reader/writer progress,
growth, indices/marks, subtype dispatch, invalid arguments and ownership. Before
the change 115 fail; all pass afterwards. Strict CLR decoder/encoder fallback
failures preserve cursors and record one request. Cleanup closes each tracker once
and returns isolated native reservations to zero.

All 93 pinned Buffer sources were byte-checked and freshly compiled. Each Unsafe/NIO
path passes 126 independently modeled Java scenarios. Live results/bytes/cursors and
request dispatch agree. Eleven released-search outcomes per path retain existing
CLR differences: six heap BytesBefore value/range cases reject inaccessible storage
where Java reaches its zero-capacity IndexOf fast return; five composite IndexOf
cases return -1 because CLR release clears capacity, while Java retains capacity and
then rejects access. Both policies are explicitly tested; no shared search/lifetime
kernel was changed. Strict caller-supplied CLR Encoding failures and physical native
accounting are checked separately from Java's charset replacement behavior.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1
pass 6291/6291 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Common unchanged; whole-source/inherited-suite statuses remain
partial. Evidence: artifacts/buffer-text-search-dispatch-validation (ignored),
buffer-text-search-dispatch-*.trx.

## Derived buffer iteration

ByteBufView, ReadOnlyByteBuf and PooledDerivedByteBuf now implement the original
indexed ForEachByte/ForEachByteDesc parent/root delegates. Slices translate input
coordinates and convert parent results back to view coordinates, normalizing any
result below their adjustment to -1, including zero-offset slices. Duplicates and
read-only views preserve the parent's result. Explicit readable-overload overrides
keep AbstractByteBuf's view-local loop; forwarding those through the newly indexed
override would incorrectly invoke the parent's indexed policy. The six indexed and
six readable overrides retain existing bounded CLR access and callback safety.

All 110 added cases fail before this change and pass afterwards. Eleven derivation
paths cover ordinary/nested/zero-offset slices, duplicates, read-only slices, owned
pooled slices/duplicates and borrowed pooled derivatives on heap/native storage in
both directions. Tests check subtype dispatch/processor identity, stop indices,
not-found normalization, readable ranges, callbacks/exceptions, empty/invalid ranges,
native growth with subsequent byte mutation, indices/marks and ownership. CLR also
rejects independently released pooled views even while their root remains alive;
that safety guard is checked separately from Java's unchecked delegates and recycled
wrapper diagnostics. Native reservations return to zero after cleanup.

All 93 pinned Buffer sources were byte-checked and freshly compiled; each Unsafe/NIO
path passes 110 independently modeled portable scenarios. Original source comments/
licenses remain retained; these indexed override bodies add no operation comments.
Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1
pass 6401/6401 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Common unchanged; whole-source/inherited-suite statuses stay partial.
Evidence: artifacts/buffer-derived-iteration-validation (ignored),
buffer-derived-iteration-*.trx.

## Concrete slice specializations

SlicedByteBuf's constructor and initLength/length/capacity state map to the existing
Slice factory/private fixed ByteBufView and immutable MaxCapacity. Its deprecated
public constructor adds no portable capability. UnpooledSlicedByteBuf's constructor,
capacity, typed unwrap and nine raw getter/setter pairs map to the same view plus
ByteBuf's bounded Memory/Span/BinaryPrimitives kernel. Original consumers are
AbstractByteBuf.slice and the borrowed pooled subclass in AbstractPooledDerivedByteBuf;
the latter maps to PooledDerivedByteBuf and retains its own duplicate/root-coordinate
policy. Both complete concrete classes are now CLR replacements. Their inherited
sources and AbstractByteBufTest coverage remain separate and partial. All original
concrete-class comments are retained; the missing legacy class documentation is added.

28 new CLR cases cover seven heap/native/mixed-composite/pooled/borrowed paths at
zero/nonzero offsets, all 18 scalar accessors with an independent byte oracle and
neighbor sentinels, fixed capacity, initial independent marks, bounds, shared ownership
and parent shrink/regrow. Each original Unsafe/NIO path passes 48 independently modeled
scenarios using both concrete classes and actual borrowed pooled consumers. All 93
pinned Buffer sources were byte-checked and freshly compiled. CLR additionally checks
wide reads against live root capacity, even if a pooled physical region is larger;
unchecked JVM access and provider performance equivalence are not claimed.

The SlicedByteBufTest entry is now in-progress for its stated portable subset; no
complete inherited suite, skipped placeholder, or untested constructor API is claimed.
Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1
pass 6429/6429, no failures/skips; 51 existing Common warnings, zero Buffer/test warnings/
errors. Cleanup returns native reservations to zero. Runtime algorithms/Common unchanged.
Evidence: artifacts/buffer-slice-specialization-validation (ignored),
buffer-slice-specialization-*.trx. Inventory: 159 (33 pending, 62 in-progress,
37 verified, 27 CLR replacements).

## Unsafe copy fixture adaptation

All 12 scenarios in UnsafeByteBufUtilTest now map to 32 CLR cases using the public
SetBytes(int, ReadOnlySpan<byte>) operation. Targets cover unpooled native, pooled
native and unpooled native with a pooled creator. Tests retain every original comment
and cover read-only input, shared pooled heap storage with different offsets, array/
empty copying, all seven invalid ranges, unchanged bytes/cursors/marks and cleanup.
Metadata inspection follows optional leak facades; final Release still uses the
original facade. This removes a reproduced InvalidCastException in the new test
under SIMPLE sampling interval 1. The helper implementation was already functional.

The original null-array-plus-length request has no direct span representation. Null
arrays converted to spans and default spans are valid empty inputs; a nonzero range
over null fails at span construction. ByteBuffer position/limit becomes an immutable
source range. Source range construction can precede target validation; combined-invalid
argument precedence is not asserted equivalent. Existing CLR empty-copy target bounds/
accessibility checks remain, while Java's empty ByteBuffer path may return earlier.
The Unsafe availability prerequisite is unnecessary on CLR; no test is skipped.

Only the two source setBytes overloads (array/range and ByteBuffer) are mapped in the
UnsafeByteBufUtil entry, now in-progress. Original null-crash/copy comments are retained
in the CLR operation; the obsolete internalNioBuffer bounds directive is archived.
Other scalar, zero-fill, stream and address helpers remain pending. The standalone
fixture is a CLR replacement; completing it does not complete the helper class.

All 93 pinned Buffer sources and the unmodified fixture were freshly byte-checked/
compiled. All 12 original tests pass with Unsafe; each Unsafe/NIO public-provider path
passes 31 independently modeled copy/layout/range scenarios. CLR null-span semantics
and physical native cleanup are checked separately. Full net10.0 Debug/Release/rebuilt
checked Release with SIMPLE leak sampling interval 1 pass 6461/6461, no failures/skips;
51 existing Common warnings, zero Buffer/test warnings/errors. Native reservations
return to zero; Common/runtime algorithms unchanged. Evidence:
artifacts/buffer-unsafe-copy-fixture-validation (ignored), buffer-unsafe-copy-fixture-*.trx.
Inventory: 159 (31 pending, 63 in-progress, 37 verified, 28 CLR replacements).

## Heap provider specializations

UnpooledUnsafeHeapByteBuf and PooledUnsafeHeapByteBuf are now CLR replacements for
their complete concrete specialization. Existing heap/pooled storage and the bounded
ByteBuf Memory/Span/BinaryPrimitives kernel cover all nine scalar getter/setter pairs,
checked unpooled entry points, pooled offsets and SetZero/WriteZero. Real original
consumers are UnpooledByteBufAllocator's instrumented heap type and PoolArena's heap
factory. Oracle runs assert that Unsafe selects these original classes and NIO selects
their safe counterparts. Inherited source/fixture completion remains separate.

JVM uninitialized allocation, unaligned dispatch and Recycler wrappers require no
additional CLR subtype. Existing managed initial-byte policy and fresh CLR buffer
objects remain; storage is still pooled. Deprecated swapped selection uses the
existing explicit BE/LE replacement. Every concrete comment is retained, including
the unaligned-only directive archived as reference. The original helper's BE-default
and small-write rationale are retained beside CLR scalar/Span.Clear code. Only its
array scalar/zero subset is additionally reviewed here; the whole UnsafeByteBufUtil
entry remains in-progress for its unclosed address/stream/copy contracts.

32 added cases verify three independent word patterns at offsets 0/1/7/120, exact
neighbor bytes, nonzero pooled storage offsets, zero lengths 0/1/7/8/63/64/65/128/129,
WriteZero growth and writer/mark changes. Heap disposal metrics return to zero;
the absent direct arena reports -1 (unknown), not a positive reservation. Stale CLR
owners cannot revive after a later allocation. JVM recycling identity, initial
unwritten bytes and hardware timing are not asserted equivalent.

All 93 pinned Buffer sources were byte-checked/freshly compiled; each Unsafe/NIO
path passes 32 independently modeled portable scenarios. Full net10.0 Debug/Release/
rebuilt checked Release with SIMPLE leak sampling interval 1 pass 6493/6493, no failures/
skips; 51 existing Common warnings, zero Buffer/test warnings/errors. Runtime algorithms/
Common unchanged. Evidence: artifacts/buffer-heap-provider-validation (ignored),
buffer-heap-provider-*.trx. Inventory: 159 (29 pending, 63 in-progress, 37 verified,
30 CLR replacements).

## Native reallocation specialization

UnpooledUnsafeNoCleanerDirectByteBuf is now a CLR replacement for its complete
concrete specialization. Existing UnpooledDirectByteBuf/UnpooledByteBufAllocator
and NativeMemoryOwner cover owned native allocation, Capacity, cleanup and logical
capacity metrics. JVM-only throwing ByteBuffer allocation/free hooks and Cleaner
provider selection require no separate CLR API. The original sole comment (2016
license) is retained in DirectByteBufReallocationTest. Inherited Unsafe/NIO direct
sources, UnsafeByteBufUtil and full direct fixtures remain partial.

CLR capacity replacement deliberately allocates, copies and then publishes rather
than calling NativeMemoryAllocator.Reallocate: old pins must retain their original
allocation and growing writes may alias old storage. The quota therefore covers
old plus replacement storage, even on shrink; an empty owner reserves one physical
byte. Newly added bytes are cleared. Allocation failure preserves bytes, indices,
marks and counters, whereas Java trims indices before a potentially failing realloc.
Old managed Memory views become invalid after replacement; outstanding pins retain
physical storage through final buffer release. Factory metrics count logical capacity,
independently of physical reservations. JVM delta-budget and pointer/timing equivalence
are not claimed. The capacity setter now explains this policy; its algorithm is unchanged.

32 new CLR cases cover constructor/factory zero/no-op/grow/shrink transitions, data
prefixes, cursor trimming, marks after regrowth, invalid capacities, released no-op
requests, peak-budget failures/success, old pins and borrowed-owner detachment at
zero/nonzero offsets. Borrowed-owner behavior is an additional CLR ownership check,
not a claim about a NoCleaner borrowing constructor. Cleanup returns reservations
and logical metrics to zero, including pins released after logical final release.

All 93 pinned Buffer sources were byte-checked and freshly compiled. Each actual
Unsafe/NIO/NoCleaner provider passes 22 independently modeled portable states; the
NoCleaner run enables reflective access and asserts both platform availability and
the concrete constructor/factory selection. Full net10.0 Debug/Release/rebuilt checked
Release with SIMPLE leak sampling interval 1 pass 6525/6525 without failures/skips;
51 existing Common warnings, zero Buffer/test warnings/errors. Common/runtime
algorithms unchanged. Evidence: artifacts/buffer-native-reallocation-validation
(ignored), buffer-native-reallocation-*.trx. Inventory: 159 (28 pending,
63 in-progress, 37 verified, 31 CLR replacements).

## Pooled native provider specialization

PooledUnsafeDirectByteBuf is now a CLR replacement for its complete concrete
specialization. Existing PooledDirectByteBuf/PooledByteBuf/PoolDirectArena and the
bounded ByteBuf kernel cover region initialization, all 18 scalar accessors, four
input/output forms, direct Copy, SetZero and WriteZero. Raw-address caching becomes
per-access Memory/Span region resolution; NIO duplicate/position/limit becomes an
immutable range. Scoped Memory.Pin replaces native interop addresses; it preserves
physical storage, not ownership of a reusable pooled region. Callers retain the
buffer throughout region use. Native memory exposes no managed array. Fresh CLR
wrappers replace Recycler reuse; storage remains pooled. The original license is
retained in the fixture and the unaligned-only swapped directive is archived.
No hardware timing, wrapper identity or stable-address equivalence is claimed.

62 new cases verify normal pooled regions (actual nonzero offsets and poisoned
neighbors) and huge unpooled backing. They cover independent scalar byte models,
zero-fill thresholds/end bounds, WriteZero growth, direct copy ownership/provenance,
heap/native/segmented peers, array/span ranges, scoped pins, and one-read stream
short read, EOF, exception and output behavior. Absolute operations preserve cursors
and marks; native reservations and allocator metrics return to zero after disposal.
CLR additionally checks released-owner safety, read-only destinations and zero-length
input callback bypass. Java EOF -1 maps to CLR Stream EOF zero. ByteBuffer cursor
changes are modeled as explicit ranges, not mutable CLR span cursors.

All 93 pinned Buffer sources were byte-checked/freshly compiled. Original provider
selection and normal/huge storage are asserted on both Unsafe/NIO paths. Unsafe
agrees with 62 independent portable states; NIO agrees with 60 and reproduces two
UnsupportedOperationException cases on composite output: its AbstractByteBuf branch
calls CompositeByteBuf.internalNioBuffer for a multi-component range. CLR keeps its
existing bounded segmented copy, as does the original Unsafe path. This is a recorded
original-provider difference, not a CLR regression or a skipped test. Full inherited
fixtures, UnpooledUnsafeDirectByteBuf and raw-address helper internals remain partial.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1
pass 6587/6587 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Runtime algorithms/Common unchanged; production additions explain
the provider consolidation. Evidence: artifacts/buffer-pooled-native-provider-validation
(ignored), buffer-pooled-native-provider-*.trx. Inventory: 159 (27 pending,
63 in-progress, 37 verified, 32 CLR replacements).

## Unpooled native provider specialization

UnpooledUnsafeDirectByteBuf is now a CLR replacement for its complete concrete
specialization. Existing UnpooledDirectByteBuf and the shared bounded kernel cover
all 18 scalar accessors, checked overrides, bulk/stream operations, direct Copy and
zero-fill. Owned constructors/factories use NativeMemoryOwner; borrowed ByteBuffer
constructors become explicit immutable Memory ranges. The dangerous slice=false
mutable-cursor API and JVM expensive-clean/Unsafe/VarHandle switches need no CLR
compatibility members. Scoped pins replace cached addresses, including both original
setByteBuffer address updates. The existing raw-address wrapper covers the actual
internal doFree consumer with explicit owned/borrowed SafeHandle behavior.

All 18 original concrete comments are retained: 12 in implementation and six as
references for obsolete JVM APIs/argument labels/dispatch. The original Java9
borrowed-owner rationale remains beside the CLR constructor, with a CLR explanation.
Established allocation/copy/publish, cleared tails, peak quota, failure atomicity,
old-Memory invalidation and pin-delayed free policies remain. Borrowed owner lifetime
is the caller's responsibility; detaching/releasing a buffer never disposes that owner.

The former PooledNativeByteBufProviderTest is now NativeByteBufProviderTest: its
62 pooled/huge cases are preserved and 93 cases add unpooled factory, explicit owned
and borrowed offset-11 storage. The shared 155 cases verify scalar byte order/bounds,
zero/writeZero growth, direct copy ownership, heap/native/segmented transfers, spans/
pins and streams. Borrowed prefix/suffix sentinels survive mutation, growth and final
release. Existing reallocation/raw-address ownership fixtures pass in the full suite.

All 93 pinned Buffer sources were byte-checked/freshly compiled. Actual Unsafe and
forced VarHandle paths each agree with 155 independent states; NIO agrees with 150
and reproduces five composite-output exceptions. Each path additionally checks 15
invalid-input-range probes. In nine unpooled NIO probes, input is consumed before
bounds checking; EOF can bypass the check and an input failure can take precedence.
NIO output range errors can also originate as IllegalArgumentException from a
ByteBuffer limit setter. CLR keeps pre-I/O range validation and idiomatic exceptions,
EOF zero, read-only guards and zero-length callback bypass. These differences are
recorded; no JVM mutable-cursor, exception-precedence or hardware timing equivalence
is claimed. Inherited direct sources, helper internals and complete fixtures remain partial.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1
pass 6680/6680 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Common/runtime algorithms unchanged. Evidence:
artifacts/buffer-unpooled-native-provider-validation (ignored),
buffer-unpooled-native-provider-*.trx. Inventory: 159 (26 pending, 63 in-progress,
37 verified, 33 CLR replacements).

## Unsafe helper completion

UnsafeByteBufUtil is now a complete CLR replacement. All 55 static methods are
accounted for: 36 scalar overloads, two zero-fill forms, Copy, eight bulk/stream
overloads, two alignment conveniences, native creation, and five private strategy
helpers. Existing Memory/Span/BinaryPrimitives/Span.Clear and native constructors
provide these semantics; Java signed-byte reads become CLR unsigned wire bytes.
No duplicate unsafe CLR helper class is needed. The two
alignment conveniences have only the private zero loop as a consumer, so the CLR
zeroing kernel replaces them without exposing general raw-address arithmetic.

Seven actual original consumers are reviewed. Heap/native pooled/unpooled/read-only
providers use the shared bounded kernel. The original creation helper is called by
PooledByteBufAllocator's arena-free fallback and AdaptiveByteBufAllocator's direct
chunk allocator; both CLR paths use the existing native storage constructor. Tests
cover those actual public consumers, not an unused compatibility factory. Adaptive
Memory views use region claim managers over native backing; they are not direct
NativeMemoryOwner views. Native-budget denial and final disposal return reservations
to zero; an absent pooled direct arena reports -1 (unknown).

72 added cases cover all eight alignment residues and zero lengths around 8/64 bytes,
output sizes 1024/1025/8192/8193 with success/failure, factory sizes 1/128/1048577 and
zero-budget failure. Original 32 fixture cases remain, for 104 UnsafeByteBufUtilTest
cases. New small-allocation probes use 4 KiB pages and 64 KiB chunks in both runtimes,
large enough for the original subpage run requirements. All 15 helper comments are
retained: 11 in implementation and four archived JVM strategy directives.

All 93 pinned Buffer sources were byte-checked/freshly compiled. Unsafe, VarHandle,
bytewise, NoCleaner and NIO paths each pass 70 independently modeled storage/consumer
scenarios; each of four Unsafe-enabled paths additionally passes 16 direct helper
scalar cases (432 getter/setter pairs), covering both address and array overloads.
Actual factory delegates and NoCleaner selection are asserted. Raw array/address
Unsafe helpers require Unsafe; NIO compares public provider operations. CLR's two
quota failures and physical cleanup are additional checks, not JVM limit equivalence.

CLR stream input keeps one staged read, EOF zero and pre-I/O bounds checks. Output
keeps one whole-range ArrayPool snapshot/write to prevent callback invalidation,
using O(request length) temporary space; Java may use multiple 8 KiB writes and
provider-dependent temporary buffers. Callback partitioning, peak temporary memory
and performance are not asserted equivalent. Empty/null-span, permission and native
ownership rules remain as previously documented. No runtime algorithm changed.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1
pass 6752/6752 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Common unchanged. Evidence: artifacts/buffer-unsafe-helper-validation
(ignored), buffer-unsafe-helper-*.trx. Inventory: 159 (26 pending, 62 in-progress,
37 verified, 34 CLR replacements). Full inherited sources/fixture completion is separate.

## Derived stream forwarding

Added five synchronous and five async stream overrides across ByteBufView,
PooledDerivedByteBuf and ReadOnlyByteBuf. Previously inherited stream code copied
storage directly and bypassed custom parent GetBytes/SetBytes Stream overrides.
Now duplicate/read-only output and sliced/pooled input/output follow the pinned
parent/root delegates with the appropriate coordinate adjustment. Output returns
the view; input returns the parent's actual count. Relative stream operations
still move only the view's cursor after successful completion. Read-only input
remains rejected before any parent call.

The C# async counterparts directly await the parent's ValueTask with the original
cancellation token and ConfigureAwait(false). No Task.Run or synchronous fallback
is introduced. Pending operations leave view cursors unchanged until completion;
exceptions propagate without publishing cursor updates. Async validation failures
remain faults of the returned operation. Callers continue to keep borrowed views,
their owners and streams alive for the operation, excluding concurrent mutation.

106 new cases reproduce 98 failures before the fix. They cover heap/native roots
and eleven ordinary/nested/read-only/retained/borrowed slice/duplicate paths, exact
parent method/stream/coordinates, absolute bytes, fluent identity, short reads,
own relative cursors, marks, references, parent exceptions and invalid ranges.
Independent pooled-view release is checked while the root remains alive. Eighteen
pending async cases verify tokens, awaited completion and cursor publication;
pending work is completed/drained before fixture disposal. Existing guards stay
before custom parent dispatch, even for duplicate/read-only forwarding.

All 93 pinned Buffer sources were byte-checked and freshly compiled. Each original
Unsafe/NIO path agrees with 44 independently modeled sync scenarios and 40 invalid
range probes. Java visits the custom parent in 14 invalid duplicate/read-only/
pooled-duplicate requests before rejecting the range; CLR keeps its earlier own-view
guards. Exact JVM exception precedence is not claimed. The 62 async cases are CLR
extensions of the same contract, not original Java async Stream methods.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1
pass 6858/6858 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Original affected source comments remain retained; Common unchanged.
This unit changes Stream forwarding, with socket/file/scalar/bulk forwarding and
full source/fixture completion remaining separate. Evidence:
artifacts/buffer-derived-stream-validation (ignored), buffer-derived-stream-*.trx.
Inventory remains 159 (26 pending, 62 in-progress, 37 verified, 34 CLR replacements).

## Derived socket and positioned-file forwarding

Added ten synchronous and ten async overrides across ByteBufView,
PooledDerivedByteBuf and ReadOnlyByteBuf. The inherited Socket/SafeFileHandle
implementations previously bypassed custom parent channel methods. Duplicate,
slice and pooled views now follow the pinned GatheringByteChannel,
ScatteringByteChannel and FileChannel delegates. Read-only views forward only
output. The view offset adjusts the buffer index; the long file position is
forwarded unchanged. Actual counts propagate to relative operations, which move
only the view's cursor after successful completion.

CLR own-view bounds, lifetime, null, handle, permission and cancellation checks
remain before parent dispatch. The existing file validator is shared by internal
derived types through a private protected helper. Async overrides await the
parent ValueTask with the original token and ConfigureAwait(false), preserving
fault delivery through the returned operation. Caller lifetime/exclusion rules
and the existing CLR socket EOF/partial-send and complete-file-write policies
remain in effect; no Task.Run or synchronous fallback is introduced.

212 new cases reproduce 196 failures before the fix. The 176 ordinary cases cover
heap/native roots and eleven ordinary/nested/read-only/retained/borrowed view
paths, absolute bytes, short input/output counts, marks, references, parent
exceptions, relative cursors, invalid/empty ranges, negative/overflowed file
positions, offsets above Int32.MaxValue, pre-cancellation and independent pooled
view release while the root remains alive. All 160 permitted combinations also
perform real loopback socket or positioned-file I/O. Thirty-six pending custom
parent cases verify exact tokens and cursor publication only after completion;
pending work is completed/drained before disposal.

All 93 pinned Buffer sources were byte-checked and freshly compiled. Each original
Unsafe/NIO path matches 88 independently modeled synchronous scenarios, 80 invalid
range probes and 80 empty-range delegates. Java visits the custom parent in 28
invalid duplicate/read-only/pooled-duplicate probes; CLR keeps its earlier own-view
guards. File offsets above Int32.MaxValue are observed unchanged without creating
large files. The 124 async cases extend the synchronous Java channel contracts
with CLR ValueTask/token completion semantics.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1
pass 7070/7070 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Source comments retained; Common unchanged. This closes the
channel-forwarding gap, not entire derived classes or inherited fixtures.
Evidence: artifacts/buffer-derived-channel-validation (ignored),
buffer-derived-channel-*.trx. Inventory remains 159 (26 pending, 62 in-progress,
37 verified, 34 CLR replacements).

## Derived bulk forwarding

Added ten Span/ByteBuf GetBytes/SetBytes overrides across ByteBufView,
PooledDerivedByteBuf and ReadOnlyByteBuf. Inherited storage fast paths previously
bypassed custom parent bulk methods. Ordinary and pooled views now preserve the
parent/root overload, translated buffer index, original peer identity/index and
fluent view identity. Read-only views forward output only. Relative operations
continue to update the view and ByteBuf peer cursors after successful transfer;
absolute calls preserve both cursors and marks.

Java's byte-array and NIO ByteBuffer ranges map to CLR Span/ReadOnlySpan ranges.
Spans have no NIO position/limit cursor and can also alias native memory. The
existing bounded copy kernels preserve overlapping bytes. Own CLR bounds,
lifetime, null and read-only checks precede dispatch; peer validation remains
part of the parent's overload. JVM provider-internal secondary bulk calls are
not reproduced as mandatory CLR dispatches.

352 new cases reproduce 320 failures before the fix. There are 176 ordinary cases
across heap/native roots, eleven ordinary/nested/read-only/retained/borrowed view
paths and Span/heap/native/segmented peers. Native input peers are read-only.
Tests verify exact parent overload and peer identity, bytes, fluent receiver,
absolute/relative cursors, marks, references, parent exceptions, own and peer
range validation, null peers, empty ranges and independently released pooled
owners while the root stays alive. Another 176 cases verify forward/backward
aliases through spans and ByteBuf views, including native storage.

All 93 pinned Buffer sources were byte-checked and freshly compiled. Each original
Unsafe-enabled/NIO profile matches an independent 440-scenario model: 220 ordinary
cases (including NIO ranges) and 220 aliases. Direct Java buffers lack array
aliases; NIO covers those, while heap aliases cover arrays and NIO. Original own
range probes number 200 per profile, empty-range probes 200, peer-range probes
120; Java visits custom parents before invalid own-range rejection in 70 probes.
CLR retains its earlier own-view/null guards.

The model explicitly records 11 original UnpooledDirectByteBuf output failures
per profile when the destination is a multi-component CompositeByteBuf. Its
AbstractByteBuf destination fast path requests internalNioBuffer, which the
composite rejects, including for empty ranges. Source and destination bytes and
cursors remain unchanged. CLR keeps its existing segmented-copy support instead
of importing this provider restriction; JVM/CLR behavior is not claimed identical
for those cases.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1
pass 7422/7422 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Original source comments retained; Common unchanged. Full derived
sources, scalar dispatch and inherited fixtures remain partial. Evidence:
artifacts/buffer-derived-bulk-validation (ignored), buffer-derived-bulk-*.trx.
Inventory remains 159 (26 pending, 62 in-progress, 37 verified, 34 CLR replacements).

## Derived scalar dispatch

Added 45 public absolute scalar overrides across ByteBufView,
PooledDerivedByteBuf and ReadOnlyByteBuf. Ordinary duplicates, owned/borrowed
pooled duplicates and read-only views follow the parent's public getter/setter
with translated coordinates; slices use their bounded storage kernel. Read-only
views forward getters only. CLR own bounds/lifetime/write guards remain first.
Signed/unsigned/boolean/char/floating-point absolute conveniences compose these
nine byte/short/medium/int/long BE/LE getters/setters as in the original.

Relative primitives now use the existing bounded Span/word kernels rather than
public absolute overrides, following AbstractByteBuf and the optimized unpooled,
pooled and read-only factory views. This also fixes relative access on a custom
root provider. No unsafe JVM kernel hierarchy or unchecked memory API is added.
The pinned PooledDuplicatedByteBuf._setLongLE is an exception: it calls the public
root setter. Owned duplicate WriteLongLE, and composed WriteDoubleLE, preserve
that policy and publish the view writer only after success. Borrowed duplicates
and slices retain their bounded relative storage path.

Signed medium reads compose relative unsigned-medium reads. Boolean/char/floating
relative APIs compose their relative integer methods, preserving custom relative
policies. Boolean/char and BE floating writes return the receiver after invoking
the integer writer; Java's LE floating methods return the delegated writer's
result, which is also preserved for custom overrides. Raw float/double bits,
24-bit sign extension and checked-build truncation remain explicit. Java signed
byte getters are normalized to CLR unsigned wire octets.

934 added cases cover heap/native roots and eleven ordinary/nested/read-only/
owned/borrowed view paths across 21 scalar forms. Parent providers alter returned
values/written bytes or throw, verifying actual policy and call coordinates.
Tests check absolute/relative bytes, independent cursors/marks/references,
read-only guards, invalid ranges and released owners while the root remains
alive. Two root cases exercise every relative primitive; eight composition cases
check custom integer overrides and returned receiver identity. Replaying the final
tests against the previous commit produces 304 failures among 934 cases.

All 93 pinned Buffer sources were byte-checked and freshly compiled. Unsafe-enabled
and NIO profiles each match an independent 934-scenario custom-provider model.
The original views make 840 invalid-range probes; 294 visit the public parent
before rejection, while CLR keeps its earlier own-view guards. Four scenarios
exercise the owned duplicate's relative LongLE exception. Deprecated explicit
unoptimized wrapper constructors are not introduced.

The Java direct test provider overrides four protected medium hooks with bounded
NIO access to isolate view dispatch from provider-internal callbacks. Production
UnpooledDirectByteBuf uses three public byte calls for medium values; CLR keeps
its existing word kernel. Original production sources are unchanged, and JVM
protected-provider callback topology/performance is not claimed identical.

UnpooledDuplicatedByteBuf's complete concrete purpose (constructor/unwrap and 18
optimized protected scalar hooks) now maps to the CLR duplicate and bounded
relative kernel. Its class comment is restored alongside the existing license;
both original comments are retained. The class is a CLR replacement; inherited
DuplicatedByteBuf and full original fixture completion remain separate.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1
pass 8356/8356 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Common unchanged. Evidence:
artifacts/buffer-derived-scalar-validation (ignored), buffer-derived-scalar-*.trx.
Inventory remains 159: 26 pending, 61 in-progress, 37 verified, 35 CLR replacements.

## Read-only fixture completion

ReadOnlyByteBufTest.java is now verified. Its 27 original JUnit methods are
accounted for individually in the existing manifest's caseMappings: 19 semantic
ports and eight explicit CLR adaptations. Completion applies to this fixture;
ReadOnlyByteBuf.java and inherited source/fixture status remain independent.

111 added CLR cases bring the fixture to 130 cases. Constructor/factory tests
cover ordinary, wrapped and duplicated heap/native parents without JVM subtype
casts, identity/metadata, explicit BE/LE reads and force/non-force writable status
classification. Two public memory-parent cases verify exact callback coordinates,
returned read-only borrow identity, live shared storage and pre-dispatch guards.
A 96-case matrix ports both original duplicate-of-slice tests
across pooled/unpooled heap/native writable/read-only parents, borrowed/retained
slices and duplicates, and readable/explicit/empty ranges. It verifies capacity,
indices, retained-parent lifetime, final release and zero native reservations.

Java's mutable ByteOrder facade is replaced by explicit BE/LE access. Byte arrays
and NIO ByteBuffer inputs/views use Span and ReadOnlyMemory. Existing CLR own-view
range/lifetime/null validation remains before parent dispatch; null Stream,
Socket and ByteBuf inputs fail argument validation before read-only permission
checking, while valid and empty span inputs still reject mutation. Existing
stream/channel/bulk/scalar fixtures verify actual parent policies with real
providers. The original blind forwarding Mockito test therefore has a documented
CLR adaptation rather than an invalid-argument forwarding requirement.

All 93 pinned Buffer sources and the exact original ReadOnlyByteBufTest.java were
byte-checked and freshly compiled. All 27 unchanged original @Test methods run
on fresh fixture instances and pass in both Unsafe-enabled/NIO profiles, using
cached Mockito/AssertJ/JUnit dependencies. No original test is filtered out. This
Java run confirms the reference fixture; the manifest records the deliberate
CLR argument/representation differences instead of claiming identical APIs.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1
pass 8467/8467 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Production source unchanged. Evidence:
artifacts/buffer-readonly-fixture-validation (ignored), buffer-readonly-fixture-*.trx.
Inventory remains 159: 26 pending, 60 in-progress, 38 verified, 35 CLR replacements.

## Duplicate fixture ownership and marks

171 added CLR cases port all four locally declared DuplicatedByteBufTest methods,
the RetainedDuplicatedByteBufTest factory handoff and 13 selected inherited
AbstractByteBufTest methods. The existing manifest records method mappings and
moves both concrete fixtures from pending to in-progress. Their remaining inherited
suite is still incomplete; RetainedSlicedByteBufTest remains pending.

Both ordinary and retained-returning factories run over unpooled heap/native,
mixed segmented composite and pooled heap/native storage. Tests cover contiguity,
parent capacity growth, initial/independent marks, duplicate grow/shrink, nested
shared content, releasing the original before handing off, repeated retained
children, borrowed release and final deallocation with zero native reservations.
Expected buffers become managed byte-array expectations; applicable original
comments and both fixture licenses are retained. No extra Java provider hierarchy
or wrapper recycling is introduced.

The deprecated Java duplicate constructor maps to the existing internal CLR view.
It now explicitly rejects null with ArgumentNullException. The final 171-case
suite has one failing null-argument case before this guard and 170 passing cases;
this unit otherwise adds reference tests without changing storage/ownership rules.
In particular, an owned pooled duplicate's borrowed duplicate copies current
indices from its owner but initializes marks from the root. A direct constructor
copies marks from its immediate parent. This original distinction is separately
tested instead of assuming all duplicate factories have the same initial marks.

All 93 pinned Buffer sources and the three original abstract/duplicate fixtures
were byte-checked and freshly compiled. Selected 17 unchanged original methods
per concrete fixture run with original init/dispose (34 tests), plus ten direct
constructor/factory mark scenarios over the same provider matrix, in each of the
Unsafe-enabled and NIO profiles. Other inherited methods are not claimed run.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1
pass 8638/8638 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Common unchanged. Evidence:
artifacts/buffer-duplicate-fixture-validation (ignored), buffer-duplicate-fixture-*.trx.
Inventory remains 159: 24 pending, 62 in-progress, 38 verified, 35 CLR replacements.

## Retained slice fixture handoff

281 added CLR cases port RetainedSlicedByteBufTest.newSlice, seven selected
SlicedByteBufTest methods and ten inherited AbstractByteBufTest methods. Existing
caseMappings record the exact methods; the retained fixture moves from pending to
in-progress. Remaining inherited cases, platform/NIO/thread tests and fixed-capacity
encoding expansion overrides stay incomplete. No empty/skipped tests are added.

Five storage paths (unpooled heap/native, mixed composite and pooled heap/native)
run ordinary and retained-returning factories at offsets 0/3. Retained factories
release the original claim before returning. Tests verify fixed capacity, fresh
marks, contiguity, content aliases, two generations of retained or explicitly
retained borrowed slices, repeated children, empty borrowed chains and final
zero reference counts/native reservations. Shared and independent counters are
both valid. Managed literal expectations replace allocated expected buffers;
applicable original annotations and both fixture licenses are retained.

The internal slice constructor previously accepted a null parent. It now rejects
that argument immediately with ArgumentNullException. Before the guard, 280 new
cases pass and the null-constructor case fails because no exception is thrown.
No storage, factory ownership or reference-counting algorithm is changed.

All 93 pinned Buffer sources and three unchanged original fixtures were byte-checked
and freshly compiled. In each Unsafe-enabled/NIO profile, selected 17 original
methods per concrete fixture pass with init/dispose (34 tests). The same selected
bodies run 170 more times with only the test factory overridden for five providers
and ordinary/retained handoff. Twenty supplemental offset/mark/empty-chain probes
also pass. Unselected original methods are not claimed run.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE leak sampling interval 1
pass 8919/8919 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Common unchanged. Evidence:
artifacts/buffer-retained-slice-fixture-validation (ignored), buffer-retained-slice-fixture-*.trx.
Inventory remains 159: 23 pending, 63 in-progress, 38 verified, 35 CLR replacements.

## Common fixture method review

The three common base fixtures now partition every declared JUnit method between
caseMappings, evidence-backed methodFindings and testMethodReview.unreviewedMethods.
Unreviewed means no individual decision yet; an existing equivalent CLR test or
implementation may already exist. Mapped counts cover only each mapping's stated
scenarios/providers, not all inherited provider variants or whole-fixture completion.

| Original fixture | Declared methods | Mapped | Test gaps | Unreviewed |
| --- | ---: | ---: | ---: | ---: |
| AbstractByteBufTest | 408 | 24 | 0 | 384 |
| AbstractCompositeByteBufTest | 85 | 52 | 0 | 33 |
| AbstractByteBufAllocatorTest | 10 | 10 | 0 | 0 |

Ten existing-test mappings are newly recorded after comparing bodies: initial
state, four iterator cases, capacity calculation and four unpooled metric cases.
The shouldReuseChunks finding initially identified a missing
warm-up/100-allocation/<8 MiB thread-allocation test. The allocator chunk-reuse unit
below resolves that finding for pooled/adaptive heap factories; the original
unpooled applicability exclusion is retained. No implementation gap is confirmed by this limited review;
that is not a claim that unreviewed methods have complete implementations.

Update-BufferPortingManifest.ps1 regenerates the partition from pinned sources;
-Check detects stale/missing decisions and invalid original/CLR method names
without changing files. It preserves existing status/comment semantics. Parser
and decision checks cover comments/literals, package-visible and parameterized
methods, duplicate/unknown/stale decisions and missing target declarations.
Java reflection independently agrees on all 503 declared JUnit names after fresh
compilation of 93 pinned Buffer sources and four unchanged test sources. Original
test bodies are not executed in this metadata audit.

Full net10.0 Release tests pass 8919/8919 without failures/skips; mapped CLR tests
are confirmed in that TRX. No runtime/test implementation or fixture status changed.
Evidence: artifacts/buffer-method-review-validation (ignored), buffer-method-review-release.trx.

## Allocator chunk reuse budget

The shouldReuseChunks method-level test gap is now mapped for pooled/adaptive
heap factories: one warm-up, then 100 allocations/releases with initial=max=1 MiB,
requiring strictly less than 8 MiB of managed allocation on the calling physical
thread. GC.GetAllocatedBytesForCurrentThread replaces the optional JVM MXBean.
Three added CLR cases include both allocators and an unpooled negative control
that detects at least 100 MiB of fresh backing arrays. The original unpooled
fixture explicitly disables the reuse requirement; no skipped CLR test is added.

The measurement disables leak diagnostics in a nonparallel collection and restores
the prior level in finally, matching the Java reference-run profile. Forced SIMPLE
sampling interval 1 allocates about 27 MB per 100 cycles for diagnostics and fails
the 8 MiB bound; that diagnostic cost is documented, not covered by this reuse
budget. The remaining checked suite keeps its existing diagnostic settings.
No runtime allocation policy or Common implementation is changed. Fresh CLR wrappers
remain supported; no zero-allocation or cross-language performance equivalence is claimed.

All 93 pinned Buffer sources and three unchanged original test sources were
freshly compiled. The exact inherited shouldReuseChunks body runs for pooled and
adaptive factories in each Unsafe-enabled/NIO profile, with no abort/skip. Matching
supplemental MXBean measurements pass, and the original unpooled Disabled annotation
is verified without executing that excluded test.

Full net10.0 Debug/Release/rebuilt checked Release pass 8922/8922 without failures/skips;
51 existing Common warnings, zero Buffer/test warnings/errors. The common-fixture
method review now records 34 mapped, zero confirmed gaps and 469 unreviewed methods;
unreviewed does not mean unimplemented. File statuses are unchanged. Evidence:
artifacts/buffer-chunk-reuse-validation (ignored), buffer-chunk-reuse-*.trx.

## Allocator storage contracts

48 added CLR cases map the four remaining unreviewed declarations in
AbstractByteBufAllocatorTest: factory storage type, direct address availability,
maximum-capacity rejection and copying from the buffer's own internal memory.
All ten declarations now have scoped mappings and all eight original comments
are retained. The fixture remains in-progress: inherited ByteBufAllocatorTest
scenarios and provider-specific metric overrides are not thereby complete.

Unpooled, disabled-arena pooled fallback, enabled pooled and adaptive factories
run with both preferences. JVM safe/Unsafe type branches become managed heap and
owned native implementations. Persistent addresses become a nonzero usable pointer
inside Memory.Pin only; no address escapes its pin. Invalid 100000 -> 100001
capacity growth preserves endpoint bytes, indices, references, metrics and native
reservations. ReadOnlyMemory/Span replaces internal NIO input: the original
[4,8) -> [0,4) copy plus both six-byte overlap directions match an independent
snapshot model. Every case releases its buffer and final native reservations.
No runtime/Common change was needed.

All 93 pinned sources and two unchanged original test fixtures were freshly
compiled. On JDK 21 with Unsafe, eleven original method runs pass; with Unsafe
disabled, eight pass and the three actual address assumptions abort as specified
by the original. Each profile also passes an adaptive-type probe corresponding
to its original override and 24 supplemental copy windows. Those probes are not
claimed unchanged original method executions. C# runs all 48 cases without skips.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
pass 8970/8970 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. The three common base fixtures now have 38 mapped and 465
unreviewed declarations; these are scoped mappings, not a completion percentage.
Evidence: artifacts/buffer-allocator-contract-validation (ignored), buffer-allocator-contract-*.trx.

## Composite decomposition and removal ownership

44 added CLR cases port eight declared AbstractCompositeByteBufTest methods:
three original decomposition ranges, one/two retained-source counter cases and
three original removal shapes. Heap/native pooled/unpooled public factories cover
both shared and independent counters. File status remains in-progress; the common
fixture inventory now maps 46 declarations with 457 individually unreviewed.

The original 200/400/100 windows over a 1024-byte root are checked against a
flat deterministic byte model. Decompose returns borrowed slices without retaining;
mutations alias the original bytes and cursors remain independent. Both cleanup
orders are tested. Explicit retained claims can be released through decomposed
slices, which must follow the original component counter rather than its root.
With independent pooled counters, released parts become inaccessible even while
the root still has its own claim. Shared-counter parts live until the root's
final release. Native reservations are zero after allocator cleanup.

Removing the sole/final component or components 4..6 from ten components releases
only the transferred claims and rebuilds offsets/content. Supplemental nonzero
captured source indices remain intact. Composite reader/writer are intentionally
unchanged after removal, including indices beyond the reduced capacity; callers
must reset them before relative access. A zero-count tail removal is a no-op.
No runtime/Common change was needed.

All 93 pinned Buffer sources and two unchanged original fixtures were freshly
compiled. Each Unsafe-enabled/NIO profile passes eight exact original methods
under both original byte orders (16 runs with init/dispose), plus 44 supplemental
ownership/range/removal scenarios. CLR comparisons use concatenated borrowed
bytes instead of allocating an unreleased temporary wrapping container.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
pass 9014/9014 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Evidence: artifacts/buffer-composite-ownership-validation (ignored),
buffer-composite-ownership-*.trx.

## Composite shrink and discard lifetimes

Eight more declared AbstractCompositeByteBufTest methods are mapped. 24 new cases
cover borrowed/retained slice chains, shrink release and repeated discard over
heap/native pooled/unpooled factories. Two added pooled cases extend the existing
flattened-component retain/remove/re-add test. Three existing cases are reused,
so the focused selection contains 29 cases. No runtime/Common change was needed.
The common fixture inventory maps 54 declarations with 449 individually unreviewed;
this fixture remains in-progress with 20/85 declarations mapped.

Four nested borrowed slice Retain claims are transferred even when intermediate
slices have no readable bytes. Retained pooled children instead have independent
counters and immediate-parent claims. Shrinking a two-component composite releases
only the removed child; composite and final root release consume the remaining
claims in order. Native reservations reach zero after allocator cleanup. Original
pooled explanations, release comments and repeated-discard helper comments are
preserved, including the original inline counter labels.

The original skip/discard/read sequence verifies both discard variants through
component removal and subsequent lookup. CLR bounded binary search supplies the
Java last-access cache's observable behavior. A retained composite slice does not
retain discarded components: its existing CLR test rejects access with
ArgumentOutOfRangeException before advancing its reader, adapting Java's expected
IllegalStateException. Flattened component slices retain the original 01234 view
after removal and re-add with both pooled and unpooled factories.

All 93 pinned Buffer sources and two unchanged original test fixtures freshly
compile. Each Unsafe/NIO profile passes 16 exact original runs (eight methods,
both byte orders, init/dispose) plus 28 supplemental lifecycle scenarios.
Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
pass 9040/9040 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Evidence: artifacts/buffer-composite-discard-validation (ignored),
buffer-composite-discard-*.trx. No fixture completion or runtime improvement claim.

## Composite component lookup and limits

28 added CLR cases map eight more declared AbstractCompositeByteBufTest methods:
componentAtOffset, toComponentIndex, toByteIndex, isContiguous, findComponent and
three constructor-limit cases. Heap/native pooled/unpooled public factories cover
the original component data, cursor behavior and release paths. Original loop,
spot-check, factory and constructor-helper comments are preserved. The shared
fixture inventory maps 62 declarations with 441 individually unreviewed; this
fixture remains in-progress with 28/85 declarations mapped.

The original 5/7-byte components are visited at every offset and return borrowed
source duplicates with independent indices. The 5/7/7 layout maps every offset
against a flat byte model and the original starts 0/5/12. Repeated 1..9-byte
fragments separated by empties are truncated to four bytes for the contiguity
check, or ten bytes for three nested components exposing only a/b/c. Reading a,
skipping b and reading c preserves the original relative-lookup sequence and
releases all nested owners. Explicit AddComponent retains this fragmented layout
instead of depending on Unpooled's wrapping optimization.

Constructor limits 0/1/5 reproduce the original rejection/lower-bound/positive
cases. CLR uses ArgumentOutOfRangeException and its parameter name instead of
Java's IllegalArgumentException message. An additional three-component Java probe
finds that toByteIndex(numComponents) passes checkComponentIndex then dereferences
a null slot, throwing NullPointerException. CLR's existing range guard rejects
that missing component with ArgumentOutOfRangeException. Valid-index behavior
agrees; no runtime/Common change is needed. Native reservations reach zero after
allocator cleanup.

All 93 pinned Buffer sources and two unchanged test fixtures freshly compile.
Each Unsafe/NIO profile passes 16 original runs (eight methods, both byte orders,
init/dispose), plus 28 supplemental lookup/limit scenarios. Full net10.0
Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1 pass
9068/9068 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Evidence: artifacts/buffer-composite-lookup-validation (ignored),
buffer-composite-lookup-*.trx. Whole-file/inherited coverage remains partial.

## Composite consolidation and copy lifetimes

28 added CLR cases map eight further declared AbstractCompositeByteBufTest methods:
automatic/full/ranged consolidation, conversion to contiguous bytes, allocator
identity for both copy overloads, and empty copy/duplicate behavior. Original
hard-coded six-byte range comments are preserved. The shared fixture inventory
maps 70 declarations with 433 individually unreviewed; this fixture remains
in-progress with 36/85 declarations mapped.

Original 1/2/3-byte components consolidate automatically at limit two, all at once,
or with a fourth four-byte component through consolidate(1,2). Writer remains zero
throughout. Absolute bytes/capacity survive; only replaced claims are released,
with all remaining claims released at cleanup. Memory and bounded heap array
segments replace Java array access: unpooled heap keeps the original zero offset,
while pooled segments may begin inside a larger array. Native reservations reach
zero after allocator cleanup.

The original 3/1/2-byte layout yields six contiguous bytes despite writer=0.
Java's merged nioBuffer becomes an explicit owned Copy(0,6), then borrowed Memory;
the copy is released explicitly. Both Java representations match the same bytes
and do not alias sources. Source layout/cursors/claims remain unchanged. Both Copy
overloads preserve allocator identity, storage kind and readable content. Their
eight-byte source retains real 1/2/3/2-byte fragments; empty separators are omitted
so extra native variants have an all-native component set. Mutation and source-first
cleanup prove independent bytes/lifetime. Empty duplicates share the root count;
empty copies retain independent ownership after the source is released.

All 93 pinned Buffer sources and two unchanged original fixtures freshly compile.
Each Unsafe/NIO profile passes 16 exact original runs (eight methods, both byte
orders, init/dispose) plus 28 supplemental scenarios. Supplemental probes select
heap/direct factories explicitly: pinned AbstractByteBufAllocator's preferDirect
falls back to heap when direct buffers cannot be reliably freed. CLR native owners
do not need this JVM-dependent fallback. Original methods run unchanged.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
pass 9096/9096 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Evidence: artifacts/buffer-composite-copy-validation (ignored),
buffer-composite-copy-*.trx. No runtime/Common change or fixture completion claim.

## Composite transferred references and empty components

32 added CLR cases map eight further declared AbstractCompositeByteBufTest methods:
three reference-count paths, component duplicate shape, single/batched empty-source
release and two middle-empty insertion paths. Heap/native pooled/unpooled factories
cover each path. Original reference-count and insertion/skip-empty comments are
preserved. The shared fixture inventory maps 78 declarations with 425 individually
unreviewed; this fixture remains in-progress with 44/85 declarations mapped.

Original 1/2/3 component claims remain unchanged after array or List/IEnumerable
insertion and borrowed component access. Releasing the extra caller claims first
leaves exactly one claim per component for composite release. In the nested path,
releasing the outer composite deallocates the inner and leaves counts 0/1/2 for
final caller cleanup. The existing ComponentsKeepOriginalReferenceCountsAndReleaseOneOwnedReferenceEach
test retains the complementary non-nested composite-first release order.

A source with capacity=4,maxCapacity=6 and indices 1/3 returns a distinct duplicate
with the original limits and two readable bytes. CLR verifies this contract instead
of requiring Java's AbstractDerivedByteBuf hierarchy. Duplicate index changes leave
source/composite indices intact, and explicit Retain/Release uses the source count.

Single or batched owned empty-readable sources keep their own claim after transfer
despite contributing no bytes, then reach zero on composite release. Their backing
capacity is explicitly 16 rather than the original default; batch lengths remain
0/4/0. The immutable empty singleton instead remains a component slot between bytes
1/2, whether appended or inserted at index one. Relative reads and absolute copy
skip its zero byte range while internal component lookup preserves its identity.
All owned source claims reach zero; native reservations are zero after allocator
cleanup. No runtime/Common change was needed.

All 93 pinned Buffer sources and two unchanged original fixtures freshly compile.
Each Unsafe/NIO profile passes 16 original runs (eight methods, both byte orders,
init/dispose) plus 32 supplemental scenarios with explicit heap/direct factories.
Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
pass 9128/9128 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Evidence: artifacts/buffer-composite-reference-validation (ignored),
buffer-composite-reference-*.trx. Whole-file/inherited coverage remains partial.

## Nested composite flattening and borrowed segments

16 added CLR cases map four further declared AbstractCompositeByteBufTest methods:
mixed retained-slice flattening with/without WrappedCompositeByteBuf, nested layout
and full forward/reverse visitor traversal. Heap/native pooled/unpooled providers
cover every path. Original pooled, readable-range, excluded-component and release
comments are preserved. The shared fixture inventory maps 82 declarations with
421 individually unreviewed; this fixture remains in-progress with 48/85 mapped.

The original double-owned 1/2/3 prefix is followed by the [1,5) readable intersection
of a mixed s1/s2/s3/s4 chain. Both plain and wrapped paths yield five components
and ten bytes [1,2,3,1,2,3,2,1,2,1]. Retained components keep their own counters:
after flattening releases the input container, the excluded pooled s4 is deallocated,
while s1/s2/root retain the claims needed by the destination. Unpooled views instead
share three root claims, including the excluded view's observable counter. Final
destination release zeros all original owners. An alias mutation updates both
intersecting destination ranges while leaving the comparison copy unchanged.

CLR asserts the original helper's unchecked ByteBufUtil.equals result through byte
arrays and an independent literal model. Its temporary owned copy is explicitly
released. The original mixed-retained wrapper scenario is now mapped; this does
not complete all wrapper/provider/inherited coverage. No runtime/Common change
was needed. Native reservations reach zero after allocator cleanup.

Nested [1,2]/[3,4] components with writer=0 expose Slice(1,2) as two borrowed one-byte
ReadOnlySequence segments [2]/[3], adapting nioBuffers without Java ByteBuffer
objects. Mutation confirms aliasing, and outer release consumes the nested owner's
claim. Func<byte,bool> delegates replace anonymous ByteProcessor classes for the
original ascending [1..6] and descending [6..1] visitor arrays, return -1 on full
traversal and preserve nested reader/writer indices.

All 93 pinned Buffer sources and two unchanged original fixtures freshly compile.
Each Unsafe/NIO profile passes eight original runs (four methods, both byte orders,
init/dispose) plus 16 supplemental scenarios with explicit heap/direct factories.
Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
pass 9144/9144 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Evidence: artifacts/buffer-composite-flatten-validation (ignored),
buffer-composite-flatten-*.trx. Whole-file/inherited coverage remains partial.

## Composite discard marks and empty I/O

20 added cases in the existing CompositeByteBufDiscardTest fixture map four further
declared AbstractCompositeByteBufTest methods: discard/reset marker parity, grown
tail discard, zero-length read at end and 65 interleaved writes/reads from empty.
The focused run also retains its 24 earlier cases (44 total). Heap/native
pooled/unpooled factories cover each path; the integer-tail case has explicit BE/LE
variants. Original discard and issue 1976/1060 comments are preserved. The shared
fixture inventory maps 86 declarations with 417 individually unreviewed; this
fixture remains in-progress with 52/85 declarations mapped.

The original ten-byte data and offset-five component slices mark reader=6/current=5
and writer=9/current=10. Discard matches the contiguous reference's readable bytes
and markers, then ResetReaderIndex/ResetWriterIndex yield reader=1,writer=4 and
bytes [7,8,9]. Composite capacity shrinks to five and the consumed source is released;
the ordinary reference keeps ten-byte capacity. All remaining claims are released
at cleanup.

Issue 1976's eight integers plus an extra byte grow capacity from 32 to 64. After
all 33 bytes are read, DiscardSomeReadBytes releases the eight consumed components
but retains the 32-byte padding component with reader=writer=1. Explicit sixteen-byte
source backing replaces the default size without changing contributed lengths.
An independent byte model checks BE/LE content. A zero-length read after [1,2]/[3,4]
leaves reader/writer/capacity=4 and source ownership intact. Issue 1060's 65 alternating
one-byte writes/reads start at zero capacity, keep no readable bytes and end with
capacity=128/two padding components. Native reservations are zero after cleanup.
No runtime/Common change was needed.

All 93 pinned Buffer sources and two unchanged original fixtures freshly compile.
Each Unsafe/NIO profile passes eight original runs (four methods, both byte orders,
init/dispose) plus 20 supplemental scenarios with explicit heap/direct factories.
Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
pass 9164/9164 without failures/skips; 51 existing Common warnings, zero Buffer/test
warnings/errors. Evidence: artifacts/buffer-composite-discard-marks-validation
(ignored), buffer-composite-discard-marks-*.trx. Whole-file/inherited coverage remains partial.

## Partial discard extension point

An implementation difference was reproduced while reviewing ByteBuf/AbstractByteBuf.
Java discardSomeReadBytes compacts through setBytes directly; CLR instead called
the independent virtual DiscardReadBytes operation. A subclass overriding that
operation could unexpectedly throw or alter partial-discard behavior. CLR now uses
the original SetBytes hook and updates indices/marks only after a successful copy.
The original operation comment remains intact.

Fourteen heap/native cases cover reader positions 0/1/3/4/5/8 and throwing copy hooks.
An override that rejects full discard must never be invoked by partial discard.
Before the fix, six cases failed and eight passed; all 14 now pass, including data,
half-capacity threshold, all-consumed handling, marker reset and copy-failure state.
Fresh pinned Java source passes the same 14 cases in Unsafe/NIO profiles.

Public instance inventories now list ByteBuf=192 and AbstractByteBuf=149 declarations,
independently matched by reflection excluding compiler bridges/synthetic methods.
Only discardSomeReadBytes is decided in each source; the other 191/148 signatures
remain explicitly unreviewed. These are overlapping Java source declarations,
not 341 distinct CLR operations or a count of missing implementations.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
pass 9178/9178 without failures/skips; 51 existing Common warnings, no Buffer/test
warnings. Validation ran against isolated committed HEAD sources plus the Buffer
fix/tests: an unrelated working Common/Internal/ByteOrder.cs namespace edit causes
CS8956 in the primary working copy. That edit was preserved and excluded from the
porting commit. Evidence: artifacts/buffer-discard-dispatch-validation (ignored).

## Relative ByteBuf transfer extension points

ReadBytes(ByteBuf,int) and WriteBytes(ByteBuf,int) now call the indexed virtual
ReadBytes/WriteBytes overload, matching AbstractByteBuf. Previously they bypassed
those overrides and copied directly. The indexed operation owns local validation,
growth, copying and cursor updates; the relative overload commits the peer cursor
only after success. A throwing override can leave local progress while keeping the
peer cursor unchanged, as in Java. Whole-transfer overloads also return the receiver
even when the relative override returns a different buffer. Original comments remain.

All 44 new heap/native cases pass; before both fixes, 32 failed and 12 passed.
Coverage includes zero/exact-length transfers, growth, invalid ranges, failures
before/after copying, delegated arguments, return identity, marks and ownership.
Fresh pinned Java Unsafe/NIO each pass 44 probes: 42 match the CLR extension contract;
two negative-write cases retain CLR's existing early length/source-accessibility
guard, while Java reaches indexed validation. This is an explicit CLR policy.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
each pass 9222/9222 without failures/skips; 51 existing Common warnings and no
Buffer/test warnings. Validation uses isolated committed HEAD sources plus the exact
Buffer changes, preserving the unrelated working Common/ByteOrder.cs edit (CS8956).
Six more public declarations per source are reviewed: ByteBuf has 7 reviewed/185
unreviewed and AbstractByteBuf has 7/142. Source/fixture status remains in-progress.
Evidence: artifacts/buffer-relative-transfer-validation (ignored).

## Absolute ByteBuf transfer guards and returns

Whole GetBytes/SetBytes now return the receiver even when a nested length override
returns another buffer. SetBytes(index,source,length) also restores AbstractByteBuf's
destination range/accessibility check before the indexed virtual copy. Previously an
override could run for an invalid or released destination and consume the source.
Peer cursors still commit only after a successful copy; absolute transfers never grow.
Existing CLR null/source validation policies and original Java comments are preserved.

All 54 new heap/native cases pass; before the fixes, 14 failed and 40 passed. They
cover return identity, delegated arguments, invalid zero/negative/overflow ranges,
insufficient bytes, failures before/after copy, no-copy overrides, marks, ownership
and released destinations. Fresh pinned Java Unsafe/NIO each pass the same 54 cases.
Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
each pass 9276/9276 without failures/skips; 51 existing Common warnings and no
Buffer/test warnings. Validation uses isolated committed HEAD sources plus the exact
Buffer changes, preserving the unrelated working Common/ByteOrder.cs edit (CS8956).
Four more public declarations per source are reviewed: ByteBuf has 11 reviewed/181
unreviewed and AbstractByteBuf has 11/138. Source/fixture status remains in-progress.
Evidence: artifacts/buffer-absolute-transfer-validation (ignored).

## Zero-fill storage and facade dispatch

Inherited WriteZero now uses the bounded SetZeroCore storage kernel independently
of the public SetZero operation, matching AbstractByteBuf's low-level writes. A
SetZero override can no longer reject or alter an inherited sequential zero write.
The writer commits only after the kernel returns; growth may remain after failure.
WrappedByteBuf, WrappedCompositeByteBuf and UnreleasableByteBuf now explicitly
forward each zero-fill operation to its parent and preserve the outer receiver.
This also preserves parent WriteZero overrides and one advanced leak access.
Original comments remain; Span.Clear replaces Java's manual word loops.

All 65 new CLR cases pass; before the fixes, 48 failed and 17 passed. They cover
18 heap/native width/growth cases, two partial-write kernel failures, 44 operation/
exception cases through 11 heap/native/mixed-composite facade shapes and one native
budget failure. Data, markers, cursor commit, exception identity, one leak record,
reference ownership and zero native reservations after cleanup are checked.
Fresh pinned Java Unsafe/NIO each pass 64 probes: 62 matching extension contracts
and two empty-write differences. CLR deliberately keeps empty-write permission and
lifetime validation; Java exits before ensureWritable. The native budget failure
is CLR-specific. Derived-view/provider source reviews remain separate work.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
each pass 9341/9341 without failures/skips; 51 existing Common warnings and no
Buffer/test warnings. Validation uses isolated committed HEAD sources plus the exact
Buffer changes, preserving the unrelated working Common/ByteOrder.cs edit (CS8956).
Two more public declarations per source are reviewed: ByteBuf has 13 reviewed/179
unreviewed and AbstractByteBuf has 13/136. Source/fixture status remains in-progress.
Evidence: artifacts/buffer-zero-dispatch-validation (ignored).

## Allocation and copy write extension points

ReadBytes(int) now invokes WriteBytes(ByteBuf,index,length) on the newly allocated
buffer, then advances its source reader only after success. Previously it bypassed
that override with SetBytes and a direct writer assignment. Empty/invalid reads
still do not allocate, and the allocated result is returned even if its write
returns another buffer. UnpooledHeapByteBuf.Copy now uses the CLR span adapter for
the original array write and preserves that write override's return. Native Copy's
ByteBuffer-to-span path already matches. Whole Copy on transparent ordinary,
composite and unreleasable facades now calls the parent's original Copy() override.
Original comments remain; other indexed providers/derived factories remain separate.

All 134 new CLR cases pass; before the fixes, 54 failed and 80 passed. The 120
allocation cases exercise heap/native providers, opposing allocator preferences,
negative/zero/exact/capacity-bounded lengths, allocation failure, exceptions before/
after writing, no-write overrides, redirected results, provenance, independent data,
cursors and marks. Fourteen whole-copy facade cases verify factory dispatch and
exception identity without invoking an independent indexed-copy override.
Fresh pinned Java Unsafe/NIO each pass 134 probes: 110 matching contracts plus 24
explicit CLR cleanup differences. CLR retains its existing release of a fresh,
unpublished result after failed writing; Java leaves that buffer owned. No native
reservation remains after CLR case cleanup.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
each pass 9475/9475 without failures/skips; 51 existing Common warnings and no
Buffer/test warnings. Validation uses isolated committed HEAD sources plus the exact
Buffer changes, preserving the unrelated working Common/ByteOrder.cs edit (CS8956).
Two more public declarations per source are reviewed: ByteBuf has 15 reviewed/177
unreviewed and AbstractByteBuf has 15/134. Source/fixture status remains in-progress.
Evidence: artifacts/buffer-allocation-write-validation (ignored).

## Iteration loops and storage kernels

Readable ForEachByte/ForEachByteDesc now use private loops independently of indexed
virtual overrides, matching AbstractByteBuf. All four base overloads read the bounded
storage kernel rather than invoking a public GetByte override. An unrelated indexed
operation or getter can no longer intercept the readable scan or alter processor
input. Original operation comments remain. Func<byte,bool> replaces ByteProcessor;
unsigned CLR octets retain all eight bits of signed Java bytes.

The loops snapshot their start/end bounds and reacquire each byte before invoking
the processor. Callback index changes do not change the original scan range; byte
mutation and heap/native growth are visible on subsequent reads. No span survives
across a callback. CLR keeps its existing upfront null/range/lifetime checks and
rejects a next read after callback release. A callback that releases and stops
returns its stop index without reading again. Derived indexed delegation and
transparent facade forwarding are unchanged.

All 92 new CLR cases pass; before the fixes, 80 failed and 12 passed. The set covers
48 owner direction/range/callback cases, 24 readable derived cases, four validation
cases and 16 release cases. Duplicate/slice/nested/read-only/pooled views, high-bit
octets, stop/full/error scans, mutation, resize, indices/marks and ownership are
checked. Fresh pinned Java Unsafe/NIO each pass 80 matching portable scenarios.
Twelve extra CLR validation/continue-after-release cases are checked separately.
Native reservations return to zero after every case.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
each pass 9567/9567 without failures/skips; 51 existing Common warnings and no
Buffer/test warnings. Validation uses isolated committed HEAD sources plus the exact
Buffer changes, preserving the unrelated working Common/ByteOrder.cs edit (CS8956).
Four more public declarations per source are reviewed: ByteBuf has 19 reviewed/173
unreviewed and AbstractByteBuf has 19/130. Source/fixture status remains in-progress.
Evidence: artifacts/buffer-iteration-kernel-validation (ignored).

## Scalar composition and CLR bit representation

GetChar now calls GetShort directly and reinterprets the result with unchecked
UTF-16 conversion. An independent GetUnsignedShort override no longer intercepts it.
SetBoolean, SetChar, BE SetFloat and BE SetDouble now return their receiver after
the primitive setter succeeds. LE float/double helpers retain the nested primitive
return specified by ByteBuf; their different rule is tested explicitly.
Original operation comments remain unchanged.

Thirty-six scalar helpers are reviewed for primitive dispatch and value conversion.
Native ushort/uint retain the Java widened unsigned domains; signed mediums extend
bit 23. BitConverter preserves IEEE bits, including quiet NaN payloads and negative
zero. SetChar/WriteChar intentionally take C# char rather than Java int, requiring
explicit caller conversion to a UTF-16 code unit. Primitive overrides retain control
of their own validation/cursors; wrappers add no independent cursor movement.

All 864 new heap/native cases pass; before the fixes, 72 failed and 792 passed.
They combine 36 operations, six bit patterns and primitive success/failure, checking
exact hook/arguments, unsigned/sign extension, zero/all-ones/sign bits, NaN payloads,
negative zero, infinity, BE/LE returns, exceptions, memory, marks and ownership.
Fresh pinned Java Unsafe/NIO each pass the same 864 cases. Existing concrete and
derived storage tests remain the evidence for ordinary wire reads/writes.
Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
each pass 10431/10431 without failures/skips; 51 existing Common warnings, no
Buffer/test warnings and zero native reservations after case cleanup. Validation
uses isolated committed HEAD Common, preserving the working ByteOrder.cs edit (CS8956).

ByteBuf adds 36 decisions: 55 reviewed/137 unreviewed, including two CLR char adapters.
AbstractByteBuf directly declares 28 of these methods, now 47 reviewed/102 unreviewed;
eight LE float/double helpers are declared only on ByteBuf. Source/fixture statuses
remain in-progress. Evidence: artifacts/buffer-scalar-composition-validation (ignored).

## Native Copy read access and physical lifetime

UnpooledDirectByteBuf.Copy now borrows ReadOnlySpan rather than requesting writable
memory. This preserves the original duplicate-ByteBuffer read contract, including
subclasses that report IsReadOnly. Allocator.DirectBuffer(length, MaxCapacity),
capacity-based bounds, independent result storage and nested WriteBytes dispatch
remain intact, including empty copies and derived-view offsets.

A scoped existing read lease also keeps the captured native allocation physically
valid through virtual allocation/write callbacks. Resizing may detach the source;
final Release may reduce its logical reference count to zero. The old allocation
survives until copying exits, including allocation/write failures, without retaining
the source's logical reference count. This is CLR span lifetime handling, not a
claim that original Java permits reentrant release/resize safely. Java comments
remain unchanged. Common source is unchanged by this unit.

All 129 new cases pass; before the fix 39 failed and 90 passed. The 105 portable
cases combine seven native/read-only/duplicate/nested-slice forms, five ranges and
three allocation/write outcomes. Fresh pinned Java Unsafe/NIO each pass these
105 cases: 84 matching contracts and 21 explicit CLR failed-result cleanup cases
(Java leaves the allocated failed-write result owned; CLR releases it). Twenty-four
additional CLR cases verify allocation callbacks that resize/release source storage
and successful/failed allocation/write exits. They first assert a live pin before
mutating storage, so the red reproduction cannot deliberately dereference freed
memory. Independent bytes, marks, offsets, reference counts and separate native
reservation domains verify copy results and all cleanup paths.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
each pass 10560/10560 without failures/skips; 51 existing Common warnings, zero
Buffer/test warnings and zero native reservations after cleanup. Validation uses
isolated committed Common, preserving the working ByteOrder.cs edit (CS8956).
Source/fixture statuses are unchanged; this unit reviews the native Copy path.
Evidence: artifacts/buffer-native-copy-validation (ignored).

## Directional search storage and original fixtures

Segmented IndexOf now reads bounded storage independently of public GetByte
redefinitions, matching AbstractByteBuf/ByteBufUtil's low-level search path.
Previously a composite root/duplicate could throw an unrelated getter exception
only when its range crossed components. Contiguous Span.IndexOf/LastIndexOf and
segmented single-byte reads now share that rule. Span primitives replace JVM SWAR;
no JVM performance equivalence or arbitrary-subclass graph equivalence is claimed.
Existing directional clamping, capacity bounds, empty/released short circuits and
BytesBefore virtual delegation/distance rules are unchanged. Indexed BytesBefore
retains the explicit CLR range/lifetime guard before delegation, including empty,
negative and overflowing ranges; Java can short-circuit before such checks.

Ported all scenarios/comments from five declared AbstractByteBufTest methods:
testSWARIndexOf, testUnrolledSWARIndexOf, testIndexOf, testIndexOfReleaseBuffer and
testEndiannessIndexOf. They run on heap/native/mixed-composite storage (15 cases),
including zero initialization, forward/reverse matches, unrolled tails, clamping,
invalid endpoints, release capacity branches and explicit little-endian writes.
Another 72 cases model direction, high-bit/missing needles, three component sizes
and ordinary/optimized duplicate/slice/nested views; 18 cases verify BytesBefore's
IndexOf arguments/results/exceptions. All 105 pass; before, 24 failed and 81 passed.
Fresh pinned Java Unsafe/NIO each pass the same 105; five Java fixture bodies are
executed verbatim. Independent arrays verify positions/content; marks, references
and zero final native reservations are checked.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
each pass 10665/10665 without failures/skips; 51 existing Common warnings and zero
Buffer/test warnings. Isolated committed Common preserves the ByteOrder.cs edit
(CS8956). ByteBuf now has 59 reviewed/133 unreviewed public declarations;
AbstractByteBuf has 51/98. AbstractByteBufTest has 29 mapped/379 unreviewed declared
tests. Whole source/fixture statuses remain partial; other provider specializations
and test methods remain separate work. Evidence: artifacts/buffer-search-kernel-validation (ignored).

## Primitive write reservation and public growth hooks

WriteByte, BE/LE WriteShort/WriteMedium/WriteInt/WriteLong now reserve through a
private EnsureWritableCore corresponding to final AbstractByteBuf.ensureWritable0.
Previously an independent public EnsureWritable override intercepted all nine
writes, potentially throwing an unrelated exception even when capacity was enough.
Explicit EnsureWritable calls retain virtual dispatch. Bulk, zero, channel and text
operations retain their separately specified reservation paths.

The kernel retains MaxFastWritableBytes, allocator rounding and capacity-setter
hooks, bounds, access/read-only guards and original growth behavior. Subtraction
avoids Java target-capacity overflow even in checked CLR builds. The original
branch-optimization comment is retained with this CLR explanation. Integer value
truncation and explicit BE/LE encoding are unchanged. CLR commits the writer after
a successful storage write; Java WriteByte increments before its low-level write,
so arbitrary subclass storage-failure cursor equivalence is not claimed.

All 132 new heap/native cases pass; before, 108 failed and 24 passed. Nine operations
cover sufficient/growing/exhausted capacity and accepting/rejecting public overrides
(108 cases). Twelve cases verify explicit public reservation still invokes its
hook. Another 12 port six declared original tests: forced/unforced expansion,
forced/unforced read-only rejection, access after release and integer overflow.
Original helper comments are retained. Fresh pinned Java Unsafe/NIO each pass all
132, executing the six original fixture bodies and three helpers verbatim.
Independent expected wire arrays, original prefixes, cursor/marks and final native
reservations verify successful writes, failure state and cleanup. Existing pooled
hidden-capacity, allocator policy and native budget-failure tests also pass.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
each pass 10797/10797 without failures/skips; 51 existing Common warnings, zero
Buffer/test warnings. Isolated committed Common preserves the ByteOrder.cs edit
(CS8956). ByteBuf now has 70 reviewed/122 unreviewed public declarations;
AbstractByteBuf has 62/87. AbstractByteBufTest has 35 mapped/373 unreviewed declared
tests. Whole source/fixture/provider completion remains separate work.
Evidence: artifacts/buffer-writable-kernel-validation (ignored).

## View factory bounds and original lifetime fixtures

Ported 23 previously unreviewed declared AbstractByteBufTest methods covering the
Slice/RetainedSlice/Duplicate/RetainedDuplicate and readable-slice factory family.
All 115 new cases pass against the existing runtime across heap/native/mixed
composite/pooled heap/pooled native. No production code change was needed.

Scenarios include eight nested bounds combinations, two readable-slice bounds
cases, six factory calls after release, shared slice release, initial reader/writer
indices, duplicate-of-slice capacity, readable retained children before/after read,
and 4096-byte sequential slicing with 128-byte blocks and live alias mutation.
Original comments are retained. Additional assertions check unchanged failed-call
cursors/marks/reference counts, independent readable-byte patterns and zero native
reservations after root/pool cleanup. Managed arrays replace temporary expected
buffers; explicit BE/LE reads replace Java mutable byte-order facade comparisons.
Deterministic reseeded random streams are tested within each runtime; their actual
random octet sequences are not compared across Java/.NET.

Fresh byte-checked pinned Java Unsafe/NIO each pass 115 cases by executing all
23 original test bodies and five helpers verbatim. The runner tracks and releases
original temporary wrapped expected buffers after assertions. Java's pool uses its
default maxOrder=9 to satisfy original run-size assertions; CLR uses its supported
small test chunks (maxOrder=1). Pool geometry/performance is not compared.

Eight base factory declarations now have explicit public-API decisions, linked to
these cases and existing derived/pooled/leak-aware tests. Source/provider completion
is not inferred. ByteBuf has 78 reviewed/114 unreviewed public declarations;
AbstractByteBuf has 70/79. AbstractByteBufTest has 58 mapped/350 unreviewed declared
tests. Whole source/fixture statuses are unchanged.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
each pass 10912/10912 without failures/skips; 51 existing Common warnings and zero
Buffer/test warnings. Validation uses isolated committed runtime plus the exact new
test, preserving the unrelated ByteOrder.cs edit (CS8956).
Evidence: artifacts/buffer-view-factory-source-validation (ignored).

## Raw integer access and CLR octets

Reviewed 29 shared raw integer get/set/read declarations. Absolute operations use
capacity bounds and preserve cursors; relative readers require a readable width
and commit after success. Bounded spans and ReadWord/SetWord replace JVM raw access,
including BE/LE, low-bit truncation, 24-bit unsigned values and component crossings.
No executable runtime change was needed. Provider-specific overrides and JVM
performance remain separate review work.

Java signed byte and widened unsigned-byte APIs intentionally share GetByte/ReadByte
returning CLR byte (0..255). For Java's signed numeric domain use
unchecked((sbyte)value), including in checked builds. Both omitted original unsigned
API comments are now preserved beside these methods. No redundant unsigned-byte
aliases are added. Other existing unsigned/char adapters keep their own decisions.

Ported 69 declared original tests: 56 scalar get/set/read operations after release,
12 absolute getter boundaries and the exhausted-readable-byte scenario. Five
storage providers produce 345 new fixture cases. Ten additional provider cases
exercise nine raw integer operations against independent byte models with five bit
patterns at offset 15, crossing the composite split at 16, plus all 256 byte values.
They check full wire/guard bytes, signed numeric conversion, readable/capacity
bounds, indices/marks, failed-read state and zero final native reservations.
All 355 cases pass against the existing runtime. Fresh pinned Java Unsafe/NIO each
pass 355 cases, running all 69 original bodies and releasedBuffer helper verbatim;
unsigned byte results are explicitly normalized to the CLR representation.
Java uses default pooled maxOrder=9, CLR supported test maxOrder=1; pool geometry
and performance are not compared.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
each pass 11267/11267 without failures/skips; 51 existing Common warnings and zero
Buffer/test warnings. Validation uses isolated committed Common, preserving the
unrelated ByteOrder.cs edit (CS8956). ByteBuf now has 107 reviewed/85 unreviewed
public declarations; AbstractByteBuf has 99/50. Each has six CLR adapters.
AbstractByteBufTest has 127 mapped/281 unreviewed declared tests. Whole source,
fixture and provider statuses are unchanged.
Evidence: artifacts/buffer-scalar-source-validation (ignored).

## Index properties, marks and skip

Reviewed 19 base metadata/index/skip declarations. Ten property-accessor targets
record Java method pairs as actual public CLR get/set accessors. Single-index
assignment gives up Java's fluent return; SetIndex remains the paired/chainable
operation. CanRead/CanWrite preserve count-query semantics under distinct names
because CLR properties already occupy IsReadable/IsWritable. Byte counts are
metadata budgets, separate from read-only write permission. Existing nonvirtual
index properties centralize transparent-wrapper index ownership; arbitrary Java
cursor-rewriting overrides are not reproduced.

The public API checker now recognizes explicit property-accessor targets while
retaining all Java method inventories. Twelve focused parser cases distinguish
expression/block/auto properties, restricted accessors, static/private/nested
members, methods and comment/string lookalikes; eight actual ByteBuf metadata
properties also pass. The checker validates registered targets only.

Ported twelve original index-boundary/skip tests across five providers (60 cases).
Thirty additional cases check root/duplicate/slice/read-only/retained view marks:
Clear keeps marks; failed resets keep current indices; restore writer before reader
after Clear; metadata changes preserve bytes/reference claims and separate view
indices. Original skip comments are retained. All 90 pass unchanged CLR runtime;
fresh pinned Java Unsafe/NIO each pass the same cases, executing all twelve original
test bodies verbatim. Negative Java skip argument exceptions map to CLR argument
range exceptions. Existing size-query tests cover nonpositive/maximum counts,
read-only permissions and the empty sentinel. No production source change needed.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
each pass 11357/11357 without failures/skips; 51 existing Common warnings, zero
Buffer/test warnings and zero final native reservations. Isolated committed runtime
preserves the ByteOrder.cs edit (CS8956). ByteBuf now has 126 reviewed/66 unreviewed
public declarations; AbstractByteBuf has 118/31. Each has 18 CLR adapters.
AbstractByteBufTest has 139 mapped/269 unreviewed declared tests. Whole source,
fixture/provider statuses remain partial. Java pooled maxOrder=9 and CLR supported
test maxOrder=1 are not a pool geometry/performance comparison.
Evidence: artifacts/buffer-index-state-validation (ignored).

## Discard fixtures and marker relocation

Ported five original AbstractByteBufTest methods: both full-discard byte patterns,
all bytes consumed, and full/partial discard after release. Five heap/native/mixed
composite/pooled providers give 25 cases. Managed byte arrays replace original copied
comparison buffers and borrowed comparison slices. Original comments are retained,
including AbstractCompositeByteBufTest's relaxed writable-region comparison.

Another 80 cases independently model unread bytes, unchanged writable tails,
capacity and all marker relocation branches for full/partial discard. Composite
storage trims prefixes/components; contiguous buffers move only unread bytes.
Four heap/native cases verify the public SetBytes hook and unchanged indices/marks
when it throws before copying. All 109 pass the existing runtime; no production
change was required. Fresh pinned Java Unsafe/NIO each pass 109 cases, executing
all five original fixture bodies and releasedBuffer helper verbatim.

Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
each pass 11466/11466, no failures/skips; 51 existing Common warnings and zero
Buffer/test warnings. Native reservations return to zero. Validation uses committed
Common in an isolated workspace; the unrelated ByteOrder.cs edit (CS8956) is kept.
Java pool maxOrder=9 versus supported CLR test maxOrder=1 is not a pool geometry or
performance comparison. ByteBuf has 127 reviewed/65 unreviewed public declarations,
AbstractByteBuf 119/30, and AbstractByteBufTest 144 mapped/264 unreviewed methods.
Whole source/fixture statuses remain partial.
Evidence: artifacts/buffer-discard-source-validation (ignored).

## Capacity properties and original fast-space fixtures

Ported nine original AbstractByteBufTest methods to 37 provider cases: four bounded
capacity tests (16), ordinary/retained fixed-slice resize rejection (10), default
fast-space query (2), tracking written bytes (4), and saved-writer reset validation
with hello/world ASCII writes (5). Composite bounded-maximum cases follow the
original fixture assumption. The default fast-space test runs on unpooled owners;
the existing AbstractPooledByteBufTest already ports the original pooled override,
including allocation identity before/after crossing the fast-space boundary.
Original constants/order/comments are retained; Charset/CharSequence use
Encoding.ASCII and WriteString. Existing deeper resize/failure/view tests remain.

Reviewed three ByteBuf declarations as CLR properties: Capacity getter/setter and
virtual MaxFastWritableBytes getter. Fluent Java capacity setters become assignment.
The fast-space value is a storage budget, separate from read-only permission.
Native allocate/copy/publish retains the existing deliberate CLR failure atomicity;
Java unpooled shrink may trim indices before allocation. Duplicate capacity changes
continue to delegate without silently rewriting independent view cursors/marks.

All 37 pass unchanged runtime and fresh pinned Java Unsafe/NIO; nine original bodies
plus the fixed-slice helper execute verbatim. No production source change needed.
Full net10.0 Debug/Release/rebuilt checked Release with SIMPLE sampling interval 1
each pass 11503/11503, no failures/skips; 51 existing Common warnings, zero Buffer/test
warnings, final native reservations zero. Isolated committed Common preserves the
unrelated ByteOrder.cs edit (CS8956). Java maxOrder=9 versus supported CLR test
maxOrder=1 is not a pool geometry/performance comparison. ByteBuf now has 130
reviewed/62 unreviewed public declarations (21 CLR adapters); AbstractByteBuf stays
119/30 (18 CLR adapters). AbstractByteBufTest has 153 mapped/255 unreviewed methods.
Whole sources, fixtures and provider combinations remain partial.
Evidence: artifacts/buffer-capacity-source-validation (ignored).

## Remaining work

Source in-progress counts describe incomplete review, not a count of confirmed
missing implementations. Historical pending/unported notes above describe each
unit's scope at that time; they must not be read as a current missing-API list.
The manifest's publicApiReview begins the explicit availability partition.

ByteBufUtil's 53 declared public static signatures now have exact CLR target
declarations: 32 implemented portable operations and 21 CLR replacements, with
zero confirmed missing public operations or unreviewed public signatures in this
file. Groups link existing contract tests and language/ownership policies.
CharSequence/CharBuffer become spans or strings, Charset becomes Encoding,
copy=false uses whole-array Memory eligibility, and a bounded native storage cache
replaces Java recycler object reuse. These are implemented alternatives, not gaps.

This review checks API availability/representation. Existing tests validate their
stated scenarios; the full original ByteBufUtilTest, remaining internal/protected
members, fields, provider details and callers still require separate review.
ByteBufUtil remains in-progress. ByteBuf/AbstractByteBuf now have instance-method
inventories, with partial discard, ten relative/absolute ByteBuf transfers, two
zero-fill operations, allocated reads, whole Copy, four iteration operations and the
scalar composition, directional search, primitive reservation, view factory and raw
integer, index-state, full-discard and capacity-property families reviewed. ByteBuf
has 130 decided/62 unreviewed declarations (21 CLR adapters); AbstractByteBuf has
119/30 (18 CLR adapters). Other 32
in-progress source files have no signature inventory yet. None is assumed missing
or complete.

Run python tools/Check-BufferPublicApi.py to validate the pinned public signature
inventory and CLR declaration targets; this is not a behavioral equivalence test.
Run tools/Update-BufferPortingManifest.ps1 -Check separately for the original file
inventory and declared-test mappings. Next review targets ByteBuf/AbstractByteBuf,
CompositeByteBuf and allocator/utility consumers. Implement confirmed portable gaps
first; classify deliberate CLR alternatives explicitly, then port uncovered tests.
Common changes must cite the actual buffer contract that requires them.
