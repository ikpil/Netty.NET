# codec-base porting

Netty.NET ports `netty-codec-base` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `src/Netty.NET.Handler.Codec`, with its original tests in
`test/Netty.NET.Handler.Codec.Tests`. The [manifest](codec-base-porting-manifest.json) records every pinned file of
`codec-base/src` (106 files) with its C# counterpart, its CLR adaptations and evidence, or the reason it is not ported.
Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module codec-base
```

The checker verifies the file inventory and source hashes, that every original comment (license, Javadoc and line
comments) is present in the mapped C# files, that every declared JUnit test is mapped to a C# test of the same name
(case may differ) or carries a recorded disposition, and the deprecation facts, replacement and evidence of every
unit that is not ported. It fails while any entry is still `pending`.

| Status | Files |
| --- | --- |
| `clr-adapted` | 53 |
| `verified` (tests whose every original method is mapped) | 27 |
| `deprecated-not-ported` | 17 |
| `jvm-only-replaced` | 9 |

Of the 297 declared original tests, 293 are mapped to C# tests with the same names, 1 is CLR-inapplicable
(`Base64Test.testEncodeDecodeLE`, see below) and 3 belong to fixtures that are not ported (serialization and the
GraalVM metadata test).

## Namespaces and assembly (owner decisions, 2026-11-18 and 2026-11-19)

The project, folder and assembly are `Netty.NET.Handler.Codec`, the namespace root of the module (owner decision
2026-11-19). The first name, `Netty.NET.Codec.Base`, came from the Maven artifact `netty-codec-base`, whose JPMS
module name is `io.netty.codec`. Every other Netty.NET project uses its namespace root as its project name, and the
.NET guideline [Names of Assemblies and DLLs](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/names-of-assemblies-and-dlls)
suggests to "name the DLL based on the common prefix of the namespaces contained in the assembly". The common
prefix alone would give `codec-http` (packages `http`, `rtsp` and `spdy`) the same name, so each later module takes
the namespace of its main package: `codec-http` becomes `Netty.NET.Handler.Codec.Http` and `handler` becomes
`Netty.NET.Handler`. The tests use `Netty.NET.Handler.Codec.Tests`, like the other test projects.

Namespaces follow the Java package path:

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.handler.codec` | `Netty.NET.Handler.Codec` | Extensible decoder and its common implementations which deal with the packet fragmentation and reassembly issue found in a stream-based transport such as TCP/IP. |
| `io.netty.handler.codec.base64` | `Netty.NET.Handler.Codec.Base64Codec` | Encoder and decoder which transform a Base64-encoded String or ByteBuf into a decoded ByteBuf and vice versa. |
| `io.netty.handler.codec.bytes` | `Netty.NET.Handler.Codec.Bytes` | Encoder and decoder which transform an array of bytes into a ByteBuf and vice versa. |
| `io.netty.handler.codec.json` | `Netty.NET.Handler.Codec.Json` | JSON specific codecs. |
| `io.netty.handler.codec.string` | `Netty.NET.Handler.Codec.String` | Encoder and decoder which transform a String into a ByteBuf and vice versa. |
| `io.netty.handler.codec.serialization` | not ported (deprecated) | Encoder, decoder and their compatibility stream implementations which transform a Serializable object into a byte buffer and vice versa. |
| test `io.netty.handler.codec` and subpackages | `Netty.NET.Handler.Codec.Tests` (`.Base64Codec`, `.Bytes`, `.Frame`, `.Json`, `.String`) | Test classes for frame based decoders (`frame`). |

- Following the Java path means the `handler` and `codec-http` modules continue as `Netty.NET.Handler.*` and
  `Netty.NET.Handler.Codec.Http`.
- Only the base64 namespace is renamed (`Base64Codec`) because the package declares a class `Base64`. The .NET
  guideline says "DO NOT use the same name for a namespace and a type in that namespace" (several compilers then
  require full qualification), and `Base64.Encode` written under `Netty.NET.Handler.Codec.Http` would otherwise bind
  to the namespace ([Names of Namespaces](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/names-of-namespaces)).
  The classes keep their Java names.
