# handler ssl: design decisions (2026-11-24)

This page records how `io.netty.handler.ssl` of Netty 4.2.19.Final (pinned commit
`64cc10f38ea5f5bd7eae48507817c66680d0afdc`) is ported to `Netty.NET.Handler`, the measurements it rests on and the
owner decisions. The package was ported on 2026-11-24 following this design; the port as built, its measurements, test
results and review items are in [handler-porting.md](handler-porting.md#ssl), and every file is recorded in
[handler-porting-manifest.json](handler-porting-manifest.json) (no entry is pending).

## Owner decisions (2026-11-24)

1. **TLS engine.** `SslHandler` keeps Netty's structure: it parses TLS record lengths itself (`SslUtils`), keeps the
   handshake and close futures (as `Task`s), the handshake and close-notify timeouts, `startTls` and the wrap data
   size. Only the `SSLEngine.wrap/unwrap` calls are replaced by `System.Net.Security.SslStream`, driven through an
   internal mediation stream that receives the ciphertext one whole TLS record per read and collects the ciphertext
   `SslStream` writes. Pending reads of that stream complete inline on the event loop, so the handshake and reads run
   on the channel's event loop as Netty's do.
2. **SSLEngine in the public API.** No SSLEngine-shaped facade: the `SslHandler(SSLEngine, ...)` constructors take the
   BCL authentication options (`SslClientAuthenticationOptions` / `SslServerAuthenticationOptions`) instead, and
   `SslHandler.engine()`, `SslContext.newEngine(...)` and `SslContext.sessionContext()` are recorded as JVM-only
   (owner decision 2026-11-16: JVM-only API families are replaced without facades).
3. **OpenSSL and other libraries.** The OpenSSL (netty-tcnative JNI) units and the units that need Bouncy Castle,
   Conscrypt, the JDK keytool or JDK-internal classes are not ported (the rule set for codec-compression on
   2026-11-23). `SslProvider` keeps its constants; `OPENSSL` and `OPENSSL_REFCNT` report as unavailable. As ported,
   78 entries are `library-not-ported`: 43 netty-tcnative main units with 23 test files, 4 Bouncy Castle main units
   with 2 test files, 2 Conscrypt main units with 3 test files, and 1 test file that needs the Amazon Corretto Crypto
   Provider (166 declared tests). Units of these families that are `@Deprecated` or serve the `@Deprecated`
   `SelfSignedCertificate` (the `OpenSsl*ApplicationProtocolNegotiator` types and the Bouncy Castle, keytool and
   OpenJDK-internal certificate generators) are recorded as `deprecated-not-ported` instead, and `SslMasterKeyHandler`
   (JDK-internal reflection or the OpenSSL engine) as `jvm-only-replaced`.
4. **pkitesting.** The test-support module is ported as its own project on the BCL (`CertificateRequest` and related
   types replace the Bouncy Castle certificate building); algorithms the BCL cannot produce are recorded.

## Measurements (SslStream on this host)

Windows 11 (10.0.26300), .NET 10.0.8, `SslStream` over an in-memory mediation stream that the test pushes ciphertext
into, as the handler will (scratch probe `sslprobe/Program.cs` in the session scratchpad):

| Check | Result |
| --- | --- |
| Handshake TLS 1.2 and 1.3, RSA 2048 and ECDSA P-256 | succeeds; suites `TLS_ECDHE_RSA/ECDSA_WITH_AES_256_GCM_SHA384` (1.2), `TLS_AES_256_GCM_SHA384` (1.3) |
| Ciphertext fed one byte per read | handshake and 100 000 bytes of data succeed |
| ALPN (`h2`, `http/1.1`) | negotiated `h2`; `NegotiatedApplicationProtocol` set |
| SNI | `ServerCertificateSelectionCallback` receives the client's `TargetHost` |
| Client certificates (TLS 1.2 and 1.3) | the server sees the client certificate |
| Renegotiation / post-handshake authentication (`NegotiateClientCertificateAsync`) | succeeds on TLS 1.2 and TLS 1.3 |
| `ShutdownAsync` (close_notify) | the peer's `ReadAsync` returns 0 |
| `ReadAsync` with a whole record already pushed | completes synchronously (50 of 50; 7 of 7 with record-aligned feeding) |
| `ReadAsync` with half a record | stays pending until the rest is pushed |
| `WriteAsync` | completes synchronously and writes the record to the mediation stream at once |
| Pending reads completed inline (no `RunContinuationsAsynchronously`) | both handshakes finish on the driving thread in 3-4 exchange rounds; validation callbacks run there too |
| Plain bytes after close_notify, whole-buffer feeding | `SslStream` reads past the record and consumes them (21 of 21) |
| Plain bytes after close_notify, record-aligned feeding | all 21 bytes stay unread, as with Netty's packet-based unwrap |
| Untrusted server certificate | client gets `AuthenticationException` (`RemoteCertificateNameMismatch, RemoteCertificateChainErrors`); this probe saw no alert record. Corrected by the `SslHandler` measurements of 2026-11-24: the server handshake completes first and the client's alert then fails the server's first read (see Known differences) |
| `new CipherSuitesPolicy(...)` | `PlatformNotSupportedException` on Windows |
| Throughput, TLS 1.3 AES-256-GCM, 16 KiB writes | 240-294 MB/s |

Record-aligned feeding is what Netty does itself: `SslHandler.decode` cuts the input at
`SslUtils.getEncryptedPacketLength` and unwraps whole packets, so the mediation stream follows the same boundaries.

## Evidence

- `SslStream`: "Provides a stream used for client-server communication that uses the Secure Socket Layer (SSL)
  security protocol to authenticate the server and optionally the client", and "An SslStream instance transmits data
  using a stream that you supply when creating the SslStream"
  ([SslStream](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-10.0)).
- `NegotiateClientCertificateAsync`: "Negotiates the client certificate on the authenticated connection"; supported on
  freebsd, linux and windows
  ([page](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.negotiateclientcertificateasync?view=net-10.0)).
- `ShutdownAsync`: "Shuts down this SslStream"
  ([page](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.shutdownasync?view=net-10.0)).
- `NegotiatedApplicationProtocol`: "The negotiated application protocol in TLS handshake"
  ([page](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.negotiatedapplicationprotocol?view=net-10.0)).
- `CipherSuitesPolicy` is marked `UnsupportedOSPlatform("windows")` and `("android")`
  ([page](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.ciphersuitespolicy?view=net-10.0)).
- `ServerCertificateSelectionCallback`: "When the server connection is established, the server first uses the
  ServerCertificateSelectionCallback callback"
  ([page](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslserverauthenticationoptions.servercertificateselectioncallback?view=net-10.0)).
- Prior art: Azure DotNetty's `TlsHandler` drives `SslStream` through a `MediationStreamBase` and unwraps "with packet
  boundaries - call SslStream.ReadAsync only once"
  ([TlsHandler.cs](https://raw.githubusercontent.com/Azure/DotNetty/dev/src/DotNetty.Handlers/Tls/TlsHandler.cs)).

## What the package becomes

The plan classified the 125 main files (28 679 lines of types) from a read of every pinned file:

| Group | Files | Lines | Plan |
| --- | ---: | ---: | --- |
| Engine-independent Netty logic (SNI/ClientHello parsing, `SslUtils` record parsing, PEM, ALPN names and configuration, cipher-suite constants and filters, events, exceptions, `OptionalSslHandler`, `ApplicationProtocolNegotiationHandler`) | 42 | 6 613 | ported as is |
| Built on `SSLEngine`/JCA (`SslHandler`, `SslContext`, `SslContextBuilder`, `JdkSsl*Context`, JDK ALPN wrappers, trust/key-manager helpers) | 26 | 8 809 | ported on `SslStream` and the BCL options |
| OpenSSL (netty-tcnative) | 45 | 11 579 | library-not-ported |
| Bouncy Castle, Conscrypt, keytool, JDK-internal `sun.security.x509` | 10 | 1 678 | library-not-ported |

The `@Deprecated` types and the deprecated members follow the owner rule of 2026-11-16 (not ported when the
`@deprecated` text names a replacement). `transport-native-unix-common` is only used to pass a socket descriptor to the
OpenSSL engine, so it is not needed.

As ported (2026-11-24, counted from the manifest), the 240 ssl entries are:

| Status | Main files (ssl + ssl.util) | Test files (ssl + ssl.util) | Other | Content |
| --- | ---: | ---: | --- | --- |
| `clr-adapted` | 47 (41 + 6) | 13 (13 + 0) | | the ported units on `SslStream` and the BCL; tests with CLR-inapplicable methods and adapted helpers |
| `verified` | | 22 (19 + 3) | 39 test resources | tests whose every method is mapped (and `SilenceExceptionHandler`); the resources copied byte for byte |
| `jvm-only-replaced` | 15 (9 + 6) | 6 (5 + 1) | 2 GraalVM metadata files | JDK SSLEngine wrappers, trust and resumption glue, JCA SPI helpers, `SslMasterKeyHandler`, `ApplicationProtocolAccessor`, the two package-info files; test helpers for delegated tasks, JCA trust managers and providers |
| `library-not-ported` | 49 (48 + 1) | 29 (28 + 1) | | netty-tcnative, Bouncy Castle, Conscrypt, Amazon Corretto Crypto Provider (decision 3) |
| `deprecated-not-ported` | 14 (9 + 5) | 4 (0 + 4) | | `JdkSslClientContext`/`JdkSslServerContext`, the `Jdk*`/`OpenSsl*` `ApplicationProtocolNegotiator` families, the `SelfSignedCertificate` family |

Tests (74 files, 460 declared test methods): 180 are mapped to C# tests, 107 are CLR-inapplicable and 173 are not
ported (166 library, 4 deprecated, 3 JVM-only). 50 of the 64 tests of the `SSLEngineTest` fixture became
CLR-inapplicable (the plan estimated about 47), because they call `wrap`/`unwrap` on an `SSLEngine` or inspect its
`SSLSession`; the `SslHandler`/pipeline, SNI, `SslContext` configuration and PEM/utility tests are ported. The C# run
has 308 ssl cases: 281 pass and 27 are skipped on this host, none fails.

## Known differences to carry into the port

- No cipher-suite selection on Windows (`CipherSuitesPolicy` unsupported): `SslContextBuilder.ciphers(...)` cannot be
  applied there; it works on Linux and macOS. As ported, a context with configured cipher suites throws
  `PlatformNotSupportedException` from `NewHandler` on Windows and Android.
- A failed certificate validation is reported later than in Java (corrected 2026-11-24; earlier this page said that no
  alert is sent). `SslStream` validates the remote certificate after its TLS handshake loop, so the server handshake
  completes first. A validation that fails or a callback that returns false makes the client send a fatal alert
  (83/32 bytes on TLS 1.2, 112 bytes on TLS 1.3, measured), so the server's first read fails with
  `AuthenticationException` ("The decryption operation failed"). A callback that throws sends no alert: the exception
  propagates as is, and `SslHandler` reports it as the `InnerException` of an `AuthenticationException`. Java fails the
  server handshake with the client's alert.
- No session cache size or timeout (`sessionCacheSize`/`sessionTimeout`, `SSLSessionContext`): `SslStream` resumes
  sessions through the OS stack without such settings, and it reports no session facts (id, times, resumption);
  `SslContextBuilder.sessionCacheSize/sessionTimeout` are kept but not applied. On Windows SChannel resumes sessions
  across connections of one process.
- No answer to a received close_notify: `SslStream` writes nothing, where the JDK engine answers with its own below
  TLS 1.3 (RFC 5246 7.2.1). `SslHandler` answers it below TLS 1.3 itself (`EngineAcknowledgeCloseNotify`).
- No close_notify before the handshake completed: `ShutdownAsync` needs an authenticated stream, so closing a channel
  whose handshake never finished sends no alert.
- Renegotiation only as a server (`NegotiateClientCertificateAsync`, FreeBSD, Linux and Windows); it always asks the
  client for a certificate (so `JdkSslContext` accepts any client certificate on a server without client
  authentication), application data the client sends before answering is rejected on TLS 1.2 and lost on TLS 1.3, and
  a client `renegotiate()` fails with `NotSupportedException`.
- ALPN without a common protocol fails on both sides (`SEC_E_APPLICATION_PROTOCOL_MISMATCH` on Windows), where JSSE
  completes the handshake without ALPN for `NO_ADVERTISE`/`ACCEPT`.
- SChannel rejects a first TLS record that carries only a few ClientHello bytes (1, 4 or 5 bytes measured; 10 bytes
  and more are accepted), where the JDK engine accepts such fragments (`SniHandlerTest.testTinyFragments` and
  `testTinyFirstFragment` are skipped on Windows).
- Windows reports an expired trust anchor of a custom trust store as `NotTimeValid`, where PKIX (RFC 5280 6.1) does not
  check the anchor's validity; `JdkSslContext` accepts a chain whose only errors are such anchors.
- PKCS#1 keys in the legacy OpenSSL `Proc-Type: 4,ENCRYPTED` PEM form need Bouncy Castle in Java too. Answered
  2026-11-24 (measured): the BCL cannot read them, nor the PKCS #1 DSA key, so the five `SslContextTest` cases that
  need these Bouncy Castle-only forms are CLR-inapplicable; every PKCS #8 form of the test resources loads.

## Progress

- 2026-11-24: pkitesting is ported on the BCL ([pkitesting-porting.md](pkitesting-porting.md)): 18 original tests,
  37 of 74 cases pass and 37 skip for algorithms the platform lacks (Ed25519/Ed448 have no BCL type, SLH-DSA is not
  supported on this host). Findings that matter for ssl: the BCL post-quantum key types (`MLDsa`, `MLKem`, `SlhDsa`)
  do not derive from `AsymmetricAlgorithm`, so key pairs are held as the BCL key objects; on this host
  `CopyWithPrivateKey` fails for ML-DSA and ML-KEM, so such keys are kept next to their certificates; a certificate
  for `SslStream` must come from a persisted (PKCS#12 round-tripped) key, an ephemeral key fails with "platform does
  not support ephemeral keys" (measured).
- 2026-11-24: `io.netty.handler.ssl` and `io.netty.handler.ssl.util` are ported
  ([handler-porting.md](handler-porting.md#ssl)) in three waves: (1) the engine-independent part, 35 main and 11 test
  files; (2) `SslHandler` and `ApplicationProtocolNegotiationHandler` on `SslStream` behind the record-aligned
  mediation stream, and the context layer (`SslContext`, `SslContextBuilder`, `JdkSslContext`, `DelegatingSslContext`,
  `SslProvider`, `SniHandler`, `OptionalSslHandler`, the trust manager factories); (3) the tests in three groups
  (handler, the `SSLEngineTest` fixture, context). The lead recorded the library, deprecated and JVM-only units, copied
  the 39 test resources and ported `SilenceExceptionHandler`. Results: the handler checker passes with no pending entry
  (316 files: 103 clr-adapted, 18 deprecated-not-ported, 36 jvm-only-replaced, 78 library-not-ported, 81 verified; 331
  mapped, 107 CLR-inapplicable and 174 not-ported original tests) and 19 of 19 manifest mutations were rejected and
  restored; `Netty.NET.Handler.Tests` runs 460 cases, 432 pass, 28 skip, 0 fail, identical in three runs (ssl: 308
  cases, 281 pass, 27 skip); the Transport checker passes; pkitesting is unchanged at 37 passed and 37 skipped; the
  solution builds with 0 warnings and 0 errors. Lead fixes: `JdkSslContext` accepts expired trust anchors of a custom
  trust store and, on a server without client authentication, any client certificate (renegotiation); `SslHandler`
  answers a received close_notify below TLS 1.3, treats a record shorter than its header as `BUFFER_UNDERFLOW` and
  gives the tests internal options accessors; pkitesting's `ToKeyManagerFactory` keeps the loaded PKCS#12 entries
  alive with the returned context; the test project references `Netty.NET.PkiTesting`.
