# codec-http porting

Netty.NET ports `netty-codec-http` of Netty 4.2.19.Final (pinned commit `64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to
`src/Netty.NET.Handler.Codec.Http`, with its original tests in `test/Netty.NET.Handler.Codec.Http.Tests`: the package
`io.netty.handler.codec.http` with its `cookie`, `cors`, `multipart` and `websocketx` packages (`websocketx` with
`extensions` and `extensions.compression`), `io.netty.handler.codec.rtsp` and `io.netty.handler.codec.spdy`. The
[manifest](codec-http-porting-manifest.json) records every pinned file of `codec-http/src` (377 files: 262 main and 108
test Java sources, `module-info.yml`, two main resources and four test resources) with its C# counterpart, its CLR
adaptations and evidence, or the reason it is not ported. No entry is `pending`. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module codec-http
```

`codec-http` is also in the checker's default module list, so a run without `-Module` checks it with the other codec,
pkitesting and handler manifests; `-AllowPending` is not needed.

The checker verifies the file inventory and the source hashes of all 377 files. For every entry it also verifies that
every original comment (license, Javadoc and line comments) is present in the mapped C# files or recorded as belonging
to a member that is not ported, that every declared JUnit test is mapped to a C# test of the same name (case may differ)
or carries a recorded disposition, and the facts and evidence of every unit that is not ported. It fails on a `pending`
entry. For codec-http the manifest generator and the checker (`Get-DeclaredJavaTests`) were extended (2026-11-24): a
JUnit meta-annotation declared in the same source, that is an `@interface` that carries a test annotation, marks test
methods too, and the test annotation on the `@interface` itself is not counted as a method. `HttpDataTest` declares its
seven tests with such an annotation, `@ParameterizedHttpDataTest` (`@ParameterizedTest` with `@MethodSource("data")`).

- **Order.** The port was done on 2026-11-24 in waves: first the `http` package itself (core) and, in parallel, the zlib
  preset dictionary of codec-compression ([codec-compression-porting.md](codec-compression-porting.md)); then cookie
  with cors, multipart, websocketx (with extensions and compression) and rtsp with spdy in parallel. The package-info
  files, the JVM metadata and the test resources were recorded separately.

## Owner decisions (2026-11-24)

- **Project and namespaces.** One project, `Netty.NET.Handler.Codec.Http`, for the Maven module. The packages map to
  `Netty.NET.Handler.Codec.Http`, `.Http.Cookie`, `.Http.Cors`, `.Http.Multipart`, `.Http.WebSocketx`,
  `.Http.WebSocketx.Extensions`, `.Http.WebSocketx.Extensions.Compression`, `Netty.NET.Handler.Codec.Rtsp` and
  `Netty.NET.Handler.Codec.Spdy`, in the folders `Cookie/`, `Cors/`, `Multipart/`, `WebSocketx/`, `Rtsp/` and `Spdy/`
  (as codec-socks keeps `Socksx/`). `websocketx` takes the casing of its Java types (`WebSocketFrame`), as `ipfilter`
  became `IpFilter`.
- **zlib preset dictionary.** SPDY header blocks are compressed with a zlib preset dictionary (`SPDY_DICT`), which the
  BCL `DeflateStream`/`ZLibStream` cannot set. The dictionary is implemented on the BCL streams in the
  `java.util.zip.Deflater`/`Inflater` stand-ins of codec-compression, which codec-http uses through
  `InternalsVisibleTo`: the inflater is primed with the dictionary in stored deflate blocks (RFC 1951 3.2.4) and drops
  the dictionary's bytes from its output; the deflater compresses the dictionary, sync-flushes and keeps only what
  follows, behind a zlib header with `FDICT` and the dictionary's Adler-32 (RFC 1950 2.2). Measured on 2026-11-24: a
  stream that CPython's zlib compressed with `zdict` decodes byte for byte when fed in 1-63 byte pieces, and a stream
  produced this way decodes with CPython's `decompressobj(zdict=...)`.
- **Listener scheduler.** The listener continuations that need the timing of `DefaultPromise.notifyListeners()` (at once
  when the promise completes on its event loop, otherwise through `executor.execute`) use one public, CLR-only Common
  helper, `Netty.NET.Common.Concurrent.EventExecutorTaskScheduler(Func<IEventExecutor>)`
  (`src/Netty.NET.Common/Concurrent/EventExecutorTaskScheduler.cs`; see
  [common-native-completion.md](common-native-completion.md) and [handler-porting.md](handler-porting.md), Listener
  timing). It replaces handler's internal `IdleStateHandler.ExecutorTaskScheduler` and the seven private copies that
  codec-http had during the port (`HttpServerCodec`, `HttpServerKeepAliveHandler`, `HttpServerExpectContinueHandler`,
  `CorsHandler`, `SpdySessionHandler`, `WebSocketProtocolHandler`, `WebSocketServerExtensionHandler`). A lambda that
  captures a method parameter makes the C# compiler allocate its closure on entry to the method, so the five write paths
  that attach a listener in one branch only (`HttpServerCodec`, `HttpServerKeepAliveHandler`, `CorsHandler`,
  `WebSocketProtocolHandler` and handler's `WriteTimeoutHandler`) capture a local `listenerCtx` declared in that branch,
  and writes without a listener allocate no closure (checked in the IL). See [Listener timing](#listener-timing) for
  the measurements.
- **Shared-API fixes.** The four shared-API requests of the port (codec-base `DefaultHeaders.Copy()`, Buffer
  `ByteBufUtil.ReadBytes` on a replaying buffer, Java charset names, a `Throwable.toString()` text helper) are fixed
  in the shared modules, with the owner's public names `Netty.NET.Common.CharsetUtil.ForName(string)` and
  `Netty.NET.Common.Internal.ThrowableUtil.ThrowableToString(Exception)` (see [Shared-API
  requests](#shared-api-requests)).

## Status

| Status | Files |
| --- | --- |
| `clr-adapted` (the 239 ported main sources, and 3 test classes with CLR-inapplicable methods) | 242 |
| `verified` (103 test classes and helpers whose every original method is mapped, and the 3 test resources `file-0N.txt`, copied byte for byte) | 106 |
| `deprecated-not-ported` (13 `@Deprecated` main units and `HttpHeaderDateFormatTest`) | 14 |
| `jvm-only-replaced` (9 `package-info.java`, `module-info.yml`, the two GraalVM metadata files, `junit-platform.properties` and `NativeImageHandlerMetadataTest`) | 14 |
| `library-not-ported` (`SpdyHeaderBlockJZlibEncoder`, jzlib) | 1 |

The checker reports:

```
codec-http manifest verified: 377 pinned files (242 clr-adapted, 14 deprecated-not-ported, 14 jvm-only-replaced, 1 library-not-ported, 106 verified), 1081 mapped original tests, 4 CLR-inapplicable and 3 not-ported original tests, 237 deprecated-member, 0 JVM-only-API and 0 library-not-ported comment dispositions; 14 deprecated-not-ported, 14 JVM-only-replaced and 1 library-not-ported entries carry 206 original comments.
```

Of the 1088 declared original tests, 1081 are mapped to C# tests with the same names, 4 are CLR-inapplicable (the three
zstd tests of the content codecs and `HttpUtilTest.testFormatHostnameForHttpFromResolvedAddressWithHostname`, see
[Core](#core-ionettyhandlercodechttp)) and 3 are not ported: the two tests of the deprecated-not-ported
`HttpHeaderDateFormatTest` and `NativeImageHandlerMetadataTest.collectAndCompareMetadata` (JVM-only). 237 comments of
deprecated members are recorded instead of ported, and the 29 entries that are not ported carry 206 original comments.

The C# run of `Netty.NET.Handler.Codec.Http.Tests` on the real projects executes 9055 test cases (2026-11-27): 9055 pass, 0 fail and
0 are skipped. Theories run once per data row and the tests of abstract fixtures run for each derived class, so there
are more cases than mapped methods. The two exhaustive 2^32 tests of `HttpHeaderValidationUtilTest` run in
`Parallel.For` slices (see [Core test notes](#core-test-notes)).

| Package | Main files | Test files | Mapped original tests | CLR-inapplicable | Not ported | C# test cases |
| --- | --- | --- | --- | --- | --- | --- |
| `http` | 70 ported, 6 deprecated-not-ported | 35 ported (32 verified, 3 clr-adapted), 1 deprecated-not-ported, 1 jvm-only-replaced | 555 of 562 | 4 | 3 | 8384 (with one CLR supplement) |
| `http.cookie` | 10 | 4 | 36 | 0 | 0 | 36 |
| `http.cors` | 3 | 2 | 62 | 0 | 0 | 62 |
| `http.multipart` | 26 | 14 | 121 | 0 | 0 | 185 |
| `http.websocketx` | 51 | 24 | 99 | 0 | 0 | 179 |
| `http.websocketx.extensions` | 13 | 6 (one helper without tests) | 19 | 0 | 0 | 19 |
| `http.websocketx.extensions.compression` | 12 | 9 | 63 | 0 | 0 | 64 (one theory with two rows) |
| `rtsp` | 7 ported, 7 deprecated-not-ported | 3 | 13 | 0 | 0 | 13 |
| `spdy` | 47 ported, 1 library-not-ported | 9 (one helper without tests) | 113 | 0 | 0 | 113 |
| **total** | 239 ported, 13 deprecated-not-ported, 1 library-not-ported | 106 ported, 2 not ported | 1081 of 1088 | 4 | 3 | 9055 |

Each package also has a `package-info.java` recorded as `jvm-only-replaced`. The `http` row includes the deprecated
cookie API of that package (`ClientCookieEncoder` ported with `[Obsolete]`, six units and `HttpHeaderDateFormatTest`
not ported), which is described with [Cookie and CORS](#cookie-and-cors). The testsuite's `SocketSpdyEchoTest` (two
tests) is ported in `Netty.NET.Transport.Tests` ([transport-porting.md](transport-porting.md), Testsuite inventory).

## Namespaces and assembly

The project references `Netty.NET.Handler.Codec.Compression` (the content codecs and the zlib stand-ins of SPDY) and
`Netty.NET.Handler` (`io.netty.handler.ssl` and `io.netty.handler.stream`), which bring codec-base, Transport,
Resolver, Buffer and Common. `InternalsVisibleTo` opens its internals to `Netty.NET.Handler.Codec.Http.Tests` only;
codec-compression opens its internals to `Netty.NET.Handler.Codec.Http` for the internal `Deflater`/`Inflater`
stand-ins (owner decision above), and codec-base opens its internals to it for the CLR-only `ExecutorPromiseCombiner`
that `HttpObjectEncoder` combines its writes with ([codec-base-porting.md](codec-base-porting.md), Outbound
operations). The test project also references `Netty.NET.PkiTesting` and Moq, and deploys the three
`file-0N.txt` resources next to the test assembly. `Netty.NET.Transport.Tests` references the project for the
testsuite's `SocketSpdyEchoTest`.

| Java package | C# namespace | Original package summary |
| --- | --- | --- |
| `io.netty.handler.codec.http` | `Netty.NET.Handler.Codec.Http` (project root) | Encoder, decoder and their related message types for HTTP. |
| `io.netty.handler.codec.http.cookie` | `Netty.NET.Handler.Codec.Http.Cookie` (`Cookie/`) | This package contains Cookie related classes. |
| `io.netty.handler.codec.http.cors` | `Netty.NET.Handler.Codec.Http.Cors` (`Cors/`) | This package contains Cross Origin Resource Sharing (CORS) related classes. |
| `io.netty.handler.codec.http.multipart` | `Netty.NET.Handler.Codec.Http.Multipart` (`Multipart/`) | HTTP multipart support. |
| `io.netty.handler.codec.http.websocketx` | `Netty.NET.Handler.Codec.Http.WebSocketx` (`WebSocketx/`) | Encoder, decoder, handshakers and their related message types for <a href="https://en.wikipedia.org/wiki/Web_Sockets">Web Socket</a> data frames. (The summary continues with the supported specification versions, hence the X suffix: draft-ietf-hybi-thewebsocketprotocol-00, -07, -10 and RFC 6455, and the <tt>WebSocketServerX</tt> example; the full text is kept in the manifest entry of `websocketx/package-info.java`.) |
| `io.netty.handler.codec.http.websocketx.extensions` | `Netty.NET.Handler.Codec.Http.WebSocketx.Extensions` (`WebSocketx/Extensions/`) | Encoder, decoder, handshakers to handle <a href="https://tools.ietf.org/html/rfc6455#section-9.1">WebSocket Extensions</a>. See <tt>WebSocketServerExtensionHandler</tt> for more details. |
| `io.netty.handler.codec.http.websocketx.extensions.compression` | `Netty.NET.Handler.Codec.Http.WebSocketx.Extensions.Compression` (`WebSocketx/Extensions/Compression/`) | Encoder, decoder, handshakers to handle most common WebSocket Compression Extensions. (The summary continues with the supported specifications, permessage-deflate and perframe-deflate/x-webkit-deflate-frame, and the example clients; the full text is kept in the manifest entry of `compression/package-info.java`.) |
| `io.netty.handler.codec.rtsp` | `Netty.NET.Handler.Codec.Rtsp` (`Rtsp/`) | An <a href="https://en.wikipedia.org/wiki/Real_Time_Streaming_Protocol">RTSP</a> extension based on the HTTP codec. |
| `io.netty.handler.codec.spdy` | `Netty.NET.Handler.Codec.Spdy` (`Spdy/`) | Encoder, decoder, session handler and their related message types for the SPDY protocol. |
| test packages of the nine above | `Netty.NET.Handler.Codec.Http.Tests` with `.Cookie`, `.Cors`, `.Multipart`, `.WebSocketx`, `.WebSocketx.Extensions`, `.WebSocketx.Extensions.Compression`, `.Rtsp` and `.Spdy`, in the same folders | (no test package-info) |

**Name lookup.** The deprecated root type `Netty.NET.Handler.Codec.Http.ClientCookieEncoder` is found before a
compilation-unit `using Netty.NET.Handler.Codec.Http.Cookie;` from any namespace below `Netty.NET.Handler.Codec.Http`,
so the cookie tests put that using after the file-scoped namespace declaration. No other codec-http package uses the
cookie package (the Java sources name it only in Javadoc); new code under `Netty.NET.Handler.Codec.Http.*` that does
needs the same, or a qualified name.

## Shared CLR adaptations

The adaptations below apply across the packages; each package section lists only what is specific to it.

| Java | C# | Notes and evidence |
| --- | --- | --- |
| Interfaces (`HttpMessage`, `HttpContent`, `Cookie`, `InterfaceHttpData`, `WebSocketFrameDecoder`, `WebSocketExtension`, `SpdyFrame`, ...) | `I`-prefixed interfaces (`IHttpMessage`, `IHttpContent`, `ICookie`, `IInterfaceHttpData`, `IWebSocketFrameDecoder`, `IWebSocketExtension`, `ISpdyFrame`, ...) | Re-declarations that only narrow return types (`copy`/`duplicate`/`retainedDuplicate`/`replace`/`retain`/`touch` of `ByteBufHolder`, `setProtocolVersion`/`setMethod`/`setUri`/`setStatus`, SPDY `setStreamId`/`setLast`/`setInvalid`) are inherited (owner decision 2026-11-19, [transport-porting.md](transport-porting.md) Decisions). The classes narrow them with covariant overrides ([override](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/override)) and implement the interface members with other return types explicitly; through an interface variable a narrowed member returns the base type, so callers cast (`(IFullHttpRequest) request.Replace(buf)`). Interface constants are C# interface constants or static readonly fields, and nested types stay nested (implementing classes write `ICookie.UNDEFINED_MAX_AGE`, `IInterfaceHttpData.HttpDataType`, `IWebSocketExtension.RSV1`) ([interface](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/interface)). |
| `CharSequence`, `String` | Common `ICharSequence`, `string` | Where Java has both overloads (`HttpHeaders.add/get/getAll/contains/remove/set`) both are kept: a `string` binds to the `String` overload, an `AsciiString` to the `CharSequence` one. A C# `string` is not an `ICharSequence`, so Strings that Java passes or stores as CharSequence (decoded SPDY headers, CORS header names, `headers.get(...)` results) are wrapped in the Common `StringCharSequence`, which is also the `CharBuffer.wrap(s, begin, end)` view (as the codec-stomp port does). |
| `Iterable<?>`, `Map.Entry`, `Iterator`, wildcards | `IEnumerable<object>`, codec-base `IEntry`/`IIterator` ([codec-base-porting.md](codec-base-porting.md), Headers) | Reference-type covariance lets a `List<string>` bind; an `IEnumerable` of a value type binds to the `Object` overload, so value types are passed boxed. `Collection<? extends Cookie>` is the covariant `IReadOnlyCollection<ICookie>` ([IReadOnlyCollection](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.ireadonlycollection-1?view=net-10.0)) and `Iterable<? extends Cookie>` is `IEnumerable<ICookie>`. |
| `@Deprecated` members and units | not ported, or `[Obsolete]` | Owner decision 2026-11-16: a deprecated member or unit whose `@deprecated` text names a replacement is not ported and its comments are recorded; one without a named replacement is ported with `[Obsolete]`. The cases are listed in each package section and under [Owner review items](#owner-review-items). |
| `ChannelFuture`, `ChannelPromise`, `write(ctx, msg, promise)` | `Task`, `TaskCompletionSource`, `WriteAsync(ctx, msg, cancellationToken)` | The returned Task replaces the promise ([codec-base-porting.md](codec-base-porting.md), Outbound operations); there is no void promise and `PromiseCombiner` is `Task.WhenAll`. Where a handler stores, completes or listens to a promise (`SpdySessionHandler`, the WebSocket handshakers, `WebSocketServerExtensionHandler`), a `TaskCompletionSource` stands for it and `TrySetFromTask` copies the outcome of the operation Task ([TrySetFromTask](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcompletionsource.trysetfromtask?view=net-10.0); the PromiseNotifier mapping of [common-task-composition.md](common-task-composition.md)). Task-returning methods take the `Async` suffix (`HandshakeAsync`, `CloseAsync`, `ProcessHandshakeAsync`). `ChannelFutureListener.CLOSE` closes the channel the operation ran on, because a Task has no channel. |
| Listener notification | Task continuations | See [Listener timing](#listener-timing): Common's `EventExecutorTaskScheduler` where Java's timing is observable, synchronous continuations on `TaskScheduler.Default` where the Task is already complete when the continuation is attached. |
| `java.net.URI` | `System.Uri` | `java.net.URI` keeps every component as written ([URI](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/URI.html)); `System.Uri` canonicalizes, and `Host` throws for a relative URI ([Uri.Host](https://learn.microsoft.com/en-us/dotnet/api/system.uri.host?view=net-10.0)). `QueryStringDecoder` and `WebSocketClientHandshaker` read the raw components from `OriginalString`, "the exact URI specified when this instance was constructed" ([Uri.OriginalString](https://learn.microsoft.com/en-us/dotnet/api/system.uri.originalstring?view=net-10.0)), with private helpers for the RFC 3986 generic syntax. `URI.create(s)`/`new URI(s)` is `new Uri(s, UriKind.RelativeOrAbsolute)`; `URISyntaxException` is `UriFormatException`. |
| `InetSocketAddress` | `EndPoint` | `DnsEndPoint` when unresolved, `IPEndPoint` when resolved. |
| `Charset`, `Charset.forName` | `Encoding`, Common's `CharsetUtil.ForName` | `ForName` (CLR-only, owner decision 2026-11-24) resolves the six standard Java charsets and their aliases such as `UTF8` as Java does, applies Java's legal-name rule and passes any other name to `Encoding.GetEncoding`; its `ArgumentException` covers both Java exceptions ([Charset.forName](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/charset/Charset.html#forName(java.lang.String)), [GetEncoding](https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.getencoding?view=net-10.0)). `HttpUtil.GetCharset` and the multipart decoder use it (see [Multipart](#multipart)). `String.getBytes()` in the tests (default charset) is `Encoding.Default` ([codec-smtp-porting.md](codec-smtp-porting.md)). |
| `writeCharSequence(..., US_ASCII)`, `writeCharSequence(..., UTF_8)` | `ByteBufUtil.WriteAscii` (or a `C2b` loop), `ByteBufUtil.WriteUtf8` | Java writes `AsciiString.c2b` per char: 0x80-0xFF keep their low byte and a char above 0xFF becomes `?`; `Encoding.ASCII` would replace everything above 0x7F. An unpaired surrogate becomes `?` in both UTF-8 writers. `Unpooled.copiedBuffer(text, UTF_8)` is `Unpooled.CopiedBuffer(text, Encoding.UTF8)`, which writes an unpaired surrogate as EF BF BD where Java writes `?` ([buffer-porting.md](buffer-porting.md)). |
| `trim()`, `toUpperCase/toLowerCase(Locale.US)`, `Character.isISOControl` | `HttpUtil.TrimJava`, `ToUpperInvariant`/`ToLowerInvariant`, `char.IsControl` | [ToUpperInvariant](https://learn.microsoft.com/en-us/dotnet/api/system.string.toupperinvariant?view=net-10.0), [ToLowerInvariant](https://learn.microsoft.com/en-us/dotnet/api/system.string.tolowerinvariant?view=net-10.0), [IsControl](https://learn.microsoft.com/en-us/dotnet/api/system.char.iscontrol?view=net-10.0). `Character.isWhitespace` keeps the Java rule in `HttpVersion.IsJavaWhitespace` ([Character](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Character.html)). |
| `String.split(",")` | private `Split` helpers | Java discards trailing empty strings and returns the input when nothing matches ([String](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html)); the helpers keep that result (multipart, WebSocket handshakers, `WebSocketExtensionUtil`). `split(regex, n)` with a one-char regex is `Split(char, n)`. |
| `String.compareTo`, `equalsIgnoreCase` | `string.CompareOrdinal`, `StringComparison.OrdinalIgnoreCase` | Ordinal comparisons give Java's order for the ASCII names and tokens; `DefaultCookie` keeps Java's case fold (see [Cookie and CORS](#cookie-and-cors)). |
| `Long.parseLong`, `Integer.parseInt`, `Float.parseFloat` | `HttpUtil.ParseLong`, `PerMessageDeflateServerExtensionHandshaker.ParseInt`, `float.Parse` | `NumberFormatException` is `FormatException`, also for overflow. Details in the package sections. |
| `java.util.Date`, `Calendar`, `System.currentTimeMillis()` | `DateTimeOffset`, `DateTime`, `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` | As codec-base `DateFormatter` ([DateTimeOffset(DateTime)](https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset.-ctor?view=net-10.0)). |
| `Pattern`/`Matcher` | `Regex` | Same patterns; `Matcher.group(n)`, null for a non-participating group, is the group value only when `Group.Success` is true ([Group.Success](https://learn.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.group.success?view=net-10.0)). RTSP's `matches()` wraps the pattern in `\A(?:...)\z` and writes Java's `\d` as `[0-9]` ([character classes](https://learn.microsoft.com/en-us/dotnet/standard/base-types/character-classes-in-regular-expressions)). |
| `TreeMap`, `TreeSet` | `SortedDictionary`, `SortedSet` | Both sets keep the first of two equal elements ([TreeSet](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/TreeSet.html), [SortedSet.Add](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.sortedset-1.add?view=net-10.0)). |
| `LinkedHashMap`, `LinkedHashSet`, `HashMap`, `IntObjectMap<Long>` | Common `LinkedHashMap`/`LinkedHashSet`, `Dictionary`, `Dictionary<int, long>` | Neither `HashMap` nor `Dictionary` defines an iteration order ([HashMap](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/HashMap.html), [Dictionary](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2?view=net-10.0)). `IntObjectMap` is `Dictionary` by owner decision 2026-11-19. A `Dictionary`-backed set or map throws for a null key, where `HashMap` accepts one. |
| `Collections.unmodifiable*`, `emptyMap()`/`emptyList()`/`singletonList` | `ReadOnlyCollection`/`ReadOnlySet`/`ReadOnlyDictionary` views, immutable collections | [ReadOnlySet](https://learn.microsoft.com/en-us/dotnet/api/system.collections.objectmodel.readonlyset-1?view=net-10.0), [ReadOnlyDictionary](https://learn.microsoft.com/en-us/dotnet/api/system.collections.objectmodel.readonlydictionary-2?view=net-10.0). |
| `ConcurrentHashMap`, `ConcurrentLinkedQueue`, `AtomicInteger`, `AtomicIntegerFieldUpdater`, `volatile long` | `ConcurrentDictionary`, `ConcurrentQueue`, `int` with `Interlocked`, `Interlocked.CompareExchange`, `Volatile.Read/Write` | C# has no volatile `long`. |
| `java.util.BitSet` | `BitArray` | `BitSet.get` is false past the set's size ([BitSet](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/BitSet.html)) while `BitArray.Get` throws ([BitArray.Get](https://learn.microsoft.com/en-us/dotnet/api/system.collections.bitarray.get?view=net-10.0)); `CookieUtil.FirstInvalidOctet` checks the length first, and `HttpChunkLineValidatingByteProcessor` keeps Java's throw (see [Core byte values](#core-byte-values)). |
| `ByteProcessor` | `Process(byte)` as the Common `Func<byte, bool>` | `HttpChunkLineValidatingByteProcessor`, `Utf8Validator`. |
| `hasArray()`/`array()`/`arrayOffset()`, `ByteBuf.order()` | `MemoryMarshal.TryGetArray` helpers; none | Array extraction of [buffer-porting.md](buffer-porting.md). The Buffer port's buffers are big-endian with explicit BE/LE accessors ([codec-base-porting.md](codec-base-porting.md), Byte order), so no code looks up the order. |
| Enums with constructors or methods (`CookieHeaderNames.SameSite`, `WebSocketVersion`) | sealed classes with static readonly instances | As codec-base `Base64Dialect`; simple Java enums are C# enums ([enum](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/enum)). |
| Name clashes | trailing underscore | A method cannot be declared next to a nested type of the same name ([CS0102](https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0102)): `QueryStringDecoder.builder()` is `Builder_()` (owner rule), and the test helper `WebSocketExtensionDataMatcher_(name)`. |
| Package access | `internal`, `protected internal`, internal `Invoke*` forwarders | Package-private types and members are `internal`; members that the package reaches through a subclass are `protected internal` ([codec-base-porting.md](codec-base-porting.md), Package access). Package-private base classes of public classes are public with internal constructors, because "a derived class cannot have broader accessibility than its base class" ([CS0060](https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs0060)): `AbstractMixedHttpData<D>`, `WebSocketProtocolHandler`. Protected members that the Java tests call through package access have internal forwarders (`HttpObjectEncoder<H>.InvokeEncodeInitialLine`, `InvokeNewHandshakeRequest()`), as codec-base `MessageToMessageEncoder.InvokeEncode`. |
| `switch` fall-through, labeled `break`, `throw new Error(...)` | `goto case`, `goto` a label after the loop, `UnreachableException` | "control can't fall through from one switch section to the next" ([selection statements](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/selection-statements)): `QueryStringDecoder`, `HttpContentEncoder`, `WebSocket08FrameDecoder`. `Error` is `UnreachableException` ([handler-porting.md](handler-porting.md), Exceptions). |
| Exceptions | `IllegalArgumentException` is `ArgumentException`, `NumberFormatException` is `FormatException`, `ClassCastException` is `InvalidCastException`, `checkNotNull`'s `NullPointerException` is `ArgumentNullException`, `OutOfMemoryError` is `OutOfMemoryException` | [codec-base-porting.md](codec-base-porting.md). `new IllegalArgumentException(cause)` keeps Java's detail message `cause.toString()` through Common's `ThrowableUtil.ThrowableToString` (CLR-only, owner decision 2026-11-24), which multipart and spdy use in place of their former copies (see [Shared-API requests](#shared-api-requests)). |
| `Callable<T>`, `ThreadLocalRandom`, `java.util.Random` | `Func<T>`, `Random.Shared`, `System.Random` | `Callable` is the Common mapping of the executor submissions; `Random.Shared` is a "thread-safe Random instance that may be used concurrently from any thread" ([Random.Shared](https://learn.microsoft.com/en-us/dotnet/api/system.random.shared?view=net-10.0)). `nextInt()` over all ints is the low 32 bits of `NextInt64` ([NextInt64](https://learn.microsoft.com/en-us/dotnet/api/system.random.nextint64?view=net-10.0)). Unseeded random data in the tests is unseeded `System.Random` data. |
| `java.io.File`, `RandomAccessFile`/`FileChannel` | `FileInfo`, `FileStream` | [handler-porting.md](handler-porting.md), java.io and NIO; multipart adds helpers for the `File` results its code tests (see [Multipart](#multipart)). |
| `InternalThreadLocalMap.get().stringBuilder()`/`arrayList()` | an operation-owned `StringBuilder`/`List` | Retired from the Common port ([common-clr-design.md](common-clr-design.md), Operation-owned CSV storage). |
| A static member of a generic class | a non-generic static class | `HttpObjectEncoder<H>`'s package-private `CRLF_SHORT` lives on a non-generic static `HttpObjectEncoder`, because a static member of a generic class belongs to each closed type (the codec-base `DefaultHeaders` precedent). |
| `byte` | `System.Byte` | The wire octets are CLR bytes; the places where Java's sign matters keep it with `(sbyte)` and a `// CLR:` comment (see each package's byte values). |
| `Locale.setDefault` (tests) | `CultureInfo.CurrentCulture` | "the culture used by the current thread and task-based asynchronous operations" ([CurrentCulture](https://learn.microsoft.com/en-us/dotnet/api/system.globalization.cultureinfo.currentculture?view=net-10.0)), so `@Isolated` has no counterpart (`HttpVersionLocaleTest`, `RtspMethodsTest`, the multipart locale tests). |
| `@BeforeEach`/`@AfterEach`, Mockito, `CountDownLatch` (tests) | test-class constructor/`Dispose()`, Moq, `CountdownEvent` | xUnit "creates a new instance of the test class for every test that is run" ([shared context](https://xunit.net/docs/shared-context)). Mockito `mock`/`verify(...)`/`times(n)`/`never()`/`verifyNoInteractions` are Moq `Mock<T>`/`Verify(..., Times.Once())`/`Times.Exactly(n)`/`Times.Never()`/`VerifyNoOtherCalls()` ([Moq Quickstart](https://github.com/devlooped/moq/wiki/Quickstart)). `HttpHeaders` is an `IEnumerable`, so xUnit's `Assert.Equal` would compare it element by element; header equality assertions call `Equals` directly, as Java's `assertEquals` calls `equals`. Overloaded test helpers take the suffix `0` ([xUnit1024](https://xunit.net/xunit.analyzers/rules/xUnit1024)). |

### Listener timing

Java's `DefaultPromise` notifies a listener at once when the promise completes on its event loop, and otherwise through
`executor.execute`. The Transport completes its operation Tasks with `RunContinuationsAsynchronously`
(`src/Netty.NET.Transport/Channels/AbstractChannel.cs`), so a continuation on `TaskScheduler.Default` of a write that
completes later runs on a thread-pool thread after the write completed ([handler-porting.md](handler-porting.md),
Listener timing). codec-http uses two policies:

- **Common's `EventExecutorTaskScheduler`** (owner decision above), with `() => ctx.Executor`, where a test observes the
  timing. It runs the continuation at once when the completing thread is the event loop (`EmbeddedEventLoop.InEventLoop`
  is always true) and otherwise hands it to `Execute`:
  - `HttpServerCodec` (close after the response when both `Transfer-Encoding` and `Content-Length` were sent):
    `HttpServerCodecTest.testConnectionClosedAfterResponseWhenBothTransferEncodingAndContentLengthRfc7230` saw the
    channel still open with `TaskScheduler.Default` (measured).
  - `HttpServerKeepAliveHandler` (CLOSE after the last content of a non-persistent exchange): with
    `TaskScheduler.Default`, 43 of the 49 `HttpServerKeepAliveHandlerTest` cases saw the channel still open right after
    `writeAndFlush` (measured).
  - `HttpServerExpectContinueHandler` (CLOSE_ON_FAILURE of the 100/417 response) and `CorsHandler.respond` (CLOSE).
  - `WebSocketProtocolHandler` (the listeners on the close-frame write and on `closeSent`): with
    `TaskScheduler.Default`, `WebSocketServerProtocolHandlerTest` saw the channel still open after `close()` in 5 of 6
    runs (one or two cases per run); with the scheduler, in 0 of 8 runs.
  - `WebSocketServerExtensionHandler.handlePotentialUpgrade` (add the extension codecs and remove the handler when the
    101 response is written): with `TaskScheduler.Default`, 10 to 11 of the 83 extension cases failed in three runs.
  - `SpdySessionHandler` (`ClosingChannelFutureListener` and the write-failure listeners).
- **Synchronous continuations on `TaskScheduler.Default`**, as codec-base `MessageAggregator`, where the Task is already
  complete when the continuation is attached (the flush of `writeAndFlush` completes it inline): "If the antecedent is
  already complete when the continuation is created, the continuation will run on the thread that creates the
  continuation"
  ([TaskContinuationOptions](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.taskcontinuationoptions?view=net-10.0)).
  This covers `HttpObjectAggregator.handleOversizedMessage` (413; the tests check `isOpen()` false right after
  `writeInbound`), `HttpServerUpgradeHandler` (`CLOSE_ON_FAILURE`), `Utf8FrameValidator`, `WebSocket08FrameDecoder` and
  the other WebSocket handshake listeners. `SpdyFrameCodec`'s close-future listener of `handlerAdded` follows handler's
  `ChunkedWriteHandler` policy (at once on the event loop, otherwise `executor.Execute`).

`write(ctx, msg, promise)` with CLOSE added to the promise also closes the channel when `base.WriteAsync` throws
synchronously (Java's pipeline fails the promise, which fires CLOSE).

## Core: io.netty.handler.codec.http

The package itself (70 main and 35 test files) is ported to the project root; its deprecated cookie API is described
with [Cookie and CORS](#cookie-and-cors). Of its 559 declared tests outside `HttpHeaderDateFormatTest` and
`NativeImageHandlerMetadataTest`, 555 are mapped and pass (8383 xUnit cases) and 4 are CLR-inapplicable.

### Headers

- `abstract class HttpHeaders : IEnumerable<IEntry<string, string>>, IEquatable<HttpHeaders>`. `IEquatable<HttpHeaders>`
  routes xUnit/`EqualityComparer<T>` equality to `equals(Object)`, as codec-base `DefaultHeaders` does.
- The `@Deprecated iterator()` names `iteratorCharSequence()` and `iteratorAsString()`, so it is not ported. The final
  `iteratorAsString()`, which only returned `iterator()`, becomes the abstract `IteratorAsString()`; the implementations
  override it with the body of their `iterator()`, and `GetEnumerator()` returns it.
- **Wildcard storage.** `DefaultHttpHeaders` wraps `DefaultHeaders<CharSequence, CharSequence, ?>`, a wildcard that C#
  cannot express. The C# field is `DefaultHeaders<ICharSequence, ICharSequence, HeadersImpl>`, where `HeadersImpl` is a
  public abstract nested class (CLR-only) that the default storage and `CombinedHttpHeaders`' storage derive from,
  because the self-returning `DefaultHeaders` members cast `this` to `T`.
- `copy()` is `new DefaultHttpHeaders(headers.copy())` as in Java. codec-base `DefaultHeaders.Copy()` builds a
  `DefaultHeaders<K, V, T>` of exactly that type, which is not a `HeadersImpl`; its this-returning members return
  `null` instead of throwing `InvalidCastException`, as Java's erased `(T) this` fails only where the result is used
  (codec-base `DefaultHeaders.ThisT`, 2026-11-24). `DefaultHttpHeaders` ignores those results, so the former
  `HeadersImpl.Copy()` override is gone; as in Java, the copy of combined headers is a plain `DefaultHeaders`. Measured:
  a copy of `DefaultHttpHeaders` takes `Add`/`Set` chains, and a copy of combined headers keeps the combined value
  `1,2` and adds a later value separately.
- `CombinedHttpHeaders` has internal constructors (create it through `DefaultHttpHeadersFactory`); `HttpHeaderNames.X`
  and `HttpHeaderValues.X` are `static readonly AsciiString` fields.

### Messages

- `IHttpObject : IDecoderResultProvider`, `IHttpMessage`, `IHttpRequest`, `IHttpResponse`, `IHttpContent : IHttpObject,
  IByteBufHolder`, `ILastHttpContent` (with `EMPTY_LAST_CONTENT`), `IFullHttpMessage`, `IFullHttpRequest`,
  `IFullHttpResponse` and `IHttpHeadersFactory`. Java getters are methods (`ProtocolVersion()`, `Method()`, `Uri()`,
  `Status()`, `Headers()`).
- The classes narrow their members with covariant overrides (`DefaultFullHttpRequest.SetMethod` and `Copy()` return
  `IFullHttpRequest`), which are virtual; the tests cast through interface variables, for example
  `(IFullHttpRequest) request.Replace(buf)`.

### Decoders, encoders and codecs

- `HttpObjectDecoder : ByteToMessageDecoder` and `HttpObjectEncoder<H> : MessageToMessageEncoder<object>` keep their
  protected extension points (`CreateMessage`, `IsContentAlwaysEmpty`, `SplitHeaderName`, `EncodeInitialLine`,
  `SanitizeHeadersBeforeEncode`, ...), which `RtspDecoder`/`RtspEncoder` override. `HttpDecoderConfig` is `ICloneable`.
- `array()`/`arrayOffset()` of the decoder's heap scratch buffer are the array segment of the line's readable memory
  (`MemoryMarshal.TryGetArray`; [buffer-porting.md](buffer-porting.md), Array extraction).
- `HttpServerCodec` and `HttpClientCodec` are sealed `CombinedChannelDuplexHandler`s that implement the upgrade
  handlers' nested `ISourceCodec`. `HttpServerUpgradeHandler` and `HttpClientUpgradeHandler` derive from
  `HttpObjectAggregator` (codec-base `MessageAggregator<IHttpObject, IHttpMessage, IHttpContent, IFullHttpMessage>`) and
  keep their nested `IUpgradeCodec`/`IUpgradeCodecFactory` interfaces and `UpgradeEvent` types.
- `Long.parseLong` is `HttpUtil.ParseLong`: `NumberFormatException` is `FormatException`, also for overflow, and its
  `trim()` is `HttpUtil.TrimJava`. `java.util.Date` is `DateTimeOffset`.

### Query strings

- **QueryStringDecoder.** `new String(bytes, 0, n, charset)` is `Encoding.GetString` (replacement fallback, as the JDK
  decoder replaces malformed input). `URI.getRawPath()`/`getRawQuery()` return the components exactly as written,
  while `Uri.AbsolutePath` normalizes (`http://localhost:8080?a=b` has the path `/`), so a private helper reads the path
  and query of `Uri.OriginalString` with the RFC 3986 generic syntax; an opaque URI has neither, as in Java.
  `Map<String, List<String>>` is `IDictionary<string, IList<string>>` (Common `LinkedHashMap`, `ImmutableDictionary` for
  `emptyMap()`, a one-element `ImmutableList` for `singletonList`). `builder()` is `Builder_()` (CS0102), and the
  private constructor and fields of `Builder` that the decoder uses are `internal`. The labeled `break loop` is a `goto`
  label after the loop; the `;` to `&` fall-through is `goto case`.
- **QueryStringEncoder.** "Is UTF-8" (`CharsetUtil.UTF_8.equals(charset)`, a canonical-name comparison) is a code-page
  comparison (65001). `String.getBytes(charset)` is `Encoding.GetBytes`, which never writes a preamble
  ([GetPreamble](https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.getpreamble?view=net-10.0): it "can
  be prefixed to the sequence of bytes resulting from the encoding process"), while the JDK `UTF-16` charset "writes a
  big-endian byte-order mark" when encoding
  ([Charset](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/charset/Charset.html)). `toUri()` is
  `new Uri(ToString(), UriKind.RelativeOrAbsolute)`. The encoded octets are masked (`& 0xF`) before the hex lookup, so
  the Java sign does not matter.

### Content encoders and decoders

- `HttpContentEncoder`, `HttpContentCompressor` and `HttpContentDecoder` stay public and subclassable with the Java
  member names: `BeginEncode(IHttpResponse, string)` (protected abstract), the public sealed nested `Result`
  (`TargetContentEncoding()`, `ContentEncoder()`), `NewContentDecoder(string)` (protected abstract),
  `GetTargetContentEncoding(string)` and `DetermineEncoding(string)` (protected virtual), the protected `ctx` field of
  `HttpContentDecoder`, and `DEFAULT_MAX_PIPELINE_DEPTH`. `MessageToMessageCodec<HttpRequest, HttpObject>` and
  `MessageToMessageDecoder<HttpObject>` are the codec-base generics over `IHttpRequest`/`IHttpObject`.
- **HttpContentEncoder.** The Accept-Encoding queue is `Queue<string>`; `IDENTITY`, `ZERO_LENGTH_HEAD` and
  `ZERO_LENGTH_CONNECT` are compared by reference (`ReferenceEquals`), as in Java. The `AWAIT_HEADERS` to
  `AWAIT_CONTENT` fall-through is a `goto case` after the `try/finally`.
- **HttpContentDecoder.** The inner `ByteBufForwarder` gets the decoder instance (it writes `needRead`) and overrides
  the `IsSharable` property; `IDENTITY` is `internal`.
- **HttpContentCompressor.**
  - zstd-jni is library-not-ported (owner decision 2026-11-23; the BCL Zstandard types exist only from .NET 11,
    [ZstandardStream](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zstandardstream)), so the
    compressor behaves as Java with `Zstd.isAvailable() == false`: zstd is never a default option, no option selects
    it, and the `zstdOptions` field stays an always-null `ICompressionOptions`.
  - `Brotli.IsAvailable()` is always true (BCL encoder); the `BrotliEncoder` is built from the `BrotliOptions`
    (`BrotliOptions.parameters()` returns a brotli4j type and is not ported).
  - The `@Deprecated` constructors `(int)`, `(int, int, int)`, `(int, int, int, int)`, `determineWrapper(String)` and
    the private `defaultCompressionOptions(...)` name no replacement, so they are `[Obsolete]`. `DetermineWrapper`
    returns `ZlibWrapper?`. A window size or memory level other than 15/8 makes `ZlibCodecFactory.NewZlibEncoder` throw
    `NotSupportedException` (jzlib is not ported).
  - `Float.parseFloat` is `float.Parse(TrimJava(s), NumberStyles.Float, InvariantCulture)`. Java also accepts an
    `f/F/d/D` suffix and hexadecimal floats
    ([Double.valueOf](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Double.html#valueOf(java.lang.String))),
    which `float.Parse` rejects, so such a q-value takes the `NumberFormatException` branch (`q = 0.0f`). Overflow is
    infinity in both ([Single.Parse](https://learn.microsoft.com/en-us/dotnet/api/system.single.parse?view=net-10.0)).
- **HttpContentDecompressor.** gzip/x-gzip, deflate/x-deflate (ZLIB if strict, else ZLIB_OR_NONE), br and snappy are
  decoded; the `Zstd.isAvailable() && ZSTD` branch is left out, so `zstd` passes through undecoded (as Java without
  zstd-jni). The two `@Deprecated` constructors (replacements `(int)` and `(boolean, int)`) are not ported.

### Keep-alive, expect-continue and chunked input

- `HttpServerKeepAliveHandler` and `HttpServerExpectContinueHandler` close through Common's `EventExecutorTaskScheduler`
  (see [Listener timing](#listener-timing)); `promise.unvoid()` has no counterpart (no void promise).
- `HttpChunkedInput` implements the handler port's `IChunkedInput<IHttpContent>`; the `@Deprecated`
  `readChunk(ChannelHandlerContext)` override has no counterpart, because the interface member is not ported
  (`src/Netty.NET.Handler/Stream/IChunkedInput.cs`).
- `FullHttpResponse.retainedDuplicate()` returns `IByteBufHolder` in C# (narrowing re-declarations are inherited), so
  `HttpServerExpectContinueHandler` casts it to `IHttpResponse`.

### Core byte values

| Place | Java | C# |
| --- | --- | --- |
| `HttpObjectDecoder` `SP_LENIENT_BYTES`, `LATIN_WHITESPACE`, `ISO_CONTROL_OR_WHITESPACE` | indexed by the signed byte + 128; the static initializers widen a signed byte to a code point, so 0x80-0xFF are neither whitespace nor control, and the loops stop before 0x7F; exception texts print the signed octet and `Integer.toHexString` of it (`ffffff80`) | lookups and texts read the octet as `sbyte` |
| `HttpUtil.isValidTokenChar(byte)` | `octet < 0` | kept through `sbyte` |
| `HttpChunkLineValidatingByteProcessor` | `BitSet.get(int)` of the sign-extended byte throws for 0x80-0xFF ([BitSet.get](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/BitSet.html)) | the octet is read as `sbyte`, so `BitArray.Get` throws too and an obs-text chunk extension stays invalid |

The other byte uses keep the source code: the OR-ed header-name and method fast paths of `HttpRequestDecoder` give the
same result with unsigned bytes because every constant is pure ASCII (the low four octets are widened as `uint`,
CS0675), `HttpHeaderValidationUtil`'s `array[i] & 0xFF` gives the same values, and `QueryStringEncoder` masks before
the lookup.

### Core not ported

| Unit or member | Status | Reason |
| --- | --- | --- |
| `HttpHeaders.EMPTY_HEADERS`, `Names`, `Values`, 52 static helper methods, `equalsIgnoreCase`, `newEntity`, `iterator()` | deprecated member, comments recorded | The `@deprecated` text names `EmptyHttpHeaders.INSTANCE`, `HttpHeaderNames`, `HttpHeaderValues`, `HttpUtil`/the instance methods, `AsciiString`, `iteratorCharSequence()`/`iteratorAsString()`. |
| `HttpHeaderNames.KEEP_ALIVE`, `PROXY_CONNECTION` | deprecated member | "use CONNECTION" (owner review: the named replacement is a different header); `HttpHeaderValidationUtil` keeps them as private constants. |
| `getDecoderResult()`, `getProtocolVersion()`, `getMethod()`, `getUri()`, `getStatus()` and their overrides | deprecated member | Named replacements `decoderResult()`, `protocolVersion()`, `method()`, `uri()`, `status()`. |
| `validateHeaders`/`singleFieldHeaders` constructors of the messages, `DefaultHttpHeaders(boolean)`, `CombinedHttpHeaders(boolean)`, the parameter constructors of `HttpObjectDecoder`/`HttpRequestDecoder`/`HttpResponseDecoder`/`HttpClientCodec`/`HttpServerCodec` | deprecated member | Named replacements: the `HttpHeadersFactory`/`HttpDecoderConfig` constructors. |
| `EmptyHttpHeaders.instance()`, `InstanceInitializer` | deprecated member | Named replacement `INSTANCE`; the initialization cycle they broke does not exist without `HttpHeaders.EMPTY_HEADERS`. |
| `HttpUtil.getCharsetAsString(HttpMessage)` | deprecated member | Named replacement `getCharsetAsSequence(HttpMessage)`. |
| `HttpContentDecompressor` deprecated constructors, `HttpChunkedInput.readChunk(ChannelHandlerContext)` | deprecated member | Named replacements `(int)`/`(boolean, int)` and `readChunk(ByteBufAllocator)`. |
| `HttpContentCompressorOptionsTest.testGetZstdTargetContentEncoding`, `HttpContentDecoderTest.testResponseZstdDecompression`, `testResponseChunksZstdDecompression` | CLR-inapplicable | zstd only; zstd-jni is library-not-ported. |
| `HttpUtilTest.testFormatHostnameForHttpFromResolvedAddressWithHostname` | CLR-inapplicable | A resolved `InetSocketAddress` keeps the host name it was created from; `IPEndPoint` keeps only the address and `NetUtil.GetHostname` returns the numeric address (`src/Netty.NET.Common/NetUtil.cs`, [handler-porting.md](handler-porting.md) Addresses). |

Ported with `[Obsolete]` because no replacement is named: `HttpHeaders.EncodeAscii`, `HttpObjectEncoder.EncodeAscii`,
the `HttpObjectDecoder.validateHeaders` field, and the `HttpContentCompressor` constructors and `DetermineWrapper`
above.

### Core test notes

| Tests | Methods | Notes |
| --- | --- | --- |
| headers, messages, version, method, status, `HttpUtil` and query strings | 219 (218 mapped, 1 CLR-inapplicable) | Deprecated constructors are replaced by their named replacements: `new DefaultHttpHeaders(false)` is `DefaultHttpHeadersFactory.HeadersFactory().WithValidation(false).NewHeaders()`, `new CombinedHttpHeaders(true)` is `HeadersFactory().WithCombiningHeaders(true).NewHeaders()`, `HttpHeaders.EMPTY_HEADERS` is `EmptyHttpHeaders.INSTANCE`, `iterator()` is `IteratorAsString()`, and `KEEP_ALIVE`/`PROXY_CONNECTION` are local `AsciiString` constants with the same names. |
| `HttpRequestDecoderTest`, `HttpResponseDecoderTest`, `HttpRequestEncoderTest`, `HttpResponseEncoderTest`, `HttpInvalidMessageTest`, `MultipleContentLengthHeadersTest` | 185 (78, 61, 15, 23, 5, 3) | 239 xUnit cases with the theory rows, and the CLR supplement `HttpResponseEncoderTest.TestChunkedContentWriteCompletesWhenFlushed`: the three combined writes of a chunk complete when `WriteAndFlushAsync` returns, as Java's `PromiseCombiner` completes on its executor. It failed on every run while `HttpObjectEncoder` returned `Task.WhenAll` of the writes ([common-task-composition.md](common-task-composition.md), Executor affinity of a combined write). No test depends on the sign of a Java `byte`. |
| `HttpServerCodecTest`, `HttpObjectAggregatorTest`, `HttpClientCodecTest` | 55 (15, 26, 14) | `testServerCloseSocketInputProvidesData` runs over real NIO sockets with `ALLOW_HALF_CLOSURE` and `ShutdownOutputAsync`; the server binds the IPv4 wildcard (as `ServerBootstrap.BindAsync(int)` does) and the client connects to `NetUtil.LOCALHOST4`. The anonymous aggregators are `PostAggregator`/`TextAggregator`. |
| content encoders and decoders (five classes) | 74 (71 mapped, 3 CLR-inapplicable) | 74 xUnit cases with the four `testZipBomb` rows. |
| keep-alive, expect-continue, chunked input and upgrade (five classes) | 26 | 71 xUnit cases with the theory rows. |

- **Exhaustive tests.** `HttpHeaderValidationUtilTest`'s two exhaustive 2^32 tests (`@DisabledForJreRange(max =
  JAVA_17)`, "much too slow on older Java versions") run on the CLR. The old algorithms return the rejection instead of
  throwing a preallocated stack-less exception (a CLR throw "can be orders of magnitude slower", [Exceptions and
  Performance](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/exceptions-and-performance)), and the
  values are walked in 256 `Parallel.For` slices, because the sequential walk did not finish within 10 minutes on this
  host. `HttpResponseStatusTest.testHttpStatusClassValueOf` asserts only for a code that does not map to `UNKNOWN` in
  its two 2^31 loops (same outcome, minutes saved).
- **Behaviour.** `QueryStringEncoderTest.testNonDefaultEncoding` expects `%00%A5` instead of Java's `%FE%FF%00%A5` (no
  BOM from `Encoding.GetBytes`). `CombinedHttpHeadersTest.addCharSequencesCsvNullValue`: Java's `NullPointerException`
  comes from `value.toString()` on null (not a `checkNotNull`), whose CLR counterpart is `NullReferenceException`.
- **Exceptions and futures.** `assertInstanceOf(NumberFormatException.class, ...)` is
  `Assert.IsAssignableFrom<FormatException>`; Java's `NumberFormatException` extends `IllegalArgumentException`, but no
  ported test asserts `IllegalArgumentException` for such a cause. `Future.get()` wraps a write failure in
  `ExecutionException`; `Task.GetAwaiter().GetResult()` throws the faulted task's exception itself ("The task completed
  in a Faulted state",
  [TaskAwaiter.GetResult](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.taskawaiter.getresult?view=net-10.0)),
  so `e.getCause()` is the thrown `EncoderException`. `assertInstanceOf`/`isInstanceOf`/`isNotInstanceOf` are
  `Assert.IsAssignableFrom`/`IsNotAssignableFrom`
  ([TypeAsserts.cs](https://raw.githubusercontent.com/xunit/assert.xunit/main/TypeAsserts.cs)), `assertThrows(T.class)`
  is `Assert.ThrowsAny<T>`
  ([ExceptionAsserts.cs](https://raw.githubusercontent.com/xunit/assert.xunit/main/ExceptionAsserts.cs)) and
  `assertDoesNotThrow` is `Assert.Null(Record.Exception(...))`.
- **Byte order.** `HttpRequestEncoderTest.getBuffers()` builds little-endian buffers with the deprecated
  `ByteBuf.order(ByteOrder)`, which the Buffer port does not have; the helper returns the two big-endian buffers, and
  the encoder writes with `writeShortBE`/`writeMediumBE`, which give the same bytes for either order in Java.
- **Package access.** `HttpRequestEncoderTest` reaches the protected `encodeInitialLine(...)` through the internal
  forwarder `HttpObjectEncoder<H>.InvokeEncodeInitialLine`. `HttpResponseDecoderTest` overloads two test methods with
  private helpers of the same name, which take the suffix `0` (xUnit1024).
- **Content codecs.** `StandardCompressionOptions.zstd()` is dropped from the option lists of the brotli/snappy option
  tests (expectations unchanged), and `HttpContentDecompressorTest.encodings()` leaves out `zstd`, as Java does without
  zstd-jni. brotli4j's `DirectDecompress` is `BrotliDecoder.Decompress`
  ([docs](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotlidecoder.decompress?view=net-10.0)),
  and `BrotliOutputStream` is `BrotliStream(Stream, CompressionMode.Compress)`
  ([docs](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.brotlistream.-ctor?view=net-10.0)) over a
  `MemoryStream`. The brotli conditions (`@EnabledIf("isBrotiAvailable")`, `@DisabledIf`, `assumeTrue`) always let the
  tests run; `@DisabledForSlowLeakDetection` always runs. The gzip byte-exact expectations of
  `HttpContentCompressorTest` and the brotli compressed length match the codec-compression encoders unchanged.
  `testExecutorPreserveOrdering` runs over the Transport local channel; the deprecated `addLast(EventExecutorGroup,
  ChannelHandler...)` is `AddLast(group, null, h)`. `HttpContentDecompressorTest.testZipBomb` compresses 256 MiB per
  encoding.
- **Keep-alive and upgrade.** `testConnectionCloseHeaderHandledCorrectlyForVoidPromise`: `channel.voidPromise()` has no
  counterpart; the test writes with the Task discarded and still checks that the channel is closed.
  `HttpClientUpgradeHandlerTest.testSuccessfulUpgrade`: `ctx.writeAndFlush(msg, promise)` is the Task of
  `ctx.WriteAndFlushAsync(msg)`. `HttpServerUpgradeHandlerTest.upgradesPipelineInSameMethodInvocation`: the deferred
  `ctx.write(msg, promise)` completes a `TaskCompletionSource` from the deferred write's Task (`TrySetFromTask`), and
  `EmbeddedChannel.FlushOutbound()` runs the deferred task first, as in Java. `HttpChunkedInputTest` reuses the
  temporary-file and `deleteOnExit` mapping of handler's `ChunkedWriteHandlerTest` (`AppDomain.ProcessExit`).
- **Other.** `OutOfMemoryError` thrown by the tests' allocator and header iterator is `OutOfMemoryException`, which
  `HttpObjectEncoder` wraps in `EncoderException` as Java wraps the `Throwable`. `FileRegion` is `IFileRegion`. Unseeded
  `SplittableRandom` (`DefaultHttpRequestTest.validUris`) is an unseeded `System.Random` (sequence not reproduced;
  `DisableDiscoveryEnumeration`). xUnit1031/xUnit1051 are disabled where the source blocks on a future or its
  `close()`/`writeAndFlush` observes no test cancellation, with that reason.

## Cookie and CORS

The packages `io.netty.handler.codec.http.cookie` (10 main, 4 test files) and `io.netty.handler.codec.http.cors` (3
main, 2 test files) are ported to `Cookie/` and `Cors/`. Of the deprecated cookie API of `io.netty.handler.codec.http`
(7 main files, 1 test file), `ClientCookieEncoder` is ported with `[Obsolete]` at the project root, and the other 6
units and `HttpHeaderDateFormatTest` are `deprecated-not-ported`. All 98 declared tests of the six ported test classes
pass (98 xUnit cases).

- **Cookie types.** `Cookie` is `ICookie : IComparable<ICookie>`. `CookieHeaderNames` is a static class of `const
  string`s, and its enum `SameSite` has the static `of(String)`, so it is a sealed class with the static readonly
  instances `Lax`, `Strict` and `None` (`Name()`, `ToString()`, internal `Of(string)`). The members of the non-final
  `DefaultCookie` are virtual.
- **Case folding.** `equalsIgnoreCase`/`compareToIgnoreCase` fold every char with
  `Character.toLowerCase(Character.toUpperCase(c))`
  ([String](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/String.html)). `DefaultCookie` keeps
  that fold with `char.ToUpperInvariant`/`ToLowerInvariant` in a private `CompareToIgnoreCase`, because
  `StringComparison.OrdinalIgnoreCase` only uppercases
  ([StringComparison](https://learn.microsoft.com/en-us/dotnet/api/system.stringcomparison?view=net-10.0)). Measured:
  `string.Compare("_", "a", OrdinalIgnoreCase)` is positive (30), while Java's `"_".compareToIgnoreCase("a")` is
  negative (-2). `ToUpperInvariant('ı')` (dotless i) is unchanged, where `Character.toUpperCase` gives `'I'`; cookie
  domains are ASCII.
- **Region matches.** `String.regionMatches([true, ]offset, name, 0, len)` is `string.Compare(header, offset, name, 0,
  len, OrdinalIgnoreCase|Ordinal) == 0`. The number of characters compared is the lesser of the substring lengths and
  `len` ([String.Compare](https://learn.microsoft.com/en-us/dotnet/api/system.string.compare?view=net-10.0)), and a
  shorter region compares unequal. Measured: `Compare("a$Pa", 1, "$Path", 0, 5, Ordinal)` is -2 and
  `Compare("$Ver", 0, "$Version", 0, 8, OrdinalIgnoreCase)` is -4, so a region at the end of the header does not match,
  as in Java. `String.compareTo` is `string.CompareOrdinal`, so the server decoder's sorted set orders
  `"ARPT" < "__utma" < "kw-..."` and `"Session_id" < "session_id"` as the Java tests expect.
- **Scratch storage.** `CookieUtil.StringBuilder()` returns a new builder (the retired `InternalThreadLocalMap`);
  callers name it qualified, because a `using static` import would be ambiguous with the `System.Text.StringBuilder`
  type.
- **Dates.** `ServerCookieEncoder` computes `Expires` with `DateTimeOffset.FromUnixTimeMilliseconds`, whose range ends
  at -62,135,596,800,000 and 253,402,300,799,999 ms
  ([FromUnixTimeMilliseconds](https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset.fromunixtimemilliseconds?view=net-10.0)),
  so it clamps the instant to that range (a Max-Age above about 7,975 years), where Java prints a year above 9999; the
  Max-Age attribute is unchanged. Java's `Long.parseLong` throws `NumberFormatException` for a null string too
  ([Long](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Long.html)), while `long.Parse` throws
  `ArgumentNullException`
  ([Int64.Parse](https://learn.microsoft.com/en-us/dotnet/api/system.int64.parse?view=net-10.0)); the client decoder
  ignores both, so a valueless `Max-Age` keeps a session cookie.
- **Collections.** A `List<DefaultCookie>` binds to `IReadOnlyCollection<ICookie>` as the Java wildcard allows; an
  `ISet<T>` takes the Iterable overload, which encodes the same text. `CorsConfig.origin()` returns the first origin of
  the Common `LinkedHashSet`, whose `Contains(null)` throws, since it is backed by a `Dictionary`
  ([ContainsKey](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2.containskey?view=net-10.0)),
  so `CorsHandler.getForOrigin` tests a null origin first. `Arrays.sort(T[], Comparator)` "is guaranteed to be stable"
  ([Arrays](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Arrays.html)), while `Array.Sort`
  "performs an unstable sort"
  ([Array.Sort](https://learn.microsoft.com/en-us/dotnet/api/system.array.sort?view=net-10.0)), so the strict
  `ClientCookieEncoder` sorts with `Enumerable.OrderBy`, which "performs a stable sort"
  ([OrderBy](https://learn.microsoft.com/en-us/dotnet/api/system.linq.enumerable.orderby?view=net-10.0)), to keep the
  creation order that the Java comment relies on.
- **CORS.** `CorsConfigBuilder` stores `Dictionary<ICharSequence, Func<object>>` (`Callable` is `Func`). `Func`'s
  covariance applies only to reference types
  ([variance](https://learn.microsoft.com/en-us/dotnet/standard/generics/covariance-and-contravariance)), so a `Func<T>`
  of a value type (the date generator's `Func<DateTimeOffset>`) is wrapped in a boxing lambda.
  `preflightResponseHeader(CharSequence, Iterable<T>)` takes `IEnumerable<object>`, because a generic `IEnumerable<T>`
  would also bind a string (an `IEnumerable<char>`). `CorsConfig.toString()` keeps the Java text: `true`/`false`,
  [AbstractCollection](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/AbstractCollection.html)
  `[a, b]` and [AbstractMap](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/AbstractMap.html)
  `{k=v}`; a `Func` value prints its delegate type name, where Java prints the Callable's class name and hash.
  `CorsHandler.respond` closes through Common's `EventExecutorTaskScheduler` (see [Listener timing](#listener-timing)).
- **Byte values.** The cookie and CORS code has no byte arithmetic, and no test depends on the sign of a Java `byte`.

### Cookie and CORS deprecated units and members

| Unit or member | Handling | Reason |
| --- | --- | --- |
| `http.Cookie`, `http.DefaultCookie`, `http.CookieDecoder`, `http.ServerCookieEncoder` | `deprecated-not-ported` | `@deprecated` names `cookie.Cookie`, `cookie.DefaultCookie`, `cookie.ClientCookieDecoder`/`ServerCookieDecoder`, `cookie.ServerCookieEncoder`. No original test uses them. |
| `http.CookieUtil` | `deprecated-not-ported` | "Duplicate of package private `cookie.CookieUtil`"; its only user is `http.CookieDecoder`. |
| `http.HttpHeaderDateFormat` and `HttpHeaderDateFormatTest` (2 tests) | `deprecated-not-ported` | "Use `DateFormatter` instead"; codec-base `DateFormatterTest` parses and formats the same dates. |
| `http.ClientCookieEncoder` (class and 4 methods) | ported with `[Obsolete]` | `@Deprecated` without `@deprecated` text. Its parameters of the not-ported `http.Cookie` are the named replacement `Cookie.ICookie`. |
| `cookie.DefaultCookie.validateValue(String, String)` | ported with `[Obsolete("CookieUtil is package private, will be removed once old Cookie API is dropped")]` | No replacement named. |
| `CorsConfig.isShortCurcuit()`, `withAnyOrigin()`, `withOrigin(String)`, `withOrigins(String...)`, nested `Builder` | not ported (comments recorded) | Replacements `isShortCircuit()` and `CorsConfigBuilder`. |
| `CorsConfig.DateValueGenerator` | ported with `[Obsolete("Removed without alternatives.")]` | No replacement named. |

### Cookie and CORS test notes

| Java test | Declared | Passed |
| --- | --- | --- |
| `cookie/ClientCookieDecoderTest` | 17 | 17 |
| `cookie/ClientCookieEncoderTest` | 4 | 4 |
| `cookie/ServerCookieDecoderTest` | 8 | 8 |
| `cookie/ServerCookieEncoderTest` | 7 | 7 |
| `cors/CorsConfigTest` | 16 | 16 |
| `cors/CorsHandlerTest` | 46 | 46 |

- `Iterator.next()`/`hasNext()` is `Assert.True(it.MoveNext())` followed by `it.Current`, and
  `Assert.False(it.MoveNext())`.
- `testDecodingLongDates`: the Java `Calendar` keeps the current millisecond, the C# `DateTimeOffset` has 0 ms; the
  test's 2-second tolerance covers that.
- The String header names that the CORS tests pass as `CharSequence` are `StringCharSequence` values; the anonymous
  `Callable<String>` is a lambda, and the anonymous `SimpleChannelInboundHandler` is the nested `VaryHandler`.

## Multipart

The package `io.netty.handler.codec.http.multipart` (26 main and 14 test files) is ported to `Multipart/`. All 121
declared tests pass (185 xUnit cases, about 0.9 s on this host); none is CLR-inapplicable.

- **Type shapes.** The interfaces are `IInterfaceHttpData`, `IHttpData`, `IAttribute`, `IFileUpload`, `IHttpDataFactory`
  and `IInterfaceHttpPostRequestDecoder`; inside the namespace `IAttribute` binds to the multipart interface, not to the
  Common `IAttribute<T>`. `AbstractHttpData` declares abstract the `IHttpData` members that the Java abstract class
  leaves to its subclasses (a C# class must map every interface member). `AbstractMixedHttpData<D>` (package-private in
  Java, `D : class`) is public with internal constructor, fields and abstract `MakeDiskData()`, because the public
  `MixedAttribute`/`MixedFileUpload` derive from it (CS0060). `getBaseDirectory()`/`deleteOnExit()` of
  `AbstractDiskHttpData` and the `protected` enum `MultiPartStatus` and `getMultipartDataBoundary(String)` of
  `HttpPostRequestDecoder` are `protected internal`. `offer(HttpContent)` returns the decoder class, and
  `IInterfaceHttpPostRequestDecoder.Offer` is implemented explicitly, because an interface member cannot be implemented
  with a covariant return.
- **Files.** `File.getPath()` is `FullName`. The `File` results that the code tests are CLR-only helpers of
  `AbstractDiskHttpData`, also used by the memory and mixed classes, the encoder and the tests:
  - `FileLength`: `length()` is "0L if the file does not exist"
    ([File](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/File.html)), while `FileInfo.Length`
    throws `FileNotFoundException` and caches until `Refresh()`
    ([Length](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileinfo.length?view=net-10.0),
    [Refresh](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesysteminfo.refresh?view=net-10.0)).
  - `FileExists`, `CreateNewFile` (`FileMode.CreateNew`, false for an existing file), `DeleteFile` (false on failure;
    `FileInfo.Delete` does nothing for a missing file,
    [Delete](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileinfo.delete?view=net-10.0)) and `RenameFile`
    (`File.Move`, false where it throws; `MoveTo` "does not overwrite the destination file",
    [MoveTo](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileinfo.moveto?view=net-10.0)).
  - `OpenFile`: `RandomAccessFile` modes `"r"`/`"rw"` are `FileMode.Open`/`OpenOrCreate` (`"rw"` creates and does not
    truncate,
    [RandomAccessFile](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/io/RandomAccessFile.html)),
    with the JDK's Windows sharing `FILE_SHARE_READ | FILE_SHARE_WRITE`
    ([io_util_md.c](https://github.com/openjdk/jdk17u/blob/master/src/java.base/windows/native/libjava/io_util_md.c)),
    which is `FileShare.ReadWrite`
    ([FileShare](https://learn.microsoft.com/en-us/dotnet/api/system.io.fileshare?view=net-10.0)), and without a
    `FileStream` buffer ("0 or 1 means that buffering should be disabled",
    [BufferSize](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestreamoptions.buffersize?view=net-10.0)). A
    `FileChannel` has no user-space buffer, so `get()` of a `DiskAttribute` whose channel is still open reads what
    `addContent` wrote (measured: with buffering disabled a second handle sees the written length).
  - `force(false)` is `Flush(true)`, which "clears all intermediate file buffers"
    ([Flush](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=net-10.0)).
    `getBytes(index, FileChannel, position, length)` is `ByteBuf.GetBytes(int, Stream, int)` at the stream position; the
    Buffer port's `SafeFileHandle` overload is not used, because reading `FileStream.SafeFileHandle` "automatically
    flushes the stream and sets the current stream position to 0"
    ([SafeFileHandle](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.safefilehandle?view=net-10.0)).
    `transferTo` is a private positional copy helper.
  - `PlatformDependent.createTempFile` is the Common `CreateTempFile(string, string, DirectoryInfo)`
    ([common-clr-design.md](common-clr-design.md), Native temporary-file creation). The temporary-file postfix keeps
    Java's `String.hashCode()` value, as `string.GetHashCode()` "is not guaranteed to be stable"
    ([GetHashCode](https://learn.microsoft.com/en-us/dotnet/api/system.string.gethashcode?view=net-10.0)).
  - `DeleteFileOnExitHook`: `Runtime.addShutdownHook` is an `AppDomain.ProcessExit` handler
    ([ProcessExit](https://learn.microsoft.com/en-us/dotnet/api/system.appdomain.processexit?view=net-10.0)), as
    pkitesting's `X509Bundle`; `ConcurrentHashMap.newKeySet()` is a `ConcurrentDictionary<string, bool>`.
- **Charsets.** `Charset.name()` is `HttpPostBodyUtil.CharsetName`: the canonical java.nio names (UTF-8, US-ASCII,
  ISO-8859-1, UTF-16BE, UTF-16LE) by code page, else `WebName`, the lower-case IANA name
  ([WebName](https://learn.microsoft.com/en-us/dotnet/api/system.text.encoding.webname?view=net-10.0)), so the multipart
  headers keep `charset=UTF-8`. `Charset.forName` is Common's `CharsetUtil.ForName` (moved from multipart's
  `HttpPostBodyUtil.ForName` on 2026-11-24): the six standard charsets and their java.base aliases
  ([Charset](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/charset/Charset.html), [Supported
  encodings](https://docs.oracle.com/en/java/javase/17/intl/supported-encodings.html)), looked up case-insensitively,
  Java's legal-name rule, else `Encoding.GetEncoding`. Measured on .NET 10: `Encoding.GetEncoding` rejects 19 of the 47
  Java names and aliases (`UTF8`, `ISO8859_1`, `UTF_16BE`, ...) with `ArgumentException` and maps `UTF-16`, `unicode`
  and `ISO-10646-UCS-2` to little-endian UTF-16, while Java accepts them (a test sends `charset=UTF8`). Java's `UTF-16`
  maps to the big-endian encoding without byte-order-mark detection. Because `ForName`'s `ArgumentException` stands
  for both Java exceptions, the multipart decoder also wraps the one Java lets through: an unsupported legal name in
  the constructor, an illegal name in the field/file-upload steps.
- **Exceptions.** `catch (IllegalArgumentException)` and `catch (NullPointerException)` keep the Java types with
  `HttpPostBodyUtil.IsIllegalArgument`/`IsNullPointer`, which leave out the BCL's `ArgumentOutOfRangeException` (the
  Buffer port's `IndexOutOfBoundsException`, [codec-dns-porting.md](codec-dns-porting.md)) and count
  `ArgumentNullException` as `NullPointerException`. `new IllegalArgumentException(e)` and the cause-only
  `ErrorDataEncoderException(e)` keep Java's detail message through Common's `ThrowableUtil.ThrowableToString`.
- **Text.** `URLEncoder.encode` is a private `UrlEncode` that follows the URLEncoder rules
  ([URLEncoder](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/net/URLEncoder.html)). Measured:
  `WebUtility.UrlEncode("a b*~!()'")` is `a+b*%7E!()%27`, so it keeps `!`, `(` and `)`, which Java encodes. The
  multipart boundary `Long.toHexString(nextLong())` is the `"x"` text of the `ulong` of
  `Random.Shared.NextInt64(long.MinValue, long.MaxValue)`. `CaseIgnoringComparator` implements
  `IComparer<ICharSequence>` and `IComparer<string>`; the decoders' `TreeMap`s are `SortedDictionary` instances keyed by
  the string text.
- **Collections.** `Collections.synchronizedMap(new IdentityHashMap<>())` of `DefaultHttpDataFactory` is a `Dictionary`
  with `ReferenceEqualityComparer.Instance`
  ([ReferenceEqualityComparer](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.referenceequalitycomparer?view=net-10.0))
  under a lock; `cleanAllHttpData` traverses a snapshot under the lock.
- **Byte values.** No conversion is needed: every byte comparison of the decoders is with an ASCII constant (`CR`, `LF`,
  `-`, `%`, `+`), `findDelimiter` compares two unsigned bytes, `skipControlCharacters` already masks with `0xFF`, and
  `UrlDecoder`'s `(char) hiByte`/`(char) value` give -1 from `StringUtil.DecodeHexNibble` both for Java's sign-extended
  chars and for the C# chars 0x80-0xFF.

### Multipart not ported

| Unit or member | Status | Reason |
| --- | --- | --- |
| `HttpDataFactory.cleanRequestHttpDatas(HttpRequest)`, `cleanAllHttpDatas()` and the `DefaultHttpDataFactory` overrides | deprecated member, comments recorded | The `@deprecated` texts name `cleanRequestHttpData(HttpRequest)` and `cleanAllHttpData()`. |
| `HttpPostRequestEncoder.readChunk(ChannelHandlerContext)` | deprecated member (no Javadoc) | The interface member it overrides is not ported (`src/Netty.NET.Handler/Stream/IChunkedInput.cs`). |
| `getMethod()`/`getUri()`/`getProtocolVersion()`/`getDecoderResult()` of `HttpPostRequestEncoder.WrappedHttpRequest` | deprecated member (no Javadoc) | Not ported in core's `IHttpRequest`/`IHttpObject`. |
| `CaseIgnoringComparator`: `Serializable`, `serialVersionUID`, `readResolve()` | JVM serialization | No CLR counterpart; the private constructor keeps the single `INSTANCE`. |

### Multipart test notes

| Java test | Methods | Notes |
| --- | --- | --- |
| `AbstractDiskHttpDataTest`, `AbstractMemoryHttpDataTest` | 1, 3 | The test subclasses override the `protected internal` members as `protected internal` (friend assembly); `getHttpDataType()` returns `default`, the C# enum cannot be null. `SecureRandom` is `RandomNumberGenerator`; `buffer.array()` is `ByteBufUtil.GetBytes(buffer, 0, n)`. |
| `DefaultHttpDataFactoryTest` | 3 | `@BeforeAll`/`@BeforeEach` run from the constructor, `@AfterEach` is `Dispose()`. |
| `DeleteFileOnExitHookTest` | 2 | `@Isolated`: `runHook()` deletes every registered file, so the class is in a collection with `DisableParallelization = true`, "with parallelism turned off" ([xUnit](https://xunit.net/docs/running-tests-in-parallel)). |
| `DiskFileUploadTest`, `MemoryFileUploadTest`, `MixedTest` | 17, 6, 3 | AssertJ `assertThatThrownBy(...).isInstanceOf(T).hasMessageContaining(s)` is `Assert.ThrowsAny<T>` plus `Assert.Contains`; `@ValueSource` rows are `InlineData`. |
| `HttpDataTest` | 7 | The meta-annotation `@ParameterizedHttpDataTest` is `[Theory, MemberData(nameof(Data))]` on each method; the rows are not serializable, so xUnit enumerates them when the theory runs and every theory gets new instances, as `data()` is called per JUnit method. |
| `HttpPostMultipartLocaleDecoderTest`, `HttpPostMultipartLocaleEncoderTest` | 1, 1 | `Locale.setDefault` is `CultureInfo.CurrentCulture`. |
| `HttpPostStandardRequestDecoderTest`, `HttpPostMultiPartRequestDecoderTest` | 11, 16 | The 10 MB chunk sequences are kept (about 0.3 s per disk test). |
| `HttpPostRequestDecoderTest` | 39 | `UnsupportedCharsetException` causes are `ArgumentException` (see Charsets); `URLEncoder.encode` of the test file names is `WebUtility.UrlEncode` ([UrlEncode](https://learn.microsoft.com/en-us/dotnet/api/system.net.webutility.urlencode?view=net-10.0)), equal for those names; `String.getBytes()` is `Encoding.Default`. |
| `HttpPostRequestEncoderTest` | 11 | The resources come from `ResourcesUtil.GetFile` (deployed next to the test assembly); the expected bodies compare with `StringUtil.NEWLINE`, as the CRLF checkout of `file-0N.txt` here. |

## WebSocket

The packages `io.netty.handler.codec.http.websocketx` (51 main, 24 test files), `.websocketx.extensions` (13 main, 6
test files including the `WebSocketExtensionTestUtil` helper) and `.websocketx.extensions.compression` (12 main, 9 test
files) are ported to `WebSocketx/`, `WebSocketx/Extensions/` and `WebSocketx/Extensions/Compression/`. All 181 declared
tests pass (262 xUnit cases: inherited fixture tests run per derived class, and one `@ValueSource` has two rows); none
is CLR-inapplicable.

### Frames and frame codecs

- **Frames.** `WebSocketFrame` derives from the Buffer port's `DefaultByteBufHolder`; the Java overrides that narrow the
  holder's return types are covariant overrides, and the abstract `replace(ByteBuf)` is an abstract override, as
  codec-smtp's `DefaultSmtpContent`. `content()` is the `Content` property ([buffer-porting.md](buffer-porting.md),
  Buffer holders and conversion). `CloseWebSocketFrame` writes its reason with `ByteBufUtil.WriteUtf8`; numbers in
  messages use the invariant culture and booleans in `toString()` print `true`/`false`.
- **WebSocketVersion** is a sealed class with static readonly instances; `ToString()` returns the constant name.
- **WebSocketUtil.** `MessageDigest` is `IncrementalHash`, which "Provides support for computing a hash or HMAC value
  incrementally across several segments"
  ([IncrementalHash](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.incrementalhash?view=net-10.0)):
  `getInstance("MD5"/"SHA1")` is `CreateHash(HashAlgorithmName.MD5/SHA1)`, `update` is `AppendData`, `digest()` (which
  resets,
  [MessageDigest](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/security/MessageDigest.html)) is
  `GetHashAndReset`, and `reset()` is a dropped `GetHashAndReset()`; `NoSuchAlgorithmException` is the
  `CryptographicException` of
  [CreateHash](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.incrementalhash.createhash?view=net-10.0).
  The per-thread digests stay in the Common `FastThreadLocal`. `Base64.getEncoder().encodeToString` (Basic encoder,
  [Base64](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/Base64.html)) is
  `Convert.ToBase64String` (same alphabet and `=` padding,
  [ToBase64String](https://learn.microsoft.com/en-us/dotnet/api/system.convert.tobase64string?view=net-10.0)). The mask
  generator's `nextInt()` is the low 32 bits of `Random.Shared.NextInt64(0, 1L << 32)`, so every int is possible.
- **WebSocket08FrameDecoder/Encoder.** The state machine's fall-through is `goto case`. The Buffer port has no
  `ByteBuf.order()`, so the masking code of `unmask` and `encode` has no order lookup and no LITTLE_ENDIAN mask reversal
  (CLR comments mark the places); the 8-byte and 4-byte XOR fast paths are kept.
- **WebSocket00FrameDecoder** is `ReplayingDecoder<object>` (`ReplayingDecoder<Void>`). Its static import
  `ByteBufUtil.readBytes(alloc, buffer, length)` is `using static ByteBufUtil`: since 2026-11-24 the Buffer port's
  `ByteBufUtil.ReadBytes` reads through `buffer.ReadBytes(dst)`, which `ReplayingDecoderByteBuf` supports, when the
  allocator returns exactly `length` writable bytes (every Netty.NET allocator), as Java does
  (`src/Netty.NET.Buffer/ByteBufUtil.Allocation.cs`). The private `ReadBytes` copy of the Java body is gone; before,
  `ByteBufUtil.ReadBytes` called `ReadBytes(dst, length)`, which the replaying buffer rejects as Java's does, and the
  two `WebSocketClientHandshaker00Test` cases that decode a 00 frame failed with "not a replayable operation".
- `Utf8FrameValidator` and `WebSocket08FrameDecoder` close with synchronous continuations (see
  [Listener timing](#listener-timing)).

### Handshakers, configurations and protocol handlers

- **URI.** `WebSocketClientHandshaker` reads scheme, host, port, raw path and raw query from `Uri.OriginalString` with a
  private `UriComponents` helper (generic syntax `[scheme:][//authority][path][?query][#fragment]`); `toString()` is
  `OriginalString`. `getHost()` returns the host "or null if the host is undefined" (an IPv6 literal with its
  brackets, the case as written) and `getPort()` "or -1 if the port is undefined". `URI.create` throws
  `IllegalArgumentException` where `new Uri` throws `UriFormatException`. The helper does not reject a host that
  `java.net.URI` would treat as registry-based (for example one with `_`), where Java's `getHost()` is null.
- **Promises.** The promise overloads (`handshake(Channel, FullHttpRequest, HttpHeaders, ChannelPromise)`,
  `close(..., promise)`, `processHandshake(..., promise)`, `sendUnsupportedVersionResponse(Channel, ChannelPromise)`)
  take the `TaskCompletionSource` that the method completes and return its Task. `channel.newPromise()` is a new
  `TaskCompletionSource` without `RunContinuationsAsynchronously`, so a listener added with `ExecuteSynchronously` runs
  when the promise completes, as `DefaultPromise` notifies at once on the event loop.
- **Package access and accessibility.** The protected members that the Java tests call through package access
  (`newHandshakeRequest()`, `newHandshakeResponse(...)`, `newWebsocketDecoder()`, `newWebSocketEncoder()`,
  `isForceCloseComplete()`) have internal forwarders (`InvokeNewHandshakeRequest()`, ...). The package-private abstract
  `WebSocketProtocolHandler` is public because the public protocol handlers derive from it (CS0060); its constructors
  stay internal. The package-private handshake handlers are internal classes.
- **Text.** A null header prints `null` as in Java string concatenation and `String.format("%s")`; the configurations'
  `toString()` keep Java's `true`/`false` and invariant-culture numbers. `cause.getMessage().getBytes()` (the platform
  default charset, which "typically depends upon the locale and charset of the underlying operating system",
  [Charset](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/charset/Charset.html)) is
  `Encoding.UTF8.GetBytes` in `WebSocketServerProtocolHandler.exceptionCaught`.
- **Hixie-76 (version 00).** `ByteBuffer.allocate(4).putInt(n).array()` is `BinaryPrimitives.WriteInt32BigEndian` (a new
  buffer is big-endian,
  [ByteBuffer](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/nio/ByteBuffer.html);
  [WriteInt32BigEndian](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.binary.binaryprimitives.writeint32bigendian?view=net-10.0)).
  `ThreadLocalRandom.nextInt(0x7e)` is `Random.Shared.Next(0x7e)` (exclusive bound,
  [Random.Next](https://learn.microsoft.com/en-us/dotnet/api/system.random.next?view=net-10.0)), and
  `Pattern.matcher(s).replaceAll("")` is `Regex.Replace(s, "")`
  ([Regex.Replace](https://learn.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.regex.replace?view=net-10.0)).
  The server keeps the 16-byte challenge array in a local, because the Buffer port has no `array()`.
- **Other.** `volatile long forceCloseTimeoutMillis` uses `Volatile.Read/Write`, the `AtomicIntegerFieldUpdater` is
  `Interlocked.CompareExchange`, and scheduled timeouts are `ScheduleAsync` with a `CancellationTokenSource`
  ([handler-porting.md](handler-porting.md), Executors and scheduling). `WebSocketProtocolHandler` notifies through
  Common's `EventExecutorTaskScheduler` (see [Listener timing](#listener-timing)).

### Extensions and compression

- **Interfaces.** `IWebSocketExtension`, `IWebSocketClientExtension`, `IWebSocketServerExtension`,
  `IWebSocketClientExtensionHandshaker`, `IWebSocketServerExtensionHandshaker`, `IWebSocketExtensionFilter` and
  `IWebSocketExtensionFilterProvider`. Their constants are interface constants (`IWebSocketExtension.RSV1/RSV2/RSV3`) or
  static readonly fields (`IWebSocketExtensionFilter.NEVER_SKIP/ALWAYS_SKIP`,
  `IWebSocketExtensionFilterProvider.DEFAULT`); the anonymous classes behind them are private nested classes.
  Implementing classes and derived interfaces do not expose those static members by their own names, so code and tests
  write `IWebSocketExtension.RSV1` where Java writes `RSV1` or `WebSocketServerExtension.RSV1`. `newReponseData()` keeps
  its Java spelling (`NewReponseData()`).
- **Parameters.** `Map<String, String>` is `IDictionary<string, string>`; `WebSocketExtensionData.parameters()` is a
  `ReadOnlyDictionary` (mutators throw `NotSupportedException`). The handshakers' `HashMap` is `Dictionary`, so the
  order of the parameters a handshaker writes into `Sec-WebSocket-Extensions` may differ from Java's (RFC 7692
  parameters are order-independent; no test depends on it).
- **WebSocketServerExtensionHandler.** `handlePotentialUpgrade` adds two listeners to the caller's promise before the
  101 response is written. A `TaskCompletionSource` stands for that promise, the `base.WriteAsync` Task completes it
  with `TrySetFromTask`, and the listeners run on Common's `EventExecutorTaskScheduler`, so the pipeline is changed
  before `writeOutbound` returns, as in Java. The protected `onHttpResponseWrite(ctx, response, promise)` keeps its
  name, takes the caller's `CancellationToken` and returns the Task (as handler's
  `AbstractTrafficShapingHandler.SubmitWrite`).
- **zlib.** `DeflateDecoder`/`DeflateEncoder` run codec-compression's `JdkZlibDecoder`/`JdkZlibEncoder` (raw `NONE`
  wrapper, BCL `DeflateStream` through the internal stand-ins, whole-buffer feeding,
  [codec-compression-porting.md](codec-compression-porting.md)) in an inner `EmbeddedChannel`.
  `ZlibCodecFactory.newZlibEncoder(NONE, level, windowSize, memLevel)` needs jzlib for a window size other than 15 or
  a memory level other than 8; jzlib is library-not-ported (owner decision 2026-11-23), so such an encoder throws
  `NotSupportedException` at the first compressed frame (a JVM without jzlib fails with `NoClassDefFoundError`), and
  `ZlibCodecFactory.isSupportingWindowSizeAndMemLevel()` is false, so the default handshakers never negotiate a smaller
  local window, as Java without jzlib. The handshakers still accept and negotiate smaller windows when configured
  explicitly; compressing with them is the open limitation.
- **Integer.parseInt** is the internal `PerMessageDeflateServerExtensionHandshaker.ParseInt`: `int.TryParse` with
  `NumberStyles.AllowLeadingSign` and the invariant culture, and `FormatException` for null, non-digits and overflow
  with OpenJDK 17's messages
  ([Integer](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/lang/Integer.html),
  [Int32.TryParse](https://learn.microsoft.com/en-us/dotnet/api/system.int32.tryparse?view=net-10.0),
  [Integer.java](https://raw.githubusercontent.com/openjdk/jdk17u/jdk-17+35/src/java.base/share/classes/java/lang/Integer.java),
  [NumberFormatException.java](https://raw.githubusercontent.com/openjdk/jdk17u/jdk-17+35/src/java.base/share/classes/java/lang/NumberFormatException.java)).
  Java also accepts non-ASCII Unicode digits, the BCL does not.
- **Inner class.** `PerMessageDeflateClientExtensionHandshaker.PermessageDeflateExtension` is a Java inner class that
  reads `compressionLevel` of its enclosing instance
  ([JLS 8.1.3](https://docs.oracle.com/javase/specs/jls/se17/html/jls-8.html#jls-8.1.3)); the C# nested class gets the
  handshaker as its first constructor argument.

### WebSocket byte values

No spot depends on the sign of a Java `byte`:

- `Utf8Validator`: `TYPES[b & 0xFF]` is `TYPES[b]`, and the `& 0x3f` and `(0xff >> type) & b` masks clear the
  sign-extension bits.
- `WebSocket08FrameDecoder` masks the header octets; the close status is read with `GetShort`, signed as in Java, so a
  status of 0x8000-0xFFFF is negative and accepted by `isValidStatusCode` as in Java. `WebSocket08FrameEncoder` XORs and
  writes the low eight bits.
- `WebSocket00FrameDecoder`'s `type == (byte) 0xFF` is `type == 0xFF`, and `WebSocket00FrameEncoder` writes `(byte)
  0xFF` and `(byte) 0x80` as ints. `DeflateDecoder.FRAME_TAIL`'s `(byte) 0xff` octets are the unsigned 0xFF of a C# byte
  (same bits, only written and compared byte for byte).
- The tests' signed literals are `unchecked((byte) ...)` or `sbyte[]` converted with `unchecked((byte) v)`.

### WebSocket not ported

| Unit or member | Status | Reason |
| --- | --- | --- |
| `WebSocketChunkedInput.readChunk(ChannelHandlerContext)` | deprecated member, comment recorded | `@deprecated Use {@link #readChunk(ByteBufAllocator)}`; the interface member it overrides is not ported, as for core's `HttpChunkedInput`. |
| `WebSocketServerProtocolHandler.ServerHandshakeStateEvent.HANDSHAKE_COMPLETE` | deprecated member, comment recorded | `@deprecated in favor of {@link HandshakeComplete} class`. The handshake handler fires only the `HandshakeComplete` event (owner review). |
| `WebSocketServerHandshakerFactory.sendUnsupportedWebSocketVersionResponse(Channel)` | deprecated member, comment recorded | `@deprecated use {@link #sendUnsupportedVersionResponse(Channel)}`. |
| `DeflateFrameServerExtensionHandshaker()`, `(int)`, `(int, WebSocketExtensionFilterProvider)`, `(int, WebSocketExtensionFilterProvider, int)` | deprecated member, comments recorded | The `@deprecated` text names `(int, int)` / `(int, int, int, WebSocketExtensionFilterProvider, int)`. |
| `DeflateFrameClientExtensionHandshaker(boolean)`, `(int, boolean)`, `(int, boolean, WebSocketExtensionFilterProvider)` | deprecated member | Named replacements `(boolean, int)`, `(int, boolean, int)`, `(int, boolean, WebSocketExtensionFilterProvider, int)`. |
| `PerMessageDeflateServerExtensionHandshaker()`, `(int, boolean, int, boolean, boolean)`, `(..., WebSocketExtensionFilterProvider)`, `(..., WebSocketExtensionFilterProvider, int)` | deprecated member | Named replacements with `maxAllocation` / server window and memory level. |
| `PerMessageDeflateClientExtensionHandshaker()`, `(int, boolean, int, boolean, boolean)`, `(..., WebSocketExtensionFilterProvider)` | deprecated member | Named replacements with `maxAllocation`. |
| `WebSocketClientCompressionHandler.INSTANCE` (and the private no-argument constructor that only it used) | deprecated member | Named replacement `WebSocketClientCompressionHandler(int)`. |
| `WebSocketServerCompressionHandler()` | deprecated member | Named replacement `WebSocketServerCompressionHandler(int)`. |

`WebSocketServerHandshaker.uri()` is `@Deprecated` without `@deprecated` text (no named replacement) and is ported with
`[Obsolete]`; it is still the Location value of the 00 handshake (CS0612 disabled there with that reason).

### WebSocket test notes

| Tests | Methods | C# cases |
| --- | --- | --- |
| frames and frame codecs (9 classes) | 32 | 32 |
| handshakers, configurations and protocol handlers (14 classes and the `WebSocketRequestBuilder` helper) | 67 | 147 (inherited fixture tests run per derived class) |
| extensions (5 classes and the `WebSocketExtensionTestUtil` helper) | 19 | 19 |
| compression (9 classes) | 63 | 64 (one `@ValueSource` with two rows) |

- `WebSocket08EncoderDecoderTest`: `binTestData.array()[i] = j` is `SetByte(i, j)`; the byte counter wraps at 256 as the
  Java byte wraps at 128 (same bit patterns). `WebSocketCloseStatusTest`: AssertJ `isGreaterThan`/`isLessThan` are
  `CompareTo` checks and `TreeSet` is `SortedSet<int>`. `WebSocketUtf8FrameValidatorTest`: the third `assertThrows`
  argument (a failure message) has no xUnit counterpart.
- `WebSocketServerProtocolHandlerTest.testSimultaneousCloseDoesNotLeakOriginalPromise`: Java's `peerEchoWrite` is the
  promise that the protocol handler passed down the pipeline. The Transport passes no promise down, so the stalled
  handler's `TaskCompletionSource` is not the cascaded promise; the C# test keeps every other assertion, and
  `assertTrue(peerEchoWrite.isDone())` has no counterpart (recorded in `perMethod`).
- `WebSocketHandshakeHandOverTest.testHandover`/`testClientHandshakeTimeout` react to the `HandshakeComplete` event (the
  named replacement) instead of `ServerHandshakeStateEvent.HANDSHAKE_COMPLETE`.
- `@Timeout` tests run their body with `Task.Run` under `[Fact(Timeout = ...)]`, which applies to async tests only. The
  three server-handshaker tests that `WebSocketServerHandshaker00Test` overrides with `// ignore` call a protected
  virtual body with the suffix `0`, because the manifest checker requires the `[Fact]` declaration as `public void`.
  `WebSocketRequestBuilder`'s CharSequence parameters are `IEnumerable<char>`, so both strings and `AsciiString` bind.
- `WebSocketServerExtensionHandlerTest.testExtensionHandlerNotRemovedByFailureWritePromise`: there is no ChannelPromise
  to pass to `writeOneOutbound(msg, promise)` and fail later; a test-only `FailingWritePromiseHandler` before the
  extension handler forwards the response to the outbound buffer and returns the Task of a `TaskCompletionSource` that
  the test fails with the same `IOException`. The handler stays, the response is unflushed and `finish()` finds it, as
  in Java.
- Moq's `Match.Create` in the helper `WebSocketExtensionDataMatcher_(name)` stands for `argThat(new
  WebSocketExtensionDataMatcher(name))`. `PerMessageDeflateDecoderTest.testFragmentedFrameWithLeftOverInLastFragment`
  depends on the exact compressed bytes of a fixed hex payload (last slice one byte with the value 1); the BCL deflater
  gives such a slice on this host and the test runs unchanged.
  `PerMessageDeflateClientExtensionHandshakerTest.testNormalData` expects 0 parameters
  (`isSupportingWindowSizeAndMemLevel()` is false).
- The handshaker and extension tests passed in eight and five consecutive runs.

## RTSP

The package `io.netty.handler.codec.rtsp` is ported to `Rtsp/`: 7 of its 14 main files (without `package-info.java`);
the other 7 are `@Deprecated` units whose `@deprecated` text names the replacement. All 13 declared tests of its 3 test
files pass.

- `RtspDecoder`/`RtspEncoder` subclass core's `HttpObjectDecoder`/`HttpObjectEncoder<H>`.
  `Pattern.matcher(...).matches()` is a `Regex` wrapped in `\A(?:...)\z`, with Java's `\d` ("A digit: [0-9]") written
  `[0-9]`, because the .NET `\d` accepts any Unicode decimal digit
  ([Pattern](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/regex/Pattern.html), [character
  classes](https://learn.microsoft.com/en-us/dotnet/standard/base-types/character-classes-in-regular-expressions)).
  `toUpperCase(Locale.US)` is `ToUpperInvariant`. The RTSP code has no byte arithmetic of its own.
- `RtspMethodsTest`: `Locale.setDefault` is `CultureInfo.CurrentCulture` (per thread), so `@Isolated` has no
  counterpart.

| Unit or member | Status | Reason |
| --- | --- | --- |
| `RtspHeaders` | deprecated unit | "@deprecated Use {@link RtspHeaderNames} or {@link RtspHeaderValues} instead." |
| `RtspObjectDecoder`, `RtspRequestDecoder`, `RtspResponseDecoder` | deprecated unit | "Use {@link RtspDecoder} instead" / "directly instead". |
| `RtspObjectEncoder`, `RtspRequestEncoder`, `RtspResponseEncoder` | deprecated unit | "Use {@link RtspEncoder} instead" / "directly instead". |
| `RtspDecoder(int, int, int, boolean)` | deprecated member | Named replacements `RtspDecoder(HttpDecoderConfig)` and `RtspDecoder(int, int, int)`. |

## SPDY

The package `io.netty.handler.codec.spdy` is ported to `Spdy/`: 47 of its 48 main files (without
`package-info.java`); `SpdyHeaderBlockJZlibEncoder` needs jzlib and is `library-not-ported`. All 113 declared tests of
its 9 test files (one helper without tests) pass. The testsuite's `SocketSpdyEchoTest` echoes every frame type through
`SpdyFrameCodec` over real sockets ([transport-porting.md](transport-porting.md), Testsuite inventory).

- **Types.** `ISpdyFrame`, `ISpdyStreamFrame`, `ISpdyDataFrame`, `ISpdyHeadersFrame`, `ISpdySynStreamFrame`,
  `ISpdySynReplyFrame`, `ISpdyRstStreamFrame`, `ISpdySettingsFrame`, `ISpdyPingFrame`, `ISpdyGoAwayFrame`,
  `ISpdyWindowUpdateFrame`, `ISpdyUnknownFrame`, `ISpdyHeaders` and `ISpdyFrameDecoderDelegate`. `DefaultSpdyDataFrame`
  implements the `IByteBufHolder`/`IReferenceCounted` members explicitly, as core `DefaultHttpContent` does. The
  interface constants of `SpdySettingsFrame` are `const` members, `SpdyHeaders.HttpNames` is a static class nested in
  the interface, and the default method `readUnknownFrame` is a default interface member. SPDY headers are
  `IHeaders<ICharSequence, ICharSequence, ISpdyHeaders>` (as codec-stomp `IStompHeaders`).
- **Promises and listeners (`SpdySessionHandler`).** The handler stores, completes and listens to its write promises, so
  each `ChannelPromise` is a `TaskCompletionSource` (`RunContinuationsAsynchronously`) whose Task `WriteAsync`/
  `CloseAsync` return. `ctx.write(msg, promise)`, `ctx.writeAndFlush(msg, promise)` and `ctx.close(promise)` complete it
  from the context operation's Task with `TrySetFromTask`, and the listeners run on Common's
  `EventExecutorTaskScheduler`. `SpdySession.PendingWrite` holds the `TaskCompletionSource`. A scratch probe confirmed
  that a stalled data frame is sent and its Task completes after a `WINDOW_UPDATE`, and that `close()` with an active
  stream sends GOAWAY and closes the channel once the last stream closes.
- **SpdyFrameCodec** is a `ByteToMessageDecoder` and `IChannelOutboundHandler` with the `*Async` members, as core
  `HttpClientUpgradeHandler`.
- **zlib header blocks.** `SpdyHeaderBlockZlibEncoder`/`SpdyHeaderBlockZlibDecoder` use codec-compression's internal
  `Deflater`/`Inflater` stand-ins with `SetDictionary(SPDY_DICT)` (owner decision above). The decoder creates
  `new Inflater(false, true)` (exactRemaining): every SPDY stream sets `FDICT`, after which the stand-in reads one byte
  per `Read` anyway, and exactRemaining also keeps `getRemaining() != 0` (bytes after the end of a zlib stream) an
  invalid header block for a stream without `FDICT`. `hasArray()/array()/arrayOffset()` are the private
  `TryGetArray`/`ArrayOf` helpers of codec-compression's `JdkZlibDecoder`. CPython's level-1 stream with `SPDY_DICT`
  starts with `78 3f e3 c6 a7 c2`, the `zlibHeader` of `SpdyHeaderBlockZlibDecoderTest`, and the stand-ins interoperate
  with CPython's zlib (22 decoded and 165 encoded dictionary streams, see
  [codec-compression-porting.md](codec-compression-porting.md), Test notes). A scratch probe sent five rounds of every
  frame type (SYN_STREAM with multi-value and empty headers, DATA, SETTINGS, PING, WINDOW_UPDATE, RST_STREAM, HEADERS,
  GOAWAY) and an HTTP request through `SpdyHttpCodec` from one `SpdyFrameCodec` to another; all decoded unchanged over
  the shared compression context.
- **Accessibility.** Java `protected` constructors that the package uses (`SpdyHttpDecoder`) are `protected internal`.
  `SpdyHeaderBlockEncoder.encode` is package-private but overridden as public in `SpdyHeaderBlockRawEncoder`; an
  override cannot change accessibility, so `Encode` is public abstract in the base (the base still cannot be subclassed
  outside the assembly, because `End()` stays internal).
- **Collections.** The settings and the priority-ordered active streams are `SortedDictionary` instances (the
  `StreamComparator` is an `IComparer<int>`); `ids()` returns a `SortedSet<int>` snapshot where `TreeMap.keySet()` is a
  live view ([TreeMap](https://docs.oracle.com/en/java/javase/17/docs/api/java.base/java/util/TreeMap.html)). The
  `ArrayDeque<Integer>` of `SpdyHttpResponseStreamIdHandler` is a `LinkedList<int>` (`remove(Object)` removes the first
  occurrence; ArrayDeque's null rejection is `ArgumentNullException`).

### SPDY byte values

The `byte` members (`priority`, `flags`) are CLR bytes, the wire octets. Sign-dependent spots keep the Java result:

| Place | Java | C# |
| --- | --- | --- |
| `DefaultSpdySynStreamFrame.setPriority` | `priority < 0 \|\| priority > 7` on the signed byte, signed value in the exception text | the `(sbyte)` view |
| `DefaultSpdyUnknownFrame.hashCode`/`toString` | adds and prints the sign-extended byte | `(sbyte) flags` |
| `SpdyHttpEncoder` | narrows `getInt(PRIORITY, 0)` to a byte | unchecked cast (same low eight bits) |

The frame decoder and `SpdyCodecUtil` mask every octet (`& 0xFF`, `& 0x7F`, `& 0x80`, `>> 5 & 0x07`) and compare flags
as bit patterns, so the unsigned `GetByte` gives the Java results without a conversion.

### SPDY not ported

| Unit or member | Status | Reason |
| --- | --- | --- |
| `SpdyHeaderBlockJZlibEncoder` | `library-not-ported` (jzlib, `com.jcraft:jzlib:1.1.3`, optional; `com.jcraft.jzlib`) | jzlib has no BCL counterpart (owner decision 2026-11-23); Netty never creates it (`SpdyHeaderBlockEncoder.newInstance` returns the zlib encoder), and the dictionary works through the Deflater stand-in. |
| `SpdyHttpDecoder(SpdyVersion, int, boolean)`, `SpdyHttpDecoder(SpdyVersion, int, Map, boolean)` | deprecated member | Named replacement `SpdyHttpDecoder(SpdyVersion, int, Map, HttpHeadersFactory, HttpHeadersFactory)`. |

Ported with `[Obsolete]` (no `@deprecated` text): `SpdyHttpCodec(SpdyVersion, int, boolean)`, which builds the decoder
through the replacement constructor with `withValidation(validateHttpHeaders)` factories.

### SPDY test notes

- Mockito `verify(delegate).x(...)` (exactly once) is `Verify(..., Times.Once())`, and `anyString()` is
  `It.IsNotNull<string>()`; ByteBuf arguments match by readable content in both.
- `SpdyHeaderBlockZlibDecoderTest.deflate` (`java.util.zip.Deflater(BEST_COMPRESSION)`) is a `ZLibStream` with
  `ZLibCompressionOptions.CompressionLevel = 9`
  ([page](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zlibcompressionoptions.compressionlevel?view=net-10.0)).
  The dictionary tests (`testHeaderBlock`, `testHeaderBlockMultipleDecodes` one byte per decode,
  `testHeaderBlockInvalidDictionary`, `testHeaderBlockExtraData`, ...) pass unchanged on the stand-in.
- Header values that Java compares as `String.equals(CharSequence)` are read with `GetAsString`/`GetAllAsString`;
  `assertEquals(n, size())` stays `Assert.Equal` (xUnit2013 disabled with a reason), and `SpdySessionHandlerTest`
  disables xUnit1051 (the source's writeAndFlush/close observe no test cancellation).
- No test depends on a measured behaviour difference from Java.

## Not ported: JVM-only units

| Unit | Status | Reason |
| --- | --- | --- |
| 9 `package-info.java` | `jvm-only-replaced` | C# documentation comments must precede a type or member and cannot be added to a namespace; each summary is kept verbatim in its manifest entry and mapped in the namespace table above (owner decision 2026-11-16). |
| `module-info.yml` | `jvm-only-replaced` | JPMS module descriptor of `io.netty.codec.http` (it also requires brotli4j and jzlib); `Netty.NET.Handler.Codec.Http.csproj` references codec-compression and handler, and `InternalsVisibleTo` opens internals only to the test assembly. |
| `META-INF/native-image/.../reflect-config.json` | `jvm-only-replaced` | GraalVM reflection metadata of the codec-http handlers; NativeAOT keeps handler metadata through the `DynamicallyAccessedMembers` annotations on `IChannelHandler`/`ChannelHandlerAdapter`. |
| `META-INF/native-image/.../native-image.properties` | `jvm-only-replaced` | `--initialize-at-run-time` for `HttpObjectEncoder`, `WebSocket00FrameEncoder` and `DeflateDecoder`, whose static state holds pooled or direct buffers ([build configuration](https://www.graalvm.org/latest/reference-manual/native-image/overview/BuildConfiguration/), [class initialization](https://www.graalvm.org/latest/reference-manual/native-image/optimizations-and-performance/ClassInitialization/)); NativeAOT describes no build-time class initialization option ([Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)), and C# static constructors run before first use at run time ([static constructors](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/static-constructors)). |
| `NativeImageHandlerMetadataTest` (1 test) | `jvm-only-replaced` | Generates the handler reflection records and compares them with the committed JSON (owner decision 2026-11-16, as for the Transport `ChannelHandlerMetadataUtil`). |
| `junit-platform.properties` | `jvm-only-replaced` | Concurrent JUnit execution of classes and methods; xUnit v3 runs a collection per test class in parallel and the tests of one class sequentially ([xUnit](https://xunit.net/docs/running-tests-in-parallel)); nothing in the tests depends on that concurrency. |

The deprecated units are listed with [Cookie and CORS](#cookie-and-cors) and [RTSP](#rtsp), the library-not-ported unit
with [SPDY](#spdy).

## Owner review items

### Deprecated rule cases

- **`HttpHeaderNames.KEEP_ALIVE`/`PROXY_CONNECTION`** are not ported, because their `@deprecated` text "use CONNECTION"
  counts as a named replacement, although it names a different header; `HttpHeaderValidationUtil` keeps them as private
  constants and the tests as local `AsciiString` constants.
- **`WebSocketServerProtocolHandler.ServerHandshakeStateEvent.HANDSHAKE_COMPLETE`** is not ported (its `@deprecated`
  text names the `HandshakeComplete` class), so the server handshake handler fires only the `HandshakeComplete` event,
  where Java fires both. Code that listens for the enum constant sees only the class event.
- **Ported with `[Obsolete]`** because no replacement is named: `http.ClientCookieEncoder` (no `@deprecated` text; its
  `Cookie` parameters are the replacement type `Cookie.ICookie`, since `http.Cookie` is not ported),
  `DefaultCookie.validateValue`, `CorsConfig.DateValueGenerator`, `HttpHeaders.EncodeAscii`,
  `HttpObjectEncoder.EncodeAscii`, `HttpObjectDecoder.validateHeaders`, the `HttpContentCompressor` constructors and
  `DetermineWrapper`, `WebSocketServerHandshaker.uri()` (still the Location value of the 00 handshake) and
  `SpdyHttpCodec(SpdyVersion, int, boolean)`.
- **Deprecated overrides of members that are not ported** (`readChunk(ChannelHandlerContext)` of `HttpChunkedInput`,
  `WebSocketChunkedInput` and `HttpPostRequestEncoder`, the deprecated getters of `WrappedHttpRequest`) have no member
  to override and are not ported. `HttpHeaders.iterator()` folds into the abstract `IteratorAsString()` plus
  `GetEnumerator()`.
- `http.CookieUtil` counts its "Duplicate of package private `cookie.CookieUtil`" as the named replacement, and
  `HttpHeaderDateFormatTest` is not ported with its unit; codec-base `DateFormatterTest` covers the same dates.

### Behaviour differences

- **Charsets.** `QueryStringEncoder` writes no byte-order mark for UTF-16 (`%00%A5` where Java writes `%FE%FF%00%A5`).
  Java's `UTF-16` decodes without byte-order-mark detection (`CharsetUtil.ForName`). The multipart decoder wraps both
  Java charset exceptions in `ErrorDataDecoderException`. (`HttpUtil.GetCharset` now returns UTF-8 for a Java alias
  such as `UTF8`, as Java does, through Common's `CharsetUtil.ForName`; see [Shared-API requests](#shared-api-requests).)
- **Header copies.** The `DefaultHeaders` behind a copied `DefaultHttpHeaders` (`Unwrap()` of a copy) returns `null`
  from its this-returning members where Java returns the object, which Java code could only reach through the `?`
  type of `unwrap()`; no code in the port uses those results.
- **Text writes.** US-ASCII writes use Java's `c2b` mapping (`ByteBufUtil.WriteAscii`) instead of `Encoding.ASCII`,
  which codec-base `AsciiHeadersEncoder` uses. `Unpooled.CopiedBuffer(text, Encoding.UTF8)` (WebSocket text frames)
  writes an unpaired surrogate as EF BF BD where Java writes `?`.
- **Parsing.** A q-value with an `f/F/d/D` suffix or in hexadecimal is 0 in `HttpContentCompressor` (Java parses it).
  `ParseInt` of the permessage-deflate parameters rejects non-ASCII Unicode digits, which Java accepts.
- **zstd** is never selected or decoded by the content codecs (zstd-jni is library-not-ported), and 3 zstd tests are
  CLR-inapplicable. `DeflateEncoder` cannot compress with a window other than 15 or a memory level other than 8 (jzlib
  is library-not-ported); such an encoder throws `NotSupportedException` at the first frame, while the handshakers still
  negotiate such windows when configured explicitly (the defaults never do).
- **Addresses and URIs.** `HttpUtil.formatHostnameForHttp` of a resolved, named address returns the numeric address
  (`HttpUtilTest.testFormatHostnameForHttpFromResolvedAddressWithHostname` is CLR-inapplicable). The WebSocket client's
  `UriComponents` helper does not reproduce Java's registry-based authority rule (a host with `_` is null in Java).
- **Cookies.** `ServerCookieEncoder` clamps an `Expires` instant beyond year 9999 (`DateTimeOffset` range), where Java
  prints a five-digit year. `DefaultCookie` implements Java's `compareToIgnoreCase`/`equalsIgnoreCase` fold, so the
  domain order matches Java for `_` and the other chars between `Z` and `a`; U+0131 still folds differently.
- **CORS.** `CorsConfigBuilder.ExposeHeaders()`/`AllowedRequestHeaders()` called without arguments are ambiguous in C#
  (`params string[]` against `params ICharSequence[]`), where Java picks `String...`. The `Dictionary` of
  `CorsConfigBuilder` throws for a null header name when it is configured, where Java fails later when the preflight
  headers are built. `CorsConfig.toString()` prints a `Func` value's delegate type name.
- **Order.** The WebSocket handshakers write extension parameters from a `Dictionary` (Java `HashMap`); neither defines
  an order, so the parameter order of `Sec-WebSocket-Extensions` may differ from Java's.
- **SPDY.** `DefaultSpdySettingsFrame.Ids()` returns a sorted snapshot, not a live key view. The shared stackless
  `SpdyProtocolException` with a null message (`PROTOCOL_EXCEPTION`) has the CLR default message where Java's
  `getMessage()` is null. A data frame queued by flow control is written later without the caller's
  `CancellationToken`.
- **Tests.** `WebSocketServerProtocolHandlerTest.testSimultaneousCloseDoesNotLeakOriginalPromise` keeps every assertion
  except `assertTrue(peerEchoWrite.isDone())`: the Transport passes no promise down the pipeline, so the cascaded
  promise cannot be observed.

### API shape

- `DefaultHttpHeaders.HeadersImpl` (public nested, CLR-only) stands for the wildcard storage type; since 2026-11-24
  it inherits `DefaultHeaders.Copy()` (see [Shared-API requests](#shared-api-requests)).
- Public where Java is package-private, with internal constructors: `AbstractMixedHttpData<D>` and
  `WebSocketProtocolHandler` (CS0060). `protected internal`: `GetBaseDirectory()`/`DeleteOnExit()`, `MultiPartStatus`
  and `GetMultipartDataBoundary` of multipart, the `SpdyHttpDecoder` constructors. `SpdyHeaderBlockEncoder.Encode` is
  public abstract (Java: package-private) so that `SpdyHeaderBlockRawEncoder.Encode` stays public.
- CLR-only multipart helpers: `HttpPostBodyUtil.CharsetName` (Java canonical charset names),
  `IsIllegalArgument`/`IsNullPointer`, `HasArray` (`ForName` and `ThrowableToString` moved to Common's `CharsetUtil` and
  `ThrowableUtil` on 2026-11-24); `AbstractDiskHttpData.FileLength`/`FileExists`/
  `CreateNewFile`/`DeleteFile`/`RenameFile`/`OpenFile`. `OpenFile` disables the `FileStream` buffer to keep
  `FileChannel` visibility, so every disk write is a system call, as in Java.
- `SpdyHeaderBlockZlibDecoder` uses `new Inflater(false, true)` (exactRemaining) for an exact `getRemaining()` on every
  stream, at the stand-in's one-byte-per-Read speed, the speed every `FDICT` stream already has (see
  [codec-compression-porting.md](codec-compression-porting.md), Owner review items).
- `HttpObjectAggregator` and `HttpServerUpgradeHandler` keep synchronous continuations on `TaskScheduler.Default`, as
  codec-base `MessageAggregator` does, because their writes are complete when the continuation is attached.

## Shared-API requests

Done on 2026-11-24 (owner decision; the owner chose the public names):

1. **codec-base `DefaultHeaders.Copy()`** built a `DefaultHeaders<K, V, T>` of exactly that type, so a following
   this-returning call threw `InvalidCastException` (Java's cast is erased). `DefaultHeaders.ThisT()` is now
   `this is T t ? t : default`: an ignored result works and a used one fails (`NullReferenceException` in place of
   Java's `ClassCastException`), as Java's unchecked cast does. The `DefaultHttpHeaders.HeadersImpl.Copy()` override
   is removed (`copy()` is `new DefaultHttpHeaders(headers.copy())` as in Java), and codec-base's
   `DefaultHeadersImpl.Copy()` no longer has the latent problem.
2. **Buffer `ByteBufUtil.ReadBytes(alloc, buffer, length)`** keeps its exact-length guarantee and calls
   `buffer.ReadBytes(dst)`, Java's call, which `ReplayingDecoderByteBuf` supports, when the new buffer has exactly
   `length` writable bytes; otherwise it calls `ReadBytes(dst, length)`. Measured: 27216 allocations of the Unpooled,
   Pooled, Adaptive and PreferHeap allocators (heap and direct, 1134 lengths from 0 to 16 MiB + 1) all have
   `WritableBytes == length`. `WebSocket00FrameDecoder`'s private copy and `StompSubframeDecoder`'s `ReadBytes(int)`
   replacement are gone; both call `ByteBufUtil.ReadBytes` as Java does.
3. **Java charset names.** Common's `CharsetUtil.ForName(string)` (CLR-only, the counterpart of `Charset.forName`)
   resolves the six standard charsets and their aliases as Java does, applies Java's legal-name rule and otherwise calls
   `Encoding.GetEncoding`; `HttpUtil.GetCharset` and the multipart decoder use it, so `charset=UTF8` gives UTF-8 in
   `GetCharset` too. `HttpPostBodyUtil.CharsetName` (`Charset.name()`) stays in multipart.
4. **A public `Throwable.toString()` text helper.** Common's `ThrowableUtil.ThrowableToString(Exception)` replaces
   codec-base's internal `CodecException.ThrowableToString`, multipart's `HttpPostBodyUtil.ThrowableToString` and the
   inline copies in spdy (`SpdyProtocolException`) and handler (`SslCompletionEvent`, `SniCompletionEvent`,
   `NotSslRecordException`, `SslContext`, `LazyX509Certificate`, `LazyJavaxX509Certificate`, `LoggingHandler`).

The port still works around this one without changing shared code:

5. **`CompositeByteBuf.AddComponent(ByteBuf, bool)`** takes its arguments in the other order than Java's
   `addComponent(boolean, ByteBuf)`, so `AbstractMemoryHttpData`, `DeflateDecoder`, `DeflateEncoder` and
   `HttpContentDecoderTest` write `AddComponent(buf, true)`.

Done on 2026-11-24 (owner decision): the executor `TaskScheduler` that the groups requested (a public scheduler instead
of the private copies of handler's internal `IdleStateHandler.ExecutorTaskScheduler`) is Common's
`EventExecutorTaskScheduler` (see [Owner decisions](#owner-decisions-2026-11-24)).
