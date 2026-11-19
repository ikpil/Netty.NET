# codec-dns porting

Netty.NET ports `netty-codec-dns` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `src/Netty.NET.Handler.Codec.Dns`, with its original tests in
`test/Netty.NET.Handler.Codec.Dns.Tests`. The [manifest](codec-dns-porting-manifest.json) records every pinned file of
`codec-dns/src` (53 files) with its C# counterpart, its CLR adaptations and evidence, or the reason it is not ported.
Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module codec-dns
```

The checker verifies the file inventory and source hashes, that every original comment (license, Javadoc and line
comments) is present in the mapped C# files or carries a recorded disposition, that every declared JUnit test is
mapped to a C# test of the same name (case may differ) or carries a recorded disposition, and the replacement and
evidence of every unit that is not ported. It fails while any entry is still `pending`.

| Status | Files |
| --- | --- |
| `clr-adapted` | 40 |
| `verified` (tests whose every original method is mapped) | 9 |
| `jvm-only-replaced` | 4 |

Of the 67 declared original tests, 66 are mapped to C# tests with the same names (80 xUnit cases, since the two
parameterized `DnsQueryTest` methods run 5 and 11 rows) and 1 belongs to the GraalVM metadata fixture that is not
ported. No test is CLR-inapplicable.

## Namespace and assembly (owner decisions, 2026-11-19)

The project, folder and assembly are `Netty.NET.Handler.Codec.Dns`, the namespace of the module's only package; the
tests use `Netty.NET.Handler.Codec.Dns.Tests`. The project references `Netty.NET.Handler.Codec` (codec-base), which
brings Transport, Resolver, Buffer and Common, and `InternalsVisibleTo` opens internals to the test assembly only.

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.handler.codec.dns` | `Netty.NET.Handler.Codec.Dns` | DNS codec. |
| test `io.netty.handler.codec.dns` | `Netty.NET.Handler.Codec.Dns.Tests` | (no package-info) |

Inside these namespaces the simple name `Dns` binds to the namespace, so `System.Net.Dns` would have to be qualified;
the port does not use it. Code uses the `string` keyword, never `String` member access, because
`Netty.NET.Handler.Codec.String` is a codec-base namespace.

## Shared CLR adaptations

- **Interfaces.** The Java interfaces take the `I` prefix (`IDnsMessage`, `IDnsQuery`, `IDnsResponse`, `IDnsRecord`,
  `IDnsQuestion`, `IDnsRawRecord`, `IDnsPtrRecord`, `IDnsOptPseudoRecord`, `IDnsOptEcsRecord`, `IDnsRecordDecoder`,
  `IDnsRecordEncoder`). Their accessors stay methods (`Name()`, `Type()`, `DnsClass()`, `TimeToLive()`, `Id()`,
  `OpCode()`, ...). Interface re-declarations that only narrow a return type are inherited unchanged, the rule of
  the Transport and Common interfaces ([transport-porting.md](transport-porting.md), "Java type names and
  hierarchy"): `IDnsQuery` and `IDnsRawRecord` are therefore empty. The classes keep the narrowed return types with
  C# 9 covariant overrides (`DefaultDnsQuery.SetId` returns `IDnsQuery`, `DatagramDnsQuery.SetId` returns
  `DatagramDnsQuery`).
- **Interface constants.** `DnsRecord.CLASS_*` are C# interface constants and `DnsRecordDecoder.DEFAULT` /
  `DnsRecordEncoder.DEFAULT` static readonly interface fields. C# classes do not inherit static interface members, so
  code names them `IDnsRecord.CLASS_IN`.
- **Generic record accessors.** `<T extends DnsRecord> T recordAt(...)`, `setRecord(section, index, record)`,
  `removeRecord` and `DnsRecordDecoder.decodeRecord(ByteBuf)` keep the type parameter (`where T : IDnsRecord`). C#
  does not infer a type argument from the assignment target, so callers write `RecordAt<IDnsQuestion>(section)`. The
  erased unchecked Java cast becomes a checked C# cast, which throws `InvalidCastException` inside the accessor for a
  mismatched type argument.
