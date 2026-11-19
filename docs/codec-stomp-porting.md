# codec-stomp porting

Netty.NET ports `netty-codec-stomp` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `src/Netty.NET.Handler.Codec.Stomp`, with its original tests in
`test/Netty.NET.Handler.Codec.Stomp.Tests`. The [manifest](codec-stomp-porting-manifest.json) records every pinned
file of `codec-stomp/src` (27 files) with its C# counterpart, its CLR adaptations and evidence, or the reason it is not
ported. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module codec-stomp
```

The checker verifies the file inventory and source hashes, that every original comment (license, Javadoc and line
comments) is present in the mapped C# files, that every declared JUnit test is mapped to a C# test of the same name
(case may differ) or carries a recorded disposition, and the facts and evidence of every unit that is not ported. It
fails while any entry is still `pending`.

| Status | Files |
| --- | --- |
| `clr-adapted` | 16 |
| `verified` (tests whose every original method is mapped) | 7 |
| `jvm-only-replaced` | 4 |

Of the 49 declared original tests, 48 are mapped to C# tests with the same names (72 xUnit cases with the
parameterized rows) and 1 belongs to the GraalVM metadata test, which is not ported. No test is CLR-inapplicable.

## Namespaces and assembly (owner decisions, 2026-11-19)

The project, folder and assembly are `Netty.NET.Handler.Codec.Stomp`, the namespace of the module's package, as for
every codec module (see [codec-base-porting.md](codec-base-porting.md)).

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.handler.codec.stomp` | `Netty.NET.Handler.Codec.Stomp` | `<a href="https://en.wikipedia.org/wiki/Streaming_Text_Oriented_Messaging_Protocol">STOMP</a> codec` |
| test `io.netty.handler.codec.stomp` | `Netty.NET.Handler.Codec.Stomp.Tests` | |

## Shared CLR adaptations

- **Headers.** `StompHeaders` is `IStompHeaders : IHeaders<ICharSequence, ICharSequence, IStompHeaders>` and
  `DefaultStompHeaders` extends the codec-base `DefaultHeaders<K, V, T>`. `CharSequence` is the Common
  `ICharSequence`, `Map.Entry`/`Iterator` are `IEntry`/`IIterator`, and the header constants are static readonly
  `AsciiString` fields of the interface ([codec-base-porting.md](codec-base-porting.md), Headers).
  - STOMP stores no `byte` header values, so the codec-base decision that a Java `byte` header value is `sbyte` is
    not exercised here.
  - Java passes `String`s as `CharSequence` names and values. C# `string` is not an `ICharSequence`, so the decoder
    and the tests use the Common `StringCharSequence`, whose `Equals` matches `String.equals`.
- **Covariant returns (owner decision 2026-11-19).** `IStompContentSubframe`, `ILastStompContentSubframe` and
  `IStompFrame` re-declare the `ByteBufHolder`/`ReferenceCounted` methods only to narrow the return type. These are
  inherited, not repeated, the rule of [transport-porting.md](transport-porting.md) (Decisions).
  - Callers that hold an `IStompFrame` and need the narrow result cast it, for example `DefaultStompFrameTest` with
    `(IStompFrame) sourceFrame.Copy()`.
  - `DefaultStompContentSubframe`/`DefaultLastStompContentSubframe` override the virtual `DefaultByteBufHolder`
    members with covariant returns
    ([override](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override)).
  - `DefaultStompFrame` implements the holder members on its content and returns `IStompFrame`, as the source does.
    Only the members that the shared `IByteBufHolder`/`IReferenceCounted` re-declare with their own return types are
    explicit implementations, as Transport's `DatagramPacket` does
    ([explicit interface implementation](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/interfaces/explicit-interface-implementation)).
    The nested `EmptyLastStompContentSubframe` does the same.
  - `content()`/`refCnt()` are the Buffer port's `Content`/`RefCnt` properties ([buffer-porting.md](buffer-porting.md)).
- **Interface constant.** `LastStompContentSubframe.EMPTY_LAST_CONTENT` is a static readonly field of
  `ILastStompContentSubframe`, and its anonymous class is the private nested class `EmptyLastStompContentSubframe`.
- **Name lookup.** Inside a class with a `DecoderResult()` method, `DecoderResult.SUCCESS` is written
  `global::Netty.NET.Handler.Codec.DecoderResult.SUCCESS`.
- **Enums.** `StompCommand` is a C# enum. `checkNotNull(command)` is gone because a C# enum cannot be null.
  `StompCommand.valueOf(String)` checks the exact name with `Enum.IsDefined` before `Enum.Parse`, which would also
  accept numbers and comma-separated names.
- **Encoder cache.** The `FastThreadLocal` and the access-order `LinkedHashMap` with `removeEldestEntry` are named
  nested classes over the Common types.
  - The Common `LinkedHashMap` has no `removeEldestEntry` hook, so the cache's `Put` inserts and then evicts the
    eldest entry as Java's `LinkedHashMap.put` does.
  - `ByteBufUtil.writeUtf8(ByteBuf, CharSequence)` has no `ICharSequence` overload, so a private `WriteUtf8` picks
    the `AsciiString` or the character overload.

