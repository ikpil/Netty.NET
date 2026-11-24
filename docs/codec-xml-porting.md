# codec-xml porting

Netty.NET ports `netty-codec-xml` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `src/Netty.NET.Handler.Codec.Xml`, with its original tests in
`test/Netty.NET.Handler.Codec.Xml.Tests`. The [manifest](codec-xml-porting-manifest.json) records every pinned file of
`codec-xml/src` (29 files: 18 main and 3 test Java sources, `module-info.yml`, the GraalVM `reflect-config.json` and
six XML test resources) with its C# counterpart, its CLR adaptations and evidence, or the reason it is not ported.
Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module codec-xml
```

The checker verifies the file inventory and source hashes, that every original comment (license, Javadoc and line
comments) is present in the mapped C# files, that every declared JUnit test is mapped to a C# test of the same name
(case may differ) or carries a recorded disposition, and the facts and evidence of every unit that is not ported. A
`library-not-ported` entry (owner decision 2026-11-23, introduced with
[codec-compression](codec-compression-porting.md)) records the library (name, Maven coordinates, Java packages), and
the checker requires the unit to import one of those packages or to be linked by code to a recorded unit of the same
library that does. The six test resources must equal their C# copies byte for byte. The checker fails while any entry
is still `pending`.

| Status | Files |
| --- | --- |
| `clr-adapted` | 16 |
| `verified` (the test whose every original method is mapped, and the six test resources) | 7 |
| `jvm-only-replaced` | 4 |
| `library-not-ported` | 2 |

Of the 36 declared original tests, 32 are mapped to C# tests with the same names and pass, 3 belong to
`XmlDecoderTest` (`library-not-ported`) and 1 to the GraalVM metadata test, which is not ported. No test is
CLR-inapplicable. The four `jvm-only-replaced` and two `library-not-ported` entries carry 12 original comments.

## Owner decisions (2026-11-23)

- **Project.** The project, folder and assembly are `Netty.NET.Handler.Codec.Xml`, the namespace of the module's
  package, with the casing of the Java types `XmlDecoder` and `XmlFrameDecoder` (the rule of
  [codec-base-porting.md](codec-base-porting.md) for codec modules, owner decision 2026-11-19).
- **Library without a BCL counterpart.** Units that need a third-party Java library without a BCL counterpart on
  .NET 10 are not ported, the rule codec-compression applies to lz4-java, zstd-jni and the others. `XmlDecoder` needs
  aalto-xml (`com.fasterxml:aalto-xml`, package `com.fasterxml.aalto`): it feeds every received `ByteBuf` to an
  `AsyncXMLStreamReader` through its `AsyncByteArrayFeeder` and turns the StAX events into the `Xml*` messages.
  aalto-xml's non-blocking parser takes input through `getInputFeeder().feedInput(...)`, and
  `AsyncXMLStreamReader.EVENT_INCOMPLETE` "is used to denote "not yet available" (without more data)"
  ([aalto-xml](https://github.com/FasterXML/aalto-xml)). The BCL `System.Xml.XmlReader` provides "fast, noncached,
  forward-only access to XML data" and is created with `XmlReader.Create` over a `Stream`, `TextReader` or URI
  ([XmlReader](https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlreader?view=net-10.0)); it pulls from its
  source and has no byte feeder that reports an incomplete event. `XmlDecoder` and `XmlDecoderTest` are therefore
  `library-not-ported`. `XmlFrameDecoder` and the fifteen message types, which do not use aalto-xml, are ported.

## Namespaces and assembly

The package files sit at the project root. The project references `Netty.NET.Handler.Codec` (codec-base), and
`InternalsVisibleTo` opens internals to the test assembly only.

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.handler.codec.xml` | `Netty.NET.Handler.Codec.Xml` | XML codec provides asynchronous and non-blocking XML parser based on the [Aalto XML parser](https://github.com/FasterXML/aalto-xml). |
| test `io.netty.handler.codec.xml` | `Netty.NET.Handler.Codec.Xml.Tests` | |

The namespace holds `XmlFrameDecoder` and the message types `XmlAttribute`, `XmlCdata`, `XmlCharacters`,
`XmlComment`, `XmlContent`, `XmlDTD`, `XmlDocumentEnd`, `XmlDocumentStart`, `XmlElement`, `XmlElementEnd`,
`XmlElementStart`, `XmlEntityReference`, `XmlNamespace`, `XmlProcessingInstruction` and `XmlSpace`; the aalto-xml
parser that the package summary describes (`XmlDecoder`) is not ported (see above).

## Shared CLR adaptations

- **Handler signature.** `decode(ctx, in, out)` is `Decode(IChannelHandlerContext ctx, ByteBuf @in, IList<object>
  @out)` without a throws clause, and `readerIndex()`/`writerIndex()`/`readableBytes()` are the Buffer port's
  properties ([codec-base-porting.md](codec-base-porting.md)). `checkPositive` is the Common `ObjectUtil.CheckPositive`
  (`ArgumentException`). The numbers in the `TooLongFrameException` messages use the invariant culture, because
  `Int64.ToString()` uses "the NumberFormatInfo object of the current culture"
  ([Int64.ToString](https://learn.microsoft.com/en-us/dotnet/api/system.int64.tostring?view=net-10.0)).
- **Control flow in `XmlFrameDecoder.decode`.**
  - The labeled `break scan` is `goto scan`, with the label on the first statement after the scan loop: a labeled
    `break` exists only "Starting in C# 15", and "The `goto` statement transfers control to a statement that a label
    marks" ([jump statements](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/jump-statements);
    the mapping of [codec-compression-porting.md](codec-compression-porting.md)). The plain `break`s inside the
    `switch` leave only the `switch`, as in Java.
  - `throw new Error()` in the `default` branch is `UnreachableException`, the `java.lang.Error` mapping of
    [codec-memcache-porting.md](codec-memcache-porting.md).
  - The private enum `BangMarkupKind` is a simple C# enum.
  - `CDATA_START_SUFFIX = {'C', 'D', 'A', 'T', 'A', '['}` casts each `char` with `(byte)`. Java narrows a constant
    "if the variable is of type byte, short, or char, and the value of the constant expression is representable"
    ([JLS 5.2](https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.2)), while the C# implicit constant
    conversion to `byte` covers only `int` constants and `char` converts to `byte` only explicitly
    ([conversions](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/conversions)).
- **Virtual accessors.** The public accessors of the non-final message classes are `virtual`: "A method may be
  declared final (§8.4.3.3), in which case it cannot be hidden or overridden"
  ([JLS 8](https://docs.oracle.com/javase/specs/jls/se17/html/jls-8.html)), while in C# "By default, methods are
  non-virtual" ([virtual](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/virtual)).
  `XmlDocumentEnd` is final, so it is `sealed`.
- **equals, hashCode and toString.** These are the overrides `Equals(object)`, `GetHashCode()` and `ToString()`.
  - Java string conversion writes a null reference as "null" and a boolean as "true"/"false"
    ([JLS 5.1.11](https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.11)). In C# "the string
    representation of null is an empty string"
    ([addition operator](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/addition-operator))
    and `Boolean.ToString()` returns "True" or "False"
    ([Boolean.ToString](https://learn.microsoft.com/en-us/dotnet/api/system.boolean.tostring?view=net-10.0)), so the
    `toString()` methods write `(field ?? "null")` and `XmlDocumentStart` writes `(standalone ? "true" : "false")`.
  - `String.hashCode()` is `string.GetHashCode()`. Java defines the value as `s[0]*31^(n-1) + ... + s[n-1]`
    ([String](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html)), while for .NET "The
    hash code itself is not guaranteed to be stable"
    ([String.GetHashCode](https://learn.microsoft.com/en-us/dotnet/api/system.string.gethashcode?view=net-10.0)). The
    hash values differ from the JVM's, and equal objects still hash equally, as in the codec-dns `AbstractDnsRecord`
    port.
  - `XmlElement.namespaces` and `XmlElementStart.attributes` are mutable `List<T>` fields returned as `IList<T>` (the
    `List<Object> out` mapping of codec-base), so a decoder can still add to them. Their Java `List` equality and hash
    ([List](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/List.html)) and
    `AbstractCollection.toString()` text
    ([AbstractCollection](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/AbstractCollection.html))
    are not the `List<T>` members, so `XmlElement` has the `private protected` static helpers `ListEquals`
    (`Enumerable.SequenceEqual` with the default equality comparer,
    [SequenceEqual](https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.sequenceequal?view=net-10.0)),
    `ListHashCode` and `ListToString`. `private protected` keeps them visible to the subclasses in this assembly only
    ([private protected](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/private-protected)).
  - `XmlElementEnd.toString()` keeps the source prefix `"XmlElementStart{"`.
- **Keyword names.** The fields `namespace` of `XmlAttribute` and `XmlElement` are `@namespace`, because for C#
  keywords "You can't use them as identifiers in your program unless you prefix them with `@`"
  ([keywords](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/)). `in` and `out` are
  `@in`/`@out` as in codec-base. The methods keep their Java names (`Type()`, `Namespace()`, `Uri()`, `Encoding()`):
  no nested type of the same name exists, so they compile without a suffix.

## Byte values

`ByteBuf.getByte` returns `System.Byte` (owner decision), while a Java `byte` widens to `int` with sign extension ("A
widening conversion of a signed integer value to an integral type T simply sign-extends",
[JLS 5.1.2](https://docs.oracle.com/javase/specs/jls/se17/html/jls-5.html#jls-5.1.2)): bytes 0x80-0xFF are negative
in Java and 128-255 in C#. No place in `XmlFrameDecoder` depends on the sign, so the module has no `(sbyte)` cast:

| Place | Java | C# |
| --- | --- | --- |
| `Character.isWhitespace(readByte)` | the `int` overload; 0x80-0xFF are negative and not white space | private `IsWhitespace(byte)`: U+0009-U+000D and U+001C-U+0020, the Java rule below 0x80 ([Character.isWhitespace(int)](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Character.html)), as codec-base `JsonObjectDecoder` writes it |
| comparisons with `'<'`, `'>'`, `'/'`, `'!'`, `'?'`, `'-'`, `'['`, `']'` | a negative byte never equals them | 128-255 never equal them |
| `isValidStartCharForXmlElement` (`a`-`z`, `A`-`Z`, `:`, `_`) | negative bytes are below every range | 128-255 are above `'z'` (0x7A) |
| `CDATA_START_SUFFIX` comparison | same bits | same bits |

## Not ported

| Unit | Status | Reason |
| --- | --- | --- |
| `XmlDecoder` | `library-not-ported` | Needs aalto-xml (`com.fasterxml:aalto-xml:1.3.4`, `com.fasterxml.aalto`); owner decision 2026-11-23, see above. |
| `XmlDecoderTest` (3 tests) | `library-not-ported` | Tests of `XmlDecoder`; linked to it as its library family. |
| `package-info.java` | `jvm-only-replaced` | C# namespaces cannot carry documentation comments; the summary is in the table above. |
| `module-info.yml` | `jvm-only-replaced` | JPMS module descriptor (requires io.netty.common, io.netty.buffer, io.netty.transport, io.netty.codec and com.fasterxml.aalto); `Netty.NET.Handler.Codec.Xml.csproj` references codec-base, and `InternalsVisibleTo` opens internals to the test assembly only. aalto-xml has no counterpart. |
| `META-INF/native-image/.../reflect-config.json` | `jvm-only-replaced` | GraalVM reflection metadata; NativeAOT keeps handler metadata through the `DynamicallyAccessedMembers` annotations on `IChannelHandler`/`ChannelHandlerAdapter`. |
| `NativeImageHandlerMetadataTest` | `jvm-only-replaced` | Generates and compares that GraalVM JSON. |

## Test notes

- "xUnit.net creates a new instance of the test class for every test that is run"
  ([shared context](https://xunit.net/docs/shared-context)), so the constructor that loads the six samples runs for
  every test.
- **Test resources.** `sample-01.xml` to `sample-06.xml` are copied byte for byte to
  `test/Netty.NET.Handler.Codec.Xml.Tests/Resources/` and embedded with the logical names
  `io/netty/handler/codec/xml/sample-0N.xml`, the class-path paths the Java test loads.
  - `sample(number)` reads them with `typeof(XmlFrameDecoderTest).Assembly.GetManifestResourceStream(path)` where
    Java uses `getClass().getClassLoader().getResource(path)`. That method returns "The manifest resource; or null if
    no resources were specified during compilation or if the resource is not visible to the caller"
    ([GetManifestResourceStream](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.assembly.getmanifestresourcestream?view=net-10.0)),
    so a missing resource still throws `ArgumentException("file not found: ...")`.
  - `Files.readAllBytes` is `Stream.CopyTo` a `MemoryStream`
    ([CopyTo](https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.copyto?view=net-10.0)).
  - The Java test decodes the bytes with `StandardCharsets.UTF_8.decode`, which "always replaces malformed-input and
    unmappable-character sequences"
    ([Charset](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/charset/Charset.html)), and the
    C# test with `Encoding.UTF8.GetString`, whose encoding "uses replacement fallback"
    ([Encoding.UTF8](https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.utf8?view=net-10.0)). The
    samples are valid UTF-8 without a byte order mark.
- `Arrays.asList` and `Collections.singletonList` are the Common `Collectives.AsList` and `Collectives.SingletonList`
  (as codec-base `EmptyHeadersTest`), so the `List<String>` parameter of `testDecodeWithXml` is
  `IReadOnlyList<string>`. `Foo.class` is `typeof(Foo)` and `cause.getClass()` is `cause.GetType()`.
- The `assertThrows` calls expect the exact types the decoder throws (`ArgumentException` from `CheckPositive`,
  `TooLongFrameException`, `CorruptedFrameException`), so they stay `Assert.Throws`.
- `testDecodeDoesNotHangOnTrickledUnbalancedElement`: `assertTimeoutPreemptively` runs the executable "in a different
  thread than that of the calling code"
  ([Assertions](https://docs.junit.org/5.10.0/api/org.junit.jupiter.api/org/junit/jupiter/api/Assertions.html)). The
  async C# test runs it in `Task.Run` and awaits `WaitAsync(TimeSpan.FromSeconds(10), ...)`, whose task "should be
  faulted with a TimeoutException if it hasn't otherwise completed"
  ([WaitAsync](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.waitasync?view=net-10.0)), as
  codec-compression's `Bzip2DecoderTest` does. The 200,000 one-byte writes finish in well under a second on this host.
- `mustDecodeRemainingDataAfterCumulationCompaction`: `frame.substring(0, 59)` is `frame.Substring(0, 59)`. Java takes
  an end index ([String](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html)) and C# a
  length ([Substring](https://learn.microsoft.com/en-us/dotnet/api/system.string.substring?view=net-10.0)), which are
  equal for begin index 0.