- **Reference counting.** `AbstractDnsMessage` extends Common's `AbstractReferenceCounted`, whose `Retain()`,
  `Retain(int)`, `Touch()`, `Touch(object)` and `Release` members are virtual as in Java; the narrowed
  `retain()`/`touch()` are C# 9 covariant overrides. `DefaultDnsRawRecord` implements the Buffer port's
  `IByteBufHolder`, where `content()` and `refCnt()` are the `Content` and `RefCnt` properties
  ([buffer-porting.md](buffer-porting.md), Buffer holders and conversion); its public members keep the
  `DnsRawRecord` return type and the wider interface members are explicit implementations, as in Transport's
  `DatagramPacket`.
- **Addressed envelopes.** `DatagramDnsQuery`/`DatagramDnsResponse` implement the Transport
  `IAddressedEnvelope<T, IPEndPoint>` (`InetSocketAddress` is `IPEndPoint`), whose `content()`, `sender()` and
  `recipient()` are the `Content`, `Sender` and `Recipient` properties. `AddressedEnvelope<?, SocketAddress>` is
  `IAddressedEnvelope<object, EndPoint>`, which the covariant `out` parameters match. The datagram encoders take
  `IAddressedEnvelope<IDnsQuery, IPEndPoint>`/`IAddressedEnvelope<IDnsResponse, IPEndPoint>`; the reified matcher of
  codec-base follows the same variance (the Java matcher compares the erased `AddressedEnvelope` class).
