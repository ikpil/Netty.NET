# codec-socks porting

Netty.NET ports `netty-codec-socks` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to `src/Netty.NET.Handler.Codec.Socks`, with its original tests in
`test/Netty.NET.Handler.Codec.Socks.Tests`. The [manifest](codec-socks-porting-manifest.json) records every pinned file
of `codec-socks/src` (118 files) with its C# counterpart, its CLR adaptations and evidence, or the reason it is not
ported. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module codec-socks
```

The checker verifies the file inventory and source hashes, that every original comment (license, Javadoc and line
comments) is present in the mapped C# files, that every declared JUnit test is mapped to a C# test of the same name
(case may differ) or carries a recorded disposition, and the facts of every deprecated member that is not ported. It
fails while any entry is still `pending`.

| Status | Files |
| --- | --- |
| `clr-adapted` | 82 |
| `verified` (tests whose every original method is mapped) | 29 |
| `jvm-only-replaced` | 7 |

Of the 82 declared original tests, 81 are mapped to C# tests with the same names and pass; the remaining one is
`NativeImageHandlerMetadataTest.collectAndCompareMetadata` (GraalVM metadata, see below).

## Namespaces and assembly (owner decisions, 2026-11-19)

The project, folder and assembly are `Netty.NET.Handler.Codec.Socks`, the Maven and JPMS name of the module, and the
assembly holds both the `socks` and the `socksx` packages. The project references `Netty.NET.Handler.Codec`
(codec-base). The tests use `Netty.NET.Handler.Codec.Socks.Tests`, with `.Socksx.V4` and `.Socksx.V5` for the subpackage
tests in matching folders.

| Java package | C# namespace (folder) | Original package summary |
| --- | --- | --- |
| `io.netty.handler.codec.socks` | `Netty.NET.Handler.Codec.Socks` (project root) | Encoder, decoder and their related message types for Socks. |
| `io.netty.handler.codec.socksx` | `Netty.NET.Handler.Codec.Socksx` (`Socksx/`) | Encoder, decoder and their related message types for SOCKS protocol. |
| `io.netty.handler.codec.socksx.v4` | `Netty.NET.Handler.Codec.Socksx.V4` (`Socksx/V4/`) | Encoder, decoder and their related message types for SOCKSv4 protocol. |
| `io.netty.handler.codec.socksx.v5` | `Netty.NET.Handler.Codec.Socksx.V5` (`Socksx/V5/`) | Encoder, decoder and their related message types for SOCKSv5 protocol. |
| test `io.netty.handler.codec.socks`, `socksx.v4`, `socksx.v5` | `Netty.NET.Handler.Codec.Socks.Tests` (`.Socksx.V4`, `.Socksx.V5`) | (no package-info) |

- The Java interfaces take the `I` prefix: `socksx.SocksMessage` is `ISocksMessage`, `Socks5AddressDecoder` is
  `ISocks5AddressDecoder`, and so on. The `socks.SocksMessage` class keeps its name.
- Code under these namespaces uses the `string` keyword; inside `Netty.NET.Handler.Codec.*` the simple name `String`
  binds to the codec-base namespace.

## Shared CLR adaptations

- **Java enums with state.** The `socks` enums with a byte field (`SocksAddressType`, `SocksAuthScheme`,
  `SocksAuthStatus`, `SocksCmdStatus`, `SocksCmdType`, `SocksProtocolVersion`, `SocksSubnegotiationVersion`) and
  `socksx.SocksVersion` are sealed classes with static readonly constants and a private constructor, as `Base64Dialect`
  in codec-base: a C# enum is a value type that "can't define a method"
  ([enum](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/enum)). `Values()` is the
  implicit `values()` (used by `valueOf(byte)` and the tests), and `ToString()` returns the constant name. A `switch`
  over these constants is an `if`/`else` chain. The simple enums (`SocksMessageType`, `SocksRequestType`,
  `SocksResponseType`, the decoder `State` enums) are C# enums; `checkNotNull` of a C# enum argument has no
  counterpart.
- **Decoder states.** `ReplayingDecoder<State>` subclasses declare their nested `State` enum with `new`, because it
  hides the inherited `State()` method, and call `base.State()`. Each Java fall-through after `checkpoint(...)` is
  `goto case`. The socksx decoders catch `Exception` `when (e is not Signal)`, because `Signal` is an Exception here but
  a `java.lang.Error` in the source ([codec-base](codec-base-porting.md), Signals). An unreachable `throw new Error(...)`
  is `InvalidOperationException`.
- **IDN.** `java.net.IDN.toASCII/toUnicode` is the shared `Netty.NET.Common.Internal.IDN` (owner decision 2026-11-19:
  one helper for codec-dns and codec-socks) over `System.Globalization.IdnMapping`. It
  keeps the Java results that the socks code and tests rely on: labels are converted one by one, an all-ASCII label is
  left unchanged apart from the 1..63 length check (RFC 3490 section 4.1), `""` gives `""`, and `toUnicode` returns a
  label it cannot convert unchanged. `IdnMapping.GetAscii` alone rejects `""` and ASCII control characters (the SOCKS4
  `dstAddr` `"unix://uds\0.sock"` of `Socks4ClientEncoderTest`). Non-ASCII labels use IDNA2008/UTS #46 where Java uses
  IDNA2003; the host names of the ported tests give the same Punycode. It is used by `SocksCmdRequest`,
  `SocksCmdResponse`, `DefaultSocks4CommandRequest`, `DefaultSocks5CommandRequest` and `DefaultSocks5CommandResponse`.
- **Values with byte constants.** `Socks4CommandStatus`, `Socks4CommandType`, `Socks5AddressType`, `Socks5AuthMethod`,
  `Socks5CommandStatus`, `Socks5CommandType`, `Socks5PasswordAuthStatus` and `Socks5PrivateAuthStatus` implement
  `IComparable<T>`; `hashCode`/`equals`/`toString` are `GetHashCode`/`Equals`/`ToString`.
- **Numbers in text** use the invariant culture. `StringBuilder.append(String)` of a null address appends `"null"`
  as in Java (`DefaultSocks4CommandResponse`, `DefaultSocks5CommandResponse`). `Collections.unmodifiableList` is a
  `ReadOnlyCollection<T>`, and the Java list text `[a, b]` is written explicitly where a list is appended.
- **Overridable accessors.** The accessors of the non-final `Default*` message classes are `virtual`, because a Java
  method that is not `final` can be overridden (`Socks5ClientEncoderTest` overrides `username()`/`password()`).
- **Interface constants.** `Socks5AddressDecoder.DEFAULT`/`Socks5AddressEncoder.DEFAULT` are static readonly fields of
  the interfaces, and their anonymous classes are private nested classes.
- **Buffers.** `ByteBuf.getByte/readByte` return the unsigned `System.Byte` (owner decision,
  [common-clr-design.md](common-clr-design.md)), so `readUnsignedByte`/`getUnsignedByte` are `ReadByte`/`GetByte`. The
  IPv6 branch of the default address decoder always reads into a 16-byte array, because the Buffer port has no
  `hasArray()`/`array()` ([buffer-porting.md](buffer-porting.md)); that is also the source path for every
  `ReplayingDecoder` input.

### Byte-sign spots

Where the Java result depends on the sign of a `byte`, the port converts to `sbyte` at that point:

| Place | Java behaviour kept |
| --- | --- |
| `SocksAuthRequestDecoder` (username and password length), `SocksCmdRequestDecoder` and `SocksCmdResponseDecoder` (DOMAIN length): `int fieldLength = (sbyte) byteBuf.ReadByte()` | a length octet of 0x80-0xFF is negative and the read fails, so fields of 128 or more characters are rejected |
| `GetHashCode()` and `CompareTo()` of the eight byte-valued classes above | `0x80`-`0xFF` hash and order as -128..-1 (for example `Socks5AuthMethod.UNACCEPTED` and `Socks5PasswordAuthStatus.FAILURE` sort before value 0) |
| "unsupported version" / "unsupported subnegotiation version" messages of `Socks5CommandRequestDecoder`, `Socks5CommandResponseDecoder`, `Socks5InitialRequestDecoder`, `Socks5InitialResponseDecoder`, `Socks5PasswordAuthRequestDecoder`, `Socks5PasswordAuthResponseDecoder`, `Socks5PrivateAuthRequestDecoder`, `Socks5PrivateAuthResponseDecoder` | the version byte is printed signed (for example `-1` for 0xFF) |

The other byte uses do not depend on the sign: equality checks (`valueOf(byte)`, version comparisons), switch labels
such as `(byte) 0xFF` (the unsigned constant 0xFF), and `& 0xFF` conversions (`toString()`, `isPrivateMethod`, the
`PRIVATE_n` names, `logUnknownVersion`, the unsupported-address-type messages).

## Deprecated members

| Member | Handling | Reason |
| --- | --- | --- |
| `fromByte(byte)` of `SocksAddressType`, `SocksAuthScheme`, `SocksAuthStatus`, `SocksCmdStatus`, `SocksCmdType`, `SocksProtocolVersion`, `SocksSubnegotiationVersion` | not ported; the Javadoc is recorded in `omittedComments` of each entry | "@deprecated Use valueOf(byte) instead." names the replacement (owner decision 2026-11-16) |
| `socks.SocksMessage.encodeAsByteBuf(ByteBuf)` | ported with `[Obsolete("Do not use; this method was intended for an internal use only.")]` | the `@deprecated` text names no replacement; the overrides suppress CS0672 (they carry no `@Deprecated` in Java), and `SocksMessageEncoder` and the tests suppress CS0618 around the call, as `@SuppressWarnings("deprecation")` does |

## Not ported (JVM-only)

| Unit | Status | Reason |
| --- | --- | --- |
| 4 `package-info.java` | `jvm-only-replaced` | C# namespaces cannot carry documentation comments; the summaries are in the table above. |
| `module-info.yml` | `jvm-only-replaced` | JPMS module descriptor; `Netty.NET.Handler.Codec.Socks.csproj` references codec-base and `InternalsVisibleTo` opens internals to the test assembly only. |
| `META-INF/native-image/.../reflect-config.json` | `jvm-only-replaced` | GraalVM reflection metadata; NativeAOT keeps handler metadata through the `DynamicallyAccessedMembers` annotations on `IChannelHandler`/`ChannelHandlerAdapter`. |
| `NativeImageHandlerMetadataTest` | `jvm-only-replaced` | Generates and compares that GraalVM JSON. |

## Test notes

- `assertInstanceOf(C.class, x)` is `Assert.IsType<C>(x, exactMatch: false)`; `NullPointerException` from
  `checkNotNull` is `ArgumentNullException` and `IllegalArgumentException` is `ArgumentException`. AssertJ
  `assertThatThrownBy(...).isInstanceOf(C).hasMessageContaining(s)` is `Assert.ThrowsAny<C>` with `Assert.Contains`.
- `SocksCmdRequestTest.testEndianessForPort` creates its buffer without `order(ByteOrder.LITTLE_ENDIAN)`: `order` is
  deprecated and the Buffer port has no swapped-order view. The test still checks the encoded request and the
  big-endian port, so `SocksCmdRequestTest` is `clr-adapted` rather than `verified`.
- The anonymous message classes of `Socks5ClientEncoderTest` and `Socks5ServerEncoderTest` are named nested classes;
  `java.net.IDN` in the tests is `Netty.NET.Common.Internal.IDN`.
- `SocketUtils.addressByName("::1")` of a literal is `IPAddress.Parse`, as in the codec-dns tests.