- Code under `Netty.NET.Handler.Codec.String` uses the `string` keyword, never `String.` member access.

## Shared CLR adaptations

- **Type matching.** `TypeParameterMatcher.find/get` are the reified type argument and `Type.IsInstanceOfType` (false
  for null), as in `SimpleChannelInboundHandler`. A `Class<? extends I>` constructor argument is a `Type` checked
  against `I` at run time.
- **Outbound operations.** `write(ctx, msg, promise)` is `Task WriteAsync(ctx, msg, cancellationToken)`; the returned
  Task replaces the promise. `MessageToMessageEncoder` writes several messages with `Task.WhenAll` in place of
  `PromiseCombiner` and has no void-promise path. As in the source, messages already encoded are written even when
  `encode` then fails.
- **Lists.** `List<Object> out` is `IList<object> @out`; `CodecOutputList` implements `IList<object>` over the
  recycled array.
- **Exceptions.** `RuntimeException` is `Exception`, `IllegalStateException` is `InvalidOperationException`,
  `IllegalArgumentException` is `ArgumentException`, `UnsupportedOperationException` is `NotSupportedException`, and
  a source `IndexOutOfBoundsException` is `ArgumentOutOfRangeException` with the source text as message. A cause-only
  constructor keeps the Java detail message `cause.toString()` (class name, `": "`, message), because .NET
  `Exception.ToString()` adds the stack trace. The text comes from Common's CLR-only
  `ThrowableUtil.ThrowableToString(Exception)` (owner decision 2026-11-24), which replaced the internal
  `CodecException.ThrowableToString` and the copies in codec-http and handler.
- **Signals.** `Signal` (Netty.NET.Common) is an `Exception`, while the Java `Signal` is an `Error` that
  `catch (Exception)` does not catch. `ReplayingDecoder`, `ByteToMessageDecoder` and `MessageToMessageDecoder`
  therefore filter `when (e is not Signal)` where the source catches `Exception`.
- **Anonymous classes.** These become named private nested classes, or delegates where the Java type is a
  functional interface.
- **Package access.** Java `protected` also admits the package. Members that other codec-base classes reach that way
  are `protected internal` or have an internal forwarder: `MessageToMessageDecoder.InvokeDecode` and
  `MessageToMessageEncoder.InvokeEncode` are used by the datagram codecs. The forwarder keeps `protected` overrides
  valid in friend assemblies (CS0507).
- **Header values.** `CharSequenceValueConverter` renders `double`/`float` with `Netty.NET.Common.Internal.JdkDecimal`
  (`Double.toString`/`Float.toString` text), made public for this use.

## Area notes

- **Headers** (`IHeaders`, `DefaultHeaders`, `EmptyHeaders`, `HeadersUtils`, `DateFormatter`):
  - `Map.Entry` is `IEntry<K, V>` with `SetValue`, and `Iterator` is `IIterator<E> : IEnumerator<E>` with `Remove`.
    The tests and the HTTP codecs mutate through both, and `KeyValuePair`/`IEnumerator` cannot do that.
  - A Java `byte` header value is `sbyte`, since header bytes are the decimal text -128..127. This differs from
    `ByteBuf.getByte`, which the owner mapped to `System.Byte`.
  - `java.util.Date` is `DateTimeOffset` in UTC. `DateFormatter` keeps the source token parser and replaces the
    lenient `GregorianCalendar` with explicit UTC arithmetic (31 Feb becomes 3 Mar).
  - `thisT()` is Java's unchecked `(T) this`, which is not checked at run time
    ([JLS 5.5](https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.5)), so the exact
    `DefaultHeaders<K, V, T>` that `copy()` creates fails only where a caller uses a this-returning result as a `T`.
    A C# cast would throw `InvalidCastException` at once, so `ThisT()` is `this is T t ? t : default` (2026-11-24): an
    ignored result works and a used one fails (`NullReferenceException` in place of `ClassCastException`). Measured:
    `Add` on a copy of a `DefaultHeadersImpl` adds the entry and returns `null`; a chained `Add` throws.