- **Text.** `toString()` output keeps the Java text with invariant-culture numbers. Two differences remain: an
  address prints as `EndPoint.ToString()` (`8.8.8.8:53`, not Java's `/8.8.8.8:53`), as in Transport's
  `DefaultAddressedEnvelope`, and `checkQuestion` names the CLR interface `IDnsQuestion` in its message.
- **Exceptions.** `IllegalArgumentException` is `ArgumentException`, `checkNotNull` throws `ArgumentNullException`,
  a source `IndexOutOfBoundsException` is `ArgumentOutOfRangeException(null, source text)`, and `java.lang.Error` for an
  impossible section number is `UnreachableException` ([codec-base-porting.md](codec-base-porting.md)).
  `DatagramDnsResponseDecoder` wraps `ArgumentOutOfRangeException`, which ByteBuf reads past the readable bytes throw
  in the Buffer port, where the source catches `IndexOutOfBoundsException`.
- **Package access.** Java `protected` also admits the package: the protected constructors, `decodeRecord(String,
  ...)`, `decodeName0` and `encodeName`, which `DnsRecordDecoder.DEFAULT`, `DnsRecordEncoder.DEFAULT` and the tests
  use, are `protected internal` (as codec-base's `LineBasedFrameDecoder.Decode`). Package-private classes
  (`DnsCodecUtil`, `DnsMessageUtil`, `DnsQueryEncoder`, `DnsResponseDecoder`) are internal.
- **Primitive map.** `DnsRecordType` keeps its `IntObjectHashMap<DnsRecordType>` as `Dictionary<int,
  DnsRecordType>`. `io.netty.util.collection` exists because a Java type argument cannot be a primitive type (JLS
  4.5.1); .NET specializes value-type type arguments ("Generics in the runtime"), so the owner kept `Dictionary`
  instead of porting it (2026-11-19, recorded in [the Common manifest](common-porting-manifest.json)).

## Area notes

- **Domain names.** `AbstractDnsRecord` converts names with `java.net.IDN.toASCII(name)`, which is the shared
  `Netty.NET.Common.Internal.IDN.ToASCII` (owner decision 2026-11-19, also used by codec-socks;
  `src/Netty.NET.Common/Internal/IDN.cs`). It keeps the OpenJDK 17 label loop: `"."` for a root label, `""` stays
  `""`, a trailing dot stays, ASCII labels are copied unchanged, and an empty label or a converted label longer than
  63 characters is rejected with `ArgumentException`. Only a label with non-ASCII characters goes to
  `IdnMapping.GetAscii`, which follows the IDNA version of the runtime; Java uses IDNA2003 (RFC 3490). The known
  differences are recorded in [common-clr-design.md](common-clr-design.md) (for example `faß`). The record name hash
  uses `ToLowerInvariant()` and `string.GetHashCode()`: equal for equal names, but not the Java value.
- **Name encoding.** `DnsCodecUtil.encodeDomainName` keeps the `String.split("\\.")` rule that trailing empty
  strings are dropped, so `"netty.io.."` encodes as `netty.io` as in Java (RFC 1035 section 3.1: "a domain name is terminated by a
  length byte of zero"). Labels are limited to 63 octets and names to 255 (RFC 1035 section 2.3.4).
- **Signed bytes.** The Java `byte` values are the unsigned `System.Byte`, as `ByteBuf.getByte` (owner decision).
  Where Java behaviour depends on the sign, the port reinterprets the value as `sbyte` and says so in a `// CLR:`
  comment:
  - `DnsOpCode.GetHashCode()` returns the signed byte (opcodes 128..255 hash negative);
  - `DnsOpCode.CompareTo` subtracts the signed bytes (opcodes 128..255 sort first);
  - `DefaultDnsOptEcsRecord.ToString()` prints `Arrays.toString(byte[])` with signed elements (`192` prints `-64`);
  - in the tests, `Byte.MAX_VALUE` is `sbyte.MaxValue` and the signed packet literals of `DnsResponseTest` are
    `sbyte` arrays converted to the unsigned arrays that ByteBuf takes.
  The opcode and record-class arithmetic (`& 0xF`, `& 0xFF`, `& 0xFFFF`) and the ECS address masks select the same
  bits for unsigned bytes.

## Not ported

| Unit | Status | Reason |
| --- | --- | --- |
| `DefaultDnsOptEcsRecord(int, InternetProtocolFamily)` constructor | comment disposition `deprecated-not-ported` | `@Deprecated`; the tag names the replacement `DefaultDnsOptEcsRecord(int, SocketProtocolFamily)`, which is ported. `InternetProtocolFamily.ToSocketProtocolFamily()` (Transport) converts an existing value. Its Javadoc is recorded in the manifest. |
| `package-info.java` | `jvm-only-replaced` | C# namespaces cannot carry documentation comments; the summary is in the table above. |
| `module-info.yml` | `jvm-only-replaced` | JPMS module descriptor (requires `io.netty.buffer`, `io.netty.transport`, `io.netty.codec`, `io.netty.common`); `Netty.NET.Handler.Codec.Dns.csproj` references codec-base and `InternalsVisibleTo` opens internals to the test assembly only. |
| `META-INF/native-image/.../reflect-config.json` | `jvm-only-replaced` | GraalVM reflection metadata; NativeAOT keeps handler metadata through the `DynamicallyAccessedMembers` annotations on `IChannelHandler`/`ChannelHandlerAdapter`. |
| `NativeImageHandlerMetadataTest` | `jvm-only-replaced` | Generates and compares that GraalVM JSON. |

## Test notes

- Anonymous subclasses (`AbstractDnsRecord`, `AbstractDnsOptPseudoRrRecord`) are named private nested classes and
  JUnit `Executable` instances are lambdas.
- `@ParameterizedTest` with `@MethodSource` is `[Theory]` with `[MemberData]` rows of the same values.
- The private helpers `testDecodeName(String, ByteBuf)` and `testEncodeName(byte[], String)` are `TestDecodeName0`
  and `TestEncodeName0`, because xUnit1024 forbids a helper overload with the name of a test method (as codec-base's
  `ReleaseWhenMergeCumulateThrowsInExpand0`).
- `SocketUtils.socketAddress`/`addressByName` are `IPEndPoint`/`IPAddress.Parse`, `new InetSocketAddress(0)` is
  `new IPEndPoint(IPAddress.Any, 0)`, and `ThreadLocalRandom`/`SplittableRandom` are `Random.Shared`.
