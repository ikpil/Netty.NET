# codec-smtp porting

Netty.NET ports `netty-codec-smtp` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `src/Netty.NET.Handler.Codec.Smtp`, with its original tests in
`test/Netty.NET.Handler.Codec.Smtp.Tests`. The [manifest](codec-smtp-porting-manifest.json) records every pinned file
of `codec-smtp/src` (21 files) with its C# counterpart, its CLR adaptations and evidence, or the reason it is not
ported. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module codec-smtp
```

The checker verifies the file inventory and source hashes, that every original comment (license, Javadoc and line
comments) is present in the mapped C# files, that every declared JUnit test is mapped to a C# test of the same name
(case may differ) or carries a recorded disposition, and the replacement and evidence of every unit that is not
ported. It fails while any entry is still `pending`.

| Status | Files |
| --- | --- |
| `clr-adapted` | 13 |
| `verified` (tests whose every original method is mapped) | 4 |
| `jvm-only-replaced` | 4 |

Of the 38 declared original tests, 37 are mapped to C# tests with the same names (40 xUnit cases, since
`SmtpCommandTest.mustRejectCRLFInCommandName` runs its 4 strings) and 1 belongs to the GraalVM metadata fixture that
is not ported. No test is CLR-inapplicable and no member is deprecated.

## Namespace and assembly (owner decisions, 2026-11-19)

The project, folder and assembly are `Netty.NET.Handler.Codec.Smtp`, the namespace of the module's only package; the
tests use `Netty.NET.Handler.Codec.Smtp.Tests`. The project references `Netty.NET.Handler.Codec` (codec-base), which
brings Transport, Resolver, Buffer and Common, and `InternalsVisibleTo` opens internals to the test assembly only.

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.handler.codec.smtp` | `Netty.NET.Handler.Codec.Smtp` | SMTP codec. (the summary links "SMTP" to RFC 2821) |
| test `io.netty.handler.codec.smtp` | `Netty.NET.Handler.Codec.Smtp.Tests` | (no package-info) |

The package-level `@UnstableApi` and the `@UnstableApi` of every public type are the Common `[UnstableApi]`
attribute, which is, like the Java annotation, visible in source only.

## Shared CLR adaptations

- **Interfaces.** `SmtpRequest`, `SmtpResponse`, `SmtpContent` and `LastSmtpContent` take the `I` prefix; their
  accessors stay methods (`Command()`, `Parameters()`, `Code()`, `Details()`). Interface re-declarations that only
  narrow a return type are inherited unchanged, the rule of the Transport and Common interfaces
  ([transport-porting.md](transport-porting.md), "Java type names and hierarchy"), so `ISmtpContent` is empty and
  `ILastSmtpContent` holds only `EMPTY_LAST_CONTENT`. `DefaultSmtpContent` and `DefaultLastSmtpContent` keep the
  narrowed return types with C# 9 covariant overrides of the Buffer port's `DefaultByteBufHolder`.
- **CharSequence.** `CharSequence` is `ICharSequence`, the Common port used by `AsciiString.Of(ICharSequence)` and the
  codec-base headers. A Java `String` passed or produced as a `CharSequence` is a `StringCharSequence`, whose equality
  and hash follow `String` (content equality only with another `StringCharSequence`, as `String.equals` only with a
  `String`). The tests therefore pass `new StringCharSequence("...")` where the Java tests pass string literals;
  `SmtpRequests.help(String)` keeps its `string` parameter.
- **Lists.** `List<CharSequence>` is `IList<ICharSequence>`. `Collections.emptyList()` is
  `ReadOnlyCollection<T>.Empty`, `unmodifiableList` a `ReadOnlyCollection<T>` view and `Arrays.asList` behind it
  `Array.AsReadOnly`. `ReadOnlyCollection<T>` keeps the `Object` members, so the `java.util.List` contract that
  `DefaultSmtpRequest`/`DefaultSmtpResponse` use in `equals`, `hashCode` and `toString` (element-wise equality,
  `31*h + e.hashCode()` from 1, `[a, b]`) is in `SmtpUtils.ListEquals`/`ListHashCode`/`ListToString`.
- **Encoder.** `IList<ICharSequence>` is always indexable, so the `RandomAccess` test of `writeParameters` keeps only
  its indexed branch. The Buffer port has `ByteBufUtil.WriteAscii(ByteBuf, ReadOnlySpan<char>)` and
  `(ByteBuf, AsciiString)` but no `ICharSequence` overload, so parameters are written from their `ToString()` text
  (the same ASCII bytes).
- **Exceptions and text.** `IllegalArgumentException` is `ArgumentException`, `IllegalStateException` is
  `InvalidOperationException` and `checkNotNull` throws `ArgumentNullException`
  ([codec-base-porting.md](codec-base-porting.md)); `CharsetUtil.US_ASCII` is `Encoding.ASCII`; numbers in text use
  the invariant culture.
- **Signed bytes.** `ByteBuf.ReadByte()` returns the unsigned `System.Byte` (owner decision). In
  `SmtpResponseDecoder` the separator is compared only with `' '` and `'-'`, so its sign changes nothing.
  `parseNumber` keeps Java's `(char) b`, which sign-extends a byte to `ﾀ..￿` (JLS 5.1.4), through an
  `sbyte` reinterpretation, and `Character.digit(ch, 10)` is `CharUnicodeInfo.GetDecimalDigitValue` (decimal digits,
  `-1` otherwise).

## Not ported

| Unit | Status | Reason |
| --- | --- | --- |
| `package-info.java` | `jvm-only-replaced` | C# namespaces cannot carry documentation comments; the summary is in the table above. |
| `module-info.yml` | `jvm-only-replaced` | JPMS module descriptor (requires `io.netty.common`, `io.netty.buffer`, `io.netty.transport`, `io.netty.codec`); `Netty.NET.Handler.Codec.Smtp.csproj` references codec-base and `InternalsVisibleTo` opens internals to the test assembly only. |
| `META-INF/native-image/.../reflect-config.json` | `jvm-only-replaced` | GraalVM reflection metadata; NativeAOT keeps handler metadata through the `DynamicallyAccessedMembers` annotations on `IChannelHandler`/`ChannelHandlerAdapter`. |
| `NativeImageHandlerMetadataTest` | `jvm-only-replaced` | Generates and compares that GraalVM JSON. |

## Test notes

- JUnit `Executable` instances are lambdas; `@ParameterizedTest @ValueSource(strings = ...)` is `[Theory]` with the
  same `[InlineData]` strings.
- `SmtpResponseDecoderTest` disables xUnit2013 so that `assertEquals(n, sequences.size())` keeps its source form, as
  codec-base's `CodecOutputListTest` does.
- The helper `newBuffer(CharSequence)` takes the `string` its callers pass, since `Unpooled.CopiedBuffer` has a
  `string` overload and no `ICharSequence` overload.