## The decoder on ReplayingDecoder

`StompSubframeDecoder` is a `ReplayingDecoder<StompSubframeDecoder.State>`. Four adaptations keep its Java behaviour:

- **Signal.** `Signal` is an `Exception` in the Common port but a `java.lang.Error` in Java, which `catch (Exception)`
  does not catch. Both `catch (Exception)` blocks therefore filter `when (e is not Signal)`, so `REPLAY` reaches
  `ReplayingDecoder` ([codec-base-porting.md](codec-base-porting.md), Signals). Without the filter an incomplete
  frame would be reported as a decoding failure.
- **Writer index.** The C# `ReplayingDecoderByteBuf` view reads `WriterIndex` as `int.MaxValue` while not
  terminated, a recorded codec-base difference. The Java view returns the cumulation's writer index, so
  `in.writerIndex()` is `InternalBuffer().WriterIndex`, which the NUL search and the chunk length need.
- **readBytes (owner-revisit item, 2026-11-19).** Java's `ByteBufUtil.readBytes(alloc, in, n)` reads through
  `in.readBytes(ByteBuf)`. The Buffer port's `ByteBufUtil.ReadBytes` reads exactly `length` bytes through
  `ReadBytes(ByteBuf, int)`, a recorded Buffer decision ([buffer-porting.md](buffer-porting.md), Allocator-returning
  utilities). The view rejects that overload, as the Java view does. The decoder therefore calls the view's
  `ReadBytes(int)`, which copies the same bytes into a buffer of the cumulation's allocator after the replay check.
  This stays until the owner revisits it.
- **State.** The nested type `State` hides the inherited `ReplayingDecoder.State()` methods in C#, so it is declared
  `new` and the decoder calls `base.State()`.

## Byte values

`ByteBuf.getByte`/`readByte` return `System.Byte` (owner decision). The STOMP control bytes (CR, LF, NUL, colon) are
below 0x80. The places whose Java result depends on the sign read the byte as `(sbyte)`, with a `// CLR:` comment:

| Place | Java | C# |
| --- | --- | --- |
| `StompSubframeDecoder.Utf8LineParser.process` (INITIAL BRANCH) | `nextByte >= 0` selects an ASCII byte | `(sbyte) nextByte >= 0` |
| `StompSubframeDecoder.skipNullCharacter` | `"unexpected byte in buffer " + b + ...` prints the signed byte | `((sbyte) b)` |

The UTF-8 masks `& 0x3F`, `& 0xE0`, `& 0x1F` and `& 0x0F` give the same bits for the signed and the unsigned value,
so they keep the source code.

## Deprecated and not ported

| Unit | Status | Reason |
| --- | --- | --- |
| `StompSubframeDecoder.State` (member) | ported with `[Obsolete("this should never be used by an user!")]` | Its `@deprecated` text names no replacement, so the owner decision of 2026-11-16 ports it with `[Obsolete]`, as codec-base `MessageAggregator.IsHandlingOversizedMessage`. The decoder itself uses `State`. javac exempts uses within the same outermost class ([JLS 9.6.4.6](https://docs.oracle.com/javase/specs/jls/se17/html/jls-9.html#jls-9.6.4.6)), but C# reports every use ([CS0618](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0618)), so `StompSubframeDecoder.cs` disables CS0618 with that reason. |
| `package-info.java` | `jvm-only-replaced` | C# namespaces cannot carry documentation comments; the summary is in the table above. |
| `module-info.yml` | `jvm-only-replaced` | JPMS module descriptor; `Netty.NET.Handler.Codec.Stomp.csproj` references codec-base, and `InternalsVisibleTo` opens internals to the test assembly only. |
| `META-INF/native-image/.../reflect-config.json` | `jvm-only-replaced` | GraalVM reflection metadata; NativeAOT keeps handler metadata through the `DynamicallyAccessedMembers` annotations on `IChannelHandler`/`ChannelHandlerAdapter`. |
| `NativeImageHandlerMetadataTest` | `jvm-only-replaced` | Generates and compares that GraalVM JSON. |

## Test notes

- xUnit creates one test class instance per test, so `@BeforeEach`/`@AfterEach` are the constructor and `Dispose()`
  ([shared context](https://xunit.net/docs/shared-context)).
- `String.getBytes()` uses the default charset, `Encoding.Default`.
- `StompTestConstants` keeps its `String`s as static readonly fields, because a C# constant expression cannot
  concatenate the `char` operands. The Java octal escape `\1` is `\u0001`.
- The AssertJ chains of `StompSubframeEncoderTest` are `Assert.ThrowsAny<EncoderException>`,
  `Exception.GetBaseException()` for the root cause, and `Assert.Contains` on the thrown message.
- `@MethodSource` is `[MemberData]`, and `@ValueSource` rows are `[InlineData]`.
- `DefaultStompFrameTest` and `StompSubframeDecoderTest` suppress xUnit2013 with a reason, as codec-base does.
