/*
 * Copyright 2015 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: the CLR-inapplicable tests of SSLEngineTest and the helpers that only serve them, in source order, with their
// original comments (each test is recorded with its reason and evidence in docs/handler-porting-manifest.json). There is
// no SSLEngine facade (owner decision 2026-11-24, docs/handler-ssl-design.md): SslHandler drives an SslStream, which
// exposes no wrap/unwrap ("Data moves through the engine by calling wrap() or unwrap() on outbound or inbound data",
// https://docs.oracle.com/en/java/javase/17/docs/api/java.base/javax/net/ssl/SSLEngine.html), no handshake status,
// delegated tasks, closeInbound/closeOutbound/isInboundDone/isOutboundDone, enabled or supported protocol and cipher
// suite lists, SSLParameters, SSLSession (session id, creation/last access times, invalidate/isValid, session values and
// binding events, peer host/port, buffer sizes, session context, peer certificate chain) or SSLSessionContext (the
// SslStream members are listed at https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=net-10.0).
// The tests whose subject is one of those members, or that drive an SSLEngine handshake with the helpers handshake(),
// pingPongPacketsUntilSessionAllocation(), closeOutboundAndInbound() and runDelegatedTasks(), are therefore
// CLR-inapplicable; the TLS behaviour they check through those members is covered where SslStream exposes it by the
// ported pipeline tests of this class.
public abstract partial class SSLEngineTest
{
    // CLR: testSetSupportedCiphers: setEnabledCipherSuites/getEnabledCipherSuites on engines of newEngine.

    // CLR: testIncompatibleCiphers: setEnabledCipherSuites on each engine and an SSLEngine handshake(). Its comments:
    // Select a mandatory cipher from the TLSv1.2 RFC https://www.ietf.org/rfc/rfc5246.txt so handshakes won't fail
    // due to no shared/supported cipher.
    // Set the server to only support a single TLSv1.2 cipher
    // Set the client to only support a single TLSv1.3 cipher

    // CLR: testGetCreationTime: SSLSession.getCreationTime() of an engine.

    // CLR: testSessionInvalidate: SSLEngine handshake(), pingPongPacketsUntilSessionAllocation() and
    // SSLSession.invalidate()/isValid().

    // CLR: testSSLSessionId: SSLSession.getId() of engines before and after an SSLEngine handshake(). Its comments:
    // This test only works for non TLSv1.3 for now
    // Before the handshake the id should have length == 0
    // With TLS1.3 we should see pseudo IDs and so these should never match.
    // After the handshake the client should have ticket ids
    // After the handshake the id should have length > 0

    // CLR: pingPongPacketsUntilSessionAllocation(): wrap/unwrap until the client session has an id. Its comment:
    // Allocate something which is big enough for sure

    // CLR: testEnablingAnAlreadyDisabledSslProtocol(param, protocols1, protocols2) (the helper of the JdkSslEngineTest
    // test of that name): setEnabledProtocols/getEnabledProtocols, closeInbound/closeOutbound of an engine. Its comments:
    // Disable all protocols
    // The only protocol that should be enabled is SSLv2Hello
    // Enable a protocol that is currently disabled
    // The protocol that was just enabled should be returned

    // CLR: handshake(type, delegate, clientEngine, serverEngine): the SSLEngine handshake loop (wrap/unwrap with handshake
    // status checks, delegated tasks and buffer growth). Its comments:
    // After the handshake completes it is possible we have more data that was send by the server as
    // the server will send session updates after the handshake. In this case continue to unwrap.
    // We need to special case conscrypt due a bug.
    // We need to ensure we feed all the data to the engine to not end up with a corrupted state.
    // This is especially important with TLS1.3 which may produce sessions after the "main handshake" is
    // done

    // CLR: increaseDstBuffer(...): grows a ByteBuffer of the handshake loop. Its comment:
    // We need to increase the destination buffer

    // CLR: cleanupClientSslEngine/cleanupServerSslEngine/unwrapEngine release an engine of newEngine that never joined a
    // pipeline (JdkSslEngine wrapper); their Javadoc:
    // Called when ever an SSLEngine is not wrapped by a {@link SslHandler} and inserted into a pipeline.

    // CLR: testMutualAuthSameCertChain: the server checks that SSLSession.getPeerCertificates() (and on Java before 15
    // getPeerCertificateChain()) report the two certificates the client's key manager sends. SslStream reports only
    // RemoteCertificate and no peer chain, and the client's SslStreamCertificateContext sends the chain built from the
    // leaf ("Supplementary certificates used to build the certificate chain", SslContext.BuildKeyManagerFactory), not the
    // configured list: with the two self-signed certificates of the test the server's validation callback saw a chain
    // of one element and an empty ExtraStore, and RemoteCertificate was the client leaf (measured for TLSv1.2 and TLSv1.3
    // on this host, with the cipher suites left out because Windows has no CipherSuitesPolicy). Its comment:
    // We create a new chain for certificates which contains 2 certificates

    // CLR: testUnwrapBehavior: two client wrap() records unwrapped by the server, with a BUFFER_OVERFLOW case. Its
    // comments:
    // create two TLS frames
    // first frame
    // second frame
    // send over to server
    // try with too small output buffer first (to check BUFFER_OVERFLOW case)
    // now with big enough buffer

    // CLR: testProtocolMatch/testProtocolNoMatch (helper testProtocol): an SSLEngine handshake() between engines with
    // given protocols.

    // CLR: nonContiguousProtocols(provider), testHandshakeCompletesWithNonContiguousProtocolsTLSv1_2CipherOnly and
    // testHandshakeCompletesWithoutFilteringSupportedCipher: an SSLEngine handshake() with TLSv1.2 and TLSv1 enabled. Their
    // comments:
    // conscrypt not correctly filters out TLSv1 and TLSv1.1 which is required now by the JDK.
    // https://github.com/google/conscrypt/issues/1013

    // CLR: testPacketBufferSizeLimit: a server wrap() of more than the application buffer size. Its comments:
    // Allocate an buffer that is bigger then the max plain record size.
    // Fill the whole buffer and flip it.

    // CLR: testSSLEngineUnwrapNoSslRecord: unwrap() of bytes that are no TLS record.

    // CLR: testBeginHandshakeAfterEngineClosed: closeInbound()/closeOutbound() then beginHandshake(). Its comments:
    // expected
    // Workaround for conscrypt bug
    // See https://github.com/google/conscrypt/issues/840

    // CLR: testBeginHandshakeCloseOutbound: beginHandshake(), closeOutbound() and wrap() until CLOSED.

    // CLR: testCloseInboundAfterBeginHandshake: beginHandshake() then closeInbound(). Its comment:
    // See https://github.com/google/conscrypt/issues/839

    // CLR: testCloseNotifySequence: the close_notify exchange through closeOutbound(), wrap/unwrap results and
    // isInboundDone/isOutboundDone (assertEngineRemainsClosed checks the CLOSED results). Its comments:
    // This will produce a close_notify
    // Something still pending in the outbound buffer.
    // Now wrap and so drain the outbound buffer.
    // Need an UNWRAP to read the response of the close_notify
    // Outbound buffer should be drained now.
    // Need a WRAP to respond to the close_notify
    // Should have consumed the complete close_notify
    // UNWRAP/WRAP are not expected after this point
    // Ensure that calling wrap or unwrap again will not produce an SSLException

    // CLR: testWrapAfterCloseOutbound: wrap() after closeOutbound().

    // CLR: testMultipleRecordsInOneBufferWithNonZeroPosition: unwrap() of two records from a buffer with a non-zero
    // position. Its comments:
    // Choose buffer size small enough that we can put multiple buffers into one buffer and pass it into the
    // unwrap call without exceed MAX_ENCRYPTED_PACKET_LENGTH.
    // We need to be able to hold 2 records + positionOffset
    // Copy the first record into the combined buffer
    // Ensure we have the first record and a tiny amount of the second record in the buffer

    // CLR: testMultipleRecordsInOneBufferBiggerThenPacketBufferSize: several wrap() records unwrapped from one buffer.
    // Its comments:
    // We did not have enough room to wrap
    // Check that we were able to wrap multiple times.

    // CLR: testBufferUnderFlow: unwrap() of partial records (BUFFER_UNDERFLOW; assertResultIsBufferUnderflow). Its
    // comments:
    // Flip so we can read it.
    // We limit the buffer so we have less then the header to read, this should result in an BUFFER_UNDERFLOW.
    // We limit the buffer so we can read the header but not the rest, this should result in an
    // BUFFER_UNDERFLOW.
    // We limit the buffer so we can read the header and partly the rest, this should result in an
    // Reset limit so we can read the full record.

    // CLR: testWrapDoesNotZeroOutSrc: wrap() leaves its source buffer unchanged.

    // CLR: testDisableProtocols (helper testDisableProtocols(param, protocol, disabledProtocols)):
    // getSupportedProtocols/setEnabledProtocols/getEnabledProtocols of an engine. Its comment:
    // It's not allowed to set only PROTOCOL_SSL_V2_HELLO if using JDK SSLEngine.

    // CLR: testUsingX509TrustManagerVerifiesHostname/testUsingX509TrustManagerVerifiesSNIHostname (helper
    // testUsingX509TrustManagerVerifiesHostname(param, useSNI)): a TrustManagerFactory built from a TrustManagerFactorySpi
    // with an X509TrustManager and an SSLEngine handshake() of newEngine(alloc, "127.0.0.1", 1234). Its comments:
    // Not supported when using conscrypt
    // NOOP
    // Provide a custom trust manager, this manager trust all certificates
    // expected as the hostname not matches.

    // CLR: testGetCiphersuite: SSLSession.getCipherSuite() of both engines after an SSLEngine handshake().

    // CLR: testSessionCache, testSessionCacheTimeout and testSessionCacheSize (helpers invalidateSessionsAndAssert,
    // assertSessionCache, doHandshakeVerifyReusedAndClose, isSessionReused, currentSessionCacheSize,
    // closeOutboundAndInbound, assertSessionReusedForEngine): SslContext.sessionContext() (jvm-only-replaced), SSLSession
    // values and reuse after SSLEngine handshakes; SslStream has no session cache settings or resumption facts
    // (docs/handler-ssl-design.md, Known differences). Their comments:
    // server may use multiple sessions
    // We should see the previous stored value on session reuse.
    // This is broken in conscrypt.
    // TODO: Open an issue in the conscrypt project.
    // If we know for sure it was reused so the accessedTime needs to be larger.
    // Ensure we sleep 1ms in between as getLastAccessedTime() abd getCreationTime() are in milliseconds.
    // If we don't sleep and execution is very fast we will see test-failures once we go into the
    // reuse branch.
    // Ensure we allocate a bit more so we can fit in multiple packets. This is needed as we may call multiple
    // time wrap / unwrap in a for loop before we drain the buffer we are writing in.
    // call wrap till we produced all data
    // call unwrap till we consumed all data
    // Now close the inbound as well
    // Let's sleep for a bit more then 1 second so the cache should timeout the sessions.
    // As we have a cache size of 1 we should never have more then one session in the cache
    // We should at least reuse b.netty.io

    // CLR: the protected enum SessionReusedState of isSessionReused(SSLEngine); its Javadoc:
    // We know for sure that the session was not reused
    // The session might have been reused.
    // The session was reused.

    // CLR: testSessionBindingEvent: SSLSession.putValue/removeValue with an SSLSessionBindingListener.

    // CLR: testSessionAfterHandshake, testSessionAfterHandshakeMutualAuth, testSessionAfterHandshakeKeyManagerFactory and
    // testSessionAfterHandshakeKeyManagerFactoryMutualAuth (helpers testSessionAfterHandshake0, additionalPeerAssertions):
    // SSLSession values, ids, times, buffer sizes, session context and peer/local certificates of engines after an
    // SSLEngine handshake(), with a ConstantTrustManagerFactory of an EmptyExtendedX509TrustManager that writes to
    // getHandshakeSession(). Their comments:
    // The values should not have been carried over.
    // Workaround for JDK 14 regression.
    // See https://bugs.openjdk.java.net/browse/JDK-8242008
    // Server trust manager factory is only called if server authenticates clients.
    // As we did not use mutual auth this is expected
    // noop

    // CLR: mustCallResumeTrustedOnSessionResumption, mustFailHandshakePromiseIfResumeServerTrustedThrows,
    // mustFailHandshakePromiseIfResumeClientTrustedThrows and
    // mustNotCallResumeWhenClientAuthIsOptionalAndNoClientCertIsProvided (helpers buildClientSslContextForMTLS,
    // buildServerSslContextForMTLS, SessionValueSettingTrustManager): the ResumableX509ExtendedTrustManager callbacks on
    // session resumption (jvm-only-replaced: SslStream reports no resumed session) and SSLSession values of
    // getHandshakeSession()/getSession(). Their comments:
    // Wait for exception to propagate.
    // Client provides no certificate!
    // Client auth is OPTIONAL!

    // CLR: testSupportedSignatureAlgorithms: a KeyManagerFactorySpi whose X509ExtendedKeyManager reads the signature
    // algorithms of ExtendedSSLSession.getHandshakeSession() during an SSLEngine handshake().

    // CLR: testHandshakeSession (TestTrustManagerFactory): an X509ExtendedTrustManager reading
    // SSLEngine.getHandshakeSession() and getUseClientMode() during an SSLEngine handshake().

    // CLR: testSessionLocalWhenNonMutualWithKeyManager/WithoutKeyManager (helper testSessionLocalWhenNonMutual):
    // SSLSession.getLocalCertificates()/getLocalPrincipal() after an SSLEngine handshake().

    // CLR: testEnabledProtocolsAndCiphers (helpers assertEnabledProtocolsAndCipherSuites, assertArrayContains): the
    // session protocol and cipher suite against getEnabledProtocols()/getEnabledCipherSuites() after an SSLEngine
    // handshake().

    // CLR: testMasterKeyLogging: SslMasterKeyHandler (jvm-only-replaced: the JDK master secret is read by reflection;
    // SslStream writes SSLKEYLOGFILE only on Linux) with a JSSE SSLSocket client. Its comments:
    /*
     * At the moment master key logging is not supported for conscrypt
     */
    /*
     * The JDK SSL engine master key retrieval relies on being able to set field access to true.
     * That is not available in JDK9+
     */

    // CLR: testDefaultProtocolsIncludeTLSv13: getEnabledProtocols() of engines of contexts without protocols. Its comment:
    // Don't specify the protocols as we want to test the default selection

    // CLR: testInvalidSNIIsIgnoredAndNotThrow: an SSLEngine handshake() of newEngine(alloc, "/invalid.path", 80) and
    // getSSLParameters().

    // CLR: testBufferUnderflowPacketSizeDependency: an SSLEngine handshake() that fails without client trust.

    // CLR: testTLSv13DisabledIfNoValidCipherSuiteConfigured and testTLSv13EnabledIfNoCipherSuiteConfigured: the session
    // protocol after an SSLEngine handshake() of contexts with a TLSv1.2 cipher suite or TLSv1.3 only. Their comments:
    // Use a TLSv12 cipher
    // Doesn't matter what kind of buffertype is used for this test.
    // TLSv1.3 is not supported by either client or server.

    // CLR: testExtraDataInLastSrcBufferForClientUnwrap (helper testExtraDataInLastSrcBufferForClientUnwrap(param,
    // clientEngine, serverEngine)): the client's unwrap() must not consume application data that follows the server's
    // last handshake record. Its comments:
    // Ensure we can fit two records as we want to include two records once the handshake completes on the
    // server side.
    // We finished the handshake on the server side, lets add another record to the sTOc buffer
    // so we can test that we will not unwrap extra data before we actually consider the handshake
    // complete on the client side as well.
}
