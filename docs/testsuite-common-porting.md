# testsuite-common porting

Netty.NET ports `netty-testsuite-common` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) to the test-support library `test/Netty.NET.Testsuite.Common`, with its
original tests in `test/Netty.NET.Testsuite.Common.Tests`. The [manifest](testsuite-common-porting-manifest.json)
records the 5 pinned files of `testsuite-common/src` with their C# counterparts and CLR adaptations. Check it with:

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-CodecPortingManifest.ps1 -Module testsuite-common
```

| Java | C# | Status |
| --- | --- | --- |
| `io.netty.util.test.LeakPresenceExtension` | `Netty.NET.Common.Test.LeakPresenceExtension` + `LeakPresenceTestFramework` | `clr-adapted` |
| `io.netty.util.test.DisabledForSlowLeakDetection` | `Netty.NET.Common.Test.DisabledForSlowLeakDetection` | `clr-adapted` |
| `LeakPresenceExtensionTest` (8 declared tests) | `LeakPresenceExtensionTest` (8 mapped + 6 CLR supplements) | `clr-adapted` |
| `package-info.java` | this document (namespace table) | `jvm-only-replaced` |
| `META-INF/services/org.junit.jupiter.api.extension.Extension` | `LeakPresenceExtension.props` | `jvm-only-replaced` |

`io.netty.util` maps to `Netty.NET.Common`, so the namespace is `Netty.NET.Common.Test` (owner decision 2026-11-27).

## How the extension runs

In Java the extension is autodetected for every test class through the JUnit service file and
`-Djunit.jupiter.extensions.autodetection.enabled=true` (`argLine.leak` of the parent `pom.xml`); the `leak` profile
sets `-Dio.netty.test.leakPresenceDetection.disabled=true` instead. xUnit v3 has no extension callbacks, so:

- A test project imports `test/Netty.NET.Testsuite.Common/LeakPresenceExtension.props`, which references the library and
  adds `[assembly: TestFramework(typeof(LeakPresenceTestFramework))]` (the counterpart of the service file).
- `LeakPresenceTestFramework` (CLR-only) replaces xUnit's collection runner. Around each test class it calls
  `LeakPresenceExtension.BeforeAll` (a new `ResourceScope` named after the class, held in an `AsyncLocal` that the
  `ExecutionContext` carries into tests, Tasks, thread-pool work and started threads, as the `InheritableThreadLocal`
  does in Java) and `AfterAll` (waits up to 5 s for open resources, then closes the scope).
- A leak fails the test collection (`[Test Collection Cleanup Failure (...)]`, exit code 1) with the detector message and
  the creation stack traces of the open resources. It is reported when the collection finishes, not per class: an
  error added to the aggregator while a collection still runs fails every later class of that collection (measured
  2026-11-27: one leak in `ByteBufLeakListenerTest` failed 333 tests of the same collection).
- Setting the environment variable `io.netty.test.leakPresenceDetection.disabled=true` runs the tests with the default
  detector, as the Java `leak` profile does.

`dotnet test` prints the leak message only with `--logger "console;verbosity=normal"`; the default summary shows only
the failed exit code.

## Enabled test projects

The Java modules whose tests depend on `netty-testsuite-common` are buffer, codec-compression, codec-dns,
codec-haproxy, codec-http, codec-http2, codec-http3, codec-marshalling, codec-memcache, codec-mqtt, codec-protobuf,
codec-redis, codec-smtp, codec-socks, codec-stomp, codec-xml, handler, handler-proxy, handler-ssl-ocsp, resolver,
resolver-dns and transport. The extension is enabled in 19 test projects and they pass with it (2026-11-27):

Handler.Codec.{Compression, Dns, HAProxy, Http, Http2, Http3, Memcache, Mqtt, Protobuf, Redis, Smtp, Socks, Stomp, Xml},
Handler, Handler.Proxy, Handler.Ssl.Ocsp, Resolver and Resolver.Dns.

codec-marshalling has no test project (its units are `library-not-ported`). Two projects are not enabled yet
(owner decision 2026-11-27, open items):

- **Buffer.Tests**: `AllocatorLeakDetectionTest` and `ByteBufLeakListenerTest` (CLR supplements of the
  `AllocatorLeakDetectionCollection`) test the default detector's reports and fail under the presence detector; the
  rest of the suite needs no change. They pass with `io.netty.test.leakPresenceDetection.disabled=true`. Enabling
  needs them in a project without the extension.
- **Transport.Tests**: most classes are ports of the Java `testsuite` and `testsuite-native` modules, which do not
  depend on `netty-testsuite-common`. Under the extension their shared event loop groups run tasks of later classes on
  threads that inherited the scope of the class that created them (`AllocationProhibitedException: Resource scope
  'DatagramUnicastInetSourceTest' already closed`), and the run stopped making progress. Enabling needs the
  testsuite-derived classes separated from the transport module's own tests.

## Leaks found and fixed

- **QUIC endpoint** (`QuicEndpointChannel`, codec-classes-quic): an event loop that shut down did not close the
  endpoint, so `Http3FrameToHttpObjectCodecTest.MultipleFramesInFin` (which shuts its group down without closing the
  channel, as in Java) left QUIC streams and their receive buffers open. The endpoint now closes from a shutdown hook
  of its event loop, as Java's datagram channel is closed by `NioIoHandler`; `QuicEndpointChannelTest` covers it.
- **`Unpooled.CompositeBuffer(int)`** (buffer): it was not `new CompositeByteBuf(ALLOC, false, maxNumComponents)` as in
  Java and returned a leak-tracked buffer, so `HttpDataTest` reported a composite that Java never tracks.

## DisabledForSlowLeakDetection

The attribute condition is ported (`Skip`/`SkipType`/`SkipWhen`, true when the leak detector records stack traces).
The four Java tests that use it (`AdaptiveByteBufAllocatorGrowthTest.concurrentBufferAllocateAndGrowth`,
`AdaptiveByteBufAllocatorTest.buddyAllocationConsistency`, `AbstractIntegrationTest.testHugeDecompress` and
`HttpContentDecompressorTest.testZipBomb`) keep their recorded adaptation: the CLR tests always run. Under the
presence detector the condition is false anyway (it records nothing), so the default runs are the same as Java's.
