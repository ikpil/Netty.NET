# Record-only modules

Netty 4.2.19.Final (pinned commit `64cc10f38ea5f5bd7eae48507817c66680d0afdc`) has 61 Maven modules. Some of them have
no Netty.NET project: the code is deprecated upstream, needs a Java library or JDK-only API with no .NET 10 BCL
counterpart, is a JNI native transport, or is JVM build and runtime material. This page records why. Every pinned file
of each module is recorded in `docs/<module>-porting-manifest.json`, with its reason, CLR counterpart and evidence, and
`tools/Test-CodecPortingManifest.ps1` checks the manifests, as it checks the ported modules:

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "& ./tools/Test-CodecPortingManifest.ps1 -Module transport-udt,transport-sctp"
```

The dispositions follow the owner decisions of 2026-11-16 (JVM-only units are recorded with their CLR counterpart, not
as placeholder files) and 2026-11-23 (units that need a Java library without a BCL counterpart are recorded, not ported;
the rule set for codec-compression). Recorded 2026-11-26.

## Modules

| Module | Files | Disposition | Reason and evidence |
| --- | --- | --- | --- |
| `transport-udt` | 32 | 27 `deprecated-not-ported`, 3 `library-not-ported`, 2 `jvm-only-replaced` | The package is `@deprecated The UDT transport is no longer maintained and will be removed.` (`io/netty/channel/udt/package-info.java`); it also needs the barchart UDT JNI library, which the three test helpers that are not linked to a `@Deprecated` type import or use (`library-not-ported`); the test `package-info.java` files are `jvm-only-replaced`. |
| `transport-rxtx` | 6 | `deprecated-not-ported` | The package is `@deprecated this transport will be removed in the next major version.`; it also needs the RXTX serial-port library. |
| `transport-sctp` | 44 | 35 `library-not-ported`, 9 `jvm-only-replaced` | The channels use the JDK's own SCTP API, `com.sun.nio.sctp` (module `jdk.sctp`), which has no BCL counterpart. On this host the socket cannot even be created (see [Measurements](#measurements)). The OIO channels are `@Deprecated` too. `jvm-only-replaced`: the package documentation, the GraalVM metadata and its test, and two helpers that do not use the API. |
| `transport-native-unix-common`, `-tests` | 49, 5 | `jvm-only-replaced` | JNI helpers (file descriptors, iovecs, socket options in C) of the native transports below. |
| `transport-classes-epoll`, `transport-native-epoll` | 44, 82 | `jvm-only-replaced` | JNI transport on Linux epoll. 13 `transport-native-epoll` test sources keep the scenarios that Transport maps onto its NIO channels (`clr-adapted`/`verified`, copied from `transport-porting-manifest.json`). |
| `transport-classes-io_uring`, `transport-native-io_uring` | 59, 107 | `jvm-only-replaced` | JNI transport on Linux io_uring. |
| `transport-classes-kqueue`, `transport-native-kqueue` | 35, 70 | `jvm-only-replaced` | JNI transport on BSD/macOS kqueue. |
| `resolver-dns-classes-macos`, `resolver-dns-native-macos` | 4, 3 | `jvm-only-replaced` | JNI reader of the macOS resolver configuration. `DnsServerAddressStreamProviders` keeps the reflective load and falls back to the default provider when the type is missing (docs/resolver-dns-porting.md), as Java does without the jar. |
| `microbench` | 160 | 126 `library-not-ported`, 34 `jvm-only-replaced` | JMH benchmarks (`org.openjdk.jmh`); the CLR counterpart would be BenchmarkDotNet, which is not part of this port. The package documentation, the data files (certificates, JFR settings, text) and the stand-alone `AllocationPatternSimulator` are `jvm-only-replaced`. |
| `testsuite-osgi` | 2 | 1 `library-not-ported`, 1 `jvm-only-replaced` | OSGi bundle tests on Pax Exam (`org.ops4j.pax`, `org.osgi`). |
| `transport-blockhound-tests` | 4 | 1 `library-not-ported`, 3 `jvm-only-replaced` | BlockHound (`reactor.blockhound`) checks that event loops do not block; its key and certificate data are `jvm-only-replaced`. |
| `testsuite-jpms` | 31 | `jvm-only-replaced` | Java module system (JPMS) tests; assemblies and accessibility replace JPMS (as every `module-info.yml` mapping). |
| `testsuite-shading`, `testsuite-native` | 1, 1 | `jvm-only-replaced` | Shaded-jar and native-library loading of the JVM build. |
| `testsuite-native-image`, `-client`, `-client-runtime-init` | 5, 3, 2 | `jvm-only-replaced` | GraalVM native-image programs; NativeAOT replaces them (`test/Netty.NET.Transport.NativeAot.Tests`). |
| `testsuite-karaf` | 1 | `jvm-only-replaced` | Apache Karaf feature descriptor. |
| `jfr-stub`, `varhandle-stub` | 16, 3 | `jvm-only-replaced` | Compile-time stubs of `jdk.jfr` and `java.lang.invoke.VarHandle` for older JDKs. |
| `dev-tools` | 1 | `jvm-only-replaced` | Build tooling resource of the Maven build. |

`all`, `bom` and `codec` have no files under `src` (aggregator and bill-of-materials POMs), so there is nothing to pin.

Not recorded here: `codec-classes-quic`, `codec-native-quic` and `codec-http3` (QUIC on System.Net.Quic and HTTP/3,
ported: [quic-design.md](quic-design.md)), `example` ([example-porting.md](example-porting.md)), and `testsuite-common`
(the JUnit leak presence extension, ported to xUnit v3: [testsuite-common-porting.md](testsuite-common-porting.md)).

## CLR counterparts

- **Native transports.** Netty.NET's transport is the NIO family on `System.Net.Sockets.Socket`
  (`NioIoHandler`, `NioSocketChannel`, `NioDatagramChannel`, `NioDomainSocketChannel`); on Linux and macOS the runtime's
  socket engine itself waits on epoll and kqueue. There is no counterpart for the transport-specific options
  (`EpollChannelOption`, `IoUringChannelOption`, splice, `TCP_MD5SIG` beyond what `Socket` exposes) or for io_uring.
- **Deprecated transports.** There is no `[Obsolete]` facade for UDT or RXTX: the CLR has no UDT stack, and serial
  ports are the `System.IO.Ports` package, which is not part of the BCL this port targets.
- **Transport testsuite.** The testsuite sources that need `transport-sctp` or `transport-udt` stay
  `deferred-to-module` in `transport-porting-manifest.json`: those modules are recorded here, not ported.

## Measurements

Windows 11 (10.0.26300), .NET 10.0.11, 2026-11-26, a scratch probe:

| Check | Result |
| --- | --- |
| `new Socket(AddressFamily.InterNetwork, SocketType.Stream, (ProtocolType) 132)` (SCTP) | `SocketException` `ProtocolNotSupported` |
| `new Socket(AddressFamily.InterNetwork, SocketType.Seqpacket, (ProtocolType) 132)` | `SocketException` `SocketNotSupported` |
| `QuicListener.IsSupported`, `QuicConnection.IsSupported` | `True`, `True` (for the QUIC design, not used here) |
| `Socket.OSSupportsUnixDomainSockets` | `True` |