- **ReplayingDecoder.** C# `ByteBuf` index members are not virtual, so `ReplayingDecoderByteBuf` mirrors the reader
  index of the cumulation. A reader index set through the view is written back at the next checkpoint or decode
  boundary. The remaining differences are listed in the manifest entry.
- **Byte order.** `LengthFieldBasedFrameDecoder`/`LengthFieldPrepender` keep their `ByteOrder` parameter and choose
  the big-endian or `*LE` accessors, following the Buffer port's replacement of swapped buffers
  ([buffer-porting.md](buffer-porting.md)).
  - `Base64` has only its big-endian branches, because `ByteBuf.order()` is deprecated and not ported.
  - `Base64Test.testEncodeDecodeLE` is CLR-inapplicable: its only difference from `testEncodeDecodeBE` is a
    little-endian buffer built with that deprecated method.
- **Text.** `Charset` is `System.Text.Encoding`, and the `CharSequence` message type of `StringEncoder`/`LineEncoder`
  is `IEnumerable<char>`, so both `string` and `ICharSequence` values match. `JsonObjectDecoder` keeps the Java
  `Character.isWhitespace(byte)` rule.
- **MessageAggregator.**
  - Future listeners are Task continuations that run synchronously.
  - The deprecated `isHandlingOversizedMessage()` names no replacement, so it is ported with `[Obsolete]`.

## Not ported

| Unit | Status | Reason |
| --- | --- | --- |
| `serialization/*` (14 sources, `package-info.java`) and its 2 tests | `deprecated-not-ported` | The package is "deprecated with no replacement, because serialization can be a security liability". It implements the Java object serialization protocol, which has no CLR counterpart; `BinaryFormatter` is no longer implemented since .NET 9 ([migration guide](https://learn.microsoft.com/en-us/dotnet/standard/serialization/binaryformatter-migration-guide/)). Package-private helpers record the deprecated type they serve (`ReferenceMap` via `WeakReferenceMap`). |
| 5 main and 1 test `package-info.java` | `jvm-only-replaced` | C# namespaces cannot carry documentation comments; the summaries are in the table above. |
| `module-info.yml` | `jvm-only-replaced` | JPMS module descriptor; `Netty.NET.Handler.Codec.csproj` references Transport, and `InternalsVisibleTo` opens internals to the test assembly only. |
| `META-INF/native-image/.../reflect-config.json` | `jvm-only-replaced` | GraalVM reflection metadata; NativeAOT keeps handler metadata through the `DynamicallyAccessedMembers` annotations on `IChannelHandler`/`ChannelHandlerAdapter`. |
| `NativeImageHandlerMetadataTest` | `jvm-only-replaced` | Generates and compares that GraalVM JSON. |

## Testsuite integration

With codec-base ported, the Transport testsuite inventory treats codec-base as a ported module
([transport-porting.md](transport-porting.md#testsuite-inventory-2026-11-18)).
- `SocketFixedLengthEchoTest` and `SocketStringEchoTest` are ported.
- `SocketObjectEchoTest` is `deprecated-not-ported` with the serialization package.
- `SocketChannelNotYetConnectedTest` uses the real `ByteToMessageDecoder` instead of its stand-in.

## How the port was done

The core was ported first: the exception family, `DecoderResult`, `CodecOutputList`, `ByteToMessageDecoder`, the
encoder/codec bases, `FixedLengthFrameDecoder` and `AdaptiveCumulator`, with their tests. Three subagents then ported
the Headers family, the frame decoders with `ReplayingDecoder`, and the subpackages with `MessageAggregator` and the
datagram codecs, each in its own git worktree. Their files were reviewed and integrated, and core changes they
needed (the Signal filters, the internal forwarders, the public `JdkDecimal`) were made in the core.
