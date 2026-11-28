/*
 * Copyright 2014 The Netty Project
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

using System;
using System.Threading;
using System.Threading.Tasks;
using Netty.NET.Handler.Ssl;
using AuthenticationException = System.Security.Authentication.AuthenticationException;
using Protocol = Netty.NET.Handler.Ssl.ApplicationProtocolConfig.Protocol;
using SelectedListenerFailureBehavior = Netty.NET.Handler.Ssl.ApplicationProtocolConfig.SelectedListenerFailureBehavior;
using SelectorFailureBehavior = Netty.NET.Handler.Ssl.ApplicationProtocolConfig.SelectorFailureBehavior;

namespace Netty.NET.Handler.Tests.Ssl;

// CLR: the JDK fixture of SSLEngineTest; its SSLEngine is SslStream (owner decision 2026-11-24). ProviderType keeps
// ALPN_JAVA; ALPN_CONSCRYPT is library-not-ported (Conscrypt, owner decision 4), so its parameters are not generated. The
// JCA Provider of a ProviderType (the provider field, provider(), clientSslContextProvider()/serverSslContextProvider())
// has no counterpart: the engine is always SslStream. @MethodSource("newJdkParams") is [MemberData(nameof(NewJdkParams))]
// over a static member (see SSLEngineTest). TestAbortedException (a skipped test) is Assert.Skip. The JDK ALPN negotiator
// types of testTlsExtensionNoCompatibleProtocolsClientHandshakeFailure and the SSLEngine calls of
// testEnablingAnAlreadyDisabledSslProtocol and TestDelegatingSslContext.initEngine are CLR-inapplicable (recorded in
// docs/handler-porting-manifest.json; see the notes at those tests below).
public class JdkSslEngineTest : SSLEngineTest
{
    // CLR: a Java enum with abstract methods is a sealed class with static readonly instances (as codec-base
    // Base64Dialect); only ALPN_JAVA exists (see the class comment).
    public sealed class ProviderType
    {
        public static readonly ProviderType ALPN_JAVA = new ProviderType(nameof(ALPN_JAVA));

        private readonly string name;

        private ProviderType(string name)
        {
            this.name = name;
        }

        // CLR: JdkAlpnSslUtils.supportsAlpn() (jvm-only-replaced) asks the JDK 9+ SSLEngine ALPN API; SslStream takes the
        // ALPN protocols as SslClientAuthenticationOptions/SslServerAuthenticationOptions.ApplicationProtocols, which
        // SslProvider.IsAlpnSupported(SslProvider.JDK) reports.
        internal bool IsAvailable()
        {
            return SslProvider.IsAlpnSupported(SslProvider.JDK);
        }

        internal ApplicationProtocolConfig.Protocol Protocol()
        {
            return ApplicationProtocolConfig.Protocol.ALPN;
        }

        // CLR: provider() returned the JCA Provider of the type; ALPN_JAVA returned null:
        // Use the default provider.
        // The engine is always SslStream, so there is no provider to return or to store in the test instance.

        internal void Activate(JdkSslEngineTest instance)
        {
            // Typical code will not have to check this, but will get a initialization error on class load.
            // Check in this test just in case we have multiple tests that just the class and we already ignored the
            // initialization error.
            if (!IsAvailable())
            {
                throw TlsExtensionNotFound(Protocol());
            }
        }

        internal static ProviderType[] Values()
        {
            return new[] { ALPN_JAVA };
        }

        public override string ToString()
        {
            return name;
        }
    }

    private const string PREFERRED_APPLICATION_LEVEL_PROTOCOL = "my-protocol-http2";
    private const string FALLBACK_APPLICATION_LEVEL_PROTOCOL = "my-protocol-http1_1";
    private const string APPLICATION_LEVEL_PROTOCOL_NOT_COMPATIBLE = "my-protocol-FOO";

    public static TheoryData<JdkSSLEngineTestParam> NewJdkParams()
    {
        // CLR: the rows of TheoryData<T> convert to T (TheoryDataRow<T>'s implicit operator), so the loop reads the
        // parameters of NewTestParams() as the source iterates the List.
        TheoryData<SSLEngineTestParam> @params = NewTestParams();

        TheoryData<JdkSSLEngineTestParam> jdkParams = new TheoryData<JdkSSLEngineTestParam>();
        foreach (ProviderType providerType in ProviderType.Values())
        {
            foreach (SSLEngineTestParam param in @params)
            {
                jdkParams.Add(new JdkSSLEngineTestParam(providerType, param));
            }
        }
        return jdkParams;
    }

    public sealed class JdkSSLEngineTestParam : SSLEngineTestParam
    {
        internal readonly ProviderType providerType;
        internal JdkSSLEngineTestParam(ProviderType providerType, SSLEngineTestParam param)
            : base(param.Type(), param.Combo(), param.Delegate())
        {
            this.providerType = providerType;
        }

        public override string ToString()
        {
            return "JdkSSLEngineTestParam{" +
                    "type=" + Type() +
                    ", protocolCipherCombo=" + Combo() +
                    ", delegate=" + (Delegate() ? "true" : "false") +
                    ", providerType=" + providerType +
                    '}';
        }
    }

    [Theory]
    [MemberData(nameof(NewJdkParams))]
    public async Task TestTlsExtension(JdkSSLEngineTestParam param)
    {
        try
        {
            param.providerType.Activate(this);
            ApplicationProtocolConfig apn = FailingNegotiator(param.providerType.Protocol(),
                PREFERRED_APPLICATION_LEVEL_PROTOCOL);
            await SetupHandlers(param, apn);
            await RunTest();
        }
        catch (SkipTestException e)
        {
            // ALPN availability is dependent on the java version. If ALPN is not available because of
            // java version incompatibility don't fail the test, but instead just skip the test
            Assert.Skip("Not expected: " + e.Message);
        }
    }

    [Theory]
    [MemberData(nameof(NewJdkParams))]
    public async Task TestTlsExtensionNoCompatibleProtocolsNoHandshakeFailure(JdkSSLEngineTestParam param)
    {
        try
        {
            param.providerType.Activate(this);
            ApplicationProtocolConfig clientApn = AcceptingNegotiator(param.providerType.Protocol(),
                PREFERRED_APPLICATION_LEVEL_PROTOCOL);
            ApplicationProtocolConfig serverApn = AcceptingNegotiator(param.providerType.Protocol(),
                APPLICATION_LEVEL_PROTOCOL_NOT_COMPATIBLE);
            await SetupHandlers(param, serverApn, clientApn);
            // CLR: runTest(null) expects the JDK to complete the handshake without an application protocol
            // (NO_ADVERTISE/ACCEPT). SslStream takes only the protocol list (JdkSslContext.ProtocolNegotiator) and fails the
            // handshake when no protocol is common: on Windows both sides fail with AuthenticationException whose inner
            // Win32Exception is 0x80090367, SEC_E_APPLICATION_PROTOCOL_MISMATCH (measured for TLSv1.2 and TLSv1.3 on this
            // host), and on Linux the OpenSSL ALPN callback of SslStream returns SSL_TLSEXT_ERR_ALERT_FATAL when "No common
            // application protocol was negotiated"
            // (https://raw.githubusercontent.com/dotnet/runtime/main/src/libraries/Common/src/Interop/Unix/System.Security.Cryptography.Native/Interop.OpenSsl.cs).
            // The test therefore checks that measured failure on both sides instead of the exchange of runTest(null).
            Assert.True(serverLatch.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            Assert.IsAssignableFrom<AuthenticationException>(serverException);
            Assert.False(await AwaitUninterruptibly(clientSslHandshakeFuture));
            Assert.IsAssignableFrom<AuthenticationException>(Cause(clientSslHandshakeFuture));
        }
        catch (SkipTestException e)
        {
            // ALPN availability is dependent on the java version. If ALPN is not available because of
            // java version incompatibility don't fail the test, but instead just skip the test
            Assert.Skip("Not expected: " + e.Message);
        }
    }

    // CLR: testTlsExtensionNoCompatibleProtocolsClientHandshakeFailure is CLR-inapplicable: its server selects a protocol
    // the client did not offer through a JdkAlpnApplicationProtocolNegotiator with a custom ProtocolSelectorFactory and
    // FAIL_SELECTION_LISTENER_FACTORY, on JdkSslServerContext/JdkSslClientContext and a TestDelegatingSslContext whose
    // initEngine sets the engine's protocols and cipher suites. Those negotiator and context types are @Deprecated and not
    // ported (their @deprecated text names ApplicationProtocolConfig and SslContextBuilder), ApplicationProtocolConfig
    // cannot express a selector, and SslStream picks the server's protocol itself from the client's list. Its comments:
    // ALPN
    // When using TLSv1.3 the handshake is NOT sent in an extra round trip which means there will be
    // no exception reported in this case but just the channel will be closed.

    [Theory]
    [MemberData(nameof(NewJdkParams))]
    public async Task TestTlsExtensionNoCompatibleProtocolsServerHandshakeFailure(JdkSSLEngineTestParam param)
    {
        try
        {
            param.providerType.Activate(this);
            ApplicationProtocolConfig clientApn = AcceptingNegotiator(param.providerType.Protocol(),
                PREFERRED_APPLICATION_LEVEL_PROTOCOL);
            ApplicationProtocolConfig serverApn = FailingNegotiator(param.providerType.Protocol(),
                APPLICATION_LEVEL_PROTOCOL_NOT_COMPATIBLE);
            await SetupHandlers(param, serverApn, clientApn);
            Assert.True(serverLatch.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            Assert.IsAssignableFrom<AuthenticationException>(serverException);
        }
        catch (SkipTestException e)
        {
            // ALPN availability is dependent on the java version. If ALPN is not available because of
            // java version incompatibility don't fail the test, but instead just skip the test
            Assert.Skip("Not expected: " + e.Message);
        }
    }

    [Theory]
    [MemberData(nameof(NewJdkParams))]
    public async Task TestAlpnCompatibleProtocolsDifferentClientOrder(JdkSSLEngineTestParam param)
    {
        try
        {
            param.providerType.Activate(this);
            // Even the preferred application protocol appears second in the client's list, it will be picked
            // because it's the first one on server's list.
            ApplicationProtocolConfig clientApn = AcceptingNegotiator(ApplicationProtocolConfig.Protocol.ALPN,
                FALLBACK_APPLICATION_LEVEL_PROTOCOL, PREFERRED_APPLICATION_LEVEL_PROTOCOL);
            ApplicationProtocolConfig serverApn = FailingNegotiator(ApplicationProtocolConfig.Protocol.ALPN,
                PREFERRED_APPLICATION_LEVEL_PROTOCOL, FALLBACK_APPLICATION_LEVEL_PROTOCOL);
            await SetupHandlers(param, serverApn, clientApn);
            Assert.Null(serverException);
            await RunTest(PREFERRED_APPLICATION_LEVEL_PROTOCOL);
        }
        catch (SkipTestException e)
        {
            // ALPN availability is dependent on the java version. If ALPN is not available because of
            // java version incompatibility don't fail the test, but instead just skip the test
            Assert.Skip("Not expected: " + e.Message);
        }
    }

    // CLR: testEnablingAnAlreadyDisabledSslProtocol is CLR-inapplicable: it calls SSLEngine.setEnabledProtocols/
    // getEnabledProtocols on an engine of newEngine (SSLEngineTest's helper of the same name), and SslStream has no
    // engine-level protocol list (see SSLEngineTest.cs).

    // CLR: @Disabled overrides of the SSLEngineTest tests; the theories stay declared once in SSLEngineTest and their
    // virtual bodies are overridden here with Assert.Skip and the @Disabled comment (see SSLEngineTest).
    protected override Task TestMutualAuthValidClientCertChainTooLongFailOptionalClientAuth0(SSLEngineTestParam param)
    {
        /* Does the JDK support a "max certificate chain length"? */
        Assert.Skip("Does the JDK support a \"max certificate chain length\"?");
        return Task.CompletedTask;
    }

    protected override Task TestMutualAuthValidClientCertChainTooLongFailRequireClientAuth0(SSLEngineTestParam param)
    {
        /* Does the JDK support a "max certificate chain length"? */
        Assert.Skip("Does the JDK support a \"max certificate chain length\"?");
        return Task.CompletedTask;
    }

    protected override bool MySetupMutualAuthServerIsValidException(Exception cause)
    {
        // TODO(scott): work around for a JDK issue. The exception should be SSLHandshakeException.
        return base.MySetupMutualAuthServerIsValidException(cause) || CausedBySSLException(cause);
    }

    private Task RunTest()
    {
        return RunTest(PREFERRED_APPLICATION_LEVEL_PROTOCOL);
    }

    protected override SslProvider SslClientProvider()
    {
        return SslProvider.JDK;
    }

    protected override SslProvider SslServerProvider()
    {
        return SslProvider.JDK;
    }

    private static ApplicationProtocolConfig FailingNegotiator(Protocol protocol, params string[] supportedProtocols)
    {
        return new ApplicationProtocolConfig(protocol,
                SelectorFailureBehavior.FATAL_ALERT,
                SelectedListenerFailureBehavior.FATAL_ALERT,
                supportedProtocols);
    }

    private static ApplicationProtocolConfig AcceptingNegotiator(Protocol protocol, params string[] supportedProtocols)
    {
        return new ApplicationProtocolConfig(protocol,
                SelectorFailureBehavior.NO_ADVERTISE,
                SelectedListenerFailureBehavior.ACCEPT,
                supportedProtocols);
    }

    private static SkipTestException TlsExtensionNotFound(Protocol protocol)
    {
        throw new SkipTestException(protocol + " not on classpath");
    }

    // CLR: serialVersionUID belongs to Java serialization, which .NET exceptions do not use.
    private sealed class SkipTestException : Exception
    {
        internal SkipTestException(string message) : base(message)
        {
        }
    }
}
